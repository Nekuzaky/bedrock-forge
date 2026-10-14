using System;
using System.Collections.Generic;
using System.Linq;
using BedrockForge.IO;
using BedrockForge.Nbt;

namespace BedrockForge.Bedrock
{
    /// <summary>A block palette entry: <c>name</c> + <c>states</c> compound, as stored on disk.</summary>
    public sealed class BlockState
    {
        public string Name { get; }
        public NbtCompound States { get; }
        public int Version { get; }

        public BlockState(string name, NbtCompound states, int version)
        {
            Name = name;
            States = states;
            Version = version;
        }

        public static BlockState FromNbt(NbtCompound nbt) =>
            new BlockState(
                nbt.GetString("name") ?? "minecraft:unknown",
                nbt.Get<NbtCompound>("states") ?? new NbtCompound(),
                nbt.GetInt("version") ?? 0);

        public bool IsAir => Name == "minecraft:air";

        private string? _key;

        public NbtCompound ToNbt() => new NbtCompound
        {
            ["name"] = Name,
            ["states"] = States.Clone(),
            ["version"] = Version,
        };

        /// <summary>Two states are equal when name and states match (the data version is ignored).</summary>
        public override bool Equals(object? obj) => obj is BlockState other && ToString() == other.ToString();

        public override int GetHashCode() => ToString().GetHashCode();

        public override string ToString()
        {
            if (_key != null)
                return _key;
            if (States.Count == 0)
                return _key = Name;
            var states = States.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={NbtFormatter.ToSnbt(kv.Value, false).Trim('"')}");
            return _key = $"{Name}[{string.Join(",", states)}]";
        }
    }

    /// <summary>One paletted 16×16×16 layer. Layer 0 is the block, layer 1 is usually water (waterlogging).</summary>
    public sealed class BlockStorage
    {
        public const int Volume = 4096;

        /// <summary>Palette index per block, in Bedrock's XZY order: <c>(x &lt;&lt; 8) | (z &lt;&lt; 4) | y</c>.</summary>
        public ushort[] Indices { get; }
        public List<BlockState> Palette { get; }
        public int BitsPerBlock { get; }

        public BlockStorage(ushort[] indices, List<BlockState> palette, int bitsPerBlock)
        {
            Indices = indices;
            Palette = palette;
            BitsPerBlock = bitsPerBlock;
        }

        public static int IndexOf(int x, int y, int z) => (x << 8) | (z << 4) | y;

        public BlockState Get(int x, int y, int z) => Palette[Indices[IndexOf(x, y, z)]];

        public static BlockStorage Filled(BlockState state) => new BlockStorage(new ushort[Volume], new List<BlockState> { state }, 0);

        public void Set(int x, int y, int z, BlockState state)
        {
            int index = Palette.IndexOf(state);
            if (index < 0)
            {
                index = Palette.Count;
                Palette.Add(state);
            }
            Indices[IndexOf(x, y, z)] = (ushort)index;
        }

        /// <summary>Persistent (on-disk) encoding. Unused palette entries are dropped.</summary>
        internal void Write(ByteWriter w)
        {
            // Compact the palette to the states actually used, keeping their first-seen order.
            var remap = new int[Palette.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var palette = new List<BlockState>();
            var indices = new ushort[Volume];
            for (int i = 0; i < Volume; i++)
            {
                int old = Indices[i];
                if (remap[old] < 0)
                {
                    remap[old] = palette.Count;
                    palette.Add(Palette[old]);
                }
                indices[i] = (ushort)remap[old];
            }

            // Smallest of the sizes Bedrock accepts. Always at least 1 bit: unambiguous for every game version.
            int bits = 1;
            foreach (int candidate in new[] { 1, 2, 3, 4, 5, 6, 8, 16 })
            {
                bits = candidate;
                if (palette.Count <= 1 << candidate)
                    break;
            }

            w.U8((byte)(bits << 1));
            int blocksPerWord = 32 / bits;
            int wordCount = (Volume + blocksPerWord - 1) / blocksPerWord;
            int i2 = 0;
            for (int word = 0; word < wordCount; word++)
            {
                uint value = 0;
                for (int b = 0; b < blocksPerWord && i2 < Volume; b++, i2++)
                    value |= (uint)indices[i2] << (b * bits);
                w.U32(value);
            }

            w.S32(palette.Count);
            foreach (var state in palette)
                NbtWriter.WriteRoot(w, state.ToNbt());
        }

        internal static BlockStorage Read(ByteReader r)
        {
            byte header = r.U8();
            int bits = header >> 1;
            if ((header & 1) != 0)
                throw new NotSupportedException("Runtime-id (network) palette found in a saved world.");

            var indices = new ushort[Volume];

            if (bits == 0)
            {
                // Uniform layer: no index words. Most versions still write a palette count of 1.
                if (r.Remaining >= 4 && r.PeekS32() == 1)
                    r.S32();
                return new BlockStorage(indices, ReadPalette(r, 1), 0);
            }

            if (bits > 16)
                throw new FormatException($"Invalid bits per block: {bits}.");

            int blocksPerWord = 32 / bits;
            int wordCount = (Volume + blocksPerWord - 1) / blocksPerWord;
            uint mask = (1u << bits) - 1;

            int i = 0;
            for (int w = 0; w < wordCount; w++)
            {
                uint word = r.U32();
                for (int b = 0; b < blocksPerWord && i < Volume; b++, i++)
                    indices[i] = (ushort)((word >> (b * bits)) & mask);
            }

            int paletteSize = r.S32();
            var palette = ReadPalette(r, paletteSize);

            for (int k = 0; k < Volume; k++)
                if (indices[k] >= palette.Count)
                    throw new FormatException($"Palette index {indices[k]} out of range ({palette.Count}).");

            return new BlockStorage(indices, palette, bits);
        }

        private static List<BlockState> ReadPalette(ByteReader r, int count)
        {
            var palette = new List<BlockState>(count);
            for (int i = 0; i < count; i++)
                palette.Add(BlockState.FromNbt(NbtReader.ReadRootCompound(r)));
            return palette;
        }
    }

    /// <summary>A decoded <see cref="ChunkTag.SubChunkPrefix"/> record (formats 1, 8 and 9).</summary>
    public sealed class SubChunk
    {
        public int Format { get; }
        public sbyte? StoredY { get; }
        public List<BlockStorage> Layers { get; }

        private SubChunk(int format, sbyte? storedY, List<BlockStorage> layers)
        {
            Format = format;
            StoredY = storedY;
            Layers = layers;
        }

        public BlockState? Get(int x, int y, int z, int layer = 0) =>
            layer < Layers.Count ? Layers[layer].Get(x, y, z) : null;

        public static SubChunk CreateEmpty(sbyte y, BlockState air) =>
            new SubChunk(9, y, new List<BlockStorage> { BlockStorage.Filled(air) });

        /// <summary>Encodes in format 9 (palette per layer + the sub-chunk's Y index), what current Bedrock writes.</summary>
        public byte[] Encode(sbyte y)
        {
            var w = new ByteWriter();
            w.U8(9);
            w.U8((byte)Layers.Count);
            w.S8(y);
            foreach (var layer in Layers)
                layer.Write(w);
            return w.ToArray();
        }

        public static SubChunk Parse(byte[] data)
        {
            var r = new ByteReader(data);
            int format = r.U8();
            int layerCount;
            sbyte? y = null;

            switch (format)
            {
                case 1:
                    layerCount = 1;
                    break;
                case 8:
                    layerCount = r.U8();
                    break;
                case 9:
                    layerCount = r.U8();
                    y = r.S8();
                    break;
                default:
                    throw new NotSupportedException($"Legacy sub-chunk format {format} (pre-1.2.13 numeric ids) is not supported yet.");
            }

            var layers = new List<BlockStorage>(layerCount);
            for (int i = 0; i < layerCount; i++)
                layers.Add(BlockStorage.Read(r));

            return new SubChunk(format, y, layers);
        }
    }
}
