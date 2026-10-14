using System;
using System.Text;

namespace BedrockForge.IO
{
    /// <summary>Little-endian cursor over a byte array, with LevelDB-style varints.</summary>
    public sealed class ByteReader
    {
        private readonly byte[] _data;
        private readonly int _end;

        public int Position { get; set; }

        public ByteReader(byte[] data, int offset = 0, int length = -1)
        {
            _data = data;
            Position = offset;
            _end = length < 0 ? data.Length : offset + length;
        }

        public bool Eof => Position >= _end;
        public int Remaining => _end - Position;

        private void Need(int n)
        {
            if (Position + n > _end)
                throw new FormatException($"Unexpected end of data (need {n} bytes at {Position}, end {_end}).");
        }

        public byte U8()
        {
            Need(1);
            return _data[Position++];
        }

        public sbyte S8() => unchecked((sbyte)U8());

        public ushort U16()
        {
            Need(2);
            ushort v = (ushort)(_data[Position] | _data[Position + 1] << 8);
            Position += 2;
            return v;
        }

        public short S16() => unchecked((short)U16());

        public uint U32()
        {
            Need(4);
            uint v = (uint)(_data[Position] | _data[Position + 1] << 8 | _data[Position + 2] << 16 | _data[Position + 3] << 24);
            Position += 4;
            return v;
        }

        public int S32() => unchecked((int)U32());

        public ulong U64()
        {
            ulong lo = U32();
            ulong hi = U32();
            return lo | hi << 32;
        }

        public long S64() => unchecked((long)U64());

        public float F32() => BitConverter.Int32BitsToSingle(S32());

        public double F64() => BitConverter.Int64BitsToDouble(S64());

        public int PeekS32()
        {
            int pos = Position;
            int v = S32();
            Position = pos;
            return v;
        }

        public ulong VarU64()
        {
            ulong result = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = U8();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new FormatException("Varint too long.");
        }

        public int VarInt() => checked((int)VarU64());

        public byte[] Bytes(int count)
        {
            Need(count);
            var result = new byte[count];
            Buffer.BlockCopy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        /// <summary>Varint length followed by that many bytes (LevelDB "length-prefixed slice").</summary>
        public byte[] Slice() => Bytes(VarInt());

        public string Utf8(int count)
        {
            Need(count);
            string s = Encoding.UTF8.GetString(_data, Position, count);
            Position += count;
            return s;
        }
    }
}
