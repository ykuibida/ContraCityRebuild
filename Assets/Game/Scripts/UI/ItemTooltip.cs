// Item tooltip in the New Era style (see Shop_Screens/wear/*_tip.png): orange name, grey slot line, grey description,
// green bonus lines, red level requirement, price, set list and set bonus. Weapons: ammo + three star rows.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public static class ItemTooltip
{
    public const float Width = 300f;
    public static Rect LastRect;   // design-space rect of the last drawn tooltip (UI test crops it)

    public static void Draw(GameItem it, Vector2 at)
    {
        var lines = Build(it, out float h);
        float W = Screen.width / UIKit.Scale;
        float x = Mathf.Min(at.x + 18, W - Width - 6), y = Mathf.Min(at.y + 18, UIKit.DesignH - h - 6);
        var r = new Rect(x, y, Width, h);
        LastRect = r;
        UIKit.Fill(r, new Color(0.16f, 0.16f, 0.16f, 0.97f));
        UIKit.Outline(r, new Color(0.38f, 0.38f, 0.38f));
        float cy = y + 8;
        foreach (var l in lines)
        {
            if (l.stars >= 0)
            {
                GUI.Label(new Rect(x + 10, cy, 170, l.h), l.text, l.style);
                DrawStars(new Rect(x + Width - 95, cy + 2, 85, 13), l.stars);
            }
            else if (l.right != null)
            {
                GUI.Label(new Rect(x + 10, cy, Width - 20, l.h), l.text, l.style);
                GUI.Label(new Rect(x + 10, cy, Width - 20, l.h), l.right, UIKit.Label(13, FontStyle.Bold, TextAnchor.UpperRight));
            }
            else GUI.Label(new Rect(x + 10, cy, Width - 20, l.h), l.text, l.style);
            cy += l.h;
        }
    }

    struct Line { public string text, right; public GUIStyle style; public float h; public float stars; }

    static void DrawStars(Rect r, float stars)
    {
        var gold = UIKit.Tex("icons/star_gold"); var black = UIKit.Tex("icons/star_black");
        for (int i = 0; i < 5; i++)
        {
            var sr = new Rect(r.x + i * 17, r.y, 13, 13);
            if (black) GUI.DrawTexture(sr, black);
            float f = Mathf.Clamp01(stars - i);
            if (f > 0 && gold) GUI.DrawTextureWithTexCoords(new Rect(sr.x, sr.y, sr.width * f, sr.height), gold, new Rect(0, 0, f, 1));
        }
    }

    static List<Line> Build(GameItem it, out float height)
    {
        var L = new List<Line>();
        var name = UIKit.Label(13, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Orange);
        var slot = UIKit.Label(12, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Grey);
        var desc = UIKit.Label(11, FontStyle.Normal, TextAnchor.UpperLeft, UIKit.Grey, true);
        var green = UIKit.Label(11, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Green, true);
        var red = UIKit.Label(11, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Red, true);
        var white = UIKit.Label(13, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
        var sub = UIKit.Label(11, FontStyle.Normal, TextAnchor.UpperLeft, UIKit.Grey);
        float W = Width - 20;
        void Add(string t, GUIStyle s, float pad = 2f) { if (string.IsNullOrEmpty(t)) return; float h = s.CalcHeight(new GUIContent(t), W) + pad; L.Add(new Line { text = t, style = s, h = h, stars = -1 }); }

        Add(it.Name, name, 1);
        Add(it.SlotName, slot, 4);
        if (it.Kind == ItemKind.Weapon)
        {
            if (it.Wt > 2) L.Add(new Line { text = "Боезапас", right = $"{it.Int("ammo")} / {Mathf.Max(0, it.Int("ammo_tot") - it.Int("ammo"))}", style = white, h = 18, stars = -1 });
            L.Add(new Line { text = "Скорострельность", style = white, h = 18, stars = it.Has("dStRa") ? it.Float("dStRa") : it.Float("stRa") });
            L.Add(new Line { text = "Дальнобойность", style = white, h = 18, stars = it.Has("dStDi") ? it.Float("dStDi") : it.Float("stDi") });
            L.Add(new Line { text = "Урон", style = white, h = 20, stars = it.Has("dStDa") ? it.Float("dStDa") : it.Float("stDa") });
        }
        Add(it.Description, desc, 3);
        if (it.Kind == ItemKind.Wear || it.Kind == ItemKind.Weapon) Add(it.Bonuses, green, 3);
        else Add(it.Bonuses != it.Description ? it.Bonuses : "", green, 3);
        var p = Profile.Current;
        if (it.Level > p.Level) Add($"Недоступно: требуется уровень {it.Level}", red, 4);
        if (it.Kind == ItemKind.Booster || it.Kind == ItemKind.Taunt)
        {
            L.Add(new Line { text = "Цена 1 день", right = it.Price1.ToString(), style = white, h = 18, stars = -1 });
            L.Add(new Line { text = "Цена 7 дней", right = it.Price7.ToString(), style = white, h = 18, stars = -1 });
            L.Add(new Line { text = "Цена 30 дней", right = it.Price30.ToString(), style = white, h = 20, stars = -1 });
        }
        else if (!it.IsDefault) L.Add(new Line { text = "Цена:", right = it.Price.ToString(), style = white, h = 22, stars = -1 });

        // New Era wear "desca" already contains the set list, set bonus and "Бонус Наследия" — only add ours when it does not.
        if (it.Kind == ItemKind.Wear && it.SetName != "" && !it.Bonuses.Contains("Набор"))
        {
            Add($"Набор \"{it.SetName}\":", UIKit.Label(11, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Grey), 1);
            var sb = new StringBuilder();
            foreach (var id in it.SetItems)
            {
                var si = ItemDB.Get(ItemKind.Wear, id);
                string n = si != null ? si.Name : "#" + id;
                bool have = si != null && p.Owns(si);
                sb.Append(have ? $"<color=#9edc26>- {n}</color>\n" : $"- {n}\n");
            }
            Add(sb.ToString().TrimEnd('\n'), sub, 3);
            var set = ItemDB.Sets.FirstOrDefault(s => s.TryGetValue("id", out var sid) && sid.ToString() == it.SetId || it.Str("assemblage_id") != "" && s["id"].ToString() == it.Str("assemblage_id"));
            string bonus = set != null ? (set.TryGetValue("desca_ru", out var d) && d is string ds && ds != "" ? ds : set.TryGetValue("desca", out var d2) ? d2 as string : "") : "";
            if (!string.IsNullOrEmpty(bonus)) { Add("Бонус за собранный набор:", UIKit.Label(11, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Grey), 1); Add(bonus, green, 3); }
        }
        if (it.Kind == ItemKind.Weapon && it.UpgradeText != "") { Add("Улучшение:", UIKit.Label(11, FontStyle.Bold, TextAnchor.UpperLeft, UIKit.Grey), 1); Add(it.UpgradeText, green, 3); }
        height = 16f;
        foreach (var l in L) height += l.h;
        return L;
    }
}
