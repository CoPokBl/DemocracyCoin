using System.Diagnostics;
using System.Numerics;
using DemCoinLib;
using DemCoinLib.Db;
using DemCoinLib.Structs;

namespace Testing.Lib;

public class DemCoinNodeTest {

    private static DemCoinNode InitNode() {
        if (File.Exists("testchain.db")) {
            File.Delete("testchain.db");
        }
        DemCoinNode node = new("testchain.db");
        return node;
    }

    private static DemCoinNode NewNode(out DemCoinWallet wallet) {
        DemCoinNode node = InitNode();
        wallet = DemCoinWallet.NewWithPhrase();
        Assert.Multiple(() => {
            Assert.That(node.ChainHeight, Is.EqualTo(1));
            Assert.That(new BigInteger(DemCoinUtils.ToInt256Bytes(node.GetCurrentDifficulty())), Is.EqualTo((BigInteger)1));
        });

        // node.BlockDatabase = new ExtendedBlockDatabase(node.BlockDatabase);

        return node;
    }

    private static Block MineBlock(DemCoinNode node, DemCoinWallet wallet) {
        Stopwatch w = Stopwatch.StartNew();
        Block block = node.GenerateNewBlock(wallet);
        Console.WriteLine($"Took {w.ElapsedMilliseconds}ms to generate new block");
        block.Nonce = new byte[8];
        ulong attempts = 0;
        Stopwatch timer = Stopwatch.StartNew();
        while (true) {
            attempts++;
            Random.Shared.NextBytes(block.Nonce);
            if (node.IsBlockNonceValid(block)) {
                Console.WriteLine("Mined test block in " + attempts + $" attempts in {timer.ElapsedMilliseconds}ms");
                return block;
            }
        }
    }

    [Test]
    public void MineBlocks() {
        const int blocks = 10;
        DemCoinNode node = NewNode(out DemCoinWallet wallet);
        double expectedBal = 0;

        for (int i = 0; i < blocks; i++) {
            Block block = MineBlock(node, wallet);
            expectedBal += node.GetCurrentMinerReward();
            node.MineBlock(block);
            Assert.That(node.ChainHeight, Is.EqualTo(i + 2));
        }

        double balance = node.GetBalance(wallet.Address);
        Assert.That(Math.Round(balance, 5), Is.EqualTo(Math.Round(expectedBal, 5)));

        bool validChain = node.ValidateChain();
        Assert.That(validChain, Is.True);
    }

    [Test]
    public void Transactions() {
        DemCoinNode node = NewNode(out DemCoinWallet wallet1);
        DemCoinWallet wallet2 = DemCoinWallet.NewWithPhrase();

        double bal1 = node.GetCurrentMinerReward();
        double bal2 = 0;
        
        node.MineBlock(MineBlock(node, wallet1));
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(Math.Round(bal1, 5)));
            Assert.That(node.GetBalance(wallet2.Address), Is.EqualTo(Math.Round(bal2, 5)));
        });
        
        node.SendMoney(wallet1, wallet2.Address, 0.1, 0.1, "Hello There!");
        bal1 -= 0.2;
        bal2 += node.GetCurrentMinerReward() + 0.2;
        node.MineBlock(MineBlock(node, wallet2));
        
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(Math.Round(bal1, 5)));
            Assert.That(node.GetBalance(wallet2.Address), Is.EqualTo(Math.Round(bal2, 5)));
        });
        
        node.SendMoney(wallet2, wallet1.Address, 0.2, 0, "General Kenobi!");
        bal1 += 0.2 + node.GetCurrentMinerReward();
        bal2 -= 0.2;
        node.MineBlock(MineBlock(node, wallet1));
        
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(Math.Round(bal1, 5)));
            Assert.That(node.GetBalance(wallet2.Address), Is.EqualTo(Math.Round(bal2, 5)));
        });
    }

    [Test]
    public void GetReward() {
        DemCoinNode node = NewNode(out DemCoinWallet wallet);
        
        double reward = node.GetCurrentMinerReward();  // Should be the minimum since we have no blocks
        Assert.That(reward, Is.EqualTo(DemCoinNode.MinMinerReward));

        byte[] maxDiff = new byte[32];
        for (int i = 0; i < maxDiff.Length; i++) {
            maxDiff[i] = byte.MaxValue;
        }
        reward = DemCoinNode.GetCurrentMinerReward(maxDiff);
        Assert.That(reward, Is.EqualTo(DemCoinNode.MaxMinerReward));
    }
}