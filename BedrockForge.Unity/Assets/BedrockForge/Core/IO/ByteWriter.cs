using System;
using System.IO;
using System.Text;

namespace BedrockForge.IO
{
    /// <summary>Little-endian writer with LevelDB-style varints, the counterpart of <see cref="ByteReader"/>.</summary>
    public sealed class ByteWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public int Length => (int)_stream.Length;

        public void U8(byte v) => _stream.WriteByte(v);

        public void S8(sbyte v) => _stream.WriteByte(unchecked((byte)v));

        public void U16(ushort v)
        {
            _stream.WriteByte((byte)v);
            _stream.WriteByte((byte)(v >> 8));
        }

        public void S16(short v) => U16(unchecked((ushort)v));

        public void U32(uint v)
        {
            _stream.WriteByte((byte)v);
            _stream.WriteByte((byte)(v >> 8));
            _stream.WriteByte((byte)(v >> 16));
            _stream.WriteByte((byte)(v >> 24));
        }

        public void S32(int v) => U32(unchecked((uint)v));

        public void U64(ulong v)
        {
            U32((uint)v);
            U32((uint)(v >> 32));
        }

        public void S64(long v) => U64(unchecked((ulong)v));

        public void F32(float v) => S32(BitConverter.SingleToInt32Bits(v));

        public void F64(double v) => S64(BitConverter.DoubleToInt64Bits(v));

        public void VarU64(ulong v)
        {
            while (v >= 0x80)
            {
                _stream.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            _stream.WriteByte((byte)v);
        }

        public void Bytes(byte[] data) => _stream.Write(data, 0, data.Length);

        public void Bytes(byte[] data, int offset, int count) => _stream.Write(data, offset, count);

        /// <summary>Varint length followed by the bytes (LevelDB "length-prefixed slice").</summary>
        public void Slice(byte[] data)
        {
            VarU64((ulong)data.Length);
            Bytes(data);
        }

        public void Utf8(string s) => Bytes(Encoding.UTF8.GetBytes(s));

        public byte[] ToArray() => _stream.ToArray();
    }
}
