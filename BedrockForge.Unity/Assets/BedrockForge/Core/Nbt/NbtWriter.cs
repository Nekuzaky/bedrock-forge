using System;
using System.Collections.Generic;
using System.Text;
using BedrockForge.IO;

namespace BedrockForge.Nbt
{
    /// <summary>Writes Bedrock's little-endian NBT, the counterpart of <see cref="NbtReader"/>.</summary>
    public static class NbtWriter
    {
        public static byte[] ToBytes(NbtCompound root, string name = "")
        {
            var w = new ByteWriter();
            WriteRoot(w, root, name);
            return w.ToArray();
        }

        /// <summary>Several root compounds back to back, as in block entity and legacy entity records.</summary>
        public static byte[] ToBytes(IEnumerable<NbtCompound> roots)
        {
            var w = new ByteWriter();
            foreach (var root in roots)
                WriteRoot(w, root, "");
            return w.ToArray();
        }

        public static void WriteRoot(ByteWriter w, object value, string name = "")
        {
            w.U8((byte)TypeOf(value));
            WriteString(w, name);
            WritePayload(w, value);
        }

        public static NbtType TypeOf(object value) => value switch
        {
            byte _ => NbtType.Byte,
            short _ => NbtType.Short,
            int _ => NbtType.Int,
            long _ => NbtType.Long,
            float _ => NbtType.Float,
            double _ => NbtType.Double,
            byte[] _ => NbtType.ByteArray,
            string _ => NbtType.String,
            NbtList _ => NbtType.List,
            NbtCompound _ => NbtType.Compound,
            int[] _ => NbtType.IntArray,
            long[] _ => NbtType.LongArray,
            _ => throw new ArgumentException($"Not an NBT value: {value?.GetType().Name ?? "null"}"),
        };

        private static void WriteString(ByteWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > ushort.MaxValue)
                throw new ArgumentException("NBT string too long.");
            w.U16((ushort)bytes.Length);
            w.Bytes(bytes);
        }

        private static void WritePayload(ByteWriter w, object value)
        {
            switch (value)
            {
                case byte b: w.U8(b); break;
                case short s: w.S16(s); break;
                case int i: w.S32(i); break;
                case long l: w.S64(l); break;
                case float f: w.F32(f); break;
                case double d: w.F64(d); break;
                case byte[] ba:
                    w.S32(ba.Length);
                    w.Bytes(ba);
                    break;
                case string str: WriteString(w, str); break;
                case NbtList list:
                {
                    var type = list.Count == 0 ? list.ElementType : TypeOf(list[0]);
                    w.U8((byte)type);
                    w.S32(list.Count);
                    foreach (var item in list)
                    {
                        if (TypeOf(item) != type)
                            throw new ArgumentException($"NBT list mixes {type} and {TypeOf(item)}.");
                        WritePayload(w, item);
                    }
                    break;
                }
                case NbtCompound compound:
                    foreach (var kv in compound)
                    {
                        w.U8((byte)TypeOf(kv.Value));
                        WriteString(w, kv.Key);
                        WritePayload(w, kv.Value);
                    }
                    w.U8((byte)NbtType.End);
                    break;
                case int[] ia:
                    w.S32(ia.Length);
                    foreach (var x in ia) w.S32(x);
                    break;
                case long[] la:
                    w.S32(la.Length);
                    foreach (var x in la) w.S64(x);
                    break;
                default:
                    throw new ArgumentException($"Not an NBT value: {value.GetType().Name}");
            }
        }
    }

    public static class NbtExtensions
    {
        /// <summary>Deep copy, so edits can be cancelled or compared against the original.</summary>
        public static object DeepClone(object value) => value switch
        {
            NbtCompound c => c.Clone(),
            NbtList l => l.Clone(),
            byte[] ba => (byte[])ba.Clone(),
            int[] ia => (int[])ia.Clone(),
            long[] la => (long[])la.Clone(),
            _ => value, // primitives and strings are immutable
        };

        public static NbtCompound Clone(this NbtCompound c)
        {
            var copy = new NbtCompound();
            foreach (var kv in c)
                copy[kv.Key] = DeepClone(kv.Value);
            return copy;
        }

        public static NbtList Clone(this NbtList l)
        {
            var copy = new NbtList(l.ElementType);
            foreach (var item in l)
                copy.Add(DeepClone(item));
            return copy;
        }
    }
}
