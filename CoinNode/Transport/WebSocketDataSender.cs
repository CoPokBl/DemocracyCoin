using WebSocketSharp;

namespace CoinNode.Transport;

public class WebSocketDataSender(WebSocket socket) : IDataSender {
    
    public void Send(byte[] data) {
        socket.Send(data);
    }
}