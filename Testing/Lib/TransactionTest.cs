using DemCoinLib;
using DemCoinLib.Structs;

namespace Testing.Lib;

public class TransactionTest {

    internal static Transaction RandomTransaction(out DemCoinWallet sender, out DemCoinWallet reciever) {
        sender = DemCoinWallet.NewWithPhrase();
        reciever = DemCoinWallet.NewWithPhrase();

        Transaction transaction = new() {
            TransactionNumber = 1,
            Sender = sender.PublicKey,
            Recipient = reciever.AddressBytes,
            TransactionFee = 1,
            Amount = 2
        };
        transaction.SetMessage("Hello World!");
        return transaction;
    }

    [Test]
    public void SignAndValidate() {
        Transaction transaction = RandomTransaction(out DemCoinWallet sender, out DemCoinWallet reciever);
        transaction.Sign(sender);
        Assert.That(transaction.IsSignatureValid(), Is.True);

        if (transaction.Signature[0] == 1) transaction.Signature[0] = 2; else transaction.Signature[0] = 1;
        Assert.That(transaction.IsSignatureValid, Is.False);
    }

    [Test]
    public void SerialiseAndDeserialize() {
        Transaction transaction = RandomTransaction(out DemCoinWallet sender, out DemCoinWallet reciever);
        transaction.Sign(sender);

        byte[] serialised = transaction.Serialize();
        Transaction deserialized = Transaction.Deserialize(serialised);
        
        Assert.That(transaction, Is.EqualTo(deserialized));
    }
}