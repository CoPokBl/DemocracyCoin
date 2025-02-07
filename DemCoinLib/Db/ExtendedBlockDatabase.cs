using DemCoinLib.Structs;

namespace DemCoinLib.Db;

/// <summary>
/// A dummy database to perform validation on a chain without modifying the main chain.
///
/// Trim amount is the amount to skip back in the baseDb (the amount of blocks to just ignore)
/// </summary>
/// <param name="baseDb">The database to extend.</param>
public class ExtendedBlockDatabase(IBlockDatabase baseDb, ulong trimAmount = 0) : IBlockDatabase {
    public readonly List<Block> ExtraBlocks = [];

    private ulong GetBaseBlockCount() {
        return baseDb.GetBlockCount() - trimAmount;
    }
    
    public ulong GetBlockCount() {
        return GetBaseBlockCount() + (ulong)ExtraBlocks.Count;
    }

    public Block GetLastBlock(ulong skip = 0) {
        if ((ulong)ExtraBlocks.Count <= skip) {
            return baseDb.GetLastBlock(skip + (ulong)ExtraBlocks.Count + trimAmount)!;
        }

        return ExtraBlocks[ExtraBlocks.Count - 1 - (int)skip];
    }

    public Block? GetBlockByIndex(ulong index) {
        if (index < GetBaseBlockCount()) {
            return baseDb.GetBlockByIndex(index);
        }

        return ExtraBlocks[ExtraBlocks.Count - 1 - (int)(index - GetBaseBlockCount())];
    }

    public Block[] GetBlockRange(ulong start, ulong end) {
        throw new NotImplementedException();
    }

    public ulong? GetBlockIndex(byte[] headerHash) {
        throw new NotImplementedException();
    }

    public void RollbackChain(ulong index) {
        throw new NotImplementedException();
    }

    public void InsertBlock(Block block) {
        ExtraBlocks.Add(block);
    }

    public void InsertTransaction(Transaction transaction, ulong block) {
        throw new NotImplementedException();
    }

    public double GetBalance(string address) {
        throw new NotImplementedException();
    }

    public ulong GetLastTransactionNumber(string address) {
        throw new NotImplementedException();
    }
}