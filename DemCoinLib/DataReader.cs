namespace DemCoinLib;

public class DataReader(byte[] data) {
    internal int Pos;

    public byte[] Read(uint bytes) => Read((int)bytes);
    
    public byte[] Read(int bytes) {
        if (Pos + bytes > data.Length) {
            throw new Exception("Reached the end of the data.");
        }

        byte[] newData = data[Pos..(Pos + bytes)];
        Pos += bytes;
        return newData;
    }

    /// <summary>
    /// Reads a uint for length and then reads that many bytes.
    /// </summary>
    /// <returns></returns>
    public byte[] ReadLengthed() => Read(ReadUInt32());

    public byte[] ReadRemaining() {
        return data[Pos..];
    }

    public int ReadInt32() {
        return BitConverter.ToInt32(Read(4));
    }

    public uint ReadUInt32() {
        return BitConverter.ToUInt32(Read(4));
    }

    public ulong ReadUInt64() {
        return BitConverter.ToUInt64(Read(8));
    }

    public double ReadDouble() {
        return BitConverter.ToDouble(Read(8));
    }

    public bool ReadBoolean() {
        return BitConverter.ToBoolean(Read(1));
    }

    public T[] ReadArray<T>(Func<DataReader, T> elementReader) {
        uint length = ReadUInt32();
        List<T> items = [];
        for (int i = 0; i < length; i++) {
            items.Add(elementReader.Invoke(this));
        }
        return items.ToArray();
    }
}