namespace DemNodeTcpLib.Packets;

public class RequestPeersPacket : TcpNodePacket {
    public override byte PacketType => 10;

    protected override byte[] GetData() {
        return [];
    }

    protected override void LoadData(byte[] data) {
        
    }
}