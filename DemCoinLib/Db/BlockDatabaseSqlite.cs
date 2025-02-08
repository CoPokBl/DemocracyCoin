using System.Data.SQLite;
using DemCoinLib.Structs;

namespace DemCoinLib.Db;

public class BlockDatabaseSqlite : IBlockDatabase {
    
    private const string ConnectionString = "Data Source=";
    private readonly SQLiteConnection _connection;

    public BlockDatabaseSqlite(string path) {
        _connection = new SQLiteConnection(ConnectionString + path + ";");
        _connection.Open();
        CreateTables();
    }
    
    private void CreateTables() {
        SQLiteCommand cmd = new("""

                                CREATE TABLE IF NOT EXISTS blocks (
                                    hash VARCHAR(32) PRIMARY KEY, 
                                    data TEXT
                                );

                                CREATE TABLE IF NOT EXISTS transactions (
                                    sender_key VARCHAR(64), 
                                    sender_address VARCHAR(32),
                                    recipient_address VARCHAR(32),
                                    amount DOUBLE,
                                    fee DOUBLE,
                                    signature TEXT,
                                    message VARCHAR(256),
                                    transaction_number NUMERIC,
                                    block_index INTEGER
                                );

                                """, _connection);
        cmd.ExecuteNonQuery();
    }
    
    public ulong GetBlockCount() {
        using SQLiteCommand cmd = new("SELECT COUNT(*) FROM blocks;", _connection);
        ulong val = Convert.ToUInt64(cmd.ExecuteScalar()!);
        return val;
    }
    
    public Block? GetLastBlock(ulong skip = 0) {
        using SQLiteCommand cmd = new("SELECT * FROM blocks ORDER BY ROWID DESC LIMIT 1 OFFSET @skip;", _connection);
        cmd.Parameters.AddWithValue("@skip", skip);
        using SQLiteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read()) {
            return null;
        }
        
        byte[] data = Convert.FromBase64String(reader.GetString(reader.GetOrdinal("data")));
        Block block = Block.Deserialize(data);
        return block;
    }
    
    public Block? GetBlockByIndex(ulong index) {
        using SQLiteCommand cmd = new("SELECT * FROM blocks ORDER BY ROWID LIMIT 1 OFFSET @index;", _connection);
        cmd.Parameters.AddWithValue("@index", index);
        using SQLiteDataReader reader = cmd.ExecuteReader();
        reader.Read();

        if (!reader.HasRows) {
            return null;
        }
        
        byte[] data = Convert.FromBase64String(reader.GetString(reader.GetOrdinal("data")));
        return Block.Deserialize(data);
    }
    
    public Block[] GetBlockRange(ulong start, ulong end) {
        if (start == end) {
            return [GetBlockByIndex(start)!];
        }
        
        using SQLiteCommand cmd = new("SELECT * FROM blocks ORDER BY ROWID LIMIT @end OFFSET @start;", _connection);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end - start);
        using SQLiteDataReader reader = cmd.ExecuteReader();
        
        List<Block> blocks = [];
        while (reader.Read()) {
            byte[] data = Convert.FromBase64String(reader.GetString(reader.GetOrdinal("data")));
            blocks.Add(Block.Deserialize(data));
        }
        
        return blocks.ToArray();
    }

    public ulong? GetBlockIndex(byte[] headerHash) {
        using SQLiteCommand cmd = new("SELECT ROWID FROM blocks WHERE hash = @hash;", _connection);
        cmd.Parameters.AddWithValue("@hash", Convert.ToBase64String(headerHash));
        using SQLiteDataReader reader = cmd.ExecuteReader();

        bool any = reader.Read();
        if (!any) {
            return null;
        }

        long height = reader.GetInt64(0);

        if (reader.Read()) {
            throw new Exception("Duplicate block found.");
        }
        
        return (ulong)height - 1;
    }

    public void RollbackChain(ulong index) {  // Delete everything above this index
        using SQLiteCommand cmd = new("DELETE FROM blocks WHERE ROWID > @target; DELETE FROM transactions WHERE ROWID > @target;", _connection);
        cmd.Parameters.AddWithValue("@target", index+1);
        cmd.ExecuteNonQuery();
    }

    public void InsertBlock(Block block) {
        using SQLiteCommand cmd = new("INSERT INTO blocks (hash, data) VALUES (@hash, @data);", _connection);
        cmd.Parameters.AddWithValue("@hash", Convert.ToBase64String(block.HashHeader()));
        cmd.Parameters.AddWithValue("@data", Convert.ToBase64String(block.Serialize()));
        cmd.ExecuteNonQuery();
    }
    
    public void InsertTransaction(Transaction transaction, ulong block) {
        using SQLiteCommand cmd = new("INSERT INTO transactions (sender_key, sender_address, recipient_address, amount, fee, signature, message, transaction_number, block_index) VALUES (@senderKey, @senderAddress, @recipientAddress, @amount, @fee, @sig, @msg, @tn, @block);", _connection);
        cmd.Parameters.AddWithValue("@senderKey", Convert.ToBase64String(transaction.Sender));
        cmd.Parameters.AddWithValue("@senderAddress", DemCoinUtils.PublicKeyToAddress(transaction.Sender));
        cmd.Parameters.AddWithValue("@recipientAddress", DemCoinUtils.AddressBytesToString(transaction.Recipient));
        cmd.Parameters.AddWithValue("@amount", transaction.Amount);
        cmd.Parameters.AddWithValue("@fee", transaction.TransactionFee);
        cmd.Parameters.AddWithValue("@sig", Convert.ToBase64String(transaction.Signature));
        cmd.Parameters.AddWithValue("@msg", Convert.ToBase64String(transaction.Message));
        cmd.Parameters.AddWithValue("@tn", transaction.TransactionNumber);
        cmd.Parameters.AddWithValue("@block", block);
        cmd.ExecuteNonQuery();
    }
    
    public double GetBalance(string address) {  // Add up all amounts where publickey is recipient and subtract where publickey is sender
        using SQLiteCommand cmd = new("SELECT * FROM transactions WHERE sender_address = @sender OR recipient_address = @sender;", _connection);
        cmd.Parameters.AddWithValue("@sender", address);
        using SQLiteDataReader reader = cmd.ExecuteReader();
        
        double balance = 0;
        while (reader.Read()) {
            double amount = reader.GetDouble(3);
            string sender = reader.GetString(1);
            string recipient = reader.GetString(2);
            if (sender == address) {
                balance -= amount + reader.GetDouble(4);
            }
            if (recipient == address) {
                balance += amount;
            }
        }
        
        return balance;
    }

    public ulong GetLastTransactionNumber(string address) {
        using SQLiteCommand cmd = new("SELECT MAX(transaction_number) FROM transactions WHERE sender_address = @sender;", _connection);
        cmd.Parameters.AddWithValue("@sender", address);
        object? o = cmd.ExecuteScalar();
        return o is DBNull ? 0 : Convert.ToUInt64(o);
    }
    
}