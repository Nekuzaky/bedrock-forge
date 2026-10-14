using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BedrockForge.Json
{
    /// <summary>
    /// Small tolerant JSON reader for Bedrock resource packs (which may contain comments and trailing commas).
    /// Objects become <see cref="Dictionary{TKey,TValue}"/>, arrays <see cref="List{T}"/>, numbers <c>double</c>.
    /// </summary>
    public static class JsonReader
    {
        public static object? Parse(string text)
        {
            int pos = 0;
            SkipWs(text, ref pos);
            var value = ReadValue(text, ref pos);
            return value;
        }

        public static Dictionary<string, object?>? ParseObject(string text) => Parse(text) as Dictionary<string, object?>;

        private static object? ReadValue(string s, ref int p)
        {
            SkipWs(s, ref p);
            if (p >= s.Length)
                throw new FormatException("Unexpected end of JSON.");

            char c = s[p];
            switch (c)
            {
                case '{': return ReadObject(s, ref p);
                case '[': return ReadArray(s, ref p);
                case '"': return ReadString(s, ref p);
                case 't': Expect(s, ref p, "true"); return true;
                case 'f': Expect(s, ref p, "false"); return false;
                case 'n': Expect(s, ref p, "null"); return null;
                default: return ReadNumber(s, ref p);
            }
        }

        private static Dictionary<string, object?> ReadObject(string s, ref int p)
        {
            var obj = new Dictionary<string, object?>();
            p++; // {
            while (true)
            {
                SkipWs(s, ref p);
                if (s[p] == '}') { p++; return obj; }
                string key = ReadString(s, ref p);
                SkipWs(s, ref p);
                if (s[p] != ':') throw new FormatException($"Expected ':' at {p}.");
                p++;
                obj[key] = ReadValue(s, ref p);
                SkipWs(s, ref p);
                if (s[p] == ',') { p++; continue; }
                if (s[p] == '}') { p++; return obj; }
                throw new FormatException($"Expected ',' or '}}' at {p}.");
            }
        }

        private static List<object?> ReadArray(string s, ref int p)
        {
            var list = new List<object?>();
            p++; // [
            while (true)
            {
                SkipWs(s, ref p);
                if (s[p] == ']') { p++; return list; }
                list.Add(ReadValue(s, ref p));
                SkipWs(s, ref p);
                if (s[p] == ',') { p++; continue; }
                if (s[p] == ']') { p++; return list; }
                throw new FormatException($"Expected ',' or ']' at {p}.");
            }
        }

        private static string ReadString(string s, ref int p)
        {
            if (s[p] != '"') throw new FormatException($"Expected string at {p}.");
            p++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[p++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(p, 4), 16)); p += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        private static double ReadNumber(string s, ref int p)
        {
            int start = p;
            while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
            if (p == start) throw new FormatException($"Unexpected character '{s[p]}' at {p}.");
            return double.Parse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int p, string word)
        {
            if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0)
                throw new FormatException($"Expected '{word}' at {p}.");
            p += word.Length;
        }

        private static void SkipWs(string s, ref int p)
        {
            while (p < s.Length)
            {
                char c = s[p];
                if (char.IsWhiteSpace(c) || c == '﻿') { p++; continue; }
                if (c == '/' && p + 1 < s.Length && s[p + 1] == '/')
                {
                    while (p < s.Length && s[p] != '\n') p++;
                    continue;
                }
                if (c == '/' && p + 1 < s.Length && s[p + 1] == '*')
                {
                    int end = s.IndexOf("*/", p + 2, StringComparison.Ordinal);
                    p = end < 0 ? s.Length : end + 2;
                    continue;
                }
                return;
            }
        }
    }
}
