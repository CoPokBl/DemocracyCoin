using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace CoinNode;

internal static class Program {
    public static void Main(string[] args) {
        DemCoinNode node = new(args.Contains("noseed") ? new IPEndPoint(Dns.GetHostAddresses("me.zaneharrison.com")[0], 9534) : null, !args.Contains("nohost"));
        node.StartNode();
        
        Console.WriteLine("Started node.");

        if (!args.Contains("nowait")) {
            Console.WriteLine("Waiting for connection (Use arg 'nowait' to skip)...");
            node.ConnectedToNetSwitch.Wait();
        }

        if (args.Contains("nowallet")) {
            Thread.Sleep(-1);
            return;
        }
        
        // Obtain wallet
        RSA wallet = RSA.Create();

        if (File.Exists("wallet.xml")) {
            wallet.FromXmlString(File.ReadAllText("wallet.xml"));
        } else {
            File.WriteAllText("wallet.xml", wallet.ToXmlString(true));
        }
        
        byte[] publicKey = wallet.ExportRSAPublicKey();
        string walletAddress = Convert.ToBase64String(publicKey);
        
        Console.WriteLine($"Wallet created. Public key ({publicKey.Length}): " + walletAddress);

        if (args.Contains("mine")) {
            Console.WriteLine("[MINER] Mining...");
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            ulong tried = 0;
            int mined = 0;
            Random random = new();
            Stopwatch elapsed = Stopwatch.StartNew();

            byte[] nonce = new byte[8];
            while (true) {
                if (node.FixingChain) {  // We can't mine if this is happening
                    Thread.Sleep(1000);
                    continue;
                }
            
                random.NextBytes(nonce);
                if (node.IsNonceValid(nonce)) {
                    node.MineBlock(nonce, publicKey);
                    Utils.Announce("Mined Block!");
                    mined++;
                }
                tried++;
                if (tried % 100000 == 0) {
                    long elapsedSeconds = elapsed.ElapsedMilliseconds / 1000;
                    int hashRate = elapsedSeconds == 0 ? 0 : (int)((long)tried / elapsedSeconds);
                    int avSecondsToBlock = hashRate == 0 ? 1 : DemCoinNode.AverageHashesToBlock() / hashRate;
                    TimeSpan avTimeToBlock = TimeSpan.FromSeconds(avSecondsToBlock);
                    Console.WriteLine("[MINER] " +
                                      "Tried " + tried + 
                                      $" nonces ({hashRate}/sec). " +
                                      $"Mined: " + mined + ". " +
                                      "Wallet balance: " + node.GetBalance(publicKey) + $". " +
                                      $"Average block time: {avTimeToBlock}" + ". " + 
                                      $"Elapsed: {elapsed.Elapsed}");
                }
            }
        }

        else if (args.Contains("wallet")) {
            //Logger.AllLogs = false;
            Console.WriteLine("Logging is disabled so wallet interface can run");

            while (true) {
                Console.WriteLine($"Wallet: {walletAddress}");
                Console.WriteLine();
                Console.WriteLine($"Balance: {node.GetBalance(publicKey)}");
                
                Console.WriteLine();
                Console.WriteLine("1. Send Money");
                Console.WriteLine("2. Exit");
                Console.Write("> ");

                string selectionStr = Console.ReadLine()!;
                if (!int.TryParse(selectionStr, out int sel)) {
                    continue;
                }
                
                Console.WriteLine();

                switch (sel) {
                    case 1: {
                        Console.Write("Enter recipient address: ");
                        string recKeyStr = Console.ReadLine()!;
                        byte[] recipientKey;

                        try {
                            recipientKey = Convert.FromBase64String(recKeyStr);
                        }
                        catch (Exception) {
                            Console.WriteLine("Invalid address");
                            Console.ReadKey(true);
                            continue;
                        }
                        
                        Console.Write("Enter amount: ");
                        string amountStr = Console.ReadLine()!;

                        if (!double.TryParse(amountStr, out double amount)) {
                            Console.WriteLine("Invalid amount");
                            Console.ReadKey(true);
                            continue;
                        }
                        
                        node.SendMoney(wallet, recipientKey, amount);
                        Console.WriteLine("Created transaction :)");
                        Console.ReadKey(true);
                        break;
                    }
                    
                    case 2: {
                        return;
                    }
                }
            }
            
        }
        
        Thread.Sleep(-1);
    }
}