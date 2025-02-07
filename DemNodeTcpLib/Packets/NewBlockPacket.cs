using DemCoinLib;
using DemCoinLib.Structs;

namespace DemNodeTcpLib.Packets;

public class NewBlockPacket : TcpNodePacket {
    public override byte PacketType => 3;

    public ulong Height;
    public Block Block = null!;  // Block without data, just headers

    /// <summary>
    /// A setting to force this class to never calculate transaction sigs.
    /// </summary>
    public bool DisableTsCalc = false;

    public NewBlockPacket() { }  // For TcpNodePacket deserialization

    public NewBlockPacket(ulong height, Block block) {
        Height = height;
        Block = block;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(Height);
        writer.WriteLengthed(Block.Serialize(false, !DisableTsCalc));
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        Height = reader.ReadUInt64();
        Block = Block.Deserialize(reader.ReadLengthed());
    }
}