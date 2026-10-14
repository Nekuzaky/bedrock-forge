using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BedrockForge.IO;
using BedrockForge.LevelDb;
using BedrockForge.Nbt;

namespace BedrockForge.Bedrock
{
    /// <summary>A Bedrock world opened read-only, from its folder or from a <c>.mcworld</c> archive.</summary>
    public sealed class BedrockWorld
    {
        public string Folder { get; }
        public string Name { get; }
        public NbtCompound LevelDat { get; }
        public int LevelDatStorageVersion { get; }
        public LevelDbReader Db { get; }

        /// <summary>Every chunk key that parsed, grouped by chunk.</summary>
        public IReadOnlyDictionary<ChunkPos, List<ChunkKey>> Chunks { get; }

        /// <summary>Keys that are not chunk records (players, villages, portals, scoreboard…).</summary>
        public IReadOnlyList<byte[]> OtherKeys { get; }

        private BedrockWorld(string folder, string name, NbtCompound levelDat, int storageVersion, LevelDbReader db)
        {
            Folder = folder;
            Name = name;
            LevelDat = levelDat;
            LevelDatStorageVersion = storageVersion;
            Db = db;

            var chunks = new Dictionary<ChunkPos, List<ChunkKey>>();
            var other = new List<byte[]>();
            foreach (var key in db.Keys)
            {
                if (ChunkKey.TryParse(key, out var ck))
                {
                    var pos = new ChunkPos(ck.X, ck.Z, ck.Dimension);
                    if (!chunks.TryGetValue(pos, out var list))
                        chunks[pos] = list = new List<ChunkKey>();
                    list.Add(ck);
                }
                else
                {
                    other.Add(key);
                }
            }
            Chunks = chunks;
            OtherKeys = other;
        }

        public static BedrockWorld Open(string path)
        {
            string folder = path;
            if (File.Exists(path) && path.EndsWith(".mcworld", StringComparison.OrdinalIgnoreCase))
            {
                folder = Path.Combine(Path.GetTempPath(), "bedrockforge", Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                ZipFile.ExtractToDirectory(path, folder);
            }

            string levelDatPath = Path.Combine(folder, "level.dat");
            if (!File.Exists(levelDatPath))
                throw new FileNotFoundException("Not a Bedrock world: level.dat is missing.", levelDatPath);

            // level.dat = int32 storage version + int32 payload length + little-endian NBT.
            var levelBytes = File.ReadAllBytes(levelDatPath);
            var r = new ByteReader(levelBytes);
            int storageVersion = r.S32();
            r.S32();
            var levelDat = NbtReader.ReadRootCompound(r);

            string namePath = Path.Combine(folder, "levelname.txt");
            string name = File.Exists(namePath)
                ? File.ReadAllText(namePath, Encoding.UTF8).Trim()
                : levelDat.GetString("LevelName") ?? Path.GetFileName(folder);

            var db = LevelDbReader.Open(Path.Combine(folder, "db"));
            return new BedrockWorld(folder, name, levelDat, storageVersion, db);
        }

        public byte[]? GetRaw(ChunkKey key) => Db.TryGet(key.ToBytes(), out var v) ? v : null;

        public SubChunk? GetSubChunk(int chunkX, int chunkZ, int subY, Dimension dim = Dimension.Overworld)
        {
            var raw = GetRaw(new ChunkKey(chunkX, chunkZ, dim, ChunkTag.SubChunkPrefix, checked((sbyte)subY)));
            return raw == null ? null : SubChunk.Parse(raw);
        }

        /// <summary>Block at world coordinates. Returns null when the sub-chunk was never generated (void/air).</summary>
        public BlockState? GetBlock(int x, int y, int z, Dimension dim = Dimension.Overworld, int layer = 0)
        {
            var sub = GetSubChunk(FloorDiv(x), FloorDiv(z), FloorDiv(y), dim);
            return sub?.Get(x & 15, y & 15, z & 15, layer);
        }

        public IEnumerable<sbyte> SubChunkIndices(ChunkPos pos) =>
            Chunks.TryGetValue(pos, out var keys)
                ? keys.Where(k => k.Tag == ChunkTag.SubChunkPrefix).Select(k => k.SubChunkY).OrderBy(y => y)
                : Enumerable.Empty<sbyte>();

        private static int FloorDiv(int v) => v >> 4;

        public static string DescribeKey(byte[] key)
        {
            // Keys like "actorprefix" + id or "digp" + chunk coords: show the text prefix, then the binary tail in hex.
            int textLen = 0;
            while (textLen < key.Length && key[textLen] >= 0x20 && key[textLen] < 0x7F)
                textLen++;
            string text = Encoding.ASCII.GetString(key, 0, textLen);
            if (textLen == key.Length)
                return text;
            string hex = BitConverter.ToString(key, textLen).Replace("-", "");
            return textLen >= 3 ? $"{text} + 0x{hex}" : "0x" + BitConverter.ToString(key).Replace("-", "");
        }
    }
}
