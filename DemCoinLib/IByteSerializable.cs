namespace DemCoinLib;

public interface IByteSerializable {
    void Serialise(DataWriter writer);
    T Deserialize<T>(DataReader reader);
}