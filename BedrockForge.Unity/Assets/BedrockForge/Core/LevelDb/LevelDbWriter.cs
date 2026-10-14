using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BedrockForge.IO;

namespace BedrockForge.LevelDb
{
    /// <summary>
    /// Writes changes to a LevelDB database the way LevelDB itself would before a crash: as a new write-ahead
    /// <c>.log</c> file. On its next open, the game replays that log over the existing tables, then compacts it.
    /// The database must not be open by the game while writing.
    /// </summary>
    public static class LevelDbWriter
    {
        private const int BlockSize = 32 * 1024;
        private const int HeaderSize = 7;
        private const byte Full = 1, First = 2, Middle = 3, Last = 4;
        private const int MaxBatchBytes = 4 * 1024 * 1024;

        /// <summary>True if another process (the game) holds the database's LOCK file.</summary>
        public static bool IsLocked(string dbDirectory)
        {
            string lockPath = Path.Combine(dbDirectory, "LOCK");
            if (!File.Exists(lockPath))
                return false;
            try
            {
                using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        /// <summary>
        /// Writes <paramref name="changes"/> (null value = delete) as a new log file. Returns the log path.
        /// <paramref name="lastSequence"/> must be the highest sequence number already used in the database.
        /// </summary>
        public static string WriteChanges(string dbDirectory, IReadOnlyCollection<KeyValuePair<byte[], byte[]?>> changes, ulong lastSequence)
        {
            if (IsLocked(dbDirectory))
                throw new IOException("The world is open in Minecraft. Close it (or quit the game) before saving.");

            ulong fileNumber = NextFileNumber(dbDirectory);
            string path = Path.Combine(dbDirectory, $"{fileNumber:000000}.log");

            var log = new LogBuilder();
            ulong sequence = lastSequence + 1;

            // Several batches keep each record reasonable in size; each batch is atomic on replay.
            var batch = new List<KeyValuePair<byte[], byte[]?>>();
            int batchBytes = 0;
            foreach (var change in changes)
            {
                batch.Add(change);
                batchBytes += change.Key.Length + (change.Value?.Length ?? 0) + 16;
                if (batchBytes >= MaxBatchBytes)
                {
                    log.AddRecord(EncodeBatch(batch, sequence));
                    sequence += (ulong)batch.Count;
                    batch.Clear();
                    batchBytes = 0;
                }
            }
            if (batch.Count > 0)
                log.AddRecord(EncodeBatch(batch, sequence));

            // Write to a temp name first so a crash never leaves a half-written log for the game to read.
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, log.ToArray());
            File.Move(temp, path);
            return path;
        }

        private static byte[] EncodeBatch(List<KeyValuePair<byte[], byte[]?>> batch, ulong sequence)
        {
            var w = new ByteWriter();
            w.U64(sequence);
            w.U32((uint)batch.Count);
            foreach (var kv in batch)
            {
                if (kv.Value == null)
                {
                    w.U8(0);
                    w.Slice(kv.Key);
                }
                else
                {
                    w.U8(1);
                    w.Slice(kv.Key);
                    w.Slice(kv.Value);
                }
            }
            return w.ToArray();
        }

        /// <summary>A number above every file in the folder and the MANIFEST's next-file counter.</summary>
        private static ulong NextFileNumber(string dir)
        {
            ulong max = 0;
            foreach (var file in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(file);
                string digits = new string(name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
                if (ulong.TryParse(digits, out ulong n) && n > max)
                    max = n;
            }

            try
            {
                string current = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
                ulong next = LevelDbReader.ReadManifestNextFile(Path.Combine(dir, current));
                if (next > max)
                    max = next;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException)
            {
            }

            return max + 1;
        }

        /// <summary>Splits records into 32 KiB blocks with the LevelDB header (masked CRC32C, length, type).</summary>
        private sealed class LogBuilder
        {
            private readonly MemoryStream _out = new MemoryStream();

            public void AddRecord(byte[] payload)
            {
                int offset = 0;
                bool begin = true;
                do
                {
                    int blockLeft = BlockSize - (int)(_out.Length % BlockSize);
                    if (blockLeft < HeaderSize)
                    {
                        _out.Write(new byte[blockLeft], 0, blockLeft);
                        blockLeft = BlockSize;
                    }

                    int available = blockLeft - HeaderSize;
                    int fragment = Math.Min(payload.Length - offset, available);
                    bool end = offset + fragment == payload.Length;
                    byte type = begin && end ? Full : begin ? First : end ? Last : Middle;

                    uint crc = Crc32C.Mask(Crc32C.Extend(Crc32C.Compute(new[] { type }, 0, 1), payload, offset, fragment));
                    _out.WriteByte((byte)crc);
                    _out.WriteByte((byte)(crc >> 8));
                    _out.WriteByte((byte)(crc >> 16));
                    _out.WriteByte((byte)(crc >> 24));
                    _out.WriteByte((byte)fragment);
                    _out.WriteByte((byte)(fragment >> 8));
                    _out.WriteByte(type);
                    _out.Write(payload, offset, fragment);

                    offset += fragment;
                    begin = false;
                }
                while (offset < payload.Length);
            }

            public byte[] ToArray() => _out.ToArray();
        }
    }

    /// <summary>CRC-32C (Castagnoli), with LevelDB's masking.</summary>
    public static class Crc32C
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0x82F63B78 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static uint Compute(byte[] data, int offset, int count) => Extend(0, data, offset, count);

        public static uint Extend(uint crc, byte[] data, int offset, int count)
        {
            uint c = ~crc;
            for (int i = offset; i < offset + count; i++)
                c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return ~c;
        }

        public static uint Mask(uint crc) => unchecked(((crc >> 15) | (crc << 17)) + 0xa282ead8);
    }
}
