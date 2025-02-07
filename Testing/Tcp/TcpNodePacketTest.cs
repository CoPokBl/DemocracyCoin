using DemCoinLib;
using DemNodeTcpLib.Packets;

namespace Testing.Tcp;

public class TcpNodePacketTest {

    [Test]
    public void SerialiseAndDeserialise() {
        PingPacket ping = new();
        Assert.That(ping.PacketType, Is.EqualTo(TcpNodePacket.Deserialize(ping.Serialize()).PacketType));
        
        PongPacket pong = new();
        Assert.That(pong.PacketType, Is.EqualTo(TcpNodePacket.Deserialize(pong.Serialize()).PacketType));
        
        NewBlockPacket newBlock = new() {
            Height = 3,
            Block = DemCoinNode.GetDefBlock()
        };
        NewBlockPacket nbDe = (NewBlockPacket) TcpNodePacket.Deserialize(newBlock.Serialize());
        Assert.Multiple(() => {
            Assert.That(newBlock.PacketType, Is.EqualTo(nbDe.PacketType));
            Assert.That(newBlock.Serialize().SequenceEqual(nbDe.Serialize()));
        });

        GetChainStatusPacket getChainStatusPacket = new(10);
        Assert.That(getChainStatusPacket.Serialize().SequenceEqual(TcpNodePacket.Deserialize(getChainStatusPacket.Serialize()).Serialize()));
    }
}