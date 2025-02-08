// using System.Runtime.CompilerServices;
// using System.Runtime.InteropServices;
//
// namespace DemNodeTcpLib;
//
// using System;
// using System.Buffers.Binary;
// using System.Security.Cryptography;
//
// public sealed unsafe class MiningSha256 : IDisposable {
//     // Initial hash values (first 32 bits of fractional parts of square roots of first 8 primes)
//     private static readonly uint[] InitialHash = [
//         0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
//         0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
//     ];
//
//     // Round constants (first 32 bits of fractional parts of cube roots of first 64 primes)
//     private static readonly uint[] K = [
//         0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
//         0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
//         0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
//         0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
//         0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
//         0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
//         0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
//         0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
//     ];
//
//     private readonly uint* _state;      // Current hash state (8 uint)
//     private readonly uint* _w;          // Message schedule (64 uint)
//     private readonly byte* _buffer;     // Input buffer (64 bytes)
//     private readonly uint* _count;      // Bit count (2 uint: low, high)
//     private bool _disposed;
//
//     public MiningSha256() {
//         _state = (uint*)NativeMemory.Alloc(8, sizeof(uint));
//         _w = (uint*)NativeMemory.Alloc(64, sizeof(uint));
//         _buffer = (byte*)NativeMemory.Alloc(64, sizeof(byte));
//         _count = (uint*)NativeMemory.Alloc(2, sizeof(uint));
//         
//         Reset();
//     }
//
//     public void Reset() {
//         Buffer.MemoryCopy(InitialHash.ToArray().AsSpan().ToArray(), _state, 8 * sizeof(uint), 8 * sizeof(uint));
//         *_count = 0;
//         *(_count + 1) = 0;
//         NativeMemory.Clear(_buffer, 64);
//         NativeMemory.Clear(_w, 64 * sizeof(uint));
//     }
//
//     public void Update(ReadOnlySpan<byte> input) {
//         if (_disposed) throw new ObjectDisposedException(nameof(MiningSha256));
//         
//         uint bitCount = (uint)input.Length << 3;
//         uint index = (*_count >> 3) & 0x3F;
//         
//         // Update number of bits
//         *_count += bitCount;
//         if (*_count < bitCount) *(_count + 1) += 1;
//
//         // Process whole blocks
//         if (input.Length >= 64 - index) {
//             input[..(64 - (int)index)].CopyTo(new Span<byte>(_buffer + index, 64 - (int)index));
//             Transform(_buffer, _state, _w);
//             index = 0;
//             input = input[(64 - (int)index)..];
//             
//             while (input.Length >= 64) {
//                 fixed (byte* ptr = input) {
//                     Transform(ptr, _state, _w);
//                 }
//                 input = input[64..];
//             }
//         }
//
//         // Buffer remaining data
//         input.CopyTo(new Span<byte>(_buffer + index, input.Length));
//     }
//
//     public void GetHash(Span<byte> output) {
//         if (_disposed) throw new ObjectDisposedException(nameof(MiningSha256));
//         if (output.Length < 32) throw new ArgumentException("Output buffer too small", nameof(output));
//
//         // Save current state
//         uint* savedState = (uint*)NativeMemory.Alloc(8, sizeof(uint));
//         Buffer.MemoryCopy(_state, savedState, 8 * sizeof(uint), 8 * sizeof(uint));
//
//         // Finalize hash
//         FinalizeHash();
//
//         // Copy result to output buffer
//         for (int i = 0; i < 8; i++) {
//             BinaryPrimitives.WriteUInt32BigEndian(output[(i * 4)..], _state[i]);
//         }
//
//         // Restore state for continued use
//         Buffer.MemoryCopy(savedState, _state, 8 * sizeof(uint), 8 * sizeof(uint));
//         NativeMemory.Free(savedState);
//     }
//
//     private void FinalizeHash() {
//         byte* padding = stackalloc byte[64];
//         padding[0] = 0x80;
//
//         uint index = (*_count >> 3) & 0x3F;
//         uint padLen = (index < 56) ? (56 - index) : (120 - index);
//         
//         Update(new Span<byte>(padding, (int)padLen));
//         
//         // Append total bit count
//         byte* bits = stackalloc byte[8];
//         BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(bits, 4), *(_count + 1));
//         BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(bits + 4, 4), *_count);
//         Update(new Span<byte>(bits, 8));
//     }
//
//     private static void Transform(byte* chunk, uint* state, uint* w) {
//         // Message schedule
//         for (int i = 0; i < 16; i++) {
//             w[i] = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(chunk + (i * 4), 4));
//         }
//         
//         for (int i = 16; i < 64; i++) {
//             uint s0 = RightRotate(w[i - 15], 7) ^ RightRotate(w[i - 15], 18) ^ (w[i - 15] >> 3);
//             uint s1 = RightRotate(w[i - 2], 17) ^ RightRotate(w[i - 2], 19) ^ (w[i - 2] >> 10);
//             w[i] = w[i - 16] + s0 + w[i - 7] + s1;
//         }
//
//         uint a = state[0], b = state[1], c = state[2], d = state[3];
//         uint e = state[4], f = state[5], g = state[6], h = state[7];
//
//         // Compression function
//         for (int i = 0; i < 64; i++) {
//             uint S1 = RightRotate(e, 6) ^ RightRotate(e, 11) ^ RightRotate(e, 25);
//             uint ch = (e & f) ^ (~e & g);
//             uint temp1 = h + S1 + ch + K[i] + w[i];
//             uint S0 = RightRotate(a, 2) ^ RightRotate(a, 13) ^ RightRotate(a, 22);
//             uint maj = (a & b) ^ (a & c) ^ (b & c);
//             uint temp2 = S0 + maj;
//
//             h = g;
//             g = f;
//             f = e;
//             e = d + temp1;
//             d = c;
//             c = b;
//             b = a;
//             a = temp1 + temp2;
//         }
//
//         // Update state
//         state[0] += a;
//         state[1] += b;
//         state[2] += c;
//         state[3] += d;
//         state[4] += e;
//         state[5] += f;
//         state[6] += g;
//         state[7] += h;
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private static uint RightRotate(uint value, int bits) {
//         return (value >> bits) | (value << (32 - bits));
//     }
//
//     public void Dispose() {
//         if (_disposed) return;
//         
//         NativeMemory.Free(_state);
//         NativeMemory.Free(_w);
//         NativeMemory.Free(_buffer);
//         NativeMemory.Free(_count);
//         
//         _disposed = true;
//     }
// }