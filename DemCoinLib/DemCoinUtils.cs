using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using DemCoinLib.Db;
using Org.BouncyCastle.Crypto.Digests;
using SimpleBase;
using HashAlgorithm = NSec.Cryptography.HashAlgorithm;

namespace DemCoinLib;

public static class DemCoinUtils {
    /// <summary>
    /// Use to rent byte arrays to use less memory.
    /// </summary>
    private static readonly ArrayPool<byte> ByteArrayProvider = ArrayPool<byte>.Create(40, 5);

    /// <summary>
    /// Slower legacy nonce validation function. Don't use this for mining.
    /// </summary>
    /// <param name="difficulty">The difficulty requirements for the hash.</param>
    /// <param name="blockHeader">The current blocks proposed header minus the none.</param>
    /// <param name="nonce">The nonce to verify. (Random data)</param>
    /// <returns></returns>
    public static bool IsNonceValid(ulong difficulty, byte[] blockHeader, byte[] nonce) {
        byte[] data = ByteArrayProvider.Rent(blockHeader.Length + nonce.Length);  // should be len 40
        Debug.Assert(data.Length == blockHeader.Length + nonce.Length, "Array is correct size, is: " + blockHeader.Length + nonce.Length);
        Array.Copy(blockHeader, data, blockHeader.Length);
        Array.Copy(nonce, 0, data, blockHeader.Length, nonce.Length);
        byte[] hash = ByteArrayProvider.Rent(32);
        SHA256.HashData(data, hash);
        ByteArrayProvider.Return(data);
        bool valid = IsHashValidBlock(hash, difficulty);
        ByteArrayProvider.Return(hash);
        return valid;
    }

    /// <summary>
    /// NSEC HASHING.
    /// Check if a nonce is valid, use existing buffers for efficiency.
    /// 
    /// This is a highly efficient function for calling repeatedly.
    /// Just make sure all the buffers and SHA256 digest are reused.
    /// </summary>
    /// <param name="difficulty">The difficulty requirements for the hash.</param>
    /// <param name="blockHeader">The current blocks proposed header minus the none.</param>
    /// <param name="nonce">The nonce to verify. (Random data)</param>
    /// <param name="dataBuff">A reused byte buffer of length blockHeader.len + nonce.len.</param>
    /// <param name="hashBuff">A reused byte buffer of length 32.</param>
    /// <param name="sha256">The reused SHA256 BouncyCastle object.</param>
    /// <returns>Whether the nonce is valid.</returns>
    public static bool IsNonceValid(ulong difficulty, byte[] blockHeader, byte[] nonce, byte[] dataBuff, byte[] hashBuff) {  // TODO
        Array.Copy(blockHeader, dataBuff, blockHeader.Length);
        Array.Copy(nonce, 0, dataBuff, blockHeader.Length, nonce.Length);

        HashAlgorithm.Sha256.Hash(dataBuff, hashBuff);
        
        return IsHashValidBlock(hashBuff, difficulty);
    }

    /// <summary>
    /// Checks if the hash value is above difficulty.
    /// </summary>
    /// <param name="hash">The hash to check.</param>
    /// <param name="difficulty">The difficulty requirements for the hash.</param>
    /// <returns>Whether the hash is a valid block.</returns>
    private static bool IsHashValidBlock(ReadOnlySpan<byte> hash, ulong difficulty) {  // TODO
        return BitConverter.ToUInt64(hash) > difficulty;
    }

    public static void WriteBuffer(this byte[] buffer, params byte[]?[] values) {
        int cIndex = 0;
        foreach (byte[]? value in values) {
            if (value == null) continue;
            Buffer.BlockCopy(value, 0, buffer, cIndex, value.Length);
            cIndex += value.Length;
        }
    }

    public static string PublicKeyToAddress(byte[] key) {
        return AddressBytesToString(PublicKeyToAddressBytes(key));
    }

    public static byte[] PublicKeyToAddressBytes(byte[] key) {
        byte[] hashed = new byte[] { 0x00 }
            .Concat(ComputeRipeMd160(SHA256.HashData(key)))
            .ToArray();

        byte[] checksum = SHA256.HashData(hashed);
        byte[] final = hashed.Concat(checksum[..4]).ToArray();
        return final;
    }

    public static string AddressBytesToString(byte[] address) {
        return Base58.Bitcoin.Encode(address);
    }

    public static byte[] AddressStringToBytes(string address) {
        return Base58.Bitcoin.Decode(address);
    }

    public static bool IsAddressValid(string address) {
        byte[] decoded = Base58.Bitcoin.Decode(address);
        if (decoded[0] != 0x00) {
            return false;
        }

        byte[] checksum = decoded[^4..];
        byte[] withoutCheck = decoded[..^4];
        byte[] actualChecksum = SHA256.HashData(withoutCheck);
        return checksum.SequenceEqual(actualChecksum[..4]);
    }
    
    public static byte[] ComputeRipeMd160(byte[] data) {
        RipeMD160Digest digest = new();
        byte[] resBuf = new byte[digest.GetDigestSize()];

        digest.BlockUpdate(data, 0, data.Length);
        digest.DoFinal(resBuf, 0);

        return resBuf;
    }

    public static uint[] SplitBytes(byte[] bytes, uint length) {
        // Calculate the padding
        int padding = ((int)length - (bytes.Length * 8) % (int)length) % (int)length;

        // Add zeros to the end of the byte array if necessary (padding)
        if (padding != 0) {
            bytes = bytes.Concat(Enumerable.Repeat((byte)0, (padding + 7) / 8)).ToArray();
        }

        // Treat the byte array as a uint array now
        uint[] uintArray = new uint[bytes.Length * 8 / (int)length];

        for (int i = 0; i < uintArray.Length; i++) {
            uint uintValue = 0;

            for (int j = 0; j < length && (i * length + j) < bytes.Length * 8; j++) {
                int bitPosition = i * (int)length + j;
                int byteIndex = bitPosition / 8;
                int bitIndex = bitPosition % 8;
                uint bit = (uint)((bytes[byteIndex] >> bitIndex) & 1);
                uintValue |= bit << j;
            }

            uintArray[i] = uintValue;
        }

        return uintArray;
    }

    public static byte[] CombineBytes(uint[] uints, int bits, int originalLength) {
        byte[] bytes = new byte[originalLength];

        for (int i = 0; i < originalLength * 8 && i < uints.Length * bits; i++) {
            int uintIndex = i / bits;
            int bitIndex = i % bits;
            int byteIndex = i / 8;
            uint bit = (uints[uintIndex] >> bitIndex) & 1;
            bytes[byteIndex] |= (byte)(bit << (i % 8));
        }

        return bytes;
    }

    public static ulong GetUtcTimestamp() {
        return (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
    }
    
    public static byte[] ToInt256Bytes(BigInteger value) {
        byte[] bigIntegerBytes = value.ToByteArray(isUnsigned:true);

        if (bigIntegerBytes.Length > 32) {
            throw new Exception("Value is too large to fit in 256 bits.");
        }
        
        if (bigIntegerBytes.Length < 32) {
            byte[] temp = new byte[32];
            Array.Copy(bigIntegerBytes, 0, temp, 0, bigIntegerBytes.Length);
            bigIntegerBytes = temp;
        }

        return bigIntegerBytes;
    }

    public static byte[] Repeat(this byte b, int times) {
        byte[] val = new byte[times];
        for (int i = 0; i < val.Length; i++) {
            val[i] = b;
        }

        return val;
    }

    public static BigInteger Multiply(this BigInteger num, double val) {
        int bigVal = (int)val;
        double decVal = val - bigVal;

        BigInteger result = new(num.ToByteArray(true), true);

        result *= bigVal;
        result += (BigInteger)((double)num / (1.0 / decVal));

        return result;
    }

    public static CachedBlockDatabase EnableCache(this IBlockDatabase db) {
        return new CachedBlockDatabase(db);
    }
}