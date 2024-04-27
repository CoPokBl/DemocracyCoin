using System.Net;
using System.Security.Cryptography;

namespace CoinNode;

internal static class Program {
    public static void Main(string[] args) {
        DemCoinNode node = new(args.Contains("noseed") ? new IPEndPoint(IPAddress.Loopback, 9534) : null, !args.Contains("nohost"));
        node.StartNode();
        
        Console.WriteLine("Started node.");

        if (!args.Contains("mine")) {
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
        
        byte[] publicKey = wallet.ExportSubjectPublicKeyInfo();
        
        Console.WriteLine($"Wallet created. Public key ({publicKey.Length}): " + Convert.ToBase64String(publicKey));
        
        Console.WriteLine("Mining...");
        ulong tried = 0;
        int mined = 0;
        Random random = new();
        while (true) {
            if (node.FixingChain) {  // We can't mine if this is happening
                Thread.Sleep(100);
                continue;
            }
            
            byte[] nonce = new byte[8];
            random.NextBytes(nonce);
            if (node.IsNonceValid(nonce)) {
                node.MineBlock(nonce, publicKey);
                Console.WriteLine("Mined block!");
                mined++;
            }
            tried++;
            if (tried % 100000 == 0) {
                Console.WriteLine("Tried " + tried + " nonces. Mined: " + mined + ". Wallet balance: " + node.GetBalance(publicKey));
            }
        }
    }
}