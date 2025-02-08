using System.Diagnostics;
using System.Numerics;
using ConsoleNode;
using DemCoinLib;
using DemCoinLib.Structs;

Console.WriteLine("Hello, World!");

Logger.Info("node", "Starting node...");
DemCoinNode node = new(args[0]);
if (!node.ValidateChain()) {
    throw new Exception("Invalid chain");
}
// TcpDemNode tcp = node.CreateTcpNode(port: int.Parse(args[0]), peers: args.Length >= 2 ? [
//     args[1]
// ] : []);
// tcp.Log += l => Logger.Info("tcp", l);
// tcp.Init();

Logger.Info("node", "Node has started");

DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();

List<double> mineTimes = [];
while (true) {
    Stopwatch t = Stopwatch.StartNew();
    Block b = MineBlock(node, wallet);
    mineTimes.Add(t.Elapsed.TotalSeconds);
    Console.WriteLine("AVERAGE BLOCK TIME: " + mineTimes.Average());
    try {
        node.MineBlock(b);
    }
    catch (Exception) {
        Console.WriteLine("--------- FAILED TO PROCESS MINED BLOCK ---------");
    }
}
return;

if (args.Length == 3) {
    Logger.Info("node", "Scheduling mine in 5 seconds...");
    await Task.Delay(1000);
    Block b = MineBlock(node, wallet);
    node.MineBlock(b);
}
Thread.Sleep(-1);

static Block MineBlock(DemCoinNode node, DemCoinWallet wallet) {
    ulong attempts = 0;
    Console.WriteLine("Diff: " + node.GetCurrentDifficulty());
    Console.WriteLine("Reward: " + node.GetCurrentMinerReward());
    Console.WriteLine("Target: " + new BigInteger(node.GetCurrentTargetValue(), true));
    Stopwatch sw = Stopwatch.StartNew();
    Stopwatch sinceReset = Stopwatch.StartNew();
    byte[] nonce = new byte[4];
    Random random = new();
    MiningUtil mining = new(node, wallet);
    mining.RefreshBlock();
    
    while (true) {
        attempts++;
        random.NextBytes(nonce);
        if (mining.CheckNonce(nonce)) {
            Console.WriteLine("Mined test block in " + attempts + $" attempts and {sw.Elapsed.TotalSeconds}s");
            mining.Block!.Nonce = nonce;
            return mining.Block;
        }

        if (attempts % 1_000_000 == 0) {
            int hashesPerSecond = (int)(attempts / sw.Elapsed.TotalSeconds);
            Console.WriteLine($"{hashesPerSecond} H/s");
            
            if (sinceReset.Elapsed.TotalSeconds >= 30) {  // Don't let timestamp slip
                Console.WriteLine($"Refreshed block after mining for {sinceReset}");
                mining.RefreshBlock();
                sinceReset.Restart();
            }
        }
    }
}