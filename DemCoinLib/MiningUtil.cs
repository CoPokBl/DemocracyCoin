using DemCoinLib.Structs;
using NSec.Cryptography;

namespace DemCoinLib;

public class MiningUtil(DemCoinNode node, DemCoinWallet wallet, int nonceLength = 4) {
    public Block? Block;  // The block being mined
    public DemCoinNode Node = node;  // The node that is mining
    public DemCoinWallet Wallet = wallet;  // The wallet that is mining
    public int NonceLength = nonceLength;  // Length of nonce in bytes, changing requires RefreshBlock
    
    private byte[] _data = null!;  // The data being hashes, cached for speed
    private byte[] _hash = null!;  // A buffer for the hash to go
    private byte[] _targetValue = null!;

    public ulong TotalHashes { get; private set; } = 0;  // The total number of hashes done
    public ulong Attempts { get; private set; } = 0;  // The total number of attempts done on this block

    public void RefreshBlock() {
        Block = Node.GenerateNewBlock(Wallet);
        
        // Generate data
        Block.Nonce = new byte[NonceLength];
        _data = Block.Serialize(false);
        _hash = new byte[32];
        _targetValue = Node.GetCurrentTargetValue();
    }

    /// <summary>
    /// Check whether a nonce would result in a valid block.
    ///
    /// This is a highly optimised function that has NO SAFETY CHECKS.
    /// Make sure that the passed nonce has a length of NonceLength.
    ///
    /// Also, please make sure that RefreshBlock has been called at least once.
    /// </summary>
    /// <returns>Whether the nonce is valid.</returns>
    public bool CheckNonce(byte[] nonce) {
        Buffer.BlockCopy(nonce, 0, _data, 140, NonceLength);
        HashAlgorithm.Sha256.Hash(_data, _hash);
        return DemCoinNode.IsHashValidBlock(_hash, _targetValue);
    }
    
}