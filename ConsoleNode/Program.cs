using ConsoleNode;
using DemCoinLib;
using DemCoinLib.Structs;
using DemNodeTcpLib;

Console.WriteLine("Hello, World!");

DemCoinNode node = new(args[0]);
Logger.Info("node", "Starting node...");
node.Init();
TcpDemNode tcp = node.CreateTcpNode(port: int.Parse(args[0]), peers: args.Length >= 2 ? [
    args[1]
] : []);
tcp.Log += l => Logger.Info("tcp", l);
tcp.Init();

Logger.Info("node", "Node has started");

DemCoinWallet wallet = DemCoinWallet.NewWithPhrase();

// for (int i = 0; i < 20; i++) {
//     Block b = MineBlock(node, wallet);
//     node.MineBlock(b);
// }
// return;

if (args.Length == 3) {
    Logger.Info("node", "Scheduling mine in 5 seconds...");
    await Task.Delay(1000);
    Block b = MineBlock(node, wallet);
    node.MineBlock(b);
}
Thread.Sleep(-1);


static Block MineBlock(DemCoinNode node, DemCoinWallet wallet) {
    Block block = node.GenerateNewBlock(wallet);
    block.Nonce = new byte[8];
    ulong attempts = 0;
    while (true) {
        attempts++;
        Random.Shared.NextBytes(block.Nonce);
        if (node.IsBlockNonceValid(block)) {
            Console.WriteLine("Mined test block in " + attempts + " attempts");
            return block;
        }
    }
}