using System.Net;

namespace CoinNode;

public class PendingSendMessage {
    public byte[] Message { get; init; }
    public IPEndPoint Peer { get; init; }
    public string Id { get; init; }
    public bool Safe { get; init; } = true;
}