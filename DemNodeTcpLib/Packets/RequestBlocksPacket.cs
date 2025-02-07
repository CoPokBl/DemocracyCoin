using DemCoinLib;

namespace DemNodeTcpLib.Packets;

public class RequestBlocksPacket : TcpNodePacket {
    public override byte PacketType => 6;

    public ulong StartIndex;  // The first block to retrieve
    public ulong EndIndex; // The last block to retrieve

    public RequestBlocksPacket() { }  // For TcpNodePacket deserialization

    public RequestBlocksPacket(ulong start, ulong count) {
        StartIndex = start;
        EndIndex = start + count;
    }

    // Just one block
    public RequestBlocksPacket(ulong blockIndex) {
        StartIndex = blockIndex;
        EndIndex = blockIndex;
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(StartIndex);
        writer.Write(EndIndex);
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        StartIndex = reader.ReadUInt64();
        EndIndex = reader.ReadUInt64();
    }
}