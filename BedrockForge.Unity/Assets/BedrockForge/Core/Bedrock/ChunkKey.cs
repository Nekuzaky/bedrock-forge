using System;
using BedrockForge.IO;

namespace BedrockForge.Bedrock
{
    public enum Dimension
    {
        Overworld = 0,
        Nether = 1,
        End = 2,
    }

    /// <summary>The record type byte at the end of a chunk key.</summary>
    public enum ChunkTag : byte
    {
        Data3D = 43,
        Version = 44,
        Data2D = 45,
        Data2DLegacy = 46,
        SubChunkPrefix = 47,
        LegacyTerrain = 48,
        BlockEntity = 49,
        Entity = 50,
        PendingTicks = 51,
        LegacyBlockExtraData = 52,
        BiomeState = 53,
        FinalizedState = 54,
        ConversionData = 55,
        BorderBlocks = 56,
        HardcodedSpawners = 57,
        RandomTicks = 58,
        Checksums = 59,
        GenerationSeed = 60,
        GeneratedPreCavesAndCliffsBlending = 61,
        BlendingBiomeHeight = 62,
        MetaDataHash = 63,
        BlendingData = 64,
        ActorDigestVersion = 65,
        LegacyVersion = 118,
        /// <summary>Seen in 1.26 worlds, purpose not documented yet.</summary>
        Unknown119 = 119,
    }

    /// <summary>
    /// A chunk-scoped LevelDB key: <c>x:int32 z:int32 [dimension:int32] tag:byte [subchunkY:sbyte]</c>.
    /// The dimension is omitted for the Overworld; the sub-chunk index only exists for <see cref="ChunkTag.SubChunkPrefix"/>.
    /// </summary>
    public readonly struct ChunkKey : IEquatable<ChunkKey>
    {
        public readonly int X;
        public readonly int Z;
        public readonly Dimension Dimension;
        public readonly ChunkTag Tag;
        public readonly sbyte SubChunkY;

        public ChunkKey(int x, int z, Dimension dimension, ChunkTag tag, sbyte subChunkY = 0)
        {
            X = x;
            Z = z;
            Dimension = dimension;
            Tag = tag;
            SubChunkY = subChunkY;
        }

        public static bool TryParse(byte[] key, out ChunkKey result)
        {
            result = default;
            bool hasDim;
            bool hasSub;
            switch (key.Length)
            {
                case 9: hasDim = false; hasSub = false; break;
                case 10: hasDim = false; hasSub = true; break;
                case 13: hasDim = true; hasSub = false; break;
                case 14: hasDim = true; hasSub = true; break;
                default: return false;
            }

            var r = new ByteReader(key);
            int x = r.S32();
            int z = r.S32();
            int dim = hasDim ? r.S32() : 0;
            byte tag = r.U8();

            if (dim < 0 || dim > 2 || !IsKnownTag(tag))
                return false;
            if (hasSub != (tag == (byte)ChunkTag.SubChunkPrefix))
                return false;

            sbyte sub = hasSub ? r.S8() : (sbyte)0;
            result = new ChunkKey(x, z, (Dimension)dim, (ChunkTag)tag, sub);
            return true;
        }

        private static bool IsKnownTag(byte tag) => (tag >= 43 && tag <= 65) || tag == 118 || tag == 119;

        public byte[] ToBytes()
        {
            bool hasDim = Dimension != Dimension.Overworld;
            bool hasSub = Tag == ChunkTag.SubChunkPrefix;
            var bytes = new byte[9 + (hasDim ? 4 : 0) + (hasSub ? 1 : 0)];

            int p = 0;
            WriteInt(bytes, ref p, X);
            WriteInt(bytes, ref p, Z);
            if (hasDim) WriteInt(bytes, ref p, (int)Dimension);
            bytes[p++] = (byte)Tag;
            if (hasSub) bytes[p] = unchecked((byte)SubChunkY);
            return bytes;
        }

        private static void WriteInt(byte[] b, ref int p, int v)
        {
            b[p++] = (byte)v;
            b[p++] = (byte)(v >> 8);
            b[p++] = (byte)(v >> 16);
            b[p++] = (byte)(v >> 24);
        }

        public bool Equals(ChunkKey o) => X == o.X && Z == o.Z && Dimension == o.Dimension && Tag == o.Tag && SubChunkY == o.SubChunkY;
        public override bool Equals(object? obj) => obj is ChunkKey o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Z, Dimension, Tag, SubChunkY);

        public override string ToString() =>
            Tag == ChunkTag.SubChunkPrefix ? $"{Dimension} ({X}, {Z}) {Tag}[{SubChunkY}]" : $"{Dimension} ({X}, {Z}) {Tag}";
    }

    public readonly struct ChunkPos : IEquatable<ChunkPos>
    {
        public readonly int X;
        public readonly int Z;
        public readonly Dimension Dimension;

        public ChunkPos(int x, int z, Dimension dimension)
        {
            X = x;
            Z = z;
            Dimension = dimension;
        }

        public bool Equals(ChunkPos o) => X == o.X && Z == o.Z && Dimension == o.Dimension;
        public override bool Equals(object? obj) => obj is ChunkPos o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Z, Dimension);
        public override string ToString() => $"{Dimension} ({X}, {Z})";
    }
}
