using System.Net;
using DemCoinLib;

namespace DemNodeTcpLib.Packets;

public class ProvidePeersPacket : TcpNodePacket {
    public override byte PacketType => 11;

    public IPEndPoint[] Peers;

    public ProvidePeersPacket() { }
    
    public ProvidePeersPacket(string[] peerIps) {
        Peers = new IPEndPoint[peerIps.Length];
        for (int i = 0; i < peerIps.Length; i++) {
            Peers[i] = new IPEndPoint(IPAddress.Parse(peerIps[i]), TcpDemNode.DefaultPort);
        }
    }

    protected override byte[] GetData() {
        DataWriter writer = new();
        writer.Write(Peers, (w, peer) => {
            w.WriteLengthed(peer.Address.GetAddressBytes());
            w.Write(peer.Port);
        });
        return writer.ToArray();
    }

    protected override void LoadData(byte[] data) {
        DataReader reader = new(data);
        Peers = reader.ReadArray(r => {
            byte[] ipBytes = r.ReadLengthed();
            IPAddress ip = new IPAddress(ipBytes);
            int port = r.ReadInt32();
            return new IPEndPoint(ip, port);
        });
    }
}