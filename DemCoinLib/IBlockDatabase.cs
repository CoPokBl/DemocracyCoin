using DemCoinLib.Structs;

namespace DemCoinLib;

public interface IBlockDatabase {
    ulong GetBlockCount();
    Block? GetLastBlock(ulong skip = 0);
    Block? GetBlockByIndex(ulong index);
    Block[] GetBlockRange(ulong start, ulong end);
    ulong? GetBlockIndex(byte[] headerHash);
    void RollbackChain(ulong index);
    void InsertBlock(Block block);
    void InsertTransaction(Transaction transaction, ulong block);
    double GetBalance(string address);
    ulong GetLastTransactionNumber(string address);
}