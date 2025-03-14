using System.Diagnostics;
using DemCoinLib.Structs;

namespace DemCoinLib;

public class GenericMiner(DemCoinNode node, DemCoinWallet wallet) {
    public event Action<Block> MinedBlock;
    public ulong CheckedNonces;

    public void MineSync(CancellationToken? token = null) {
        token ??= CancellationToken.None;
        MinerThread(token.Value);
    }
    
    public void MineAsync(CancellationToken token, int threads = 1) {
        for (int i = 0; i < threads; i++) {
            Thread thread = new(() => MinerThread(token));
            thread.Start();
        }
    }
    
    private void MinerThread(CancellationToken cancelToken) {
        Random random = new();
        MiningUtil mining = new(node, wallet);
        Stopwatch stopwatch = Stopwatch.StartNew();
        
        mining.RefreshBlock();
        byte[] nonce = new byte[4];
        ulong threadNonces = 0;
        while (!cancelToken.IsCancellationRequested) {
            random.NextBytes(nonce);  // This is extremely fast. It can do about 100mil in 1sec.
            CheckedNonces++;
            threadNonces++;
            if (mining.CheckNonce(nonce)) {
                mining.Block!.Nonce = nonce;
                MinedBlock.Invoke(mining.Block);  // We mined a block
                mining.RefreshBlock();
            }

            if (threadNonces % 100_000 == 0 && stopwatch.ElapsedMilliseconds > 30000) {
                mining.RefreshBlock();
            }
        }
    }
}