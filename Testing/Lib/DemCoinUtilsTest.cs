using System.Numerics;
using DemCoinLib;

namespace Testing.Lib;

public class DemCoinUtilsTest {
    
    [SetUp]
    public void Setup() {
        
    }
    
    private void SplitAndCombineBytes(int length) {
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);

        uint[] split = DemCoinUtils.SplitBytes(bytes, 11);
        byte[] combine = DemCoinUtils.CombineBytes(split, 11, length);
        
        Assert.That(bytes.SequenceEqual(combine), Is.True);
    }

    [Test]
    public void SplitAndCombineBytes() {
        for (int i = 8; i < 33; i++) {
            SplitAndCombineBytes(i);
        }
    }
    
    [Test]
    public void Int256ToBytes() {
        BigInteger value = new(2);
        byte[] bytes = DemCoinUtils.ToInt256Bytes(value);
        BigInteger value2 = new(bytes);
        
        Assert.That(value2, Is.EqualTo(value));
    }

    [Test]
    public void BigIntMult() {
        for (int i = 0; i < 100; i++) {
            int max = (int)Math.Sqrt(int.MaxValue);
            int a = Random.Shared.Next(max);
            double b = Random.Shared.NextDouble() * max;

            BigInteger ba = a;
            BigInteger result = ba.Multiply(b);
            int expected = (int)(a * b);
            
            Assert.That(result, Is.EqualTo((BigInteger)expected));
        }
    }
}