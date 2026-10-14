using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BedrockForge.IO;

namespace BedrockForge.Nbt
{
    public enum NbtType : byte
    {
        End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
        ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12,
    }

    /// <summary>Compound tag. Values are byte, short, int, long, float, double, byte[], string, int[], long[], NbtList or NbtCompound.</summary>
    public sealed class NbtCompound : Dictionary<string, object>
    {
        public T? Get<T>(string name) where T : class => TryGetValue(name, out var v) ? v as T : null;

        public int? GetInt(string name) => TryGetValue(name, out var v) ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : (int?)null;

        public string? GetString(string name) => Get<string>(name);
    }

    public sealed class NbtList : List<object>
    {
        public NbtType ElementType { get; }

        public NbtList(NbtType elementType)
        {
            ElementType = elementType;
        }
    }

    /// <summary>Reads Bedrock's little-endian NBT (the on-disk flavour, not the network varint one).</summary>
    public static class NbtReader
    {
        /// <summary>Reads one named root tag and advances the reader past it.</summary>
        public static (string name, object value) ReadRoot(ByteReader r)
        {
            var type = (NbtType)r.U8();
            if (type == NbtType.End)
                throw new FormatException("NBT root is an End tag.");
            string name = ReadString(r);
            return (name, ReadPayload(r, type));
        }

        public static NbtCompound ReadRootCompound(ByteReader r)
        {
            var (_, value) = ReadRoot(r);
            return value as NbtCompound ?? throw new FormatException("NBT root is not a compound.");
        }

        private static string ReadString(ByteReader r) => r.Utf8(r.U16());

        private static object ReadPayload(ByteReader r, NbtType type)
        {
            switch (type)
            {
                case NbtType.Byte: return r.U8();
                case NbtType.Short: return r.S16();
                case NbtType.Int: return r.S32();
                case NbtType.Long: return r.S64();
                case NbtType.Float: return r.F32();
                case NbtType.Double: return r.F64();
                case NbtType.ByteArray: return r.Bytes(r.S32());
                case NbtType.String: return ReadString(r);
                case NbtType.List:
                {
                    var elementType = (NbtType)r.U8();
                    int count = r.S32();
                    var list = new NbtList(elementType);
                    for (int i = 0; i < count; i++)
                        list.Add(ReadPayload(r, elementType));
                    return list;
                }
                case NbtType.Compound:
                {
                    var compound = new NbtCompound();
                    while (true)
                    {
                        var childType = (NbtType)r.U8();
                        if (childType == NbtType.End)
                            return compound;
                        string name = ReadString(r);
                        compound[name] = ReadPayload(r, childType);
                    }
                }
                case NbtType.IntArray:
                {
                    var arr = new int[r.S32()];
                    for (int i = 0; i < arr.Length; i++) arr[i] = r.S32();
                    return arr;
                }
                case NbtType.LongArray:
                {
                    var arr = new long[r.S32()];
                    for (int i = 0; i < arr.Length; i++) arr[i] = r.S64();
                    return arr;
                }
                default:
                    throw new FormatException($"Unknown NBT tag type {(byte)type}.");
            }
        }
    }

    /// <summary>SNBT-like text output, for debugging.</summary>
    public static class NbtFormatter
    {
        public static string ToSnbt(object? value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object? value, bool pretty, int indent)
        {
            switch (value)
            {
                case NbtCompound c:
                    if (c.Count == 0) { sb.Append("{}"); return; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in c.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) sb.Append('\n').Append(' ', (indent + 1) * 2);
                        sb.Append(Quote(kv.Key)).Append(": ");
                        Write(sb, kv.Value, pretty, indent + 1);
                    }
                    if (pretty) sb.Append('\n').Append(' ', indent * 2);
                    sb.Append('}');
                    return;
                case NbtList l:
                    sb.Append('[');
                    for (int i = 0; i < l.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        Write(sb, l[i], pretty && l.ElementType == NbtType.Compound, indent + 1);
                    }
                    sb.Append(']');
                    return;
                case string s: sb.Append(Quote(s)); return;
                case byte b: sb.Append(b).Append('b'); return;
                case short s16: sb.Append(s16).Append('s'); return;
                case long s64: sb.Append(s64).Append('L'); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)).Append('f'); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)).Append('d'); return;
                case byte[] ba: sb.Append("[B; ").Append(ba.Length).Append(" bytes]"); return;
                case int[] ia: sb.Append("[I; ").Append(string.Join(", ", ia)).Append(']'); return;
                case long[] la: sb.Append("[L; ").Append(string.Join(", ", la)).Append(']'); return;
                default: sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
        }

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
