// Original Contra City GUISkins (New Era Resources/gui/skins, same as Penta GUISkinManager) and style lookup.
using System.Collections.Generic;
using UnityEngine;

public static class Skins
{
    static readonly Dictionary<string, GUISkin> cache = new Dictionary<string, GUISkin>();
    static readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();

    public static GUISkin Get(string name)
    {
        if (!cache.TryGetValue(name, out var s)) { s = Resources.Load<GUISkin>("gui/skins/" + name); cache[name] = s; if (!s) Debug.LogWarning("[Skins] missing " + name); else UseRealBold(s); }
        return s;
    }

    /// <summary>Style "Skin.style"; falls back to an empty style so layout never breaks.</summary>
    public static GUIStyle S(string skin, string style)
    {
        string key = skin + "." + style;
        if (styles.TryGetValue(key, out var st)) return st;
        var s = Get(skin);
        st = s != null ? s.FindStyle(style) : null;
        if (st == null) { Debug.LogWarning("[Skins] missing style " + key); st = new GUIStyle(); }
        styles[key] = st;
        return st;
    }

    public static GUISkin Main => Get("Main");

    /// <summary>New Era renders Bold Tahoma with the real Tahoma Bold face (pixel-checked against its tab bar, ne_ui2/010_hq);
    /// Unity 6 would synthesize a thinner fake bold from tahoma.ttf, so Bold styles get tahomabd.ttf with a Normal style.</summary>
    static Font tahomaBold;
    /// <summary>For styles built in code with fontStyle = Bold: the real Tahoma Bold face instead of a synthesized (or doubled) bold.</summary>
    public static GUIStyle FixBold(GUIStyle st)
    {
        if (tahomaBold == null) tahomaBold = Resources.Load<Font>("gui/battle/prefab/fonts/tahomabd");
        if (st == null || tahomaBold == null || st.font == null || !st.font.name.ToLowerInvariant().Contains("tahoma")) return st;
        if (st.fontStyle == FontStyle.Bold) { st.font = tahomaBold; st.fontStyle = FontStyle.Normal; }
        else if (st.fontStyle == FontStyle.BoldAndItalic) { st.font = tahomaBold; st.fontStyle = FontStyle.Italic; }
        return st;
    }
    static void UseRealBold(GUISkin sk)
    {
        if (tahomaBold == null) tahomaBold = Resources.Load<Font>("gui/battle/prefab/fonts/tahomabd");
        if (tahomaBold == null) return;
        var all = new List<GUIStyle>(sk.customStyles) { sk.label, sk.button, sk.box, sk.textField, sk.textArea, sk.toggle, sk.window, sk.horizontalSlider, sk.verticalSlider };
        foreach (var st in all)
        {
            if (st == null) continue;
            var f = st.font != null ? st.font : sk.font;
            if (f == null || !f.name.ToLowerInvariant().Contains("tahoma") || f.name.ToLowerInvariant().Contains("tahomabd")) continue;
            if (st.fontStyle == FontStyle.Bold) { st.font = tahomaBold; st.fontStyle = FontStyle.Normal; }
            else if (st.fontStyle == FontStyle.BoldAndItalic) { st.font = tahomaBold; st.fontStyle = FontStyle.Italic; }
        }
    }

    /// <summary>Legacy (Unity 4/5, New Era) text width: integer sum of CharacterInfo.advance plus horizontal padding.
    /// Unity 6 CalcSize returns fractional widths that round up, so auto-sized tabs come out 1 px wider than New Era
    /// (probe: Главная 69 / Штаб 45 / Магазин 70 … equal to the New Era tab text widths).</summary>
    public static float LegacyWidth(GUIStyle st, string text)
    {
        var f = st.font != null ? st.font : GUI.skin.font;
        if (f == null || string.IsNullOrEmpty(text)) return st.CalcSize(new GUIContent(text)).x;
        int size = st.fontSize > 0 ? st.fontSize : f.fontSize;
        f.RequestCharactersInTexture(text, size, st.fontStyle);
        int w = 0;
        foreach (var ch in text) if (f.GetCharacterInfo(ch, out var ci, size, st.fontStyle)) w += ci.advance;
        return w + st.padding.horizontal;
    }

    /// <summary>GUILayout.Button sized like New Era (legacy integer text width).</summary>
    public static bool AutoButton(string text, GUIStyle st, params GUILayoutOption[] opts)
    {
        var o = new GUILayoutOption[opts.Length + 1]; opts.CopyTo(o, 0); o[opts.Length] = GUILayout.Width(LegacyWidth(st, text));
        return GUILayout.Button(text, st, o);
    }

    /// <summary>GUILayout.Label sized like New Era (legacy integer text width).</summary>
    public static void AutoLabel(string text, GUIStyle st, params GUILayoutOption[] opts)
    {
        var o = new GUILayoutOption[opts.Length + 1]; opts.CopyTo(o, 0); o[opts.Length] = GUILayout.Width(LegacyWidth(st, text));
        GUILayout.Label(text, st, o);
    }

    /// <summary>Old Unity (New Era) rounds the origin of a GUI group (GUILayout area, GUI.Window) to a whole pixel in screen
    /// space, half up: x.5 goes right, a GUI y of y.5 goes up (GL y is flipped). Unity 6 keeps the fraction, so a group centred
    /// with an odd size (select team 449 wide, loading window 515 high) landed 1 px off. Snap the rect of such groups.
    /// The group's own background is still drawn at the exact (fractional) rect — only the children move.</summary>
    public static Rect Snap(Rect r) => new Rect(Mathf.Floor(r.x + 0.5f), Mathf.Ceil(r.y - 0.5f), r.width, r.height);

    static readonly Dictionary<GUIStyle, GUIStyle> noBg = new Dictionary<GUIStyle, GUIStyle>();
    /// <summary>Copy of a style without backgrounds (keeps padding/border for the layout).</summary>
    public static GUIStyle NoBg(GUIStyle s)
    {
        if (s == null) return GUIStyle.none;
        if (!noBg.TryGetValue(s, out var c))
        {
            c = new GUIStyle(s);
            c.normal.background = c.hover.background = c.active.background = c.focused.background = null;
            c.onNormal.background = c.onHover.background = c.onActive.background = c.onFocused.background = null;
            noBg[s] = c;
        }
        return c;
    }

    /// <summary>GUILayout.BeginArea(rect, none, style) with New Era rasterisation: background at rect, content group snapped.</summary>
    public static void BeginArea(Rect r, GUIStyle style)
    {
        if (Event.current.type == EventType.Repaint && style != null) style.Draw(r, false, false, false, false);
        GUILayout.BeginArea(Snap(r), GUIContent.none, NoBg(style));
    }

    /// <summary>GUI.Window with New Era rasterisation: the window background at the exact rect, its content group snapped.</summary>
    public static void Window(int id, Rect r, GUI.WindowFunction f, GUIStyle style)
    {
        var s = Snap(r);
        var bgRect = new Rect(r.x - s.x, r.y - s.y, r.width, r.height);
        // (same size as the original rect: a wider window would move right-aligned layout content)
        GUI.Window(id, s, i => { if (Event.current.type == EventType.Repaint && style != null) style.Draw(bgRect, false, false, false, false); f(i); }, GUIContent.none, NoBg(style));
    }

    public static GUIStyle Bg(string n) => S("Backgound", n);
    public static GUIStyle Btn(string n) => S("Buttons", n);
    public static GUIStyle Txt(string n) => S("Text", n);
    public static GUIStyle Lbl(string n) => S("Label", n);
    public static GUIStyle Ico(string n) => S("Ico", n);
    public static GUIStyle Hov(string n) => S("Hover", n);
    public static GUIStyle Sep(string n) => S("Separators", n);
    public static GUIStyle Fix(string n) => S("MainMenuCameraFix", n);

    /// <summary>GUITextShadow: shadow style drawn at ±1 px in 4 directions, then the main style on top.</summary>
    public static void ShadowLabel(Rect r, string text, GUIStyle main, GUIStyle shadow)
    {
        // 嘃.TextShadow: GUI.Box with the shadow style at x-1, x+1, y-1, y+1, then the main style
        if (shadow != null)
        {
            GUI.Box(new Rect(r.x - 1, r.y, r.width, r.height), text, shadow);
            GUI.Box(new Rect(r.x + 1, r.y, r.width, r.height), text, shadow);
            GUI.Box(new Rect(r.x, r.y - 1, r.width, r.height), text, shadow);
            GUI.Box(new Rect(r.x, r.y + 1, r.width, r.height), text, shadow);
        }
        GUI.Box(r, text, main);
    }
}
