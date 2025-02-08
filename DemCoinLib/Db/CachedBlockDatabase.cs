using DemCoinLib.Structs;

namespace DemCoinLib.Db;

public class CachedBlockDatabase(IBlockDatabase db) : IBlockDatabase {
    public IBlockDatabase Database = db;

    // Caches
    private ulong? _blockCount;
    private Block? _lastBlock;

    private void ClearCache() {
        _blockCount = null;
        _lastBlock = null;
    }
    
    public ulong GetBlockCount() {
        _blockCount ??= Database.GetBlockCount();
        return _blockCount.Value;
    }

    public Block? GetLastBlock(ulong skip = 0) {
        if (skip == 0) {
            _lastBlock ??= Database.GetLastBlock();
            return _lastBlock;
        }

        return Database.GetLastBlock(skip);
    }

    public Block? GetBlockByIndex(ulong index) => Database.GetBlockByIndex(index);
    public Block[] GetBlockRange(ulong start, ulong end) => Database.GetBlockRange(start, end);
    public ulong? GetBlockIndex(byte[] headerHash) => Database.GetBlockIndex(headerHash);
    public double GetBalance(string address) => Database.GetBalance(address);
    public ulong GetLastTransactionNumber(string address) => Database.GetLastTransactionNumber(address);
    
    public void RollbackChain(ulong index) {
        ClearCache();
        Database.RollbackChain(index);
    }

    public void InsertBlock(Block block) {
        ClearCache();
        Database.InsertBlock(block);
    }

    public void InsertTransaction(Transaction transaction, ulong block) {
        Database.InsertTransaction(transaction, block);
    }
}