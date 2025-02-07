using DemCoinLib;

namespace Testing.Lib;

public class DataReaderWriterTest {

    [Test]
    public void ReadWriteArray() {
        int[] arr = [
            0,1,2,3,4,5,6,7,8,9
        ];

        DataWriter writer = new();
        writer.Write(arr, (w, num) => w.Write(num));
        byte[] data = writer.ToArray();

        DataReader reader = new(data);
        int[] newArr = reader.ReadArray(r => r.ReadInt32());
        
        Assert.That(newArr.SequenceEqual(arr));
    }
}