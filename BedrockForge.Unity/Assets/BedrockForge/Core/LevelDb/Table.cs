using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BedrockForge.IO;

namespace BedrockForge.LevelDb
{
    /// <summary>One entry of an SSTable or log, with its LevelDB internal-key metadata split out.</summary>
    internal readonly struct RawEntry
    {
        public readonly byte[] Key;
        public readonly ulong Sequence;
        public readonly bool Deleted;
        public readonly byte[]? Value;

        public RawEntry(byte[] key, ulong sequence, bool deleted, byte[]? value)
        {
            Key = key;
            Sequence = sequence;
            Deleted = deleted;
            Value = value;
        }
    }

    /// <summary>
    /// Reads a LevelDB SSTable (<c>.ldb</c>). Supports Mojang's compression ids:
    /// 0 = none, 2 = zlib, 4 = raw deflate. Snappy (1) is not used by Bedrock.
    /// </summary>
    internal static class Table
    {
        private const int FooterSize = 48;
        private const ulong Magic = 0xdb4775248b80fb57UL;

        public static IEnumerable<RawEntry> ReadAll(byte[] file)
        {
            if (file.Length < FooterSize)
                throw new FormatException("SSTable too small.");

            var footer = new ByteReader(file, file.Length - FooterSize, FooterSize);
            ReadHandle(footer); // metaindex (filters), unused
            var (indexOffset, indexSize) = ReadHandle(footer);

            var magic = new ByteReader(file, file.Length - 8, 8).U64();
            if (magic != Magic)
                throw new FormatException("Bad SSTable magic.");

            byte[] index = ReadBlock(file, indexOffset, indexSize);
            foreach (var (_, handle) in IterateBlock(index))
            {
                var hr = new ByteReader(handle);
                var (offset, size) = ReadHandle(hr);
                byte[] block = ReadBlock(file, offset, size);

                foreach (var (internalKey, value) in IterateBlock(block))
                    yield return SplitInternalKey(internalKey, value);
            }
        }

        private static RawEntry SplitInternalKey(byte[] internalKey, byte[] value)
        {
            int userLen = internalKey.Length - 8;
            if (userLen < 0)
                throw new FormatException("Internal key shorter than 8 bytes.");

            ulong tag = new ByteReader(internalKey, userLen, 8).U64();
            var userKey = new byte[userLen];
            Buffer.BlockCopy(internalKey, 0, userKey, 0, userLen);

            bool deleted = (tag & 0xFF) == 0;
            return new RawEntry(userKey, tag >> 8, deleted, deleted ? null : value);
        }

        private static (long offset, int size) ReadHandle(ByteReader r) => ((long)r.VarU64(), r.VarInt());

        private static byte[] ReadBlock(byte[] file, long offset, int size)
        {
            int start = checked((int)offset);
            byte compression = file[start + size]; // 1-byte type + 4-byte crc trailer follow the block

            switch (compression)
            {
                case 0:
                {
                    var raw = new byte[size];
                    Buffer.BlockCopy(file, start, raw, 0, size);
                    return raw;
                }
                case 2: // zlib: skip the 2-byte zlib header, then plain deflate
                    return Inflate(file, start + 2, size - 2);
                case 4: // raw deflate
                    return Inflate(file, start, size);
                default:
                    throw new NotSupportedException($"Unsupported SSTable block compression {compression}.");
            }
        }

        private static byte[] Inflate(byte[] data, int offset, int length)
        {
            using var input = new MemoryStream(data, offset, length, false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>Iterates a prefix-compressed block: entries followed by a restart array and its count.</summary>
        private static IEnumerable<(byte[] key, byte[] value)> IterateBlock(byte[] block)
        {
            int numRestarts = (int)new ByteReader(block, block.Length - 4, 4).U32();
            int entriesEnd = block.Length - 4 - 4 * numRestarts;

            var r = new ByteReader(block, 0, entriesEnd);
            byte[] lastKey = Array.Empty<byte>();

            while (!r.Eof)
            {
                int shared = r.VarInt();
                int nonShared = r.VarInt();
                int valueLen = r.VarInt();

                var key = new byte[shared + nonShared];
                Buffer.BlockCopy(lastKey, 0, key, 0, shared);
                byte[] delta = r.Bytes(nonShared);
                Buffer.BlockCopy(delta, 0, key, shared, nonShared);

                byte[] value = r.Bytes(valueLen);
                lastKey = key;
                yield return (key, value);
            }
        }
    }
}
