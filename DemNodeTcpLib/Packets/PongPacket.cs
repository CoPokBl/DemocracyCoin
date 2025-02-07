namespace DemNodeTcpLib.Packets;

/// <summary>
/// A packet to respond to a "PingPacket".
///
/// Should only be sent in response to a "PingPacket".
/// </summary>
public class PongPacket : TcpNodePacket {
    public override byte PacketType => 1;

    protected override byte[] GetData() {
        return [];
    }

    protected override void LoadData(byte[] data) {
        
    }
}