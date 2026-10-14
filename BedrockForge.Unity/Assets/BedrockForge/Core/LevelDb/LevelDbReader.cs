using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BedrockForge.IO;

namespace BedrockForge.LevelDb
{
    /// <summary>
    /// Read-only, fully managed reader for Mojang's LevelDB fork.
    /// Loads the live SSTables listed in the MANIFEST plus the current write-ahead logs,
    /// and keeps the newest version of every key (highest sequence number wins).
    /// </summary>
    public sealed class LevelDbReader
    {
        private readonly Dictionary<byte[], byte[]> _entries;

        private LevelDbReader(Dictionary<byte[], byte[]> entries, ulong lastSequence)
        {
            _entries = entries;
            LastSequence = lastSequence;
        }

        public int Count => _entries.Count;

        public IEnumerable<byte[]> Keys => _entries.Keys;

        public IEnumerable<KeyValuePair<byte[], byte[]>> Entries => _entries;

        /// <summary>Highest sequence number in use; new writes must start above it.</summary>
        public ulong LastSequence { get; private set; }

        public bool TryGet(byte[] key, out byte[] value) => _entries.TryGetValue(key, out value!);

        /// <summary>Mirrors changes just written to disk, so the in-memory view stays current without reopening.</summary>
        public void ApplyWritten(IEnumerable<KeyValuePair<byte[], byte[]?>> changes)
        {
            foreach (var kv in changes)
            {
                if (kv.Value == null)
                    _entries.Remove(kv.Key);
                else
                    _entries[kv.Key] = kv.Value;
                LastSequence++;
            }
        }

        public static LevelDbReader Open(string dbDirectory)
        {
            if (!Directory.Exists(dbDirectory))
                throw new DirectoryNotFoundException($"LevelDB folder not found: {dbDirectory}");

            if (IsEncrypted(Path.Combine(dbDirectory, "CURRENT")))
                throw new NotSupportedException("This world's database is encrypted (Marketplace content) and cannot be opened.");

            var (tables, logs) = FindLiveFiles(dbDirectory);

            var newest = new Dictionary<byte[], RawEntry>(ByteArrayComparer.Instance);

            void Apply(RawEntry e)
            {
                if (!newest.TryGetValue(e.Key, out var existing) || e.Sequence >= existing.Sequence)
                    newest[e.Key] = e;
            }

            foreach (var table in tables)
                foreach (var e in Table.ReadAll(ReadShared(table)))
                    Apply(e);

            foreach (var log in logs)
                foreach (var record in LogReader.ReadRecords(ReadShared(log)))
                    foreach (var e in ParseWriteBatch(record))
                        Apply(e);

            var live = new Dictionary<byte[], byte[]>(newest.Count, ByteArrayComparer.Instance);
            ulong lastSequence = 0;
            foreach (var e in newest.Values)
            {
                if (!e.Deleted)
                    live[e.Key] = e.Value!;
                if (e.Sequence > lastSequence)
                    lastSequence = e.Sequence;
            }

            try
            {
                string current = File.ReadAllText(Path.Combine(dbDirectory, "CURRENT")).Trim();
                var state = ManifestState.Load(ReadShared(Path.Combine(dbDirectory, current)));
                if (state.LastSequence > lastSequence)
                    lastSequence = state.LastSequence;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException)
            {
            }

            return new LevelDbReader(live, lastSequence);
        }

        /// <summary>The MANIFEST's next-file counter, so new files never reuse a number LevelDB has handed out.</summary>
        public static ulong ReadManifestNextFile(string manifestPath) => ManifestState.Load(ReadShared(manifestPath)).NextFileNumber;

        /// <summary>Marketplace worlds wrap every db file in a header: 4 zero bytes, then magic 0x9BCFB9FC.</summary>
        public static bool IsEncryptedDatabase(string dbDirectory) => IsEncrypted(Path.Combine(dbDirectory, "CURRENT"));

        private static bool IsEncrypted(string currentFile)
        {
            if (!File.Exists(currentFile))
                return false;
            var head = ReadShared(currentFile);
            return head.Length >= 8 && head[0] == 0 && head[1] == 0 && head[2] == 0 && head[3] == 0
                   && head[4] == 0xFC && head[5] == 0xB9 && head[6] == 0xCF && head[7] == 0x9B;
        }

        /// <summary>Reads a file even if the game currently holds it open.</summary>
        private static byte[] ReadShared(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[fs.Length];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = fs.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }
            return buffer;
        }

        private static IEnumerable<RawEntry> ParseWriteBatch(byte[] record)
        {
            var r = new ByteReader(record);
            ulong seq = r.U64();
            uint count = r.U32();

            for (uint i = 0; i < count && !r.Eof; i++, seq++)
            {
                byte tag = r.U8();
                byte[] key = r.Slice();
                if (tag == 1)
                    yield return new RawEntry(key, seq, false, r.Slice());
                else if (tag == 0)
                    yield return new RawEntry(key, seq, true, null);
                else
                    throw new FormatException($"Unknown write batch tag {tag}.");
            }
        }

        private static (List<string> tables, List<string> logs) FindLiveFiles(string dir)
        {
            var allTables = Directory.GetFiles(dir, "*.ldb").Concat(Directory.GetFiles(dir, "*.sst")).ToList();
            var allLogs = Directory.GetFiles(dir, "*.log").ToList();

            try
            {
                string current = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
                var state = ManifestState.Load(ReadShared(Path.Combine(dir, current)));

                var tables = allTables.Where(f => state.LiveFiles.Contains(FileNumber(f))).ToList();
                var logs = allLogs
                    .Where(f => FileNumber(f) >= state.LogNumber || FileNumber(f) == state.PrevLogNumber)
                    .OrderBy(FileNumber)
                    .ToList();
                return (tables, logs);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException)
            {
                // Damaged or missing manifest: fall back to everything on disk. Sequence numbers still pick the newest value.
                return (allTables, allLogs.OrderBy(FileNumber).ToList());
            }
        }

        private static ulong FileNumber(string path)
        {
            ulong.TryParse(Path.GetFileNameWithoutExtension(path), out ulong n);
            return n;
        }

        /// <summary>Replays the MANIFEST's VersionEdit records to know which files are live.</summary>
        private sealed class ManifestState
        {
            public readonly HashSet<ulong> LiveFiles = new HashSet<ulong>();
            public ulong LogNumber;
            public ulong NextFileNumber;
            public ulong LastSequence;
            public ulong PrevLogNumber = ulong.MaxValue;

            public static ManifestState Load(byte[] manifest)
            {
                var state = new ManifestState();
                foreach (var record in LogReader.ReadRecords(manifest))
                {
                    var r = new ByteReader(record);
                    while (!r.Eof)
                    {
                        int tag = r.VarInt();
                        switch (tag)
                        {
                            case 1: r.Slice(); break;                                   // comparator name
                            case 2: state.LogNumber = r.VarU64(); break;                // log number
                            case 3: state.NextFileNumber = r.VarU64(); break;           // next file number
                            case 4: state.LastSequence = r.VarU64(); break;             // last sequence
                            case 5: r.VarInt(); r.Slice(); break;                       // compact pointer
                            case 6: r.VarInt(); state.LiveFiles.Remove(r.VarU64()); break; // deleted file
                            case 7:                                                     // new file
                                r.VarInt();
                                state.LiveFiles.Add(r.VarU64());
                                r.VarU64();
                                r.Slice();
                                r.Slice();
                                break;
                            case 9: state.PrevLogNumber = r.VarU64(); break;            // prev log number
                            default: throw new FormatException($"Unknown manifest tag {tag}.");
                        }
                    }
                }
                return state;
            }
        }
    }

    public sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new ByteArrayComparer();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null || x.Length != y.Length) return false;
            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (byte b in obj)
                    h = (h ^ b) * 16777619;
                return (int)h;
            }
        }
    }
}
