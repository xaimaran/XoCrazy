using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XoCrazy.Core
{
    /// <summary>
    /// A deliberately small JSON reader/writer.
    ///
    /// Newtonsoft ships inside VS but binding-redirects to whatever the shell decided that
    /// release, and dragging a second copy into a VSIX is a well-known way to break other
    /// extensions. The export schema is a flat list of objects; that does not justify a
    /// dependency.
    /// </summary>
    internal static class Json
    {
        // ---- writing -----------------------------------------------------------------

        public sealed class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private int _indent;
            private bool _needComma;

            public Writer BeginObject()
            {
                Separate();
                _sb.Append('{');
                _indent++;
                _needComma = false;
                return this;
            }

            public Writer EndObject()
            {
                _indent--;
                NewLine();
                _sb.Append('}');
                _needComma = true;
                return this;
            }

            public Writer BeginArray(string name)
            {
                Separate();
                NewLine();
                _sb.Append(Quote(name)).Append(": [");
                _indent++;
                _needComma = false;
                return this;
            }

            public Writer EndArray()
            {
                _indent--;
                NewLine();
                _sb.Append(']');
                _needComma = true;
                return this;
            }

            public Writer Prop(string name, string value)
            {
                Separate();
                NewLine();
                _sb.Append(Quote(name)).Append(": ").Append(value == null ? "null" : Quote(value));
                _needComma = true;
                return this;
            }

            public Writer Prop(string name, bool value)
            {
                Separate();
                NewLine();
                _sb.Append(Quote(name)).Append(": ").Append(value ? "true" : "false");
                _needComma = true;
                return this;
            }

            private void Separate()
            {
                if (_needComma) { _sb.Append(','); _needComma = false; }
            }

            private void NewLine()
            {
                _sb.AppendLine();
                _sb.Append(' ', _indent * 2);
            }

            public override string ToString() { return _sb.ToString(); }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---- reading -----------------------------------------------------------------

        public sealed class Node
        {
            public Dictionary<string, Node> Object;
            public List<Node> Array;
            public string String;
            public bool? Bool;
            public bool IsNull;

            public Node this[string key]
            {
                get
                {
                    Node n;
                    return Object != null && Object.TryGetValue(key, out n) ? n : null;
                }
            }

            public string AsString() { return IsNull ? null : String; }
            public bool AsBool(bool fallback) { return Bool ?? fallback; }
        }

        public static Node Parse(string text)
        {
            int i = 0;
            var node = ParseValue(text, ref i);
            return node;
        }

        private static Node ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");

            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new Node { String = ParseString(s, ref i) };
                case 't': Expect(s, ref i, "true"); return new Node { Bool = true };
                case 'f': Expect(s, ref i, "false"); return new Node { Bool = false };
                case 'n': Expect(s, ref i, "null"); return new Node { IsNull = true };
                default: return new Node { String = ParseNumber(s, ref i) };
            }
        }

        private static Node ParseObject(string s, ref int i)
        {
            var node = new Node { Object = new Dictionary<string, Node>(StringComparer.Ordinal) };
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return node; }

            while (true)
            {
                SkipWs(s, ref i);
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' in JSON object.");
                i++;
                node.Object[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return node; }
                throw new FormatException("Expected ',' or '}' in JSON object.");
            }
        }

        private static Node ParseArray(string s, ref int i)
        {
            var node = new Node { Array = new List<Node>() };
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return node; }

            while (true)
            {
                node.Array.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return node; }
                throw new FormatException("Expected ',' or ']' in JSON array.");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string in JSON.");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
                i++;
            }
            i++; // closing quote
            return sb.ToString();
        }

        private static string ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || "+-.eE".IndexOf(s[i]) >= 0)) i++;
            if (i == start) throw new FormatException("Unrecognised JSON token.");
            return s.Substring(start, i - start);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException("Expected '" + literal + "' in JSON.");
            i += literal.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
