// New Era LanguageManager: Resources/Languages/default.txt (gettext msgid / msgstr, the original Russian file).
// GetText(key) returns the translation or the key itself, GetTextFormat = string.Format over it.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class LanguageManager
{
    static Dictionary<string, string> table;
    static readonly List<string> order = new List<string>();
    /// <summary>All msgids in file order (the achievement list follows it, same order as the New Era server list).</summary>
    public static IReadOnlyList<string> KeysInOrder { get { if (table == null) Load(); return order; } }

    static void Load()
    {
        table = new Dictionary<string, string>();
        var ta = Resources.Load<TextAsset>("Languages/default");
        if (ta == null) { Debug.LogError("[LanguageManager] Languages/default not found"); return; }
        using (var r = new StringReader(ta.text))
        {
            string id = null, str = null, line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.StartsWith("msgid \"")) id = line.Substring(7, line.Length - 8);
                else if (line.StartsWith("msgstr \"")) str = line.Substring(8, line.Length - 9).Replace("\\n", "\n");
                else if (id != null && str != null) { if (!table.ContainsKey(id)) order.Add(id); table[id] = str; id = str = null; }
            }
            if (id != null && str != null) { if (!table.ContainsKey(id)) order.Add(id); table[id] = str; }
        }
    }

    public static string GetText(string key)
    {
        if (table == null) Load();
        if (key == null) return "";
        if (table.TryGetValue(key, out var v)) return v;
        return key.Length > 60 ? key.Replace("\\n", "\n") : key;
    }

    public static string GetTextFormat(string key, params object[] args) => string.Format(GetText(key), args);
}
