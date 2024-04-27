using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CoinNode;

// Basically a TCP server
// Packets:
// 0 - Get block count         | 0
// 1 - Get block by index      | 1 + uint64
// 2 - Get block range         | 2 + uint64 + uint64
// 3 - Provide block           | 3 + uint64 + block data
// 4 - Provide block count     | 4 + uint64
// 5 - Provide peer            | 5 + ip + port
// 6 - Heartbeat               | 6
public class DemCoinNode(IPEndPoint? seedNode, bool listenForPeers = true) {
    private const int VerifyThreshold = 3;  // If peers is less than this, use peer count
    private const int Difficulty = 3;  // Number of leading zeroes required in hash
    private const double MinerReward = 1.001;
    private const int MaxPacketSize = 65_507;  // Max UDP packet
    
    private int FunctionalVerifyThreshold => VerifyThreshold > _peers.Count ? _peers.Count : VerifyThreshold;
    
    private readonly List<(int, ReliableUdp)> _peers = [];
    private BlockDatabase _blockDatabase;
    public bool FixingChain;
    private ulong _longestChainLength;
    private int _longestChainPeer;
    private int _pendingBlockIndex;  // Peers we are waiting to send their block counts
    private Timer _checkPeerBlocksTimer;

    public void StartNode() {
        _blockDatabase = new BlockDatabase("blockchain.db");
        Console.WriteLine("Database loaded with " + _blockDatabase.GetBlockCount() + " blocks.");

        Debug.Assert(GetDefBlock().HashString() == Block.Deserialize(GetDefBlock().Serialize()).HashString(), "Deserialize/serialize failed");

        if (_blockDatabase.GetBlockCount() == 0) {
            AddChainStartBlock();
        }
        else {
            Console.WriteLine("Loaded existing chain. Length: " + _blockDatabase.GetBlockCount());
        }

        if (seedNode != null) {
            Thread thread = new(() => ConnectToPeer(seedNode));
            thread.Start();
        }

        while (_peers.Count != _peers.Count) {  // Wait for peers to become available
            Thread.Sleep(100);
        }

        TimerCallback back = state => {
            // Query block counts
            _pendingBlockIndex = _peers.Count;
            foreach ((int, ReliableUdp) peer in _peers) {
                byte[] request = [0];
                Console.WriteLine("Querying block count from peer");
                peer.Item2.Send(request);
            }
        };
        //_checkPeerBlocksTimer = new Timer(back, null, 5*1000, 60*1000);

        if (listenForPeers) {
            Thread listenerThread = new(NewPeerListener);
            listenerThread.Start();
        }
    }
    
    private void NewPeerListener() {
        UdpClient udp = new(9534);
        Console.WriteLine("Listening for new peers...");
        
        while (true) {
            IPEndPoint endpoint = new(IPAddress.Any, 0);
            byte[] data = udp.Receive(ref endpoint);

            if (data.Length != 1 || data[0] != 69) {
                continue;
            }

            ReliableUdp con = new(udp.Client, endpoint);

            Thread peerThread = new(() => OperatePeer(endpoint.GetHashCode(), con));
            peerThread.Start();
            
            InformPeersOfNewPeer(endpoint);
        }
    }
    
    private void InformPeersOfNewPeer(IPEndPoint peer) {
        byte[] peerData = [5];
        byte[] ipBytes = peer.Address.GetAddressBytes();
        byte[] portBytes = BitConverter.GetBytes(peer.Port);
        peerData = peerData.Concat(ipBytes).Concat(portBytes).ToArray();
        foreach ((int, ReliableUdp) peerStreamPair in _peers) {
            peerStreamPair.Item2.Send(peerData);
        }
    }
    
    public bool IsNonceValid(IEnumerable<byte> nonce) {
        if (FixingChain) {
            throw new Exception("Chain is being fixed. Cannot mine.");
        }
        
        byte[] hash = SHA256.HashData(_blockDatabase.GetLastBlock().Hash().Concat(nonce).ToArray());
        return IsHashValidBlock(hash);
    }
    
    public void AddChainStartBlock() {
        _blockDatabase.InsertBlock(GetDefBlock());
    }

    private static Block GetDefBlock() {
        return new Block {
            PrevHash = new byte[32],
            Nonce = new byte[8],
            Transactions = []
        };
    }

    public void MineBlock(byte[] nonce, byte[] walletAddress) {
        if (!IsNonceValid(nonce)) {
            throw new Exception("Invalid nonce.");
        }
        
        ulong newBlockIndex = _blockDatabase.GetBlockCount();
        
        Block block = new() {
            PrevHash = _blockDatabase.GetLastBlock().Hash(),
            Nonce = nonce,
            Transactions = [
                new Transaction {
                    Sender = new byte[32],
                    Recipient = walletAddress,
                    Amount = MinerReward,
                    Signature = [0],
                    TransactionNumber = 0
                }
            ]
        };

        Debug.Assert(ValidateBlock(block));  // Sanity check to make sure our own checks pass
        
        AddBlockToDatabase(block);

        byte[] newBlockPacket = [3];
        byte[] blockData = block.Serialize();
        byte[] indexBytes = BitConverter.GetBytes(newBlockIndex);
        newBlockPacket = newBlockPacket.Concat(indexBytes).Concat(blockData).ToArray();
        foreach ((int, ReliableUdp) peer in _peers) {
            peer.Item2.Send(newBlockPacket);
            Console.WriteLine("Sent new block to peer.");
        }
    }
    
    public double GetBalance(byte[] walletAddress) {
        return _blockDatabase.GetBalance(walletAddress);
    }
    
    private void AskForBlockRange(ulong start, ulong end, int ignorePeer = -1, int selectPeer = -1) {
        bool didAny = false;
        foreach ((int, ReliableUdp) peerStreamPair in _peers) {
            if (selectPeer != -1 && peerStreamPair.Item1 != selectPeer) {
                continue;
            }
            
            if (peerStreamPair.Item1 == ignorePeer) {
                continue;
            }

            didAny = true;
            byte[] request = [2];
            byte[] startBytes = BitConverter.GetBytes(start);
            byte[] endBytes = BitConverter.GetBytes(end);
            request = request.Concat(startBytes).Concat(endBytes).ToArray();
            Console.WriteLine($"Sending {request.Length} bytes to peer to request block range");
            peerStreamPair.Item2.Send(request);
        }

        if (!didAny) {
            throw new Exception("Select Peer not found to ask for blocks.");
        }
    }
    
    private bool IsHashValidBlock(IReadOnlyList<byte> hash) {
        for (int i = 0; i < Difficulty; i++) {
            if (hash[i] != 0) {
                return false;
            }
        }

        return true;
    }

    private void GetNewBlocksFromBestPeerIfDone() {
        if (!FixingChain) {
            return;
        }
        
        _pendingBlockIndex--;
        if (_pendingBlockIndex > 0) {
            Console.WriteLine($"Got block count from new peer {_pendingBlockIndex} left");
            return;
        }
        Console.WriteLine($"Fixing chain, {_longestChainLength - _blockDatabase.GetBlockCount()} blocks to go. Getting from {_longestChainPeer}");
        AskForBlockRange(_blockDatabase.GetBlockCount(), _longestChainLength, selectPeer: _longestChainPeer);
    }
    
    /// <summary>
    /// Handles incoming packets from a peer, and sends responses. THIS DOES NOT HANDLE SENDING PACKETS TO PEERS. Except
    /// </summary>
    /// <param name="peer">The peer to listen to.</param>
    private void ConnectToPeer(IPEndPoint peer) {
        int peerId = peer.GetHashCode();
        ReliableUdp connection = new(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp),
            peer);
        connection.MaxPacketSize = MaxPacketSize;
        HeartBeater heart = new(connection);
        
        try {
            heart.Start();
            heart.WaitForContact(5000);
            
            OperatePeer(peerId, connection, heart);
        }
        catch (Exception e) {
            Console.WriteLine("Peer disconnected: " + e.Message);
        }
        finally {
            try {
                heart.Stop();
                connection.Stop();
            }
            catch (Exception) {
                // Ignored
            }
        }
    }

    private void OperatePeer(int peerId, ReliableUdp con, HeartBeater? beater = null) {
        _peers.Add((peerId, con));

        if (beater == null) {
            beater = new HeartBeater(con);
            beater.Start();
        }
        
        // Ask for block count
        byte[] request = [0];
        Console.WriteLine("Querying block count from peer");
        con.Send(request);
        
        byte[] buffer = new byte[MaxPacketSize];

        try {
            while (true) {
                Console.Write("Waiting for read... ");
                int bytesRead = con.Receive(buffer);
                Console.WriteLine("READ");
                if (bytesRead == 0) {
                    continue;
                }

                // Packet types
                byte packetType = buffer[0];
                
                Console.WriteLine($"Received message from peer: {bytesRead} bytes. Type: {packetType}");
                
                switch (packetType) {
                    /* Get block count     */  case 0: {
                        byte[] response = new byte[] { 4 }
                            .Concat(BitConverter.GetBytes(_blockDatabase.GetBlockCount()))
                            .ToArray();
                        con.Send(response);
                        break;
                    }

                    /* Get block by index  */  case 1: {
                        ulong index = BitConverter.ToUInt64(buffer.AsSpan()[1..]);
                        Block block = _blockDatabase.GetBlockByIndex(index);
                        byte[] response = block.Serialize();
                        con.Send(response);
                        break;
                    }

                    /* Get block range     */  case 2: {
                        ulong start = BitConverter.ToUInt64(buffer.AsSpan()[1..9]);
                        ulong end = BitConverter.ToUInt64(buffer.AsSpan()[10..]);
                        Block[] blocks = _blockDatabase.GetBlockRange(start, end);

                        ulong currentBlockIndex = start;
                        foreach (Block block in blocks) {
                            // Send each block using packet type 3
                            Console.WriteLine("Sending block: " + block.HashString());
                            byte[] response = [3];
                            byte[] indexBytes = BitConverter.GetBytes(currentBlockIndex);
                            byte[] blockData = block.Serialize();
                            response = response.Concat(indexBytes).Concat(blockData).ToArray();
                            con.Send(response);
                            currentBlockIndex++;
                        }

                        break;
                    }

                    /* Provide block       */  case 3: {
                        ulong index = BitConverter.ToUInt64(buffer.AsSpan()[1..9]);
                        if (index != _blockDatabase.GetBlockCount() && !FixingChain) {
                            continue; // We only accept the next block
                        }

                        byte[] blockData = buffer[9..bytesRead]; // 1 byte for block index, 8 bytes for block data
                        Block block = Block.Deserialize(blockData);

                        Console.WriteLine("Received block: " + block.HashString());

                        // Verify block
                        if (!ValidateBlock(block)) {
                            continue;
                        }
                        
                        // Block is valid
                        AddBlockToDatabase(block);
                        Console.WriteLine("Block added.");

                        if (_blockDatabase.GetBlockCount() == _longestChainLength) {
                            FixingChain = false;
                            Console.WriteLine("Chain has been fixed :)");
                        }
                        break;
                    }

                    /* Provide block count */  case 4: {
                        ulong count = BitConverter.ToUInt64(buffer.AsSpan()[1..]);
                        if (count <= _blockDatabase.GetBlockCount()) {
                            GetNewBlocksFromBestPeerIfDone();
                            return;
                        }

                        if (FixingChain && count <= _longestChainLength) {
                            GetNewBlocksFromBestPeerIfDone();
                            break;
                        }

                        _longestChainLength = count;
                        _longestChainPeer = peerId;
                        FixingChain = true;

                        GetNewBlocksFromBestPeerIfDone();
                        break;
                    }
                    
                    /* Provide Peer        */  case 5: {
                        break;
                        byte[] ipBytes = buffer[1..5];
                        byte[] portBytes = buffer[5..9];
                        IPAddress ip = new(ipBytes);
                        int port = BitConverter.ToInt32(portBytes);
                        IPEndPoint newPeer = new(ip, port);
                        Thread thread = new(() => ConnectToPeer(newPeer));
                        thread.Start();
                        break;
                    }
                    
                    default:
                        Console.WriteLine($"Invalid packet type: {packetType}");
                        break;
                }
            }
        }
        catch (Exception e) {
            Console.WriteLine("Peer disconnected: " + e.Message);
            Console.WriteLine(e);
        }
        finally {
            _peers.Remove(_peers.Find(p => p.GetHashCode() == peerId));
        }
        
    }

    private void AddBlockToDatabase(Block block) {
        _blockDatabase.InsertBlock(block);
        
        // Transactions
        foreach (Transaction transaction in block.Transactions) {
            _blockDatabase.InsertTransaction(transaction);
        }
    }

    private bool ValidateBlock(Block block) {
        if (!block.PrevHash.SequenceEqual(_blockDatabase.GetLastBlock().Hash())) {
            Console.WriteLine("Block verification failed. (PrevHash bad)");
            return false;
        }

        byte[] blockHash = SHA256.HashData(block.PrevHash.Concat(block.Nonce).ToArray());
        if (!IsHashValidBlock(blockHash)) {
            Console.WriteLine("Block verification failed. (Nonce bad)");
            return false;
        }

        if (block.Transactions.Length == 0) {
            Console.WriteLine("Block verification failed. (No transactions)");
            return false;
        }

        bool allTransactionsValid = true;
        bool recordedCoinbase = false;
        foreach (Transaction transaction in block.Transactions) {
            if (transaction.Amount <= 0) {
                Console.WriteLine($"Block verification failed. (Negative transaction, amount: {transaction.Amount})");
                allTransactionsValid = false;
                break;
            }
            
            // Addresses aren't this length lol, why did I do this
            // if (transaction.Sender.Length != 32 || transaction.Recipient.Length != 32) {
            //     Console.WriteLine($"Block verification failed. (Invalid transaction addresses, s{transaction.Sender.Length} r{transaction.Recipient.Length})");
            //     allTransactionsValid = false;
            //     break;
            // }

            if (transaction.Sender.SequenceEqual(new byte[32])) {  // coinbase
                if (recordedCoinbase) {  // Only one is allowed
                    Console.WriteLine("Block verification failed. (Only 1 coinbase allowed)");
                    allTransactionsValid = false;
                    break;
                }
                recordedCoinbase = true;
                
                if (Math.Abs(transaction.Amount - MinerReward) > 0.01) {
                    Console.WriteLine($"Block verification failed. (Invalid miner reward, reward: {transaction.Amount})");
                    allTransactionsValid = false;
                    break;
                }
                
                continue;
            }
            
            // EVERYTHING HERE IS NOT CHECKED FOR COINBASE TRANSACTIONS
            
            // Check if sender has enough money
            double senderBalance = _blockDatabase.GetBalance(transaction.Sender);
            if (senderBalance < transaction.Amount) {
                Console.WriteLine($"Block verification failed. (Insufficient funds in transaction, senderbal: {senderBalance}, amount: {transaction.Amount})");
                allTransactionsValid = false;
                break;
            }
            
            // Check if transaction is valid (Not needed for coinbases)
            if (!transaction.IsValid()) {
                Console.WriteLine("Block verification failed. (Invalid transaction signature)");
                allTransactionsValid = false;
                break;
            }
            
            // Check if the transaction number is valid
            ulong lastTn = _blockDatabase.GetLastTransactionNumber(transaction.Sender);
            if (transaction.TransactionNumber != lastTn + 1) {
                Console.WriteLine($"Block verification failed. (Transaction number invalid, expected: {lastTn+1}, actual: {transaction.TransactionNumber}.");
                allTransactionsValid = false;
                break;
            }
        }

        if (!allTransactionsValid) {
            return false;
        }

        return true;
    }
    
}