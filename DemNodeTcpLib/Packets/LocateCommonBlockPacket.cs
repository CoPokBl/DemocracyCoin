using DemCoinLib;
using DemCoinLib.Structs;

namespace DemNodeTcpLib.Packets;

public class LocateCommonBlockPacket : TcpNodePacket {
    public override byte PacketType => 8;

    public byte[][] BlockHashes;  // Height to block hash
    public ulong FromHeight;  // The height of the earliest block in BlockHashes
    public ulong ToHeight;    // The height of the latest block in BlockHashes

    public LocateCommonBlockPacket() { }  // For TcpNodePacket deserialization

    public LocateCommonBlockPacket(ulong from, ulong to, IBlockDatabase db) {
        FromHeight = from;
        ToHeight = to;
        Block[] blocks = db.GetBlockRange(from, to);
        BlockHashes = new byte[blocks.Length][];
        for (int i = 0; i < blocks.Length; i++) {
            BlockHashes[i] = blocks[i].HashHeader();
        }
    }

    public LocateCommonBlockPacket(IBlockDatabase db, ulong count) {
        ToHeight = db.GetBlockCount() - 1;
        FromHeight = ToHeight >= count ? ToHeight - count : 0;
        Block[] blocks = db.GetBlockRange(FromHeight, ToHeight);
        BlockHashes = new byte[blocks.Length][];
        for (int i = 0; i < blocks.Length; i++) {
            BlockHashes[i] = blocks[i].HashHeader();
        }
    }
    
    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(FromHeight);
        writer.Write(ToHeight);
        writer.Write(BlockHashes, (w, h) => w.Write(h));
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        FromHeight = reader.ReadUInt64();
        ToHeight = reader.ReadUInt64();
        BlockHashes = reader.ReadArray(r => r.Read(32));
    }
}