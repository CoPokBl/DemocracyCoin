using System.Net.Sockets;
using DemCoinLib;
using DemNodeTcpLib.Packets;

namespace DemNodeTcpLib;

public static class TcpDemNodeUtils {

    public static TcpDemNode StartTcpNode(this DemCoinNode node, string[]? peers = null, int port = 9534) {
        TcpDemNode tcp = new(node, peers, port);
        tcp.Init();
        return tcp;
    }
    
    public static TcpDemNode CreateTcpNode(this DemCoinNode node, string[]? peers = null, int port = 9534) {
        TcpDemNode tcp = new(node, peers, port);
        return tcp;
    }

    internal static Task SendPacket(this TcpClient client, TcpNodePacket packet) {
        return SendPacket(client.GetStream(), packet);
    }
    
    internal static async Task SendPacket(this NetworkStream client, TcpNodePacket packet) {
        try {
            byte[] buff = packet.Serialize();
            Console.Write($"Sending {buff.Length} bytes to peer... ");
            await client.WriteAsync(buff, 0, buff.Length);
            Console.WriteLine("Done.");
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }
    }
}