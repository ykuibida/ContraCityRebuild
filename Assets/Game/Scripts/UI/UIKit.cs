// IMGUI helpers: New Era GUI textures/skins from Resources/gui, design-space scaling, simple styled widgets.
using System.Collections.Generic;
using UnityEngine;

public static class UIKit
{
    // New Era draws its lobby in a 1360 x 700 area at the top-left of the window (screenshots 200_state.png, 201_wear_tab.png).
    public const float DesignW = 1360f, DesignH = 700f;
    public static float Scale { get; private set; } = 1f;
    public static Vector2 Offset { get; private set; }

    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, GUISkin> skinCache = new Dictionary<string, GUISkin>();
    static Font tahoma, redOctober;

    public static Texture2D Tex(string path)
    {
        if (!texCache.TryGetValue(path, out var t)) { t = Resources.Load<Texture2D>("gui/" + path); texCache[path] = t; }
        return t;
    }

    public static GUISkin Skin(string name)
    {
        if (!skinCache.TryGetValue(name, out var s)) { s = Resources.Load<GUISkin>("gui/skins/" + name); skinCache[name] = s; }
        return s;
    }

    public static GUIStyle Style(string skin, string style)
    {
        var s = Skin(skin);
        if (s == null) return null;
        var st = s.FindStyle(style);
        return st;
    }

    public static Font Tahoma => tahoma ? tahoma : (tahoma = Resources.Load<Font>("gui/fonts/tahoma"));
    public static Font RedOctober => redOctober ? redOctober : (redOctober = Resources.Load<Font>("gui/fonts/Red October-Regular"));

    /// <summary>Call at the start of OnGUI: fits the design area into the window (keeps aspect, top-centred).</summary>
    public static void BeginDesign(bool fullWidth = false)
    {
        float s = Mathf.Min(Screen.width / DesignW, Screen.height / DesignH);
        Scale = s;
        float w = DesignW * s;
        Offset = new Vector2(fullWidth ? 0f : (Screen.width - w) * 0.5f, 0f);
        GUI.matrix = Matrix4x4.TRS(new Vector3(Offset.x, Offset.y, 0f), Quaternion.identity, new Vector3(s, s, 1f));
    }

    public static void EndDesign() { GUI.matrix = Matrix4x4.identity; }

    public static Vector2 MouseDesign => (Event.current.mousePosition);

    static GUIStyle label, labelBold, labelCenter, box, button, tabStyle;
    public static GUIStyle Label(int size = 12, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft, Color? c = null, bool wrap = false)
    {
        var st = new GUIStyle(GUI.skin.label) { font = Tahoma, fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap, richText = true };
        st.normal.textColor = c ?? Color.white;
        st.padding = new RectOffset(0, 0, 0, 0); st.margin = new RectOffset(0, 0, 0, 0);
        return st;
    }

    public static void Fill(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
    }

    public static void Draw(Rect r, string texPath, ScaleMode mode = ScaleMode.StretchToFill)
    {
        var t = Tex(texPath);
        if (t) GUI.DrawTexture(r, t, mode);
    }

    /// <summary>9-slice draw of a GUI texture (border in texture pixels).</summary>
    public static void Sliced(Rect r, string texPath, int border)
    {
        var t = Tex(texPath);
        if (!t) return;
        var st = new GUIStyle { border = new RectOffset(border, border, border, border) };
        st.normal.background = t;
        if (Event.current.type == EventType.Repaint) st.Draw(r, false, false, false, false);
    }

    public static void Outline(Rect r, Color c, float w = 1f)
    {
        Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
        Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
    }

    public static readonly Color Orange = new Color(1f, 0.60f, 0.0f);
    public static readonly Color Green = new Color(0.62f, 0.86f, 0.15f);
    public static readonly Color Red = new Color(0.95f, 0.15f, 0.15f);
    public static readonly Color Grey = new Color(0.62f, 0.62f, 0.62f);
    public static readonly Color Dark = new Color(0.13f, 0.13f, 0.13f, 0.96f);
    public static readonly Color Panel = new Color(0.78f, 0.71f, 0.62f);

    /// <summary>Flat button (New Era tab look when no texture): returns true on click.</summary>
    public static bool FlatButton(Rect r, string text, bool active, int size = 15)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, active ? Orange : hover ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.17f, 0.17f, 0.17f));
        Outline(r, new Color(0.35f, 0.35f, 0.35f));
        GUI.Label(r, text, Label(size, FontStyle.Bold, TextAnchor.MiddleCenter, active ? Color.black : new Color(0.85f, 0.82f, 0.75f)));
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }
}
