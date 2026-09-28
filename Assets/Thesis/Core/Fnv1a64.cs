using System;

namespace Thesis.Core
{
    // FNV-1a, 64-bit. Used for state hashes (replay divergence checks) and for
    // deriving counter-based seeds (the shop). Not cryptographic; it doesn't need to be.
    //
    // Usage: a *local* variable, mutated in place.
    //     var h = new Fnv1a64();      // default is already a valid empty hash
    //     h.Add(tick); h.Add(budget);
    //     ulong value = h.Value;
    // Do not store it in a readonly field or read it through a property: both hand
    // back a copy, and the Add calls silently land on that copy instead.
    //
    // Why the XOR trick below: C# 9 structs cannot have a parameterless constructor,
    // so `new Fnv1a64()` would start at 0 instead of the FNV offset basis and produce
    // a wrong hash that still *looks* random. Storing (hash XOR basis) makes the
    // default zero-initialised struct mean "empty hash", so there is no wrong way to
    // create one.
    public struct Fnv1a64
    {
        public const ulong OffsetBasis = 14695981039346656037UL; // 0xcbf29ce484222325
        public const ulong Prime = 1099511628211UL;              // 0x100000001b3

        private ulong stored; // hash ^ OffsetBasis

        public ulong Value => stored ^ OffsetBasis;

        public void AddByte(byte b)
        {
            ulong h = stored ^ OffsetBasis;
            h ^= b;
            h = unchecked(h * Prime);
            stored = h ^ OffsetBasis;
        }

        // Multi-byte values are fed little-endian explicitly (not via
        // BitConverter.GetBytes) so the hash is the same on every platform and
        // allocates nothing.
        // Byte-for-byte identical to four AddByte calls (MultiByteValuesAreLittleEndian
        // pins that), but converts out of and back into the stored form once instead
        // of four times. Simulation hashes its whole state every tick in the
        // determinism tests, so this is the hot path.
        public void Add(uint value)
        {
            ulong h = stored ^ OffsetBasis;
            h = unchecked((h ^ (byte)value) * Prime);
            h = unchecked((h ^ (byte)(value >> 8)) * Prime);
            h = unchecked((h ^ (byte)(value >> 16)) * Prime);
            h = unchecked((h ^ (byte)(value >> 24)) * Prime);
            stored = h ^ OffsetBasis;
        }

        public void Add(int value) { Add(unchecked((uint)value)); }

        public void Add(ulong value)
        {
            Add((uint)value);
            Add((uint)(value >> 32));
        }

        public void Add(long value) { Add(unchecked((ulong)value)); }

        public void Add(bool value) { AddByte(value ? (byte)1 : (byte)0); }

        // Bit pattern, not value: 0.0f and -0.0f hash differently, and so do two NaNs
        // with different payloads. That is intended - a state hash must notice any
        // bit that differs, because any bit can grow into a divergence.
        public void Add(float value) { Add(BitConverter.SingleToInt32Bits(value)); }

        public void Add(double value) { Add(BitConverter.DoubleToInt64Bits(value)); }

        // Length-prefixed UTF-16 code units, so ("ab","c") and ("a","bc") differ.
        public void Add(string value)
        {
            if (value == null)
            {
                Add(-1);
                return;
            }
            Add(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                AddByte((byte)c);
                AddByte((byte)(c >> 8));
            }
        }
    }
}
