namespace DemNodeTcpLib.Packets;

/// <summary>
/// A packet to check if the connection is still alive.
///
/// Should elicit a "PongPacket" response.
/// </summary>
public class PingPacket : TcpNodePacket {
    public override byte PacketType => 0;

    protected override byte[] GetData() {
        return [];
    }

    protected override void LoadData(byte[] data) {
        
    }
}