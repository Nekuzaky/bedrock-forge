using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BedrockForge.Resources
{
    /// <summary>
    /// Bedrock's <c>.brarchive</c>: an uncompressed bundle of the files of one folder.
    /// Layout: 8-byte magic, uint32 entry count, uint32 version, then 256-byte entries
    /// (byte name length, 247-byte name, uint32 offset, uint32 size), then the file data.
    /// </summary>
    public sealed class BrArchive
    {
        private const int EntrySize = 256;
        private const int NameField = 247;

        public string Path { get; }
        public IReadOnlyDictionary<string, (long offset, int size)> Entries { get; }

        private BrArchive(string path, Dictionary<string, (long, int)> entries)
        {
            Path = path;
            Entries = entries;
        }

        public static BrArchive Open(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = ReadExactly(fs, 16);
            int count = BitConverter.ToInt32(header, 8);
            if (count < 0 || 16L + (long)count * EntrySize > fs.Length)
                throw new FormatException($"Not a .brarchive: {path}");

            var table = ReadExactly(fs, count * EntrySize);
            long dataStart = 16L + (long)count * EntrySize;
            var entries = new Dictionary<string, (long, int)>(count, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                int e = i * EntrySize;
                int nameLen = Math.Min(table[e], (byte)NameField);
                string name = Encoding.UTF8.GetString(table, e + 1, nameLen);
                uint offset = BitConverter.ToUInt32(table, e + 1 + NameField);
                int size = BitConverter.ToInt32(table, e + 1 + NameField + 4);
                entries[name] = (dataStart + offset, size);
            }

            return new BrArchive(path, entries);
        }

        public byte[] Read(string name)
        {
            var (offset, size) = Entries[name];
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Position = offset;
            return ReadExactly(fs, size);
        }

        private static byte[] ReadExactly(Stream s, int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }
    }
}
