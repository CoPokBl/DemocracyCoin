using DemCoinLib;
using DemCoinLib.Structs;

namespace DemNodeTcpLib.Packets;

public class ProvideChainStatusPacket : TcpNodePacket {
    public override byte PacketType => 5;

    public ulong ChainHeight;
    public Block[] LastBlockHeaders;  // [0] is the most recent block header, and so on. ONLY CONTAINS HEADERS, NO TRANSACTIONS.

    public ProvideChainStatusPacket() { }  // For TcpNodePacket deserialization

    public ProvideChainStatusPacket(ulong chainHeight, Block[] blocks) {
        ChainHeight = chainHeight;
        LastBlockHeaders = blocks;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(ChainHeight);
        writer.Write(LastBlockHeaders, (w, b) => w.WriteLengthed(b.Serialize(false)));
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        ChainHeight = reader.ReadUInt64();
        LastBlockHeaders = reader.ReadArray<Block>(r => Block.Deserialize(r.ReadLengthed()));
    }
}