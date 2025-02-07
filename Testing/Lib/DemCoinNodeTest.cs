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
        node.Init();
        return node;
    }

    private static DemCoinNode NewNode(out DemCoinWallet wallet) {
        DemCoinNode node = InitNode();
        wallet = DemCoinWallet.NewWithPhrase();
        Assert.Multiple(() => {
            Assert.That(node.ChainHeight, Is.EqualTo(1));
            Assert.That(new BigInteger(node.GetCurrentDifficulty()), Is.EqualTo((BigInteger)1));
        });

        // node.BlockDatabase = new ExtendedBlockDatabase(node.BlockDatabase);

        return node;
    }

    private static Block MineBlock(DemCoinNode node, DemCoinWallet wallet) {
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

    [Test]
    public void MineBlocks() {
        const int blocks = 10;
        DemCoinNode node = NewNode(out DemCoinWallet wallet);

        for (int i = 0; i < blocks; i++) {
            Block block = MineBlock(node, wallet);
            node.MineBlock(block);
            Assert.That(node.ChainHeight, Is.EqualTo(i + 2));
        }

        double balance = node.GetBalance(wallet.Address);
        Assert.That(balance, Is.EqualTo(blocks));

        bool validChain = node.ValidateChain();
        Assert.That(validChain, Is.True);
    }

    [Test]
    public void Transactions() {
        DemCoinNode node = NewNode(out DemCoinWallet wallet1);
        DemCoinWallet wallet2 = DemCoinWallet.NewWithPhrase();
        
        node.MineBlock(MineBlock(node, wallet1));
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(1));
            Assert.That(node.GetBalance(wallet2.Address), Is.Zero);
        });
        
        node.SendMoney(wallet1, wallet2.Address, 0.5, 0.1, "Hello There!");
        node.MineBlock(MineBlock(node, wallet2));
        
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(0.4));  // They lose 0.5 + 0.1 fee
            Assert.That(node.GetBalance(wallet2.Address), Is.EqualTo(1.6));  // They get the 0.1 fee because they mined it
        });
        
        node.SendMoney(wallet2, wallet1.Address, 0.3, 0, "General Kenobi!");
        node.MineBlock(MineBlock(node, wallet1));
        
        Assert.Multiple(() => {
            Assert.That(node.GetBalance(wallet1.Address), Is.EqualTo(1.7));  // They get 0.3
            Assert.That(node.GetBalance(wallet2.Address), Is.EqualTo(1.3));  // They lose 0.3
        });
    }
}