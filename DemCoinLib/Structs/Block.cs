using System.Security.Cryptography;
using System.Text;

namespace DemCoinLib.Structs;

public class Block {
    
    // HEADER
    public byte[] NetworkVersion { get; set; }  // 32 bytes (DemCoin v1.40.0)
    public byte[] PrevHeaderHash { get; set; }  // 32 bytes
    public byte[] TransactionsSig { get; set; }  // 32 bytes (combined hash of transaction data)
    public ulong TimeStamp { get; set; }  // 8 bytes
    public byte[] Difficulty { get; set; } // 32 bytes BigInteger (256 bits)
    public byte[] Nonce { get; set; }  // var bytes
    
    // DATA
    public Transaction[] Transactions { get; set; }  // var length
    
    public Block(byte[] prevHeaderHash, byte[] nonce, Transaction[] transactions) {
        PrevHeaderHash = prevHeaderHash;
        Nonce = nonce;
        Transactions = transactions;
    }

    public Block() { }
    
    private void CheckArray(byte[] arr, int length) {
        if (arr.Length != length) {
            throw new ArgumentException("Bad array size");
        }
    }

    public void CalculateTransactionsSig() {
        byte[][] hashes = new byte[Transactions.Length][];
        for (int i = 0; i < Transactions.Length; i++) {
            Transaction transaction = Transactions[i];
            hashes[i] = SHA256.HashData(transaction.Serialize());
        }

        if (hashes.Length == 0) {
            hashes = new byte[1][];
            hashes[0] = new byte[32];
        }

        while (hashes.Length != 1) {
            bool odd = hashes.Length % 2 == 1;
            int newLen = (int)(hashes.Length / 2d) + (odd ? 1 : 0);
            byte[][] oldHashes = hashes;
            hashes = new byte[newLen][];

            for (int i = 0; i < newLen; i++) {
                hashes[i] = SHA256.HashData(oldHashes[i].Concat(oldHashes[i + newLen]).ToArray());
            }

            if (odd) {
                hashes[^1] = SHA256.HashData(oldHashes[^1]);
            }
        }

        TransactionsSig = hashes[0];
    }

    /// <summary>
    /// Serialize this block into a byte array.
    /// </summary>
    /// <param name="includeData">
    /// Whether to include data, set to false to only include header.
    /// 
    /// WARNING: If set to false the resulting data cannot be deserialized.
    /// This is just for hashing purposes.
    /// </param>
    /// <param name="calcTs">Whether to calculate transaction signature.</param>
    /// <returns>The resulting byte array.</returns>
    public byte[] Serialize(bool includeData = true, bool calcTs = true) {
        if (calcTs) CalculateTransactionsSig();
        
        CheckArray(NetworkVersion, 32);
        CheckArray(PrevHeaderHash, 32);
        CheckArray(TransactionsSig, 32);
        
        DataWriter writer = new();
        writer.Write(NetworkVersion)
            .Write(PrevHeaderHash)
            .Write(TransactionsSig)
            .Write(TimeStamp)
            .Write(Difficulty)
            .WriteLengthed(Nonce);

        writer.Write(includeData ? Transactions : [], (w, t) => w.WriteLengthed(t.Serialize()));
        
        return writer.ToArray();
    }
    
    public static Block Deserialize(byte[] data) {
        DataReader reader = new(data);
        
        return new Block {
            NetworkVersion = reader.Read(32),
            PrevHeaderHash = reader.Read(32),
            TransactionsSig = reader.Read(32),
            TimeStamp = reader.ReadUInt64(),
            Difficulty = reader.Read(32),
            Nonce = reader.ReadLengthed(),
            Transactions = reader.ReadArray(r => Transaction.Deserialize(r.ReadLengthed()))
        };
    }
    
    public byte[] Hash() {
        byte[] data = Serialize();
        return SHA256.HashData(data);
    }

    public byte[] HashHeader(bool calcTs = true) {
        byte[] data = Serialize(false, calcTs);
        return SHA256.HashData(data);
    }

    public string HashString() {
        byte[] hash = Hash();
        return Convert.ToBase64String(hash);
    }
    
    public void SetNetworkVersion(string msg) {
        if (msg.Length > 32) {
            throw new ArgumentException("Message is too long");
        }
        NetworkVersion = new byte[32];
        NetworkVersion.WriteBuffer(Encoding.UTF8.GetBytes(msg));
    }

    public override int GetHashCode() {
        return HashString().GetHashCode();
    }

    public override bool Equals(object? obj) {
        if (obj is not Block other) {
            return false;
        }

        return HashString() == other.HashString();
    }
}