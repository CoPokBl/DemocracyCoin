using System.Diagnostics;
using System.Security.Cryptography;

namespace CoinNode;

public class Transaction {
    public ulong TransactionNumber { get; set; }
    public byte[] Sender { get; set; }  // All zeros for coinbase
    public byte[] Recipient { get; set; }
    public double Amount { get; set; }
    public byte[] Signature { get; set; }
    
    public byte[] Serialize(bool omitSig = false) {
        byte[] transactionNumberBytes = BitConverter.GetBytes(TransactionNumber);  // 8 bytes (ulong)
        byte[] senderLengthBytes = BitConverter.GetBytes(Sender.Length);           // 4 bytes (int)
        byte[] recipientLengthBytes = BitConverter.GetBytes(Recipient.Length);     // 4 bytes (int)
        byte[] amountBytes = BitConverter.GetBytes(Amount);                        // 8 bytes (ulong)
        
        Debug.Assert(transactionNumberBytes.Length == 8);
        Debug.Assert(senderLengthBytes.Length == 4);
        Debug.Assert(recipientLengthBytes.Length == 4);
        Debug.Assert(amountBytes.Length == 8);
        
        //                     transactionNum len(sender) sender          len(recipient) recipient          amount
        byte[] data = new byte[8 +            4 +         Sender.Length + 4 +            Recipient.Length + 8 +
                               (!omitSig ? Signature.Length : 0)];
        
        Buffer.BlockCopy(transactionNumberBytes, 0, data, 0, transactionNumberBytes.Length);
        Buffer.BlockCopy(senderLengthBytes, 0, data, transactionNumberBytes.Length, senderLengthBytes.Length);
        Buffer.BlockCopy(Sender, 0, data, transactionNumberBytes.Length + senderLengthBytes.Length, Sender.Length);
        Buffer.BlockCopy(recipientLengthBytes, 0, data, transactionNumberBytes.Length + senderLengthBytes.Length + Sender.Length, recipientLengthBytes.Length);
        Buffer.BlockCopy(Recipient, 0, data, transactionNumberBytes.Length + senderLengthBytes.Length + Sender.Length + recipientLengthBytes.Length, Recipient.Length);
        Buffer.BlockCopy(amountBytes, 0, data, transactionNumberBytes.Length + senderLengthBytes.Length + Sender.Length + recipientLengthBytes.Length + Recipient.Length, 8);
        if (!omitSig) Buffer.BlockCopy(Signature, 0, data, transactionNumberBytes.Length + senderLengthBytes.Length + Sender.Length + recipientLengthBytes.Length + Recipient.Length + 8, Signature.Length);
        
        return data;
    }
    
    public static Transaction Deserialize(byte[] data) {
        byte[] transNumberBytes = data[..8];
        ulong transactionNumber = BitConverter.ToUInt64(transNumberBytes);
        
        byte[] senderLengthBytes = data[8..12];
        int senderLength = BitConverter.ToInt32(senderLengthBytes);
        byte[] sender = data[12..(12 + senderLength)];
        
        byte[] recipientLengthBytes = data[(12 + senderLength)..(12 + senderLength + 4)];
        int recipientLength = BitConverter.ToInt32(recipientLengthBytes);
        byte[] recipient = data[(12 + senderLength + 4)..(12 + senderLength + 4 + recipientLength)];
        
        byte[] amountBytes = data[(12 + senderLength + 4 + recipientLength)..(12 + senderLength + 4 + recipientLength + 8)];
        double amount = BitConverter.ToDouble(amountBytes);
        
        byte[] signature = data[(12 + senderLength + 4 + recipientLength + 8)..];
        
        return new Transaction {
            Sender = sender,
            Recipient = recipient,
            Amount = amount,
            Signature = signature,
            TransactionNumber = transactionNumber
        };
    }
    
    public bool IsValid() {
        if (Sender.Length == 0) {
            return true;
        }
        
        using RSA rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Sender, out _);
        return rsa.VerifyData(Serialize(true), Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
    
    public static byte[] SerializeMany(Transaction[] transactions) {
        byte[] countBytes = BitConverter.GetBytes(transactions.Length);
        byte[] data = new byte[countBytes.Length];
        Debug.Assert(data.Length == 4);
        Buffer.BlockCopy(countBytes, 0, data, 0, countBytes.Length);
        
        foreach (Transaction transaction in transactions) {
            byte[] transactionData = transaction.Serialize();
            byte[] transactionCountBytes = BitConverter.GetBytes(transactionData.Length);
            data = data.Concat(transactionCountBytes).ToArray();
            data = data.Concat(transactionData).ToArray();
        }
        
        return data;
    }
    
    public static Transaction[] DeserializeMany(byte[] data) {
        if (data.Length == 4) {
            return [];
        }
        
        byte[] countBytes = data[..4];
        Debug.Assert(countBytes.Length == 4);
        int count = BitConverter.ToInt32(countBytes);
        data = data[4..];
        
        Transaction[] transactions = new Transaction[count];
        for (int i = 0; i < count; i++) {
            byte[] transactionLengthBytes = data[..4];
            int transactionLength = BitConverter.ToInt32(transactionLengthBytes);
            byte[] transactionData = data[4..(4 + transactionLength)];
            transactions[i] = Deserialize(transactionData);
            data = data[(4 + transactionLength)..];
        }
        
        return transactions;
    }
}