using DemCoinLib;
using DemCoinLib.Structs;

namespace DemNodeTcpLib.Packets;

public class ProvideCommonBlockPacket : TcpNodePacket {
    public override byte PacketType => 9;

    public ulong ChainHeight;
    public bool Found;  // Whether a common block was actually found
    public byte[] Block;  // The header hash of the common block (32 bytes)
    public Block[] SubsequentHeaders;  // The headers of the blocks that come after the specified common block, Sh[0] is the next block from Block

    public ProvideCommonBlockPacket() { }  // For TcpNodePacket deserialization

    public ProvideCommonBlockPacket(bool found, ulong chainHeight) {
        Found = found;
        Block = new byte[32];
        SubsequentHeaders = [];
        ChainHeight = chainHeight;
    }

    public ProvideCommonBlockPacket(byte[] block, ulong chainHeight, Block[] subsequentHeaders) {
        Found = true;
        Block = block;
        ChainHeight = chainHeight;
        SubsequentHeaders = subsequentHeaders;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(Found);
        writer.Write(Block);
        writer.Write(ChainHeight);
        writer.Write(SubsequentHeaders, (w, b) => w.WriteLengthed(b.Serialize(false)));
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        Found = reader.ReadBoolean();
        Block = reader.Read(32);
        ChainHeight = reader.ReadUInt64();
        SubsequentHeaders = reader.ReadArray(r => DemCoinLib.Structs.Block.Deserialize(r.ReadLengthed()));
    }
}