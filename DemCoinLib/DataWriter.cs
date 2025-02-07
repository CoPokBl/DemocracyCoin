namespace DemCoinLib;

public class DataWriter {
    private readonly List<byte> _data = [];

    public byte[] ToArray() => _data.ToArray();

    public DataWriter Write(byte[] value) {
        _data.AddRange(value);
        return this;
    }

    public DataWriter Write(byte value) {
        _data.Add(value);
        return this;
    }

    public DataWriter WriteLengthed(byte[] value) {
        Write((uint)value.Length);
        Write(value);
        return this;
    }

    public DataWriter Write(int value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write(uint value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write(double value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write(ulong value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write(long value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write(bool value) {
        Write(BitConverter.GetBytes(value));
        return this;
    }

    public DataWriter Write<T>(T[] arr, Action<DataWriter, T> elementSerialiser) {
        Write((uint)arr.Length);
        foreach (T element in arr) {
            elementSerialiser.Invoke(this, element);
        }
        return this;
    }
}