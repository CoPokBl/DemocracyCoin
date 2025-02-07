using System.Security.Cryptography;
using System.Text;

namespace DemCoinLib.Structs;

public class Transaction {
    public ulong TransactionNumber { get; set; }
    public byte[] Sender { get; set; }  // 64 bytes, All zeros for coinbase, THIS IS A PUBLIC KEY, NOT ADDRESS
    public byte[] Recipient { get; set; }  // 32 bytes, THIS IS AN ADDRESS, NOT A PUBLIC KEY
    public double Amount { get; set; }  // 8 bytes
    public double TransactionFee { get; set; }  // 8 bytes
    public byte[] Message { get; set; }  // 256 bytes
    public byte[] Signature { get; set; }  // 32 bytes
    
    // HELPER FIELDS
    public string SenderAddress => DemCoinUtils.PublicKeyToAddress(Sender);
    public string RecipientAddress => DemCoinUtils.AddressBytesToString(Recipient);

    private void CheckArray(byte[] arr, int length) {
        if (arr.Length != length) {
            throw new ArgumentException("Bad array size");
        }
    }
    
    public byte[] Serialize(bool omitSig = false) {
        CheckArray(Sender, 64);
        CheckArray(Recipient, 25);
        CheckArray(Message, 256);
        if (!omitSig) CheckArray(Signature, 64);
        
        DataWriter writer = new();
        writer.Write(TransactionNumber)  // 8
            .Write(Sender)               // 64
            .Write(Recipient)            // 25
            .Write(Amount)               // 8
            .Write(TransactionFee)       // 8
            .Write(Message);             // 256

        if (!omitSig) {
            writer.Write(Signature);     // 64
        }
        
        return writer.ToArray();
    }
    
    public static Transaction Deserialize(byte[] data) {
        DataReader reader = new(data);
        
        return new Transaction {
            TransactionNumber = reader.ReadUInt64(),
            Sender = reader.Read(64),
            Recipient = reader.Read(25),
            Amount = reader.ReadDouble(),
            TransactionFee = reader.ReadDouble(),
            Message = reader.Read(256),
            Signature = reader.ReadRemaining()
        };
    }
    
    public bool IsSignatureValid() {
        if (Sender.Length == 0) {
            return true;
        }
        
        DemCoinWallet senderWallet = DemCoinWallet.ImportPublic(Sender);
        return senderWallet.ValidateSignature(Serialize(true), Signature);
    }

    public void Sign(DemCoinWallet wallet) => Sign(wallet.Creds);

    public void Sign(ECDsa rsa) {
        byte[] serialized = Serialize(true);
        byte[] sig = rsa.SignData(serialized, HashAlgorithmName.SHA256);
        Signature = sig;
    }

    public void SetMessage(string msg) {
        if (msg.Length > 255) {
            throw new ArgumentException("Message is too long");
        }
        Message = new byte[256];
        Message.WriteBuffer(Encoding.UTF8.GetBytes(msg));
    }

    public override int GetHashCode() {
        return BitConverter.ToInt32(MD5.HashData(Serialize()));
    }

    public override bool Equals(object? obj) {
        if (obj is not Transaction other) {
            return false;
        }
        
        return GetHashCode() == other.GetHashCode();
    }
}