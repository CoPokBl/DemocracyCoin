using System.Diagnostics;
using System.Security.Cryptography;

namespace CoinNode;

public class Block {
    public byte[] PrevHash { get; set; }
    public byte[] Nonce { get; set; }
    public Transaction[] Transactions { get; set; }
    
    public Block(byte[] prevHash, byte[] nonce, Transaction[] transactions) {
        PrevHash = prevHash;
        Nonce = nonce;
        Transactions = transactions;
    }

    public Block() { }
    
    public byte[] Serialize() {  // prevHash[32] + nonceLength[4] + nonce[x] + transactions[y]
        byte[] prevHash = PrevHash;
        Debug.Assert(prevHash.Length == 32);
        byte[] dataLengthBytes = BitConverter.GetBytes(Nonce.Length);
        Debug.Assert(dataLengthBytes.Length == 4);
        byte[] transactionData = Transaction.SerializeMany(Transactions);
        byte[] data = new byte[prevHash.Length + dataLengthBytes.Length + Nonce.Length + transactionData.Length];
        
        Buffer.BlockCopy(prevHash, 0, data, 0, prevHash.Length);
        Buffer.BlockCopy(dataLengthBytes, 0, data, prevHash.Length, dataLengthBytes.Length);
        Buffer.BlockCopy(Nonce, 0, data, prevHash.Length + dataLengthBytes.Length, Nonce.Length);
        Buffer.BlockCopy(transactionData, 0, data, prevHash.Length + dataLengthBytes.Length + Nonce.Length, transactionData.Length);
        
        return data;
    }
    
    public byte[] Hash() {
        byte[] data = Serialize();
        return SHA256.HashData(data);
    }
    
    public string HashString() {
        byte[] hash = Hash();
        return Convert.ToBase64String(hash);
    }

    public override int GetHashCode() {
        return HashString().GetHashCode();
    }

    public static Block Deserialize(byte[] data) {  // prevHash[..32] + nonceLength[32..36] + nonce[36..(36+nonceLength)] + transactions[36+nonceLength..]
        byte[] prevHash = data[..32];
        Debug.Assert(prevHash.Length == 32);
        byte[] dataLengthBytes = data[32..36];
        Debug.Assert(dataLengthBytes.Length == 4);
        int dataLength = BitConverter.ToInt32(dataLengthBytes);
        byte[] nonce = data[36..(36 + dataLength)];
        byte[] transactionData = data[(36 + dataLength)..];
        Transaction[] transactions = Transaction.DeserializeMany(transactionData);
        
        return new Block {
            PrevHash = prevHash,
            Nonce = nonce,
            Transactions = transactions
        };
    }
}