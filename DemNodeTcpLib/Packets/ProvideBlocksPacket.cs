using DemCoinLib;
using DemCoinLib.Structs;

namespace DemNodeTcpLib.Packets;

public class ProvideBlocksPacket : TcpNodePacket {
    public override byte PacketType => 7;

    public ulong StartIndex;  // The first provided block's index
    public Block[] Blocks;  // Full blocks, including transactions

    public ProvideBlocksPacket() { }  // For TcpNodePacket deserialization

    public ProvideBlocksPacket(ulong start, Block[] blocks) {
        StartIndex = start;
        Blocks = blocks;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(StartIndex);
        writer.Write(Blocks, (w, b) => w.WriteLengthed(b.Serialize()));
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        StartIndex = reader.ReadUInt64();
        Blocks = reader.ReadArray(r => Block.Deserialize(r.ReadLengthed()));
    }
}