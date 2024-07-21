using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CoinNode.Transport;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace CoinNode;

// Packets:
// 0 - Get block count         | 0
// 1 - Get block by index      | 1 + uint64
// 2 - Get block range         | 2 + uint64 + uint64
// 3 - Provide block           | 3 + uint64 + block data
// 4 - Provide block count     | 4 + uint64
// 5 - Provide peer            | 5 + ip + port
// 6 - Heartbeat               | 6                                         DEPRECATED
// 7 - Acknowledgement         | 7 + checksum[16]
// 8 - Provide transaction     | 8 + transaction
// 9 - Provide block range     | 9 + x*(block size + block data)
public partial class DemCoinNode(IPEndPoint? seedNode) {
    private const int VerifyThreshold = 3;  // If peers is less than this, use peer count
    private const int Difficulty = 23;  // Number of leading zeroes required in hash  was 26
    private const double MinerReward = 1.001;  // Coins rewarded for mining one block
    private const int MaxPacketSize = 10_000;  // Max UDP packet
    private const int DefaultPort = 9534;
    private const int MaxPort = 65536;
    private const int HeartbeatPeriod = 5000;
    
    private int FunctionalVerifyThreshold => VerifyThreshold > _peers.Count ? _peers.Count : VerifyThreshold;
    public static int AverageHashesToBlock() => (int)Math.Pow(2, Difficulty);
    
    private readonly List<(int, WebSocket)> _peers = [];
    private readonly object _peersLock = new();
    
    private BlockDatabase _blockDatabase = null!;
    public bool FixingChain;
    private ulong _remoteHighestChainTip;  // Peers we are waiting to send their block counts
    private readonly List<Transaction> _pendingTransactions = [];  // These are currently volatile.
    public readonly ManualResetEventSlim ConnectedToNetSwitch = new(false);

    private readonly object _pendingTransactionsLock = new();

    public void StartNode() {
        _blockDatabase = new BlockDatabase("blockchain.db");
        Logger.Info("DB", "Database loaded with " + _blockDatabase.GetBlockCount() + " blocks.");

        Debug.Assert(GetDefBlock().HashString() == Block.Deserialize(GetDefBlock().Serialize()).HashString(), "Deserialize/serialize failed");

        if (_blockDatabase.GetBlockCount() == 0) {
            AddChainStartBlock();
        }
        else {
            Logger.Info("DB", "Loaded existing chain. Length: " + _blockDatabase.GetBlockCount());
        }

        if (seedNode != null) {
            ConnectToPeer(seedNode);
        }

        
        int port = DefaultPort;
        while (true) {
            try {
                WebSocketServer server = new(IPAddress.Any, port);
                server.AddWebSocketService("/node", () => new WssServe(this));
                server.Start();
                Logger.Info("NET", $"Bound to port {port}");
                break;
            }
            catch (SocketException) {
                Logger.Info("NET", $"Could not bind to port {port}, trying {port+1}");
                port++;
            }
        }

        Timer blockCountRequestTimer = new(_ => {
            lock (_peersLock) {
                byte[] req = [0];  // Request block count
                foreach ((int, WebSocket) peer in _peers) {
                    peer.Item2.Send(req);
                }
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public class WssServe(DemCoinNode node) : WebSocketBehavior {
        
        protected override void OnMessage(MessageEventArgs e) {
            node.HandlePeerPacket(new MethodDataSender(Send), e);
        }

        protected override void OnOpen() {
            IPEndPoint endPoint = Context.UserEndPoint;
            endPoint.Port = DefaultPort;  // Just assume it's this
            byte[] packet = ConstructNewPeerPacket(endPoint);
            // lock (node._peersLock) {
            //     foreach ((int, WebSocket) peer in node._peers) {
            //         peer.Item2.Send(packet);
            //     }
            //     node._peers.Add((Context.UserEndPoint.GetHashCode(), Context.WebSocket));
            // }
            node.HandleNewPeer(Context.UserEndPoint.GetHashCode(), Context.WebSocket);
        }
    }

    private static byte[] ConstructNewPeerPacket(IPEndPoint peer) {
        return new byte[] { 5 }
            .Concat(peer.Address.GetAddressBytes())
            .Concat(BitConverter.GetBytes(peer.Port))
            .ToArray();
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
        
        // Get all pending transactions
        List<Transaction> transactions = [
                new Transaction {  // Coinbase, we get a reward :)
                Sender = new byte[32],
                Recipient = walletAddress,
                Amount = MinerReward,
                Signature = [0],
                TransactionNumber = 0
            }
        ];

        lock (_pendingTransactionsLock) {
            foreach (Transaction t in _pendingTransactions) {
                transactions.Add(t);
            }
            
            _pendingTransactions.Clear();
        }
        
        Block block = new() {
            PrevHash = _blockDatabase.GetLastBlock().Hash(),
            Nonce = nonce,
            Transactions = transactions.ToArray()
        };

        Debug.Assert(ValidateBlock(block), "Valid block to add to database");  // Sanity check to make sure our own checks pass
        
        AddBlockToDatabase(block);
        
        Debug.Assert(ValidateBlock(_blockDatabase.GetLastBlock(), 1), "Block added to database correctly");

        byte[] newBlockPacket = [3];
        byte[] blockData = block.Serialize();
        byte[] indexBytes = BitConverter.GetBytes(newBlockIndex);
        newBlockPacket = newBlockPacket.Concat(indexBytes).Concat(blockData).ToArray();
        lock (_peersLock) {
            foreach ((int, WebSocket) peer in _peers) {
                peer.Item2.Send(newBlockPacket);
                Logger.Debug("NODE", "Sent new block to peer.");
            }
        }
    }

    /// <summary>
    /// Creates a transaction sending money to a wallet and queues it to be added to the blockchain
    /// when the next block is mined.
    /// </summary>
    /// <remarks>
    /// Transaction will not be executed until a block is mined.
    /// </remarks>
    /// <param name="wallet">Your RSA creds.</param>
    /// <param name="to">Recipient of funds.</param>
    /// <param name="amount">Amount to transfer.</param>
    public void SendMoney(RSA wallet, byte[] to, double amount) {
        Transaction transaction = new() {
            Sender = wallet.ExportRSAPublicKey(),
            Recipient = to,
            Amount = amount,
            TransactionNumber = GetNextTransactionNumber(wallet.ExportRSAPublicKey())
        };

        byte[] serialized = transaction.Serialize(true);
        byte[] sig = wallet.SignData(serialized, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        transaction.Signature = sig;

        PublishTransaction(transaction);
    }

    public void PublishTransaction(Transaction transaction) {
        Debug.Assert(ValidateTransaction(transaction));
        lock (_pendingTransactionsLock) {
            _pendingTransactions.Add(transaction);
        }
        
        // Send to peers
        byte[] packet = new byte[] { 8 }.Concat(transaction.Serialize()).ToArray();
        foreach ((int, WebSocket) peerInfo in _peers) {
            peerInfo.Item2.Send(packet);
        }
    }
    
    public double GetBalance(byte[] walletAddress) {
        return _blockDatabase.GetBalance(walletAddress);
    }

    public ulong GetNextTransactionNumber(byte[] walletAddress) {
        lock (_pendingTransactionsLock) {  // Check to see if they have any pending transactions
            Transaction? lastPending = _pendingTransactions.LastOrDefault(t => t.Sender.SequenceEqual(walletAddress));
            if (lastPending != null) {
                return lastPending.TransactionNumber + 1;
            }
        }
        return _blockDatabase.GetLastTransactionNumber(walletAddress) + 1;
    }
    
    private void AskForBlockRange(ulong start, ulong end, IDataSender peer) {
        byte[] request = [2];
        byte[] startBytes = BitConverter.GetBytes(start);
        byte[] endBytes = BitConverter.GetBytes(end);
        request = request.Concat(startBytes).Concat(endBytes).ToArray();
        Logger.Debug("DB", $"Sending {request.Length} bytes to peer to request block range");
        peer.Send(request);
    }

    private static bool IsHashValidBlock(IReadOnlyList<byte> hash) {
        for (byte i = 0; i < Difficulty; i++) {
            if ((hash[i/8] & (0b10000000 >> (i%8))) != 0) {
                return false;
            }
        }

        return true;
    }

    private void HandleNewPeer(int peerId, WebSocket socket) {
        ConnectedToNetSwitch.Set();
        byte[] request = [0];
        Logger.Debug("NODE", "Querying block count from peer");
        socket.Send(request);
            
        // Send transactions
        lock (_pendingTransactionsLock) {
            foreach (byte[] packet in _pendingTransactions.Select(transaction => new byte[] { 8 }.Concat(transaction.Serialize()).ToArray())) {
                socket.Send(packet);
            }
        }

        // Inform peers or new peer
        lock (_peersLock) {
                
        }
    }

    /// <summary>
    /// Handles incoming packets from a peer, and sends responses. THIS DOES NOT HANDLE SENDING PACKETS TO PEERS. Except
    /// </summary>
    /// <param name="peer">The peer to listen to.</param>
    private void ConnectToPeer(IPEndPoint peer) {
        int peerId = peer.GetHashCode();
        lock (_peersLock) {
            if (_peers.Any(p => p.Item1 == peerId)) {
                return;
            }
        }
        Logger.Info("NODE", "Connecting to peer: " + peer);
        WebSocket socket = new("ws://" + peer + "/node");
        socket.OnMessage += (_, args) => HandlePeerPacket(new WebSocketDataSender(socket), args);
        socket.OnOpen += (_, _) => {
            HandleNewPeer(peerId, socket);
        };
        socket.OnClose += (_, _) => {
            lock (_peersLock) {
                _peers.RemoveAll(p => p.Item1 == peerId);
            }
        };
        socket.Connect();
        _peers.Add((peerId, socket));
    }

    private void HandlePeerPacket(IDataSender socket, MessageEventArgs args) {
        if (args.IsPing) {
            Logger.Debug("NODE", "Peer pinged us :)");
            return;
        }
        if (!args.IsBinary) {
            Logger.Debug("NODE", "Non binary packet, dropping");
            return;
        }
        try {
            byte[] buffer = args.RawData;
            int bytesRead = buffer.Length;
            if (bytesRead == 0) {
                return;
            }

            // Packet types
            byte packetType = buffer[0];
            buffer = buffer[..bytesRead];
            
            Logger.Debug("NODE", $"Received message from peer: {bytesRead} bytes. Type: {packetType}");
            
            switch (packetType) {
                /* Get block count     */  case 0: {
                    byte[] response = new byte[] { 4 }
                        .Concat(BitConverter.GetBytes(_blockDatabase.GetBlockCount()))
                        .ToArray();
                    socket.Send(response);
                    break;
                }

                /* Get block by index  */  case 1: {
                    ulong index = BitConverter.ToUInt64(buffer.AsSpan()[1..]);
                    Block? block = _blockDatabase.GetBlockByIndex(index);
                    if (block == null) {  // We don't have it
                        Logger.Debug("NODE", $"Could not find requested block at index: {index}");
                        break;
                    }
                    byte[] response = block.Serialize();
                    socket.Send(response);
                    break;
                }

                /* Get block range     */  case 2: {
                    ulong start = BitConverter.ToUInt64(buffer.AsSpan()[1..9]);
                    ulong end = BitConverter.ToUInt64(buffer.AsSpan()[9..]);
                    Block[] blocks = _blockDatabase.GetBlockRange(start, end);
                    ulong[] indexsOfBlocks = Enumerable.Range((int)start, (int)(end - start)).Select(i => (ulong)i).ToArray();

                    int cIndex = 0;
                    byte[][] blockData = blocks.Select(b =>
                        BitConverter.GetBytes(indexsOfBlocks[cIndex++]).Concat(b.Serialize()).ToArray()).ToArray();
                    
                    // Collate blocks into packets of max size PacketMaxSize
                    byte[] currentPacket = [9];
                    int inPacket = 0;
                    for (int i = 0; i < blockData.Length; i++) {
                        byte[] bd = blockData[i];
                        byte[] bdSize = BitConverter.GetBytes(bd.Length);
                        currentPacket = currentPacket.Concat(bdSize).Concat(bd).ToArray();
                        inPacket++;
                    
                        if (i == blockData.Length-1 || currentPacket.Length + blockData[i+1].Length > MaxPacketSize) {
                            Debug.Assert(currentPacket.Length <= MaxPacketSize);
                            socket.Send(currentPacket);  // 9 + x*(size(index + bd) + index + bd)
                            Console.WriteLine($"Sent peer {inPacket} blocks, {currentPacket.Length} bytes");
                            inPacket = 0;
                            currentPacket = [9];
                        }
                    }
                    
                    // ReSharper disable once RedundantCast  It's not redundant you liar.
                    Debug.Assert(currentPacket.SequenceEqual([(byte)9]));

                    // ulong currentBlockIndex = start;
                    // foreach (Block block in blocks) {
                    //     // Send each block using packet type 3
                    //     Logger.Debug("NODE", "Sending block: " + block.HashString());
                    //     byte[] response = [3];
                    //     byte[] indexBytes = BitConverter.GetBytes(currentBlockIndex);
                    //     Debug.Assert(indexBytes.Length == 8);
                    //     byte[] blockData = block.Serialize();
                    //     response = response.Concat(indexBytes).Concat(blockData).ToArray();
                    //     con.Send(response);
                    //     currentBlockIndex++;
                    // }

                    break;
                }

                /* Provide block       */  case 3: {
                    ProcessIncomingBlockData(buffer[1..]);
                    break;
                }

                /* Provide block count */  case 4: {
                    ulong count = BitConverter.ToUInt64(buffer.AsSpan()[1..]);
                    if (count > _blockDatabase.GetBlockCount()) {
                        FixingChain = true;
                        Logger.Info("NODE", $"Chain is out of date. Fetching {_blockDatabase.GetBlockCount() - count} blocks.");
                        _remoteHighestChainTip = count > _remoteHighestChainTip ? count : _remoteHighestChainTip;
                        AskForBlockRange(_blockDatabase.GetBlockCount(), count, socket);
                    }
                    break;
                }
                
                /* Provide Peer        */  case 5: {
                    byte[] ipBytes = buffer[1..5];
                    byte[] portBytes = buffer[5..9];
                    IPAddress ip = new(ipBytes);
                    int port = BitConverter.ToInt32(portBytes);
                    IPEndPoint newPeer = new(ip, port);
                    ConnectToPeer(newPeer);
                    break;
                }
                
                /* Provide Transaction */  case 8: {
                    Transaction transaction = Transaction.Deserialize(buffer[1..bytesRead]);
                    
                    // Validate transaction
                    if (!ValidateTransaction(transaction)) {
                        Logger.Debug("NODE", "Transaction rejected, invalid");
                        break;
                    }

                    lock (_pendingTransactionsLock) {
                        _pendingTransactions.Add(transaction);
                    }
                    Logger.Info("NODE", "--------------- Transaction Received ---------------");
                    break;
                }
                
                /* Provide block range */ case 9: {
                    int nextIndex = 1;
                    while (nextIndex < buffer.Length) {
                        byte[] blockDataSizeBytes = buffer[nextIndex..(nextIndex + 4)];
                        Debug.Assert(blockDataSizeBytes.Length == 4);
                        int blockDataSize = BitConverter.ToInt32(blockDataSizeBytes);
                        ProcessIncomingBlockData(buffer[(nextIndex + 4)..(nextIndex + 4 + blockDataSize)]);
                        nextIndex += 4 + blockDataSize;
                    }
                    break;
                }
                
                default:
                    Logger.Debug("NODE", $"Invalid packet type: {packetType}");
                    break;
            }
        }
        catch (Exception e) {
            Logger.Error("NODE", "Peer handler crash: " + e.Message);
            Logger.Error("NODE", e.ToString());
        }
        finally {
            lock (_peersLock) {
                //_peers.Remove(_peers.Find(p => p.GetHashCode() == peer)!);
            }
        }
        
    }

    private void ProcessIncomingBlockData(byte[] buffer) {
        ulong index = BitConverter.ToUInt64(buffer.AsSpan()[..8]);
        if (index != _blockDatabase.GetBlockCount() && !FixingChain) {
            Logger.Debug("NODE", "Ignoring new block from peer because it's not our next one");
            return; // We only accept the next block
        }

        byte[] blockData = buffer[8..]; // 1 byte for block index, 8 bytes for block data
        Block block = Block.Deserialize(blockData);

        Logger.Debug("NODE", "Received block: " + block.HashString());

        // Verify block
        if (!ValidateBlock(block)) {
            return;
        }
                        
        // Block is valid
        AddBlockToDatabase(block);
        Logger.Info("NODE", "Block added. " + (FixingChain ? $"({_blockDatabase.GetBlockCount()}/{_remoteHighestChainTip})" : ""));

        if (_blockDatabase.GetBlockCount() == _remoteHighestChainTip) {
            FixingChain = false;
            Logger.Info("NODE", "Chain has been fixed :)");
        }
                        
        // Remove all pending transactions that were in the block
        int[] trans = block.Transactions.Select(t => t.GetHashCode()).ToArray();
        lock (_pendingTransactions) {
            _pendingTransactions.RemoveAll(t => trans.Contains(t.GetHashCode()));
        }
    }

    private void AddBlockToDatabase(Block block) {
        _blockDatabase.InsertBlock(block);
        
        // Transactions
        foreach (Transaction transaction in block.Transactions) {
            _blockDatabase.InsertTransaction(transaction);
        }
    }

    /// <summary>
    /// Checks the validity of a Block.
    /// </summary>
    /// <param name="block">The block to validate.</param>
    /// <param name="skip">
    /// How far back in the database the block is, the age of the block.
    /// Set to 0 if the block is not in the database and this block comes after the last block in the database.
    /// Set to 1 if the being validated is the last block in the database.
    /// It is how many blocks we need to skip before we get to the block that should come before this block.
    /// Defaults to 0.
    /// </param>
    /// <returns>True if the block is valid, otherwise false.</returns>
    private bool ValidateBlock(Block block, int skip = 0) {
        byte[] expectedHash = _blockDatabase.GetLastBlock(skip).Hash();
        if (!block.PrevHash.SequenceEqual(expectedHash)) {
            Logger.Debug("NODE", $"Block verification failed. (PrevHash bad)");
            return false;
        }

        byte[] blockHash = SHA256.HashData(block.PrevHash.Concat(block.Nonce).ToArray());
        if (!IsHashValidBlock(blockHash)) {
            Logger.Debug("NODE", "Block verification failed. (Nonce bad)");
            return false;
        }

        if (block.Transactions.Length == 0) {
            Logger.Debug("NODE", "Block verification failed. (No transactions)");
            return false;
        }

        bool allTransactionsValid = true;
        bool recordedCoinbase = false;
        foreach (Transaction transaction in block.Transactions) {
            if (transaction.Sender.SequenceEqual(new byte[32])) {  // coinbase
                if (recordedCoinbase) {  // Only one is allowed
                    Logger.Debug("NODE", "Block verification failed. (Only 1 coinbase allowed)");
                    allTransactionsValid = false;
                    break;
                }
                recordedCoinbase = true;
                
                if (Math.Abs(transaction.Amount - MinerReward) > 0.01) {
                    Logger.Debug("NODE", $"Block verification failed. (Invalid miner reward, reward: {transaction.Amount})");
                    allTransactionsValid = false;
                    break;
                }
                
                continue;
            }
            
            // EVERYTHING HERE IS NOT CHECKED FOR COINBASE TRANSACTIONS
            if (skip == 0 && !ValidateTransaction(transaction)) {
                allTransactionsValid = false;
                break;
            }
        }

        if (!allTransactionsValid) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check the validity of a Transaction object that has not been added to the database.
    /// This works will pending transactions.
    /// This method logs the fail reason apon rejection.
    /// </summary>
    /// <remarks>
    /// THIS WILL NOT WORK ON TRANSACTIONS ALREADY IN THE DATABASE. It will fail because the transaction number will be invalid.
    /// </remarks>
    /// <param name="transaction">The transaction to validate, MUST NOT BE IN DATABASE.</param>
    /// <returns>True if the transaction is valid, otherwise false.</returns>
    private bool ValidateTransaction(Transaction transaction) {
        if (transaction.Amount <= 0) {  // Coinbases need a specific amount so this isn't needed
            Logger.Debug("NODE", $"Block verification failed. (Negative transaction, amount: {transaction.Amount})");
            return false;
        }
            
        // Check if sender has enough money
        double senderBalance = _blockDatabase.GetBalance(transaction.Sender);
        if (senderBalance < transaction.Amount) {
            Logger.Debug("NODE", $"Block verification failed. (Insufficient funds in transaction, senderbal: {senderBalance}, amount: {transaction.Amount})");
            return false;
        }
            
        // Check if transaction is valid (Not needed for coinbases)
        if (!transaction.IsSignatureValid()) {
            Logger.Debug("NODE", "Block verification failed. (Invalid transaction signature)");
            return false;
        }
            
        // Check if the transaction number is valid
        ulong nextTn = GetNextTransactionNumber(transaction.Sender);
        if (transaction.TransactionNumber != nextTn) {
            Logger.Debug("NODE", $"Block verification failed. (Transaction number invalid, expected: {nextTn}, actual: {transaction.TransactionNumber}.");
            return false;
        }

        return true;
    }
    
}