namespace CoinNode.Transport;

public class MethodDataSender(Action<byte[]> action) : IDataSender {
    
    public void Send(byte[] data) {
        action.Invoke(data);
    }
}