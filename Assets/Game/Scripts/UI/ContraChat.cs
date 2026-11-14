// New Era «Контра Чат» side panel (decrypted client 嘑): port of OnGUI / DrawSidePanel / DrawOnlineHeader /
// DrawMessageColumn / DrawMessageCard / HandleManualChatInput / HandleMouseWheel / HandleScrollbarDrag and the styles of
// EnsureCachedStyles / EnsureExtraStyles / EnsureQuestArt. The art PNGs were embedded as base64 in 嘑; they are extracted to
// Resources/gui/chat/<style>.bytes (Tools/uidiff/extract_chat_art.py) and loaded like 嘋.Load (LoadImage, bilinear, clamp).
// Local build: there is no chat server, so a sent message is only echoed into the local list.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class ContraChat
{
    class Msg
    {
        public string Nick = "", Body = "", Time = "";
        public int Level;
        public bool Own;
        public float RowHeight, RowWidth, WrapWidth;
        public string WrapBody;
    }

    static readonly List<Msg> Messages = new List<Msg>();
    static string inputMessage = "", statusText = "";
    static bool inputCaptureActive, inputAllSelected, chatHidden, chatHiddenLoaded, forceScrollToBottom, chatWasAtBottom = true;
    static Vector2 chatScroll;
    static float pendingWheelDelta, sidePanelWidth, sidePanelHeight;
    static Rect sidePanelWindowRect, messagesScreenRect, tabScreenRect, scrollbarTrackScreenRect, scrollbarThumbScreenRect;
    static float scrollbarMaxScroll, scrollbarDragOffset;
    static bool scrollbarHot, scrollbarDragging, chatCollapsed;
    /// <summary>MainMenu.MenuTabsRect: the inner tab bar (set by LobbyUI.Row2), used to dock the collapsed «+» button.</summary>
    public static Rect MenuTabsRect;
    public static bool InputActive => inputCaptureActive && !chatCollapsed;

    static readonly Color QuestTitleColor = new Color32(255, 148, 37, 255);
    static readonly Color QuestTextColor = new Color32(230, 225, 216, 255);
    static readonly Color QuestButtonTextColor = new Color32(236, 229, 176, 255);
    static readonly Color QuestButtonHoverColor = new Color32(255, 214, 120, 255);
    static readonly Color QuestGreenTextColor = new Color32(20, 36, 10, 255);

    static GUIStyle chatPanelStyle, chatInsetStyle, chatInputBarStyle, chatInputBarFocusStyle, chatInputStyle, chatInputPlaceholderStyle,
        chatMessageBodyStyle, chatMeasureStyle, chatNickStyle, chatNickLocalStyle, chatTimeStyle, chatStatusTextStyle, artLevelTextStyle,
        artCardStyle, artCardHoverStyle, artFrameStyle, artTileGreenStyle, chatSendButtonStyle, chatSendButtonIdleStyle,
        chatSendButtonDisabledStyle, chatSendFocusStyle, hideToggleStyle;
    static Texture2D chatSendTexture;
    static Font tahomaBold;

    // ---------------- styles ----------------
    static Texture2D Art(string name)
    {
        var ta = Resources.Load<TextAsset>("gui/chat/" + name);
        var t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        if (ta != null) t.LoadImage(ta.bytes);
        t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }
    static GUIStyle ArtPanel(string name, int border) => new GUIStyle { normal = { background = Art(name) }, border = new RectOffset(border, border, border, border) };
    static GUIStyle ArtButton(Font f, int size, string name, Color text, Color hover)
    {
        var s = new GUIStyle { font = f, fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, border = new RectOffset(7, 7, 7, 7), padding = new RectOffset(4, 4, 0, 0), clipping = TextClipping.Clip };
        s.normal.background = Art(name); s.normal.textColor = text;
        s.hover.background = Art(name + "_hover"); s.hover.textColor = hover;
        s.active.background = Art(name + "_active"); s.active.textColor = hover;
        s.focused.background = s.normal.background; s.focused.textColor = text;
        return Skins.FixBold(s);
    }
    static void SetStyleTextColor(GUIStyle a, Color b) { a.normal.textColor = a.hover.textColor = a.active.textColor = a.focused.textColor = a.onNormal.textColor = a.onHover.textColor = a.onActive.textColor = a.onFocused.textColor = b; }
    static GUIStyle MakeTextStyle(Font f, int size, TextAnchor anchor, Color c, bool wrap, bool rich)
    {
        var s = new GUIStyle(GUIStyle.none) { font = f, fontSize = size, fontStyle = FontStyle.Normal, alignment = anchor, wordWrap = wrap, richText = rich, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
        SetStyleTextColor(s, c);
        return s;
    }

    static void EnsureCachedStyles()
    {
        if (chatPanelStyle != null) return;
        if (tahomaBold == null) tahomaBold = Resources.Load<Font>("gui/battle/prefab/fonts/tahomabd");
        var bold = tahomaBold;

        chatInputStyle = new GUIStyle(Skins.S("Main", "textfield02"));
        chatInputStyle.font = bold; chatInputStyle.fontStyle = FontStyle.Normal;
        chatInputStyle.normal.background = chatInputStyle.hover.background = chatInputStyle.active.background = chatInputStyle.focused.background = null;
        chatInputStyle.padding = new RectOffset(0, 0, 0, 0); chatInputStyle.alignment = TextAnchor.MiddleLeft;
        chatInputStyle.wordWrap = false; chatInputStyle.clipping = TextClipping.Clip; chatInputStyle.fontSize = 12;

        chatMessageBodyStyle = new GUIStyle(Skins.Txt("chatMsg"));
        chatMessageBodyStyle.font = bold; chatMessageBodyStyle.fontStyle = FontStyle.Normal; chatMessageBodyStyle.fontSize = 12;
        chatMessageBodyStyle.wordWrap = true; chatMessageBodyStyle.richText = true; chatMessageBodyStyle.alignment = TextAnchor.UpperLeft;
        chatMessageBodyStyle.clipping = TextClipping.Clip; chatMessageBodyStyle.padding = new RectOffset(0, 0, 0, 0); chatMessageBodyStyle.margin = new RectOffset(0, 0, 0, 0);
        chatMessageBodyStyle.fixedWidth = 0; chatMessageBodyStyle.fixedHeight = 0;
        chatMessageBodyStyle.normal.background = chatMessageBodyStyle.hover.background = chatMessageBodyStyle.active.background = null;

        chatInputPlaceholderStyle = MakeTextStyle(bold, 12, TextAnchor.MiddleLeft, new Color(0.62f, 0.6f, 0.56f, 1f), false, false);
        // EnsureExtraStyles
        chatMeasureStyle = MakeTextStyle(bold, 12, TextAnchor.UpperLeft, QuestTextColor, false, false); chatMeasureStyle.clipping = TextClipping.Overflow;
        chatNickStyle = MakeTextStyle(bold, 12, TextAnchor.MiddleLeft, QuestTitleColor, false, false);
        chatNickLocalStyle = MakeTextStyle(bold, 12, TextAnchor.MiddleLeft, new Color32(160, 230, 90, 255), false, false);
        chatTimeStyle = MakeTextStyle(bold, 10, TextAnchor.MiddleRight, new Color32(150, 144, 134, 255), false, false);
        chatStatusTextStyle = MakeTextStyle(bold, 11, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.4f, 1f), false, false);

        // EnsureQuestArt
        var room = Skins.Txt("room");
        Font a = room != null && room.font != null ? room.font : bold;
        artCardStyle = ArtPanel("artCardStyle", 9);
        artCardHoverStyle = ArtPanel("artCardHoverStyle", 13);
        var artInsetStyle = ArtPanel("artInsetStyle", 8);
        artFrameStyle = ArtPanel("artFrameStyle", 9);
        var artTileGoldStyle = ArtPanel("artTileGoldStyle", 12);
        artTileGreenStyle = ArtPanel("artTileGreenStyle", 12);
        chatSendFocusStyle = ArtPanel("chatSendFocusStyle", 7);
        chatSendButtonDisabledStyle = ArtPanel("chatSendButtonDisabledStyle", 8);
        chatSendButtonStyle = ArtButton(a, 13, "chatSendButtonStyle", QuestGreenTextColor, Color.white);
        chatSendButtonIdleStyle = ArtButton(a, 13, "chatSendButtonIdleStyle", QuestGreenTextColor, Color.white);
        chatSendButtonIdleStyle.border = new RectOffset(8, 8, 8, 8);
        artLevelTextStyle = MakeTextStyle(a, 12, TextAnchor.MiddleCenter, new Color32(28, 24, 20, 255), false, false);
        artLevelTextStyle.fontStyle = FontStyle.Bold; Skins.FixBold(artLevelTextStyle);
        chatPanelStyle = artCardStyle;
        chatInsetStyle = artInsetStyle;
        chatInputBarStyle = artInsetStyle;
        chatInputBarFocusStyle = artTileGoldStyle;
        hideToggleStyle = new GUIStyle(Skins.Btn("menuRow02"))
        {
            alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 2), margin = new RectOffset(0, 0, 0, 0),
            fixedWidth = 0, fixedHeight = 0, wordWrap = false, clipping = TextClipping.Overflow
        };
        if (hideToggleStyle.fontSize < 18) hideToggleStyle.fontSize = 18;
        SetStyleTextColor(chatMessageBodyStyle, QuestTextColor);
        SetStyleTextColor(chatInputStyle, QuestTextColor);
        SetStyleTextColor(chatInputPlaceholderStyle, new Color32(140, 134, 124, 255));
        chatSendTexture = MakeSendArrowTexture(32);
    }

    static Texture2D MakeSendArrowTexture(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var poly = new[] { new Vector2(0.06f, 0.14f), new Vector2(0.96f, 0.5f), new Vector2(0.06f, 0.86f), new Vector2(0.3f, 0.5f) };
        var px = new Color[n * n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int hits = 0;
                for (int k = 0; k < 4; k++)
                    for (int l = 0; l < 4; l++)
                        if (PointInPolygon(poly, (j + (l + 0.5f) / 4f) / n, 1f - (i + (k + 0.5f) / 4f) / n)) hits++;
                px[i * n + j] = new Color(1f, 1f, 1f, hits / 16f);
            }
        t.SetPixels(px); t.Apply();
        return t;
    }
    static bool PointInPolygon(Vector2[] a, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = a.Length - 1; i < a.Length; j = i++)
            if (a[i].y > y != a[j].y > y && x < (a[j].x - a[i].x) * (y - a[i].y) / (a[j].y - a[i].y) + a[i].x) inside = !inside;
        return inside;
    }

    static void FillRect(Rect r, Color c)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
    }

    // ---------------- Update (嘑.Tick) ----------------
    public static void Tick()
    {
        HandleManualChatInput();
        HandleMouseWheel();
        HandleScrollbarDrag();
    }

    static void HandleManualChatInput()
    {
        if (!inputCaptureActive) { inputAllSelected = false; return; }
        if (GUIUtility.keyboardControl != 0) { inputCaptureActive = false; inputAllSelected = false; return; }
        bool selectAll = Input.GetKeyDown(KeyCode.A) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        if (selectAll) inputAllSelected = !string.IsNullOrEmpty(inputMessage);
        if (string.IsNullOrEmpty(inputMessage)) inputAllSelected = false;
        if (inputAllSelected && Input.GetKeyDown(KeyCode.Delete)) { inputMessage = ""; inputAllSelected = false; }
        string s = Input.inputString;
        if (string.IsNullOrEmpty(s)) return;
        bool send = false;
        foreach (char c in s)
        {
            if (selectAll && (c == 'a' || c == 'A' || c == 'ф' || c == 'Ф' || c == '\u0001')) continue;
            if (c == '\b')
            {
                if (inputAllSelected) { inputMessage = ""; inputAllSelected = false; }
                else if (inputMessage.Length > 0) inputMessage = inputMessage.Substring(0, inputMessage.Length - 1);
                continue;
            }
            if (c == '\n' || c == '\r') { inputAllSelected = false; send = true; continue; }
            if (char.IsControl(c)) continue;
            if (inputAllSelected) { inputMessage = ""; inputAllSelected = false; }
            if (inputMessage.Length < 180) inputMessage += c;
        }
        if (send) SendCurrentMessage();
    }

    static void HandleMouseWheel()
    {
        if (chatCollapsed || messagesScreenRect.width <= 0f) return;
        float axis = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(axis) < 0.0001f) return;
        var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (messagesScreenRect.Contains(p)) pendingWheelDelta += axis;
    }

    static void HandleScrollbarDrag()
    {
        if (chatCollapsed || scrollbarTrackScreenRect.height <= 0f || scrollbarMaxScroll <= 0f) { scrollbarDragging = false; scrollbarHot = false; return; }
        var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        bool over = new Rect(scrollbarTrackScreenRect.x - 7f, scrollbarTrackScreenRect.y - 4f, scrollbarTrackScreenRect.width + 11f, scrollbarTrackScreenRect.height + 8f).Contains(p);
        scrollbarHot = over || scrollbarDragging;
        if (!scrollbarDragging && over && Input.GetMouseButtonDown(0))
        {
            scrollbarDragging = true;
            scrollbarDragOffset = scrollbarThumbScreenRect.Contains(p) ? p.y - scrollbarThumbScreenRect.y : scrollbarThumbScreenRect.height * 0.5f;
        }
        if (!scrollbarDragging) return;
        if (!Input.GetMouseButton(0)) { scrollbarDragging = false; return; }
        float span = Mathf.Max(1f, scrollbarTrackScreenRect.height - scrollbarThumbScreenRect.height);
        chatScroll.y = Mathf.Clamp01((p.y - scrollbarDragOffset - scrollbarTrackScreenRect.y) / span) * scrollbarMaxScroll;
        forceScrollToBottom = false;
        chatWasAtBottom = scrollbarMaxScroll - chatScroll.y <= 42f;
    }

    static string NormalizeOutgoing(string a)
    {
        if (string.IsNullOrEmpty(a)) return "";
        a = a.Replace("\r", " ").Replace("\n", " ").Replace("<", "‹").Replace(">", "›").Trim();
        while (a.Contains("  ")) a = a.Replace("  ", " ");
        return a.Length > 180 ? a.Substring(0, 180) : a;
    }

    static void SendCurrentMessage()
    {
        string v = NormalizeOutgoing(inputMessage);
        if (string.IsNullOrEmpty(v)) return;
        var p = Profile.Current;
        Messages.Add(new Msg { Nick = p.Name, Body = v, Level = p.Level, Own = true, Time = DateTime.Now.ToString("HH:mm") });
        inputMessage = "";
        forceScrollToBottom = true;
    }

    // ---------------- OnGUI ----------------
    public static void OnGUI()
    {
        EnsureCachedStyles();
        if (!chatHiddenLoaded) { chatHidden = PlayerPrefs.GetInt("ContraChatFolded", 0) == 1; chatHiddenLoaded = true; }
        float num = (Screen.width - 760f) * 0.5f;
        float a = Mathf.Round(Mathf.Clamp(num + 100f, 450f, 540f));
        float b = Mathf.Floor(num - 17f - 8f);
        sidePanelWidth = Mathf.Round(Mathf.Clamp(Mathf.Min(a, b), 200f, 540f));
        sidePanelWidth = Mathf.Min(sidePanelWidth, Mathf.Max(160f, Screen.width - 34f));
        sidePanelHeight = Mathf.Clamp(Screen.height - 205f, 300f, 560f);
        sidePanelHeight = Mathf.Min(sidePanelHeight, Mathf.Max(200f, Screen.height - 118f - 8f));
        sidePanelWindowRect = new Rect(17f, 118f, sidePanelWidth, sidePanelHeight);

        int depth = GUI.depth; Color color = GUI.color, content = GUI.contentColor, background = GUI.backgroundColor; bool enabled = GUI.enabled; var matrix = GUI.matrix;
        try
        {
            GUI.depth = -10000; GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white; GUI.enabled = true;
            GUI.matrix = Matrix4x4.identity;
            chatCollapsed = chatHidden;
            if (chatCollapsed)
            {
                inputCaptureActive = false;
                messagesScreenRect = default;
                tabScreenRect = MenuTabsRect.width > 0f
                    ? new Rect(MenuTabsRect.x - 8f - 30f - 2f, MenuTabsRect.y + Mathf.Floor((MenuTabsRect.height - 30f) * 0.5f) - 2f, 34f, 34f)
                    : new Rect(17f + sidePanelWidth - 10f - 30f - 2f, 124f + Mathf.Floor(7f) - 2f, 34f, 34f);
                GUI.Window(927461, tabScreenRect, _ => DrawHideToggleButton(new Rect(2f, 2f, 30f, 30f)), GUIContent.none, GUIStyle.none);
            }
            else GUI.Window(927461, sidePanelWindowRect, _ => DrawSidePanel(new Rect(0f, 0f, sidePanelWindowRect.width, sidePanelWindowRect.height)), GUIContent.none, GUIStyle.none);
        }
        finally { GUI.matrix = matrix; GUI.enabled = enabled; GUI.backgroundColor = background; GUI.contentColor = content; GUI.color = color; GUI.depth = depth; }
    }

    static void SetChatHidden(bool hide)
    {
        chatHidden = hide;
        PlayerPrefs.SetInt("ContraChatFolded", hide ? 1 : 0); PlayerPrefs.Save();
        if (!hide) forceScrollToBottom = true; else inputCaptureActive = false;
    }

    static void DrawHideToggleButton(Rect r)
    {
        if (hideToggleStyle != null && GUI.Button(r, chatHidden ? "+" : "−", hideToggleStyle)) SetChatHidden(!chatHidden);
    }

    static void DrawSidePanel(Rect a)
    {
        GUI.Box(a, GUIContent.none, chatPanelStyle);
        float num = 46f;
        float num2 = !string.IsNullOrEmpty(statusText) ? 20f : 0f;
        DrawOnlineHeader(a, 34f);
        if (chatHidden) return;
        float num3 = 50f;
        float num4 = a.yMax - num;
        float num5 = num4 - num2 - 3f;
        var inset = new Rect(a.x + 10f, a.y + num3, a.width - 20f, Mathf.Max(80f, num5 - (a.y + num3)));
        GUI.Box(inset, GUIContent.none, chatInsetStyle);
        var body = new Rect(inset.x + 4f, inset.y + 4f, inset.width - 8f, inset.height - 8f);
        DrawMessageColumn(body);
        messagesScreenRect = new Rect(sidePanelWindowRect.x + body.x, sidePanelWindowRect.y + body.y, body.width, body.height);
        if (num2 > 0f) GUI.Label(new Rect(a.x + 10f, num4 - num2, a.width - 20f, num2), statusText, chatStatusTextStyle);

        var bar = new Rect(a.x + 10f, a.yMax - 32f - 10f, a.width - 20f, 32f);
        GUI.Box(bar, GUIContent.none, inputCaptureActive ? chatInputBarFocusStyle : chatInputBarStyle);
        var send = new Rect(bar.xMax - 52f - 3f, bar.y + 3f, 52f, bar.height - 6f);
        var text = new Rect(bar.x + 10f, bar.y, Mathf.Max(40f, send.x - 8f - (bar.x + 10f)), bar.height);
        var e = Event.current;
        if (e != null && e.type == EventType.MouseDown && e.button == 0)
        {
            if (bar.Contains(e.mousePosition)) { GUIUtility.keyboardControl = 0; inputCaptureActive = true; inputAllSelected = false; }
            else if (!inset.Contains(e.mousePosition)) { inputCaptureActive = false; inputAllSelected = false; }
        }
        bool selected = inputAllSelected && inputCaptureActive && !string.IsNullOrEmpty(inputMessage);
        if (selected && e != null && e.type == EventType.Repaint)
        {
            var v = chatInputStyle.CalcSize(new GUIContent(inputMessage));
            float h = Mathf.Min(text.height - 4f, Mathf.Max(14f, Mathf.Ceil(v.y)));
            FillRect(new Rect(text.x - 1f, text.y + Mathf.Floor((text.height - h) * 0.5f), Mathf.Min(text.width + 1f, Mathf.Ceil(v.x) + 2f), h), new Color(0.96f, 0.65f, 0.14f, 0.45f));
        }
        string shown = inputMessage;
        if (inputCaptureActive && !selected && ((int)(Time.realtimeSinceStartup * 2f) & 1) == 0) shown += "|";
        GUI.Label(text, shown, chatInputStyle);
        if (string.IsNullOrEmpty(inputMessage) && !inputCaptureActive) GUI.Label(text, "Написать в чат…", chatInputPlaceholderStyle);
        bool canSend = !string.IsNullOrEmpty(inputMessage);
        bool clicked = false;
        bool hover = canSend && e != null && send.Contains(e.mousePosition);
        if (canSend) clicked = GUI.Button(send, GUIContent.none, inputCaptureActive ? chatSendButtonStyle : chatSendButtonIdleStyle);
        else GUI.Box(send, GUIContent.none, inputCaptureActive ? chatSendFocusStyle : chatSendButtonDisabledStyle);
        if (e != null && e.type == EventType.Repaint)
        {
            var old = GUI.color;
            GUI.color = hover ? new Color(0.08f, 0.14f, 0.04f, 1f) : inputCaptureActive ? new Color(0.93f, 0.8f, 0.62f, 1f) : new Color(0.78f, 0.76f, 0.72f, 1f);
            GUI.DrawTexture(new Rect(send.x + Mathf.Floor((send.width - 18f) * 0.5f), send.y + Mathf.Floor((send.height - 18f) * 0.5f), 18f, 18f), chatSendTexture, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }
        if (clicked) { SendCurrentMessage(); inputCaptureActive = true; }
    }

    static void DrawOnlineHeader(Rect a, float b)
    {
        var bg = Skins.Bg("partActive"); var txt = Skins.Txt("partActive"); var shadow = Skins.Txt("partActiveShadow");
        var area = new Rect(a.x + 9f, a.y + 6f, Mathf.Max(120f, a.width - 17f), b + 10f);
        GUILayout.BeginArea(area);
        GUILayout.BeginHorizontal();
        GUILayout.BeginHorizontal(GUIContent.none, bg);
        Skins.AutoLabel("Контра Чат", txt);
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), "Контра Чат", txt, shadow);
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        DrawHideToggleButton(new Rect(a.xMax - 10f - 30f, area.y + Mathf.Floor((area.height - 30f) * 0.5f), 30f, 30f));
    }

    // ---------------- messages ----------------
    static bool IsCompactViewport(float w) => w < 340f;
    static float GetNickColumnWidth(float w) => Mathf.Round(Mathf.Clamp(w * 0.235f, 84f, 124f));
    static float GetBodyTextWidth(float w)
    {
        float n = w - 9f;
        if (IsCompactViewport(w)) return Mathf.Max(40f, n - 16f);
        return Mathf.Max(40f, n - (42f + GetNickColumnWidth(w) + 6f) - 20f - 36f - 10f);
    }
    static float MeasureBodyWidth(string s) => chatMeasureStyle.CalcSize(new GUIContent(s)).x;
    static string GetWrappedBody(Msg m, float w)
    {
        if (m.WrapBody != null && Mathf.Abs(m.WrapWidth - w) < 0.5f) return m.WrapBody;
        m.WrapBody = BreakLongWords(m.Body, w - 2f); m.WrapWidth = w;
        return m.WrapBody;
    }
    static string BreakLongWords(string a, float w)
    {
        if (string.IsNullOrEmpty(a)) return "";
        bool hasLong = false; int run = 0;
        foreach (char c in a) { if (c == ' ' || c == '\n') { run = 0; continue; } if (++run >= 12) { hasLong = true; break; } }
        if (!hasLong) return a;
        var sb = new StringBuilder(a.Length + 8);
        int i = 0;
        while (i <= a.Length)
        {
            int sp = a.IndexOf(' ', i); if (sp < 0) sp = a.Length;
            AppendBrokenToken(sb, a.Substring(i, sp - i), w);
            if (sp >= a.Length) break;
            sb.Append(' '); i = sp + 1;
        }
        return sb.ToString();
    }
    static void AppendBrokenToken(StringBuilder sb, string tok, float w)
    {
        if (tok.Length < 12 || MeasureBodyWidth(tok) <= w) { sb.Append(tok); return; }
        for (int i = 0, n; i < tok.Length; i += n)
        {
            n = 1; int hi = tok.Length - i;
            while (n < hi) { int mid = (n + hi + 1) / 2; if (MeasureBodyWidth(tok.Substring(i, mid)) <= w) n = mid; else hi = mid - 1; }
            if (i > 0) sb.Append('\n');
            sb.Append(tok, i, n);
        }
    }
    static float GetMessageRowHeight(Msg m, float w)
    {
        if (m.RowHeight > 0f && Mathf.Abs(m.RowWidth - w) < 0.5f) return m.RowHeight;
        float bw = GetBodyTextWidth(w);
        float h = Mathf.Max(16f, chatMessageBodyStyle.CalcHeight(new GUIContent(GetWrappedBody(m, bw)), bw));
        m.RowHeight = !IsCompactViewport(w) ? Mathf.Max(34f, h + 14f) + 3f : 26f + h + 6f + 3f;
        m.RowWidth = w;
        return m.RowHeight;
    }

    static void DrawMessageColumn(Rect a)
    {
        if (a.width <= 4f || a.height <= 4f) return;
        float total = 4f;
        foreach (var m in Messages) total += GetMessageRowHeight(m, a.width);
        float max = Mathf.Max(0f, total - a.height);
        if (forceScrollToBottom) { chatScroll.y = max; if (Event.current != null && Event.current.type == EventType.Repaint) forceScrollToBottom = false; }
        chatScroll.y = Mathf.Clamp(chatScroll.y, 0f, max);
        chatWasAtBottom = max <= 4f || max - chatScroll.y <= 42f;
        if (Mathf.Abs(pendingWheelDelta) > 0.0001f)
        {
            chatScroll.y = Mathf.Clamp(chatScroll.y - pendingWheelDelta * 700f, 0f, max);
            pendingWheelDelta = 0f;
            chatWasAtBottom = max <= 4f || max - chatScroll.y <= 42f;
        }
        float bw = GetBodyTextWidth(a.width);
        bool showJump = max > 4f && !chatWasAtBottom;
        GUI.BeginGroup(a);
        var mouse = new Vector2(Input.mousePosition.x - sidePanelWindowRect.x - a.x, Screen.height - Input.mousePosition.y - sidePanelWindowRect.y - a.y);
        float y = 4f - chatScroll.y;
        for (int j = 0; j < Messages.Count; j++)
        {
            var m = Messages[j];
            float h = GetMessageRowHeight(m, a.width);
            var row = new Rect(0f, y, a.width, h);
            if (row.yMax >= 0f && row.y <= a.height) DrawMessageCard(m, new Rect(1f, y, a.width - 9f, h - 3f), bw, j);
            y += h;
        }
        GUI.EndGroup();
        if (max > 1f)
        {
            float track = Mathf.Max(20f, a.height - 8f);
            float thumb = Mathf.Max(24f, track * a.height / total);
            float ty = a.y + 4f + Mathf.Max(1f, track - thumb) * (chatScroll.y / max);
            var tr = new Rect(a.xMax - 6f, a.y + 4f, 5f, track);
            var th = new Rect(a.xMax - 6f, ty, 5f, thumb);
            scrollbarTrackScreenRect = new Rect(sidePanelWindowRect.x + tr.x, sidePanelWindowRect.y + tr.y, tr.width, tr.height);
            scrollbarThumbScreenRect = new Rect(sidePanelWindowRect.x + th.x, sidePanelWindowRect.y + th.y, th.width, th.height);
            scrollbarMaxScroll = max;
            bool hot = scrollbarHot || scrollbarDragging;
            float w = hot ? 7f : 5f, x = a.xMax - 6f - (w - 5f);
            FillRect(new Rect(x, tr.y, w, tr.height), new Color(1f, 1f, 1f, hot ? 0.16f : 0.1f));
            FillRect(new Rect(x, th.y, w, th.height), hot ? new Color(1f, 0.78f, 0.36f, 1f) : new Color(0.96f, 0.65f, 0.14f, 0.9f));
        }
        else { scrollbarTrackScreenRect = scrollbarThumbScreenRect = default; scrollbarMaxScroll = 0f; scrollbarDragging = false; }
        if (showJump && GUI.Button(new Rect(a.x + Mathf.Floor((a.width - 100f) * 0.5f), a.yMax - 34f, 100f, 26f), "↓ Вниз", ArtJumpStyle))
        { chatScroll.y = max; chatWasAtBottom = true; forceScrollToBottom = true; }
    }

    static GUIStyle artJump;
    static GUIStyle ArtJumpStyle => artJump ?? (artJump = ArtButton(Skins.Txt("room")?.font ?? tahomaBold, 12, "artButtonStyle", QuestButtonTextColor, QuestButtonHoverColor));   // chatJumpButtonStyle = artButtonStyle

    static void DrawMessageCard(Msg m, Rect b, float bodyW, int index)
    {
        var card = m.Own ? artTileGreenStyle : artCardStyle;
        GUI.Box(b, GUIContent.none, card);
        if (card == artCardStyle && (index & 1) == 1) FillRect(new Rect(b.x + 5f, b.y + 4f, b.width - 10f, b.height - 8f), new Color(1f, 1f, 1f, 0.03f));
        bool compact = IsCompactViewport(b.width + 9f);
        float mid = !compact ? b.y + b.height * 0.5f : b.y + 5f + 10f;
        var time = !compact ? new Rect(b.xMax - 8f - 36f, b.y, 36f, b.height) : new Rect(b.xMax - 8f - 36f, b.y + 5f, 36f, 20f);
        float lv = !compact ? 26f : 20f;
        var lvRect = new Rect(b.x + 8f, mid - lv * 0.5f, lv, lv);
        GUI.Box(lvRect, GUIContent.none, artFrameStyle);
        GUI.Label(lvRect, m.Level > 0 ? m.Level.ToString() : "?", artLevelTextStyle);
        float nickX = lvRect.xMax + (!compact ? 8f : 6f);
        float nickEnd = !compact ? nickX + GetNickColumnWidth(b.width + 9f) : time.x - 6f;
        GUI.Label(!compact ? new Rect(nickX, b.y, Mathf.Max(0f, nickEnd - nickX), b.height) : new Rect(nickX, b.y + 5f, Mathf.Max(0f, nickEnd - nickX), 20f), m.Nick, m.Own ? chatNickLocalStyle : chatNickStyle);
        string body = GetWrappedBody(m, bodyW);
        float bh = Mathf.Max(16f, chatMessageBodyStyle.CalcHeight(new GUIContent(body), bodyW));
        if (compact) GUI.Label(new Rect(b.x + 8f, b.y + 5f + 20f + 1f, bodyW, bh), body, chatMessageBodyStyle);
        else GUI.Label(new Rect(nickEnd + 6f, mid - bh * 0.5f, bodyW, bh), body, chatMessageBodyStyle);
        if (!string.IsNullOrEmpty(m.Time)) GUI.Label(time, m.Time, chatTimeStyle);
    }
}
