using System.Security.Cryptography;
using DemCoinLib;
using DemCoinLib.Structs;

namespace Testing.Lib;

public class BlockTest {

    private Block RandomBlock(out DemCoinWallet sender, out DemCoinWallet reciever) {
        Transaction transaction = TransactionTest.RandomTransaction(out sender, out reciever);
        transaction.Sign(sender);

        Block block = new() {
            Transactions = [transaction],
            TimeStamp = DemCoinUtils.GetUtcTimestamp(),
            PrevHeaderHash = SHA256.HashData(BitConverter.GetBytes(DemCoinUtils.GetUtcTimestamp())),
            Difficulty = new byte[32],
            Nonce = [0, 1, 2, 3, 4, 5]
        };
        block.Difficulty[0] = 3;
        block.SetNetworkVersion("TestNet v0.0.0");
        block.CalculateTransactionsSig();
        
        return block;
    }

    [Test]
    public void SerialiseAndDeserialize() {
        Block block = RandomBlock(out _, out _);
        
        byte[] serialised = block.Serialize();
        Block deserialized = Block.Deserialize(serialised);
        
        Assert.That(block, Is.EqualTo(deserialized));
    }
}