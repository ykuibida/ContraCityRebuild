// Small JSON reader/writer (objects -> Dictionary<string, object>, arrays -> List<object>, numbers -> double/long).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        var v = ParseValue(json, ref i);
        return v;
    }

    static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object ParseValue(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object>(); i++;
            Skip(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                string k = ParseString(s, ref i);
                Skip(s, ref i); i++; // ':'
                d[k] = ParseValue(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return d; // '}'
            }
        }
        if (c == '[')
        {
            var l = new List<object>(); i++;
            Skip(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return l; // ']'
            }
        }
        if (c == '"') return ParseString(s, ref i);
        if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int st = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        string num = s.Substring(st, i - st);
        if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv)) return lv;
        return double.Parse(num, CultureInfo.InvariantCulture);
    }

    static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder(); i++; // opening quote
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\')
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Serialize(object o)
    {
        var sb = new StringBuilder();
        Write(sb, o);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object o)
    {
        switch (o)
        {
            case null: sb.Append("null"); break;
            case string str:
                sb.Append('"');
                foreach (char c in str)
                {
                    if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                    else if (c == '\n') sb.Append("\\n");
                    else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                sb.Append('"'); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case IDictionary d:
                sb.Append('{'); bool first = true;
                foreach (DictionaryEntry e in d) { if (!first) sb.Append(','); first = false; Write(sb, e.Key.ToString()); sb.Append(':'); Write(sb, e.Value); }
                sb.Append('}'); break;
            case IEnumerable list:
                sb.Append('['); bool f2 = true;
                foreach (var x in list) { if (!f2) sb.Append(','); f2 = false; Write(sb, x); }
                sb.Append(']'); break;
            case float fl: sb.Append(fl.ToString(CultureInfo.InvariantCulture)); break;
            case double db: sb.Append(db.ToString(CultureInfo.InvariantCulture)); break;
            default: sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); break;
        }
    }
}
