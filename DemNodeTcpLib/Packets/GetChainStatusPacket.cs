using DemCoinLib;

namespace DemNodeTcpLib.Packets;

public class GetChainStatusPacket : TcpNodePacket {
    public override byte PacketType => 4;

    public uint Blocks;  // The amount of block headers to provide (From latest to oldest). Must be between 0 and 128 (incl,incl).

    public GetChainStatusPacket() { }  // For TcpNodePacket deserialization

    public GetChainStatusPacket(uint blocks) {
        Blocks = blocks;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(Blocks);
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        Blocks = reader.ReadUInt32();
    }
}