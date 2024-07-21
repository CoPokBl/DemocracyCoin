namespace CoinNode.Transport;

public interface IDataSender {
    void Send(byte[] data);
}