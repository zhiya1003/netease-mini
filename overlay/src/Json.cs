using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mini
{
    public enum JKind { Null, Bool, Num, Str, Arr, Obj }

    public sealed class JValue
    {
        public JKind Kind = JKind.Null;
        public bool B;
        public double N;
        public string S;
        public List<JValue> Arr;
        public Dictionary<string, JValue> Obj;

        public bool IsNull { get { return Kind == JKind.Null; } }
        public bool Has(string k) { return Obj != null && Obj.ContainsKey(k); }
        public JValue Get(string k) { JValue v; if (Obj != null && Obj.TryGetValue(k, out v)) return v; return null; }
        public string AsStr(string def) { return Kind == JKind.Str ? S : (Kind == JKind.Null ? def : S); }
        public double AsNum(double def) { return Kind == JKind.Num ? N : def; }
        public bool AsBool(bool def) { return Kind == JKind.Bool ? B : def; }
        public List<JValue> AsArr() { return Arr != null ? Arr : new List<JValue>(); }
        public string Str(string k) { JValue v = Get(k); return v == null ? null : (v.Kind == JKind.Str ? v.S : null); }
        public double Num(string k, double def) { JValue v = Get(k); return (v == null || v.Kind != JKind.Num) ? def : v.N; }
        public bool Bool(string k, bool def) { JValue v = Get(k); return (v == null || v.Kind != JKind.Bool) ? def : v.B; }
        public JValue ObjOf(string k) { JValue v = Get(k); return (v == null || v.Kind != JKind.Obj) ? null : v; }
        public JValue ArrOf(string k) { JValue v = Get(k); return (v == null || v.Kind != JKind.Arr) ? null : v; }
        public JValue Idx(int i) { return (Arr != null && i >= 0 && i < Arr.Count) ? Arr[i] : null; }
    }

    public sealed class JsonParser
    {
        private readonly string t;
        private int i;

        private JsonParser(string text) { t = text ?? ""; i = 0; }

        public static JValue Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            JsonParser p = new JsonParser(text);
            p.Skip();
            JValue v = p.Value();
            return v;
        }

        public static bool TryParse(string text, out JValue v)
        {
            try { v = Parse(text); return v != null; }
            catch { v = null; return false; }
        }

        private void Skip() { while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++; }
        private void Expect(char c) { Skip(); if (i < t.Length && t[i] == c) { i++; return; } throw new FormatException("expected " + c + " at " + i); }
        private char Peek() { Skip(); return i < t.Length ? t[i] : '\0'; }

        private JValue Value()
        {
            char c = Peek();
            switch (c)
            {
                case '{': return Obj();
                case '[': return Arr();
                case '"': return StrV();
                case 't': return Lit("true", true, JKind.Bool);
                case 'f': return Lit("false", false, JKind.Bool);
                case 'n': Lit("null", false, JKind.Null); return New(JKind.Null);
                default: return NumV();
            }
        }

        private static JValue New(JKind k) { JValue v = new JValue(); v.Kind = k; return v; }

        private JValue Lit(string word, bool val, JKind k)
        {
            if (string.Compare(t, i, word, 0, word.Length, StringComparison.Ordinal) == 0) { i += word.Length; JValue v = New(k); v.B = val; return v; }
            throw new FormatException("bad literal at " + i);
        }

        private JValue NumV()
        {
            int start = i;
            if (i < t.Length && (t[i] == '-' || t[i] == '+')) i++;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.' || t[i] == 'e' || t[i] == 'E' || t[i] == '-' || t[i] == '+')) i++;
            string raw = t.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw new FormatException("bad number '" + raw + "'");
            JValue v = New(JKind.Num); v.N = d; return v;
        }

        private JValue StrV()
        {
            Expect('"');
            StringBuilder sb = new StringBuilder();
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= t.Length) break;
                    char e = t[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= t.Length) { sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber)); i += 4; }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            JValue v = New(JKind.Str); v.S = sb.ToString(); return v;
        }

        private JValue Obj()
        {
            Expect('{');
            JValue v = New(JKind.Obj);
            v.Obj = new Dictionary<string, JValue>();
            if (Peek() == '}') { i++; return v; }
            while (true)
            {
                Skip();
                JValue key = StrV();
                Expect(':');
                JValue val = Value();
                v.Obj[key.S] = val;
                char c = Peek();
                if (c == ',') { i++; continue; }
                if (c == '}') { i++; break; }
                throw new FormatException("bad object at " + i);
            }
            return v;
        }

        private JValue Arr()
        {
            Expect('[');
            JValue v = New(JKind.Arr);
            v.Arr = new List<JValue>();
            if (Peek() == ']') { i++; return v; }
            while (true)
            {
                v.Arr.Add(Value());
                char c = Peek();
                if (c == ',') { i++; continue; }
                if (c == ']') { i++; break; }
                throw new FormatException("bad array at " + i);
            }
            return v;
        }
    }

    // Re-serialise a parsed tree (used when the page returns an object instead of a string).
    public static class Json
    {
        public static string Serialize(JValue v)
        {
            StringBuilder b = new StringBuilder();
            Write(b, v);
            return b.ToString();
        }

        private static void Write(StringBuilder b, JValue v)
        {
            if (v == null) { b.Append("null"); return; }
            switch (v.Kind)
            {
                case JKind.Null: b.Append("null"); break;
                case JKind.Bool: b.Append(v.B ? "true" : "false"); break;
                case JKind.Num: b.Append(v.N.ToString("0.######", CultureInfo.InvariantCulture)); break;
                case JKind.Str: b.Append(JsonWriter.Encode(v.S)); break;
                case JKind.Arr:
                    b.Append('[');
                    bool fa = false;
                    foreach (JValue x in v.AsArr()) { if (fa) b.Append(','); fa = true; Write(b, x); }
                    b.Append(']');
                    break;
                case JKind.Obj:
                    b.Append('{');
                    bool fo = false;
                    foreach (KeyValuePair<string, JValue> kv in v.Obj)
                    {
                        if (fo) b.Append(',');
                        fo = true;
                        b.Append(JsonWriter.Encode(kv.Key)).Append(':');
                        Write(b, kv.Value);
                    }
                    b.Append('}');
                    break;
            }
        }
    }

    // Lightweight writer used for Command / control replies.
    public sealed class JsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder();

        public static string Encode(string s)
        {
            StringBuilder o = new StringBuilder();
            o.Append('"');
            if (s != null)
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': o.Append("\\\""); break;
                        case '\\': o.Append("\\\\"); break;
                        case '\n': o.Append("\\n"); break;
                        case '\r': o.Append("\\r"); break;
                        case '\t': o.Append("\\t"); break;
                        case '\b': o.Append("\\b"); break;
                        case '\f': o.Append("\\f"); break;
                        default:
                            if (c < 0x20) o.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else o.Append(c);
                            break;
                    }
                }
            o.Append('"');
            return o.ToString();
        }

        public JsonWriter Begin() { sb.Append('{'); first = true; return this; }
        public JsonWriter End() { sb.Append('}'); return this; }
        public JsonWriter Kv(string k) { if (!first) sb.Append(','); first = false; sb.Append(JsonWriter.Encode(k)).Append(':'); return this; }
        public JsonWriter Raw(string v) { sb.Append(v); return this; }
        public JsonWriter Str(string k, string v) { Kv(k); sb.Append(v == null ? "null" : Encode(v)); return this; }
        public JsonWriter Num(string k, double v) { Kv(k); sb.Append(v.ToString("0.######", CultureInfo.InvariantCulture)); return this; }
        public JsonWriter B(string k, bool v) { Kv(k); sb.Append(v ? "true" : "false"); return this; }
        public JsonWriter Null(string k) { Kv(k); sb.Append("null"); return this; }
        public override string ToString() { return sb.ToString(); }
        private bool first;
    }
}
