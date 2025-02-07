namespace DemNodeTcpLib.Packets;

/// <summary>
/// Base class for all packets sent between nodes.
///
/// Any inherited class must have a parameterless constructor.
/// </summary>
public abstract class TcpNodePacket {
    public abstract byte PacketType { get; }

    public static readonly Dictionary<byte, Type> Packets = new() {
        { 0, typeof(PingPacket) },
        { 1, typeof(PongPacket) },
        { 3, typeof(NewBlockPacket) },
        { 4, typeof(GetChainStatusPacket) },
        { 5, typeof(ProvideChainStatusPacket) },
        { 6, typeof(RequestBlocksPacket) },
        { 7, typeof(ProvideBlocksPacket) },
        { 8, typeof(LocateCommonBlockPacket) },
        { 9, typeof(ProvideCommonBlockPacket) }
    };

    public byte[] Serialize() {
        byte[] data = GetData();
        byte[] packet = new byte[data.Length + 1];
        packet[0] = PacketType;
        data.CopyTo(packet, 1);
        return packet;
    }
    
    public static TcpNodePacket Deserialize(byte[] data) {
        byte type = data[0];
        if (!Packets.TryGetValue(type, out Type? packetType)) {
            throw new ArgumentException("Unknown packet type");
        }

        TcpNodePacket packet = (TcpNodePacket) packetType.GetConstructor([])!.Invoke([]);
        packet.LoadData(data[1..]);
        return packet;
    }

    protected abstract byte[] GetData();
    protected abstract void LoadData(byte[] data);
}