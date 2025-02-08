using System.Net;
using System.Net.Sockets;
using DemCoinLib;
using DemCoinLib.Structs;
using DemNodeTcpLib.Packets;

namespace DemNodeTcpLib;

public class TcpDemNode(DemCoinNode node, string[]? peers = null, int port = 9534) {
    private const int MaxConnections = 10;
    private const int BufferSize = 1_048_576;  // 1MB
    public const int DefaultPort = 9534;

    public event Action<string> Log = _ => { };
    
    private TcpListener _server;
    private readonly List<TcpClient> _clients = [];
    
    public void Init() {
        Log("Initialising...");
        Listen();

        node.OnBlockMined += async (height, block) => await BroadcastPacket(new NewBlockPacket(height, block));

        foreach (string peerIp in peers ?? []) {
            int peerPort = DefaultPort;
            string realIp = peerIp;
            if (peerIp.Contains(':')) {
                string[] parts = peerIp.Split(':');
                realIp = parts[0];
                peerPort = int.Parse(parts[1]);
            }

            Log("Doing init connect to: " + peerIp);
            ConnectToClient(realIp, peerPort);
        }
    }

    public async Task Listen() {
        _server = new TcpListener(IPAddress.Any, port);
        try {
            _server.Start();
        }
        catch (Exception) {
            Log("Server failed to start");
            throw;
        }

        Log("Listening for new connections...");
        while (true) {
            TcpClient newClient = await _server.AcceptTcpClientAsync();
            _clients.Add(newClient);

            _ = HandleClientAsync(newClient);
            Log("New connecting client is being handled :)");

            await newClient.SendPacket(new GetChainStatusPacket(128));
        }
    }
    
    private async Task HandleClientAsync(TcpClient client) {
        try {
            byte[] buffer = new byte[BufferSize];
            NetworkStream stream = client.GetStream();

            while (client.Connected) {
                Log($"<{client.Client.RemoteEndPoint}> Waiting on read...");
                int bytesRead = await stream.ReadAsync(buffer);

                if (bytesRead == BufferSize) {
                    // Oh no, we ran out of data
                    Log("Client message too big. Dropped.");
                    continue;
                }

                if (bytesRead <= 0) {
                    Log("0 bytes read, dc");
                    DisconnectClient(client);
                    return;
                }

                Log($"Read {bytesRead} bytes");

                TcpNodePacket packet;
                try {
                    packet = TcpNodePacket.Deserialize(buffer);
                }
                catch (Exception) {
                    Console.WriteLine("Invalid packet.");
                    continue;
                }

                Log($"[{client.Client.RemoteEndPoint}] Handling packet of ID: {packet.PacketType}");
                
                // New packet to handle
                switch (packet) {
                    case PingPacket: {
                        Log($"Ping from: {client.Client.RemoteEndPoint}. Pong!");
                        await stream.SendPacket(new PongPacket());
                        break;
                    }

                    case NewBlockPacket newBlock: {
                        if (newBlock.Height < node.ChainHeight) {  // This isn't where are tip is
                            Log($"Received a block we don't need from {client.Client.RemoteEndPoint}, block height: {newBlock.Height}, our height: {node.ChainHeight}.");

                            if (newBlock.Height < node.ChainHeight-1) {  // This is kinda old, give them our up to date chain
                                await ProvideChainInfo(stream, 64);
                            }
                            break;
                        }
                        
                        // Okay so it's newer than we have (We aren't synced)
                        if (newBlock.Height > node.ChainHeight) {
                            Log($"We aren't synced according to {client.Client.RemoteEndPoint}, requesting up to date.");
                            await stream.SendPacket(new GetChainStatusPacket(32));
                            break;
                        }

                        if (!node.ValidateBlock(newBlock.Block, out string? failReason, checkTransactions:false)) {
                            Log($"Received invalid block from {client.Client.RemoteEndPoint}, validation failed: {failReason}");
                            break;
                        }
                        
                        // Yay, request the full block (this copy is just headers)
                        Log($"Requesting block from {client.Client.RemoteEndPoint} which is new.");
                        await stream.SendPacket(new RequestBlocksPacket(node.ChainHeight));
                        newBlock.DisableTsCalc = true;  // We CANNOT let it recalc because it has no transactions and Serialise calcs it
                        await BroadcastPacket(newBlock);
                        break;
                    }

                    case GetChainStatusPacket getChainStatus: {
                        if (getChainStatus.Blocks > 128) {
                            Log("Got invalid chain status request, blocks count too large, sending 128.");
                            getChainStatus.Blocks = 128;
                        }

                        await ProvideChainInfo(stream, getChainStatus.Blocks);
                        Log($"Sent our chain status with {getChainStatus.Blocks} blocks to {client.Client.RemoteEndPoint}.");
                        break;
                    }

                    case ProvideChainStatusPacket provideChainStatus: {
                        if (provideChainStatus.ChainHeight < node.ChainHeight) {  // They have fewer blocks than us, let's tell them
                            Log($"Peer ({client.Client.RemoteEndPoint} has less blocks than us, sending them our info");
                            await ProvideChainInfo(stream, 16);
                            break;
                        }

                        Log($"{client.Client.RemoteEndPoint} provided {provideChainStatus.LastBlockHeaders.Length} block headers");

                        if (provideChainStatus.ChainHeight == node.ChainHeight) {  // Cool, we're as up to date as them
                            Log($"We have same chain high, we good, height: {provideChainStatus.ChainHeight}");
                            break;
                        }

                        // They have more blocks than us (according to them)
                        // We need to:
                        // - Find whether our chains have forked (They have the better one)
                        // or whether they just have more blocks (We are out of date)
                        // - Verify their chain

                        // ulong extraBlocks = provideChainStatus.ChainHeight - node.ChainHeight;
                        Block ourTip = node.LastBlock;
                        int tipIndex = -1;
                        for (uint i = 0; i < provideChainStatus.LastBlockHeaders.Length; i++) {
                            if (!provideChainStatus.LastBlockHeaders[i].HashHeader(false)
                                    .SequenceEqual(ourTip.HashHeader())) {
                                continue;
                            }

                            tipIndex = (int)i;
                            break;
                        }

                        if (tipIndex < 0) {  // Tips might have diverged
                            Log($"Failed to find our chain tip in peer's chain (chain divergence, or old chain): {client.Client.RemoteEndPoint}");
                            await stream.SendPacket(new LocateCommonBlockPacket(node.BlockDatabase, 10_000));
                            Log("A common block location request has been sent.");
                            break;
                        }
                        
                        // Okay our chains have not forked
                        if (tipIndex == 0) {  // Our chains are the same? Maybe they lied?
                            Log("Peer chain turned out to be the same as ours");
                            break;
                        }

                        Block[] newBlocks =
                            provideChainStatus.LastBlockHeaders[..tipIndex].Reverse().ToArray();  // Get the last tipIndex blocks (we have the rest)
                        
                        // tipIndex is how many more blocks they have, let's validate them all! (Excluding transactions)
                        bool valid = node.ValidateBlocks(newBlocks, out string? failReason,
                            checkTimestamp: false, checkTransactions: false);

                        if (!valid) {
                            Log($"Blocks provided by peer ({client.Client.RemoteEndPoint}) are invalid: {failReason}");
                            break;
                        }

                        Log($"All new blocks from {client.Client.RemoteEndPoint} are valid, requesting them...");
                        
                        // ReSharper disable once IntVariableOverflowInUncheckedContext (We checked for negative)
                        await stream.SendPacket(new RequestBlocksPacket(node.ChainHeight, (ulong)tipIndex));
                        break;
                    }

                    case RequestBlocksPacket requestBlocks: {
                        if (requestBlocks.StartIndex > requestBlocks.EndIndex) {
                            Log($"Client ({client.Client.RemoteEndPoint}) requested invalid block range: {requestBlocks.StartIndex}-{requestBlocks.EndIndex}");
                            return;
                        }
                        
                        Block[] blocks =
                            node.BlockDatabase.GetBlockRange(requestBlocks.StartIndex, requestBlocks.EndIndex);

                        await stream.SendPacket(new ProvideBlocksPacket(requestBlocks.StartIndex, blocks));
                        Log($"Provided {blocks.Length} blocks to {client.Client.RemoteEndPoint}.");
                        break;
                    }

                    case ProvideBlocksPacket provideBlocks: {
                        ulong providedIndex = provideBlocks.StartIndex;
                        for (int i = 0; i < provideBlocks.Blocks.Length; i++) {
                            if (providedIndex + (ulong)i < node.ChainHeight) {
                                continue;
                            }

                            Block block = provideBlocks.Blocks[i];
                            if (!node.ValidateBlock(block, out string? failReason, checkTimestamp:false)) {
                                Log($"Peer ({client.Client.RemoteEndPoint}) mass provided invalid block at index {providedIndex + (ulong)i}: {failReason}");
                                continue;
                            }

                            Log("Peer mass provided a valid block: " + providedIndex);
                            node.BlockDatabase.InsertBlock(block);  // Manually insert
                        }

                        Log($"Peer ({client.Client.RemoteEndPoint}) mass blocks have been processed.");
                        break;
                    }

                    case LocateCommonBlockPacket locateCommonBlock: {
                        Log($"Locating our common block with {client.Client.RemoteEndPoint}...");
                        foreach (byte[] hash in locateCommonBlock.BlockHashes) {
                            ulong? index = node.BlockDatabase.GetBlockIndex(hash);
                            if (index == null) continue;
                            Log("Found common block, sending");
                            await stream.SendPacket(new ProvideCommonBlockPacket(hash, node.ChainHeight, node.BlockDatabase.GetBlockRange(index.Value+1, index.Value+10_001)));
                            break;
                        }

                        Log("Checked all blocks");
                        break;
                    }

                    case ProvideCommonBlockPacket provideCommonBlock: {
                        Log("ProvideCommonBlock received.");
                        if (!provideCommonBlock.Found) {
                            Log($"Peer {client.Client.RemoteEndPoint} did not find a common block with us, oh no");
                            break;
                        }

                        ulong? blockIndex = node.BlockDatabase.GetBlockIndex(provideCommonBlock.Block);
                        if (blockIndex == null) {
                            Log("Apparently our common block doesn't exist anymore?");
                            break;
                        }

                        if (provideCommonBlock.ChainHeight == node.ChainHeight) {
                            Log("Chain up to date with peer.");
                            break;
                        }

                        if (provideCommonBlock.ChainHeight < node.ChainHeight) {
                            Log("Our chain is better.");
                            break;
                        }

                        ulong forkDepth = node.ChainHeight - 1 - blockIndex.Value;
                        bool valid = node.ValidateBlocks(provideCommonBlock.SubsequentHeaders, out string? failReason, forkDepth,
                            checkTimestamp: false, checkTransactions: false);
                        if (!valid) {
                            Log("Provided block headers are invalid.");
                            break;
                        }

                        Log("Received block headers are valid, checking for fork.");

                        RequestBlocksPacket request = new(blockIndex.Value + 1,
                            provideCommonBlock.ChainHeight - blockIndex.Value);
                        if (forkDepth == 0) {
                            Log("Common block was our chain tip, no fork detected, requesting new blocks.");
                            await stream.SendPacket(request);
                            break;
                        }
                        
                        // Fork, rollback chain
                        Log($"PERFORMING CHAIN ROLL BACK BY {forkDepth} BLOCKS");
                        node.BlockDatabase.RollbackChain(blockIndex.Value);
                        Log("Chain rollback complete");
                        
                        // Now request the blocks
                        await stream.SendPacket(request);
                        Log("New blocks requested");
                        break;
                    }

                    case RequestPeersPacket: {
                        string[] ipPeers = _clients
                            .Where(c => c != client)  // Don't send them themselves
                            .Select(c => c.Client.RemoteEndPoint)
                            .OfType<IPEndPoint>()
                            .Select(ipe => ipe.Address.ToString())
                            .ToArray();
                        await stream.SendPacket(new ProvidePeersPacket(ipPeers));
                        Log($"Sent our peers to {client.Client.RemoteEndPoint}");
                        break;
                    }

                    case ProvidePeersPacket providePeers: {
                        Log($"We have been give {providePeers.Peers.Length} peers by {client.Client.RemoteEndPoint}");

                        IPAddress[] existingPeers = _clients
                            .Select(c => c.Client.RemoteEndPoint)
                            .OfType<IPEndPoint>()
                            .Select(ep => ep.Address)
                            .ToArray();

                        IPEndPoint[] newPeers = providePeers.Peers
                            .Where(p => !existingPeers.Any(ep => ep.Equals(p.Address)))
                            .ToArray();

                        Log($"{newPeers.Length} of the peers are valid new peers");
                        foreach (IPEndPoint peer in newPeers) {
                            try {
                                await ConnectToClient(peer);
                            }
                            catch (IOException) {
                                Log($"Failed to connect to {peer}");
                            }
                        }
                        
                        break;
                    }

                    default: {
                        Log("Unhandled packet: " + packet.PacketType);
                        break;
                    }
                }  // Don't handle anything else (pong packets etc.)
            }

            Log("Client connected status became false.");
            DisconnectClient(client);
        }
        catch (IOException) {
            DisconnectClient(client);
        }
    }

    private Task ProvideChainInfo(NetworkStream stream, uint blockCount) {
        try {
            ProvideChainStatusPacket status = new(node.ChainHeight, null!);

            Block[] blocks = blockCount == 0 ? 
                [] : 
                node.BlockDatabase.GetBlockRange(node.ChainHeight - blockCount, node.ChainHeight - 1);

            status.LastBlockHeaders = new Block[blocks.Length];
            
            for (int i = 0; i < blocks.Length; i++) {
                status.LastBlockHeaders[i] = blocks[blocks.Length - i - 1];
            }

            Log($"We just sent {blocks.Length} block headers");
            return stream.SendPacket(status);
        }
        catch (Exception e) {
            throw;
        }
    }

    public async Task BroadcastPacket(TcpNodePacket packet) {
        foreach (TcpClient client in _clients) {
            try {
                NetworkStream stream = client.GetStream();
                await stream.SendPacket(packet);
            }
            catch (IOException) {
                DisconnectClient(client);
            }
        }
    }

    public void DisconnectClient(TcpClient client) {
        Log("Client disconnected: " + client.Client.RemoteEndPoint);
        _clients.Remove(client);
        client.Close();
    }

    public Task ConnectToClient(string ip, int clientPort) => ConnectToClient(new IPEndPoint(IPAddress.Parse(ip), clientPort));
    
    public async Task ConnectToClient(IPEndPoint endPoint) {
        Log($"Connecting to {endPoint}");
        TcpClient client = new();
        await client.ConnectAsync(endPoint);
        Log("Connection success, handling new client");
        _clients.Add(client);

        _ = HandleClientAsync(client);
    }
}