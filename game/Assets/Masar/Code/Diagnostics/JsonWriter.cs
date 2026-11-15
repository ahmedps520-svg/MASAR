using System.Globalization;
using System.Text;

namespace Masar.Diagnostics
{
    /// <summary>Minimal streaming JSON writer (Unity's JsonUtility can't do nested or dynamic shapes).</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(64 * 1024);
        private bool _needComma;

        public JsonWriter BeginObject() { Sep(); _sb.Append('{'); _needComma = false; return this; }
        public JsonWriter EndObject() { _sb.Append('}'); _needComma = true; return this; }
        public JsonWriter BeginArray() { Sep(); _sb.Append('['); _needComma = false; return this; }
        public JsonWriter EndArray() { _sb.Append(']'); _needComma = true; return this; }

        public JsonWriter Key(string k) { Sep(); Str(k); _sb.Append(':'); _needComma = false; return this; }

        public JsonWriter Value(string v) { Sep(); if (v == null) _sb.Append("null"); else Str(v); _needComma = true; return this; }
        public JsonWriter Value(bool v) { Sep(); _sb.Append(v ? "true" : "false"); _needComma = true; return this; }
        public JsonWriter Value(long v) { Sep(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; return this; }
        public JsonWriter Value(double v)
        {
            Sep();
            if (double.IsNaN(v) || double.IsInfinity(v)) _sb.Append("null");
            else _sb.Append(v.ToString("0.###", CultureInfo.InvariantCulture));
            _needComma = true;
            return this;
        }
        /// <summary>Inserts pre-serialized JSON (e.g. from the native plugin or JsonUtility).</summary>
        public JsonWriter Raw(string json) { Sep(); _sb.Append(string.IsNullOrEmpty(json) ? "null" : json); _needComma = true; return this; }

        public JsonWriter Prop(string k, string v) => Key(k).Value(v);
        public JsonWriter Prop(string k, bool v) => Key(k).Value(v);
        public JsonWriter Prop(string k, long v) => Key(k).Value(v);
        public JsonWriter Prop(string k, double v) => Key(k).Value(v);

        private void Sep() { if (_needComma) _sb.Append(','); }

        private void Str(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public override string ToString() => _sb.ToString();
    }
}
