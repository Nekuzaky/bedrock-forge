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
    /// <summary>A mob or other actor, wherever this world version stores it.</summary>
    public sealed class EntityRef
    {
        /// <summary>Modern worlds (1.18.30+): one <c>actorprefix</c> record per entity, indexed per chunk by <c>digp</c>.</summary>
        public bool IsActorRecord { get; }
        /// <summary>The <c>actorprefix</c> key, or the legacy chunk <see cref="ChunkTag.Entity"/> key.</summary>
        public byte[] Key { get; internal set; }
        /// <summary>The 8-byte id listed in <c>digp</c> (modern worlds only).</summary>
        public byte[]? StorageId { get; }
        public ChunkPos Chunk { get; internal set; }
        /// <summary>Edit freely, then call <see cref="WorldEditSession.SaveEntity"/>.</summary>
        public NbtCompound Nbt { get; internal set; }

        internal EntityRef(bool isActorRecord, byte[] key, byte[]? storageId, ChunkPos chunk, NbtCompound nbt)
        {
            IsActorRecord = isActorRecord;
            Key = key;
            StorageId = storageId;
            Chunk = chunk;
            Nbt = nbt;
        }

        public string Identifier => Nbt.GetString("identifier") ?? Nbt.GetString("id") ?? "?";

        public (float x, float y, float z)? Position =>
            Nbt.Get<NbtList>("Pos") is { Count: 3 } p && p[0] is float x && p[1] is float y && p[2] is float z ? (x, y, z) : ((float, float, float)?)null;
    }

    /// <summary>A player record: <c>~local_player</c> (the world owner on this device) or <c>player_server_…</c>.</summary>
    public sealed class PlayerRef
    {
        public byte[] Key { get; }
        public string Label { get; }
        public NbtCompound Nbt { get; }

        internal PlayerRef(byte[] key, string label, NbtCompound nbt)
        {
            Key = key;
            Label = label;
            Nbt = nbt;
        }
    }

    /// <summary>
    /// Collects edits to a Bedrock world (blocks, level.dat, players, entities, block entities, any NBT record)
    /// and writes them in one go. Saving refuses while the game has the world open, and backs the world up first.
    /// </summary>
    public sealed class WorldEditSession
    {
        private static readonly byte[] ActorPrefix = Encoding.ASCII.GetBytes("actorprefix");
        private static readonly byte[] DigpPrefix = Encoding.ASCII.GetBytes("digp");

        private readonly Dictionary<byte[], byte[]?> _pending = new Dictionary<byte[], byte[]?>(ByteArrayComparer.Instance);
        private readonly Dictionary<ChunkKey, SubChunk> _subChunks = new Dictionary<ChunkKey, SubChunk>();
        private readonly HashSet<ChunkKey> _dirtySubChunks = new HashSet<ChunkKey>();
        private bool _levelDatDirty;

        public BedrockWorld World { get; }
        public BlockState Air { get; }
        /// <summary>The block data version most of the world uses; given to newly placed block states.</summary>
        public int BlockVersion { get; }

        public event Action? Changed;

        public WorldEditSession(BedrockWorld world)
        {
            World = world;
            BlockVersion = DetectBlockVersion(world);
            Air = new BlockState("minecraft:air", new NbtCompound(), BlockVersion);
        }

        public int PendingChanges => _pending.Count + _dirtySubChunks.Count + (_levelDatDirty ? 1 : 0);

        public bool HasChanges => PendingChanges > 0;

        // ---------------------------------------------------------------- raw records

        public byte[]? GetRaw(byte[] key)
        {
            if (_pending.TryGetValue(key, out var pending))
                return pending;
            return World.Db.TryGet(key, out var value) ? value : null;
        }

        /// <summary>Sets (or deletes, with null) any database record.</summary>
        public void SetRaw(byte[] key, byte[]? value)
        {
            _pending[key] = value;
            Changed?.Invoke();
        }

        /// <summary>All keys, including records created in this session and minus the ones deleted.</summary>
        public IEnumerable<byte[]> Keys =>
            World.Db.Keys.Where(k => !_pending.TryGetValue(k, out var v) || v != null)
                .Concat(_pending.Where(kv => kv.Value != null && !World.Db.TryGet(kv.Key, out _)).Select(kv => kv.Key));

        /// <summary>The record as a list of NBT compounds (most records hold one; block entities hold several).</summary>
        public List<NbtCompound>? ReadNbtList(byte[] key)
        {
            var value = GetRaw(key);
            if (value == null)
                return null;
            var list = new List<NbtCompound>();
            var r = new ByteReader(value);
            while (!r.Eof)
                list.Add(NbtReader.ReadRootCompound(r));
            return list;
        }

        public NbtCompound? ReadNbt(byte[] key) => ReadNbtList(key)?.FirstOrDefault();

        public void WriteNbt(byte[] key, NbtCompound value) => SetRaw(key, NbtWriter.ToBytes(value));

        public void WriteNbtList(byte[] key, IReadOnlyCollection<NbtCompound> values) =>
            SetRaw(key, values.Count == 0 ? null : NbtWriter.ToBytes(values));

        /// <summary>True if the value parses as one or more NBT compounds, i.e. it can be shown in the NBT editor.</summary>
        public bool IsNbt(byte[] key)
        {
            try
            {
                var list = ReadNbtList(key);
                return list != null && list.Count > 0;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- level.dat

        public NbtCompound LevelDat => World.LevelDat;

        /// <summary>Call after editing <see cref="LevelDat"/>.</summary>
        public void MarkLevelDatChanged()
        {
            _levelDatDirty = true;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- players

        public List<PlayerRef> Players()
        {
            var players = new List<PlayerRef>();
            foreach (var key in Keys)
            {
                string text = Encoding.ASCII.GetString(key);
                bool local = text == "~local_player";
                if (!local && !text.StartsWith("player_server_", StringComparison.Ordinal))
                    continue;
                var nbt = SafeReadNbt(key);
                if (nbt != null)
                    players.Add(new PlayerRef(key, local ? "Local player" : text.Substring("player_server_".Length), nbt));
            }
            return players.OrderBy(p => p.Label == "Local player" ? 0 : 1).ThenBy(p => p.Label).ToList();
        }

        public void SavePlayer(PlayerRef player) => WriteNbt(player.Key, player.Nbt);

        // ---------------------------------------------------------------- entities

        public List<EntityRef> Entities()
        {
            var entities = new List<EntityRef>();

            // Modern storage: digp lists the actor ids of each chunk.
            foreach (var key in Keys.Where(k => StartsWith(k, DigpPrefix)).ToList())
            {
                if (!TryParseDigp(key, out var chunk))
                    continue;
                var ids = GetRaw(key);
                if (ids == null)
                    continue;
                for (int i = 0; i + 8 <= ids.Length; i += 8)
                {
                    var id = ids.Skip(i).Take(8).ToArray();
                    var actorKey = ActorPrefix.Concat(id).ToArray();
                    var nbt = SafeReadNbt(actorKey);
                    if (nbt != null)
                        entities.Add(new EntityRef(true, actorKey, id, chunk, nbt));
                }
            }

            // Legacy storage: all entities of a chunk concatenated in one record.
            foreach (var key in Keys.ToList())
            {
                if (!ChunkKey.TryParse(key, out var ck) || ck.Tag != ChunkTag.Entity)
                    continue;
                List<NbtCompound>? list;
                try { list = ReadNbtList(key); }
                catch (FormatException) { continue; }
                if (list == null) continue;
                foreach (var nbt in list)
                    entities.Add(new EntityRef(false, key, null, new ChunkPos(ck.X, ck.Z, ck.Dimension), nbt));
            }

            return entities;
        }

        /// <summary>Writes an edited entity, moving it to its new chunk's index if its position changed chunk.</summary>
        public void SaveEntity(EntityRef entity)
        {
            var target = ChunkOf(entity);

            if (entity.IsActorRecord)
            {
                WriteNbt(entity.Key, entity.Nbt);
                if (!target.Equals(entity.Chunk))
                {
                    RemoveFromDigp(entity.Chunk, entity.StorageId!);
                    AddToDigp(target, entity.StorageId!);
                    entity.Chunk = target;
                }
                return;
            }

            // Legacy: rewrite the old chunk list without it, then add it to the target chunk's list.
            var oldList = (ReadNbtList(entity.Key) ?? new List<NbtCompound>()).Where(n => !SameEntity(n, entity.Nbt)).ToList();
            var newKey = new ChunkKey(target.X, target.Z, target.Dimension, ChunkTag.Entity).ToBytes();
            if (ByteArrayComparer.Instance.Equals(newKey, entity.Key))
            {
                oldList.Add(entity.Nbt);
                WriteNbtList(entity.Key, oldList);
            }
            else
            {
                WriteNbtList(entity.Key, oldList);
                var newList = ReadNbtList(newKey) ?? new List<NbtCompound>();
                newList.Add(entity.Nbt);
                WriteNbtList(newKey, newList);
                entity.Key = newKey;
                entity.Chunk = target;
            }
        }

        public void DeleteEntity(EntityRef entity)
        {
            if (entity.IsActorRecord)
            {
                SetRaw(entity.Key, null);
                RemoveFromDigp(entity.Chunk, entity.StorageId!);
            }
            else
            {
                var list = (ReadNbtList(entity.Key) ?? new List<NbtCompound>()).Where(n => !SameEntity(n, entity.Nbt)).ToList();
                WriteNbtList(entity.Key, list);
            }
        }

        private static bool SameEntity(NbtCompound a, NbtCompound b) =>
            ReferenceEquals(a, b) || (a.TryGetValue("UniqueID", out var ia) && b.TryGetValue("UniqueID", out var ib) && Equals(ia, ib));

        private ChunkPos ChunkOf(EntityRef entity)
        {
            var pos = entity.Position;
            if (pos == null)
                return entity.Chunk;
            return new ChunkPos((int)Math.Floor(pos.Value.x) >> 4, (int)Math.Floor(pos.Value.z) >> 4, entity.Chunk.Dimension);
        }

        private static byte[] DigpKey(ChunkPos chunk)
        {
            var w = new ByteWriter();
            w.Bytes(DigpPrefix);
            w.S32(chunk.X);
            w.S32(chunk.Z);
            if (chunk.Dimension != Dimension.Overworld)
                w.S32((int)chunk.Dimension);
            return w.ToArray();
        }

        private static bool TryParseDigp(byte[] key, out ChunkPos chunk)
        {
            chunk = default;
            if (key.Length != 12 && key.Length != 16)
                return false;
            var r = new ByteReader(key, 4, key.Length - 4);
            int x = r.S32(), z = r.S32();
            int dim = key.Length == 16 ? r.S32() : 0;
            if (dim < 0 || dim > 2)
                return false;
            chunk = new ChunkPos(x, z, (Dimension)dim);
            return true;
        }

        private void RemoveFromDigp(ChunkPos chunk, byte[] id)
        {
            var key = DigpKey(chunk);
            var ids = GetRaw(key);
            if (ids == null) return;
            var kept = new List<byte>();
            for (int i = 0; i + 8 <= ids.Length; i += 8)
                if (!ids.AsSpan(i, 8).SequenceEqual(id))
                    kept.AddRange(ids.Skip(i).Take(8));
            SetRaw(key, kept.ToArray());
        }

        private void AddToDigp(ChunkPos chunk, byte[] id)
        {
            var key = DigpKey(chunk);
            var ids = GetRaw(key) ?? Array.Empty<byte>();
            SetRaw(key, ids.Concat(id).ToArray());
        }

        // ---------------------------------------------------------------- block entities

        public static byte[] BlockEntityKey(ChunkPos chunk) => new ChunkKey(chunk.X, chunk.Z, chunk.Dimension, ChunkTag.BlockEntity).ToBytes();

        public List<NbtCompound> BlockEntities(ChunkPos chunk)
        {
            try { return ReadNbtList(BlockEntityKey(chunk)) ?? new List<NbtCompound>(); }
            catch (FormatException) { return new List<NbtCompound>(); }
        }

        /// <summary>The block entity (chest, sign, furnace…) at a block, or null.</summary>
        public NbtCompound? BlockEntityAt(int x, int y, int z, Dimension dim) =>
            BlockEntities(new ChunkPos(x >> 4, z >> 4, dim)).FirstOrDefault(be => be.GetInt("x") == x && be.GetInt("y") == y && be.GetInt("z") == z);

        /// <summary>Writes a chunk's block entities after editing one of them in place.</summary>
        public void SaveBlockEntities(ChunkPos chunk, List<NbtCompound> entities) => WriteNbtList(BlockEntityKey(chunk), entities);

        // ---------------------------------------------------------------- blocks

        public BlockState GetBlock(int x, int y, int z, Dimension dim = Dimension.Overworld, int layer = 0)
        {
            var sub = LoadSubChunk(x >> 4, z >> 4, y >> 4, dim, create: false);
            return sub?.Get(x & 15, y & 15, z & 15, layer) ?? Air;
        }

        /// <summary>Sets the block (layer 0). Removes the block entity that stood there, if any.</summary>
        public void SetBlock(int x, int y, int z, BlockState state, Dimension dim = Dimension.Overworld)
        {
            int cx = x >> 4, cz = z >> 4, sy = y >> 4;
            if (!World.Chunks.ContainsKey(new ChunkPos(cx, cz, dim)))
                throw new InvalidOperationException("This chunk was never generated: the game would overwrite it.");
            if (sy < sbyte.MinValue || sy > sbyte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(y));

            var sub = LoadSubChunk(cx, cz, sy, dim, create: true)!;
            sub.Layers[0].Set(x & 15, y & 15, z & 15, state);
            _dirtySubChunks.Add(new ChunkKey(cx, cz, dim, ChunkTag.SubChunkPrefix, (sbyte)sy));

            var chunk = new ChunkPos(cx, cz, dim);
            var blockEntities = BlockEntities(chunk);
            int removed = blockEntities.RemoveAll(be => be.GetInt("x") == x && be.GetInt("y") == y && be.GetInt("z") == z);
            if (removed > 0)
                SaveBlockEntities(chunk, blockEntities);

            // Pre-1.18 worlds keep per-chunk checksums; a stale one could make the game discard our edit.
            var checksums = new ChunkKey(cx, cz, dim, ChunkTag.Checksums).ToBytes();
            if (GetRaw(checksums) != null)
                _pending[checksums] = null;

            Changed?.Invoke();
        }

        public BlockState CreateState(string name, NbtCompound? states = null) =>
            new BlockState(name.Contains(":") ? name : "minecraft:" + name, states ?? new NbtCompound(), BlockVersion);

        private SubChunk? LoadSubChunk(int cx, int cz, int sy, Dimension dim, bool create)
        {
            var key = new ChunkKey(cx, cz, dim, ChunkTag.SubChunkPrefix, checked((sbyte)sy));
            if (_subChunks.TryGetValue(key, out var cached))
                return cached;

            var raw = GetRaw(key.ToBytes());
            SubChunk? sub = raw == null ? null : SubChunk.Parse(raw);
            if (sub == null && create)
                sub = SubChunk.CreateEmpty((sbyte)sy, Air);
            if (sub != null)
                _subChunks[key] = sub;
            return sub;
        }

        private static int DetectBlockVersion(BedrockWorld world)
        {
            var counts = new Dictionary<int, int>();
            foreach (var key in world.Db.Keys)
            {
                if (!ChunkKey.TryParse(key, out var ck) || ck.Tag != ChunkTag.SubChunkPrefix)
                    continue;
                try
                {
                    var sub = SubChunk.Parse(world.Db.TryGet(key, out var v) ? v : Array.Empty<byte>());
                    foreach (var state in sub.Layers[0].Palette)
                        counts[state.Version] = counts.TryGetValue(state.Version, out int c) ? c + 1 : 1;
                }
                catch (Exception ex) when (ex is FormatException || ex is NotSupportedException)
                {
                }
                if (counts.Values.Sum() > 500)
                    break;
            }
            return counts.Count == 0 ? 0 : counts.OrderByDescending(kv => kv.Value).First().Key;
        }

        // ---------------------------------------------------------------- saving

        public sealed class SaveResult
        {
            public int RecordsWritten;
            public bool LevelDatWritten;
            public string? BackupPath;
            public string? LogPath;
        }

        /// <summary>
        /// Writes everything pending. The game must be closed. With <paramref name="backupFolder"/>, a .mcworld
        /// copy of the world is made first (importable by double-click).
        /// </summary>
        public SaveResult Save(string? backupFolder)
        {
            string dbDir = Path.Combine(World.Folder, "db");
            if (LevelDbWriter.IsLocked(dbDir))
                throw new IOException("The world is open in Minecraft. Close it before saving.");

            var result = new SaveResult();
            if (!HasChanges)
                return result;

            if (backupFolder != null)
                result.BackupPath = Backup(backupFolder);

            var changes = new Dictionary<byte[], byte[]?>(_pending, ByteArrayComparer.Instance);
            foreach (var key in _dirtySubChunks)
                changes[key.ToBytes()] = _subChunks[key].Encode(key.SubChunkY);

            if (changes.Count > 0)
            {
                var list = changes.ToList();
                result.LogPath = LevelDbWriter.WriteChanges(dbDir, list, World.Db.LastSequence);
                World.Db.ApplyWritten(list);
                result.RecordsWritten = list.Count;
            }

            if (_levelDatDirty)
            {
                WriteLevelDat();
                result.LevelDatWritten = true;
            }

            _pending.Clear();
            _dirtySubChunks.Clear();
            _levelDatDirty = false;
            Changed?.Invoke();
            return result;
        }

        /// <summary>Forgets every unsaved change. Callers should reopen the world to drop in-place NBT edits.</summary>
        public void DiscardChanges()
        {
            _pending.Clear();
            _dirtySubChunks.Clear();
            _subChunks.Clear();
            _levelDatDirty = false;
            Changed?.Invoke();
        }

        private void WriteLevelDat()
        {
            string path = Path.Combine(World.Folder, "level.dat");
            var nbt = NbtWriter.ToBytes(World.LevelDat);
            var w = new ByteWriter();
            w.S32(World.LevelDatStorageVersion);
            w.S32(nbt.Length);
            w.Bytes(nbt);

            if (File.Exists(path))
                File.Copy(path, Path.Combine(World.Folder, "level.dat_old"), true);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, w.ToArray());
            File.Copy(temp, path, true);
            File.Delete(temp);

            string namePath = Path.Combine(World.Folder, "levelname.txt");
            if (World.LevelDat.GetString("LevelName") is { } name && File.Exists(namePath))
                File.WriteAllText(namePath, name, new UTF8Encoding(false));
        }

        private string Backup(string backupFolder)
        {
            Directory.CreateDirectory(backupFolder);
            string safeName = new string(World.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string path = Path.Combine(backupFolder, $"{safeName}_{DateTime.Now:yyyyMMdd-HHmmss}.mcworld");
            ZipFile.CreateFromDirectory(World.Folder, path, CompressionLevel.Fastest, includeBaseDirectory: false);
            return path;
        }

        private NbtCompound? SafeReadNbt(byte[] key)
        {
            try { return ReadNbt(key); }
            catch (FormatException) { return null; }
        }

        private static bool StartsWith(byte[] key, byte[] prefix) =>
            key.Length >= prefix.Length && key.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }
}
