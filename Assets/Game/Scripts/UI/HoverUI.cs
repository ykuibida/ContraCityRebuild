// Item tooltip = original GUIHover (Penta GUIHover.cs / New Era GUIHover.cs, spec §3.6):
// shown after 0.8 s over the cell's top 58 px, 350 px wide, centred under the cell, Hover skin styles, New Era order of lines,
// "Бонус Наследия" side panel while Shift is held.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class HoverUI
{
    public const float ShowDelay = 0.8f, HideDelay = 0.3f, Width = 350f;
    static GameItem item; static Rect cell; static float since, lastSeen = -10f;
    static string text; static Rect textCell;
    public static bool Enabled = true;
    public static GameItem ForceItem; public static Rect ForceCell;   // UI test
    public static Rect LastRect;

    /// <summary>Register hover for this frame (cell rect in screen coordinates).</summary>
    public static void Hover(GameItem it, Rect screenCell)
    {
        if (!Enabled || Event.current.type != EventType.Repaint) return;
        if (!screenCell.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)) && !screenCell.Contains(Event.current.mousePosition)) return;
        if (item != it) { item = it; since = Time.realtimeSinceStartup; }
        cell = screenCell; lastSeen = Time.realtimeSinceStartup;
    }

    public static void HoverText(string t, Rect screenCell)
    {
        if (!Enabled || Event.current.type != EventType.Repaint) return;
        if (!screenCell.Contains(Event.current.mousePosition)) return;
        text = t; textCell = screenCell; lastSeen = Time.realtimeSinceStartup;
    }

    /// <summary>Call once at the end of OnGUI, outside any area/scroll view.</summary>
    public static void Draw()
    {
        GUI.depth = 0;
        if (ForceItem != null) { DrawItem(ForceItem, ForceCell); return; }
        float now = Time.realtimeSinceStartup;
        // Decide at Layout and keep it for the rest of the frame: a time check that flips between Layout and Repaint
        // breaks GUILayout ("Getting control N's position in a group with only N controls").
        if (Event.current.type == EventType.Layout)
        {
            drawText = text != null && now - lastSeen < 0.05f ? text : null; drawTextCell = textCell;
            drawItem = item != null && now - since >= ShowDelay ? item : null; drawCell = cell;
        }
        if (drawText != null) DrawString(drawText, drawTextCell);
        if (drawItem != null) DrawItem(drawItem, drawCell);
        if (Event.current.type == EventType.Repaint && now - lastSeen > HideDelay) { item = null; text = null; }
    }
    static string drawText; static Rect drawTextCell, drawCell; static GameItem drawItem;

    static void DrawString(string t, Rect c)
    {
        var st = Skins.Hov("string"); var bg = Skins.Hov("backgound");
        var size = st.CalcSize(new GUIContent(t));
        var r = new Rect(c.x + c.width / 2 - (size.x + 24) / 2, c.yMax + 2, size.x + 24, size.y + 22);
        r.x = Mathf.Clamp(r.x, 0, Screen.width - r.width);
        GUILayout.BeginArea(r, bg); GUILayout.Label(t, st); GUILayout.EndArea();
    }

    // ---------------- item tooltip ----------------
    struct Line { public string a, b; public GUIStyle sa, sb; public float space; public float stars; public bool cost; }

    static void DrawItem(GameItem it, Rect c)
    {
        var lines = Build(it, out var legacy);
        var bg = Skins.Hov("backgound");
        float inner = Width - bg.padding.horizontal;
        float h = bg.padding.vertical;
        foreach (var l in lines) h += l.space + LineH(l, inner);
        var r = new Rect(c.x + c.width / 2 - Width / 2, c.yMax, Width, h);
        if (r.yMax > Screen.height) { r.y = c.y - h - 27; if (r.y < 0) { r.y = c.y; r.x = c.x + c.width / 2 + Width / 2 + c.width / 2 - Width; } }
        r.x = Mathf.Clamp(r.x, 0, Screen.width - r.width); r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, Screen.height - r.height));
        LastRect = r;
        GUILayout.BeginArea(r);
        GUILayout.BeginVertical(bg);
        foreach (var l in lines)
        {
            if (l.space > 0) GUILayout.Space(l.space);
            if (l.stars >= 0)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label(l.a, l.sa); GUILayout.FlexibleSpace(); Stars(l.stars); GUILayout.EndHorizontal();
            }
            else if (l.cost)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label(l.a, l.sa); GUILayout.FlexibleSpace();
                GUILayout.Label(GUIContent.none, Skins.Hov("costIcon")); GUILayout.Label(l.b, l.sb); GUILayout.EndHorizontal();
            }
            else if (l.b != null)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label(l.a, l.sa); GUILayout.FlexibleSpace(); GUILayout.Label(l.b, l.sb); GUILayout.EndHorizontal();
            }
            else GUILayout.Label(l.a, l.sa);
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
        if (legacy.Count > 0 && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) DrawLegacy(legacy, r);
    }

    static float LineH(Line l, float w)
    {
        if (l.stars >= 0) return 20;
        var c = new GUIContent(l.a);
        float h = l.sa.CalcHeight(c, w);
        if (l.sa.fixedHeight > 0) h = l.sa.fixedHeight;
        return h + l.sa.margin.vertical;
    }

    static void Stars(float n)
    {
        var on = Skins.Lbl("starActive"); var off = Skins.Lbl("starNone");
        for (int i = 0; i < 5; i++)
        {
            float f = Mathf.Clamp01(n - i);
            GUILayout.Label(GUIContent.none, off, GUILayout.Width(14), GUILayout.Height(14));
            if (f > 0f)
            {
                var r = GUILayoutUtility.GetLastRect();
                GUI.BeginGroup(new Rect(r.x, r.y, r.width * f, r.height));      // New Era: fractional stars clipped by a group
                GUI.Label(new Rect(0, 0, r.width, r.height), GUIContent.none, on);
                GUI.EndGroup();
            }
        }
    }

    static void DrawLegacy(List<string> rows, Rect main)
    {
        var bg = Skins.Hov("backgound"); var head = Skins.Hov("itemAssembleName"); var st = Skins.Hov("itemDescAdditionalSmall");
        float w = rows.Select(x => st.CalcSize(new GUIContent(x)).x).DefaultIfEmpty(0).Max();
        w = Mathf.Max(w, head.CalcSize(new GUIContent("Бонус Наследия:")).x) + 34;
        float h = bg.padding.vertical + 20 + rows.Count * 14;
        var r = new Rect(main.xMax + 8, main.y, w, h);
        if (r.xMax > Screen.width) r.x = main.x - 8 - w;
        GUILayout.BeginArea(r); GUILayout.BeginVertical(bg);
        GUILayout.Label("Бонус Наследия:", head); GUILayout.Space(4);
        foreach (var x in rows) GUILayout.Label(x, st);
        GUILayout.EndVertical(); GUILayout.EndArea();
    }

    static List<Line> Build(GameItem it, out List<string> legacy)
    {
        var L = new List<Line>();
        legacy = new List<string>();
        void Add(string a, string style, float space = 0) { if (!string.IsNullOrEmpty(a)) L.Add(new Line { a = a, sa = Skins.Hov(style), space = space, stars = -1 }); }
        var p = Profile.Current;
        Add(it.Name, "name");
        Add(it.SlotName, "itemType");
        if (it.Kind == ItemKind.Weapon)
        {
            L.Add(new Line { space = 3, stars = -1, sa = Skins.Hov("valueName"), sb = Skins.Hov("value"), a = it.Wt <= 2 ? "" : "Боезапас",
                b = it.Wt <= 2 ? null : $"{it.Int("ammo")} /  {Mathf.Max(0, it.Int("ammo_tot") - it.Int("ammo"))}" });
            if (it.Wt <= 2) L.RemoveAt(L.Count - 1);
            float Star(string d, string s) => it.Has(d) ? it.Float(d) : it.Float(s);
            bool first = true;
            foreach (var (n, d, s) in new[] { ("Скорострельность", "dStRa", "stRa"), ("Дальнобойность", "dStDi", "stDi"), ("Урон", "dStDa", "stDa") })
            {
                float v = Star(d, s); if (v <= 0) continue;
                L.Add(new Line { a = n, sa = Skins.Hov("valueName"), stars = v, space = first ? 4 : 0 }); first = false;
            }
        }
        Add(it.Description, "itemDesc", 3);
        // New Era "desca": own bonuses, then optional set block ("Набор …"), then optional "Бонус наследия:" block.
        string desca = it.Bonuses;
        string own = desca, setBlock = "";
        int li = desca.IndexOf("Бонус наследия", System.StringComparison.OrdinalIgnoreCase);
        if (li >= 0) { var lg = desca.Substring(li); own = desca.Substring(0, li); legacy = lg.Split('\n').Skip(1).Select(x => x.Trim()).Where(x => x != "").ToList(); }
        int si = own.IndexOf("Набор \"", System.StringComparison.Ordinal);
        if (si >= 0) { setBlock = own.Substring(si); own = own.Substring(0, si); }
        if (it.Kind != ItemKind.Booster && it.Kind != ItemKind.Taunt) Add(own.Trim(), "itemDescAdditional", 3);
        if (it.Level > p.Level) Add($"Недоступно: требуется уровень {it.Level}", "itemLowLevel", 3);
        if (it.Kind == ItemKind.Booster || it.Kind == ItemKind.Taunt)
        {
            bool firstCost = true;
            foreach (var (n, v) in new[] { ("Цена 1 день", it.Price1), ("Цена 7 дней", it.Price7), ("Цена 30 дней", it.Price30), ("Цена навсегда", it.Price) })
            {
                if (v <= 0) continue;
                L.Add(new Line { a = n, b = v.ToString(), sa = Skins.Hov("costName"), sb = Skins.Hov("costValue"), cost = true, space = firstCost ? 7 : 3, stars = -1 });
                firstCost = false;
            }
        }
        else if (!it.IsDefault)
            L.Add(new Line { a = "Цена:", b = it.Price.ToString(), sa = Skins.Hov("costName"), sb = Skins.Hov("costValue"), cost = true, space = it.Kind == ItemKind.Weapon ? 7 : 9, stars = -1 });
        if (setBlock != "")
        {
            bool firstSet = true; bool inBonus = false;
            foreach (var raw in setBlock.Split('\n'))
            {
                var x = raw.Trim(); if (x == "") continue;
                if (x.StartsWith("Набор")) { Add(x, "itemAssembleName", firstSet ? 12 : 4); firstSet = false; }
                else if (x.StartsWith("Бонус за")) { Add(x, "itemAssembleName", inBonus ? 0 : 2); inBonus = true; }   // printed as often as the server text has it (New Era)
                else if (!inBonus)
                {
                    string name = x.TrimStart('-', ' ', '+');
                    bool on = p.EquippedWear.Values.Select(id => ItemDB.Get(ItemKind.Wear, id)?.Name).Contains(name);
                    Add((on ? "+ " : "- ") + name, on ? "itemAssembleOn" : "itemAssembleOff", 3);
                }
                else Add(x, "itemDescAdditionalSmall", 3);
            }
        }
        else if (it.Kind == ItemKind.Wear && it.SetItems.Count > 0)
        {
            // desca without the set block: New Era builds it from the set data (set_items + assemblage bonus text)
            Add($"Набор \"{it.SetName}\":", "itemAssembleName", 12);
            foreach (var sid in it.SetItems)
            {
                var sw = ItemDB.Get(ItemKind.Wear, sid); if (sw == null) continue;
                bool on = p.EquippedWear.Values.Contains(sid);
                Add((on ? "+ " : "- ") + sw.Name, on ? "itemAssembleOn" : "itemAssembleOff", 3);
            }
            var set = ItemDB.Sets.FirstOrDefault(x => x.TryGetValue("id", out var sv) && sv != null && (sv.ToString() == it.SetId || sv.ToString() == it.Str("assemblage_id")));
            string bonus = set != null && set.TryGetValue("desca_ru", out var b) && b is string bs && bs != "" ? bs : set != null && set.TryGetValue("desca", out var b2) ? b2 as string : "";
            if (!string.IsNullOrEmpty(bonus))
            {
                Add("Бонус за собранный набор:", "itemAssembleName", 2);
                foreach (var line in bonus.Split('\n')) if (line.Trim() != "" && !line.StartsWith("Бонус за")) Add(line.Trim(), "itemDescAdditionalSmall", 3);
            }
        }
        if (legacy.Count > 0) Add("Shift для дополнительной информации", "itemDesc", 6);
        return L;
    }
}
