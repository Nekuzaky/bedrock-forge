using System.Collections.Generic;
using System.IO;

namespace BedrockForge.LevelDb
{
    /// <summary>
    /// Reads the LevelDB log format, used by both the write-ahead <c>.log</c> files and the MANIFEST.
    /// The file is split into 32 KiB blocks; each record is fragmented across blocks as FULL or FIRST/MIDDLE/LAST.
    /// </summary>
    internal static class LogReader
    {
        private const int BlockSize = 32 * 1024;
        private const int HeaderSize = 7;

        private const byte Full = 1, First = 2, Middle = 3, Last = 4;

        public static IEnumerable<byte[]> ReadRecords(byte[] data)
        {
            var pending = new MemoryStream();
            bool inFragment = false;
            int pos = 0;

            while (pos < data.Length)
            {
                int blockLeft = BlockSize - pos % BlockSize;
                if (blockLeft < HeaderSize)
                {
                    pos += blockLeft; // trailer padding
                    continue;
                }
                if (pos + HeaderSize > data.Length)
                    yield break;

                int length = data[pos + 4] | data[pos + 5] << 8;
                byte type = data[pos + 6];

                if (type == 0 && length == 0)
                {
                    // Zero-filled preallocated space: skip the rest of the block.
                    pos += blockLeft;
                    continue;
                }

                int payload = pos + HeaderSize;
                if (payload + length > data.Length)
                    yield break; // truncated tail (game was writing / crashed)

                switch (type)
                {
                    case Full:
                        inFragment = false;
                        pending.SetLength(0);
                        yield return Copy(data, payload, length);
                        break;
                    case First:
                        pending.SetLength(0);
                        pending.Write(data, payload, length);
                        inFragment = true;
                        break;
                    case Middle:
                        if (inFragment)
                            pending.Write(data, payload, length);
                        break;
                    case Last:
                        if (inFragment)
                        {
                            pending.Write(data, payload, length);
                            inFragment = false;
                            yield return pending.ToArray();
                        }
                        break;
                    default:
                        yield break; // unknown record type: stop rather than read garbage
                }

                pos = payload + length;
            }
        }

        private static byte[] Copy(byte[] data, int offset, int length)
        {
            var result = new byte[length];
            System.Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }
    }
}
