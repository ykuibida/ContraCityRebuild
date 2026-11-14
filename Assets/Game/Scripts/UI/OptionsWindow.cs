// Options window = port of New Era OptionPopup (decrypted client, NE_Scripts_Decrypted/OptionPopup.cs):
// window 520–540 x 500–560 (Backgound skin window style), «НАСТРОЙКИ», toolbar УПРАВЛЕНИЕ / ВИДЕО / АУДИО / ПРИЦЕЛ (selectGrid),
// a 480-px layout column vertically centred in (window height − 176), passive sliders (drawn as boxes, dragged from Update),
// "–" value "+" editors, the keyboard list with its own scrollbar (rows 30 px, content 687 px), the crosshair editor.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public class OptionsWindow
{
    bool open;
    public bool Open { get => open; set { if (value && !open) { SyncSensitivityTextFields(); activePassiveDrag = 0; } open = value; } }
    int selectMenu = 1;
    public int Tab { get => selectMenu; set => selectMenu = value; }

    // settings that GameOptions does not hold yet (喞): quality, vsync, chat, mute, voice
    public static int Quality = 3;                  // QualityLevel: Fast, Simple, Good, Beautiful, Fantastic
    public static bool VSync = true, BattleChat = true, Microphone = true;
    public static bool MuteAll { get => GameOptions.SoundIsMute; set => GameOptions.SoundIsMute = value; }   // OptionPopup «Откл. все звуки»
    public static float PlayersVolume = 1f;
    public static KeyCode MicrophoneKey = KeyCode.F;

    // crosshair per weapon slot (喞.customCrosshair*BySlot defaults)
    public static readonly int[] CrossStyle = { -1, -1, -1, -1, -1, -1, -1 };
    public static readonly float[] CrossThickness = { 5, 5, 5, 5, 5, 5, 5 }, CrossSize = { 18, 18, 18, 18, 18, 18, 18 }, CrossHue = new float[7], CrossOutline = new float[7], CrossGap = { 5, 5, 5, 5, 5, 5, 5 };
    public static readonly Color[] CrossColor = { Color.red, Color.red, Color.red, Color.red, Color.red, Color.red, Color.red };
    static int Slot(int a) => Mathf.Clamp(a, 1, 7) - 1;

    Rect windowRect;
    bool isSetNewButton; int setButtonKey = -1;
    Vector2 keybordScroll;
    string mouseSensitivityText = "", aimSensitivityText = "";
    float pendingMouseSensitivity, pendingAimSensitivity;
    bool sensitivityTextInitialized;
    int activePassiveDrag;
    Rect mouseSensitivitySliderScreenRect, aimSensitivitySliderScreenRect, fovSliderScreenRect, masterVolumeSliderScreenRect, musicVolumeSliderScreenRect, effectsVolumeSliderScreenRect, voiceVolumeSliderScreenRect;
    readonly Rect[] crosshairSliderScreenRects = new Rect[5];
    Rect keyboardScrollViewScreenRect, keyboardScrollTrackScreenRect, keyboardScrollUpScreenRect, keyboardScrollDownScreenRect;
    float keyboardScrollMax;
    const float KeyboardContentHeight = 687f;
    readonly Rect[] crosshairPresetScreenRects = new Rect[9], crosshairWeaponSlotScreenRects = new Rect[7];
    bool crosshairPresetRectsValid, crosshairWeaponSlotRectsValid, crosshairColorButtonRectValid, crosshairColorPaletteOpen;
    int crosshairWeaponSlot = 3;
    Rect crosshairColorButtonScreenRect;
    readonly Dictionary<string, string> valueEditorTexts = new Dictionary<string, string>();
    static GUIStyle sensitivityArrowStyle;
    static Texture2D roundedColorMaskTexture, circleTexture, hueTexture, smoothCircleTexture, circleRingTexture;
    static string T(string k) => LanguageManager.GetText(k);

    // key list in OptionPopup.DrawKeyboardControlsList order: (language key, get, set)
    static readonly (string title, System.Func<KeyCode> get, System.Action<KeyCode> set)[] Binds =
    {
        ("Forward", () => TRInput.Forward, k => TRInput.Forward = k), ("Backward", () => TRInput.Backward, k => TRInput.Backward = k),
        ("LeftStrafe", () => TRInput.LeftStrafe, k => TRInput.LeftStrafe = k), ("RightStrafe", () => TRInput.RightStrafe, k => TRInput.RightStrafe = k),
        ("Crouch", () => TRInput.Crouch, k => TRInput.Crouch = k), ("Jump", () => TRInput.Jump, k => TRInput.Jump = k),
        ("Weapon 1", () => TRInput.Weapon1, k => TRInput.Weapon1 = k), ("Weapon 2", () => TRInput.Weapon2, k => TRInput.Weapon2 = k),
        ("Weapon 3", () => TRInput.Weapon3, k => TRInput.Weapon3 = k), ("Weapon 4", () => TRInput.Weapon4, k => TRInput.Weapon4 = k),
        ("Weapon 5", () => TRInput.Weapon5, k => TRInput.Weapon5 = k), ("Weapon 6", () => TRInput.Weapon6, k => TRInput.Weapon6 = k),
        ("Weapon 7", () => TRInput.Weapon7, k => TRInput.Weapon7 = k), ("Prim. Fire", () => TRInput.Fire1, k => TRInput.Fire1 = k),
        ("Zoom", () => TRInput.Zoom, k => TRInput.Zoom = k), ("Reload", () => TRInput.Reload, k => TRInput.Reload = k),
        ("Booster 1", () => TRInput.Enhancer1, k => TRInput.Enhancer1 = k), ("Booster 2", () => TRInput.Enhancer2, k => TRInput.Enhancer2 = k),
        ("Prev. weapon", () => TRInput.QuickChange, k => TRInput.QuickChange = k), ("Taunt 1", () => TRInput.Taunt1, k => TRInput.Taunt1 = k),
        ("Taunt 2", () => TRInput.Taunt2, k => TRInput.Taunt2 = k), ("Taunt 3", () => TRInput.Taunt3, k => TRInput.Taunt3 = k),
        ("Микрофон", () => MicrophoneKey, k => MicrophoneKey = k),
    };

    /// <summary>KeyCode.ToDisplayString (啙).</summary>
    static string KeyName(KeyCode a)
    {
        switch (a)
        {
            case KeyCode.Mouse0: return T("Left Mouse");
            case KeyCode.Mouse1: return T("Right Mouse");
            case KeyCode.Mouse2: return T("Middle Mouse");
            case KeyCode.Mouse3: return "Mouse 4";
            case KeyCode.Mouse4: return "Mouse 5";
        }
        if (a >= KeyCode.Alpha0 && a <= KeyCode.Alpha9) return ((int)(a - KeyCode.Alpha0)).ToString();
        return a.ToString();
    }

    void UpdateWindowRect()
    {
        float w = Mathf.Min(540f, Mathf.Max(520f, Screen.width - 40f)), h = Mathf.Min(560f, Mathf.Max(500f, Screen.height - 40f));
        windowRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
    }

    // ---------------- Update: mouse 4/5 binds, passive drags, crosshair clicks ----------------
    public void Update()
    {
        if (!open) return;
        if (isSetNewButton)
        {
            if (Input.GetKeyDown(KeyCode.Mouse3)) SetBind(KeyCode.Mouse3);
            else if (Input.GetKeyDown(KeyCode.Mouse4)) SetBind(KeyCode.Mouse4);
        }
        var v = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (UpdatePassiveDragInput(v) || selectMenu != 3 || !Input.GetMouseButtonDown(0)) return;
        if (crosshairWeaponSlotRectsValid)
            for (int i = 0; i < 7; i++) if (crosshairWeaponSlotScreenRects[i].Contains(v)) { crosshairWeaponSlot = i + 1; crosshairColorPaletteOpen = false; return; }
        if (crosshairPresetRectsValid)
            for (int j = 0; j < 9; j++) if (crosshairPresetScreenRects[j].Contains(v)) { CrossStyle[Slot(crosshairWeaponSlot)] = Mathf.Clamp(j - 1, -1, 7); return; }
        if (crosshairColorButtonRectValid && crosshairColorButtonScreenRect.Contains(v)) crosshairColorPaletteOpen = !crosshairColorPaletteOpen;
    }

    void SetBind(KeyCode k) { if (setButtonKey >= 0) Binds[setButtonKey].set(k); isSetNewButton = false; }

    bool UpdatePassiveDragInput(Vector2 a)
    {
        if (selectMenu == 0 && keyboardScrollViewScreenRect.Contains(a))
        {
            float axis = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(axis) > 0.0001f) keybordScroll.y = Mathf.Clamp(keybordScroll.y - axis * 90f, 0f, keyboardScrollMax);
        }
        if (Input.GetMouseButtonDown(0))
        {
            activePassiveDrag = HitPassiveDragControl(a);
            if (selectMenu == 0)
            {
                if (keyboardScrollUpScreenRect.Contains(a)) { keybordScroll.y = Mathf.Clamp(keybordScroll.y - 30f, 0f, keyboardScrollMax); return true; }
                if (keyboardScrollDownScreenRect.Contains(a)) { keybordScroll.y = Mathf.Clamp(keybordScroll.y + 30f, 0f, keyboardScrollMax); return true; }
            }
            if (activePassiveDrag != 0) { ApplyPassiveDrag(activePassiveDrag, a); return true; }
        }
        if (activePassiveDrag != 0)
        {
            if (Input.GetMouseButton(0)) { ApplyPassiveDrag(activePassiveDrag, a); return true; }
            activePassiveDrag = 0;
        }
        if (Input.GetMouseButtonUp(0)) activePassiveDrag = 0;
        return false;
    }

    int HitPassiveDragControl(Vector2 a)
    {
        if (selectMenu == 0) { if (mouseSensitivitySliderScreenRect.Contains(a)) return 1; if (aimSensitivitySliderScreenRect.Contains(a)) return 2; if (keyboardScrollTrackScreenRect.Contains(a)) return 8; }
        else if (selectMenu == 1) { if (fovSliderScreenRect.Contains(a)) return 3; }
        else if (selectMenu == 2) { if (masterVolumeSliderScreenRect.Contains(a)) return 4; if (musicVolumeSliderScreenRect.Contains(a)) return 5; if (effectsVolumeSliderScreenRect.Contains(a)) return 6; if (voiceVolumeSliderScreenRect.Contains(a)) return 7; }
        else if (selectMenu == 3) { for (int i = 0; i < 5; i++) if (crosshairSliderScreenRects[i].Contains(a)) return 20 + i; }
        return 0;
    }

    void ApplyPassiveDrag(int a, Vector2 b)
    {
        int s = Slot(crosshairWeaponSlot);
        switch (a)
        {
            case 1: pendingMouseSensitivity = ValueFromPassiveSlider(b.x, mouseSensitivitySliderScreenRect, 0.01f, 10f); mouseSensitivityText = FormatSensitivity(pendingMouseSensitivity); break;
            case 2: pendingAimSensitivity = ValueFromPassiveSlider(b.x, aimSensitivitySliderScreenRect, 0.01f, 10f); aimSensitivityText = FormatSensitivity(pendingAimSensitivity); break;
            case 3: GameOptions.FpsFieldOfView = ValueFromPassiveSlider(b.x, fovSliderScreenRect, 40f, 95f); break;
            case 4: GameOptions.SoundVolumeMaster = ValueFromPassiveSlider(b.x, masterVolumeSliderScreenRect, 0f, 1f); break;
            case 5: GameOptions.SoundVolumeMusic = ValueFromPassiveSlider(b.x, musicVolumeSliderScreenRect, 0f, 1f); break;
            case 6: GameOptions.SoundVolumeEffect = ValueFromPassiveSlider(b.x, effectsVolumeSliderScreenRect, 0f, 1f); break;
            case 7: PlayersVolume = ValueFromPassiveSlider(b.x, voiceVolumeSliderScreenRect, 0f, 1f); break;
            case 8: UpdateKeyboardScrollFromMouse(b.y); break;
            case 20: CrossThickness[s] = Mathf.Clamp(ValueFromPassiveSlider(b.x, crosshairSliderScreenRects[0], 0f, 12f) + 2f, 2f, 14f); break;
            case 21: CrossOutline[s] = Mathf.Clamp(ValueFromPassiveSlider(b.x, crosshairSliderScreenRects[1], 0f, 5f), 0f, 5f); break;
            case 22: CrossGap[s] = Mathf.Clamp(ValueFromPassiveSlider(b.x, crosshairSliderScreenRects[2], 0f, 18f), 0f, 18f); break;
            case 23: SetHue(s, ValueFromPassiveSlider(b.x, crosshairSliderScreenRects[3], 0f, 1f)); break;
            case 24: CrossSize[s] = Mathf.Clamp(ValueFromPassiveSlider(b.x, crosshairSliderScreenRects[4], 0f, 34f) + 8f, 8f, 42f); break;
        }
    }

    static void SetHue(int s, float h) { CrossHue[s] = Mathf.Repeat(h, 1f); CrossColor[s] = HueToColor(CrossHue[s]); }

    static float ValueFromPassiveSlider(float a, Rect b, float c, float d)
    {
        float lo = b.x + 12f, hi = b.xMax - 12f;
        return Mathf.Lerp(c, d, Mathf.Clamp01((a - lo) / Mathf.Max(1f, hi - lo)));
    }

    Rect WindowRectToScreen(Rect a) => new Rect(windowRect.x + a.x, windowRect.y + a.y, a.width, a.height);

    static void DrawPassiveHorizontalSlider(Rect a, float b, float c, float d)
    {
        GUI.Box(a, GUIContent.none, GUI.skin.horizontalSlider);
        DrawPassiveHorizontalSliderThumb(a, b, c, d);
    }

    static void DrawPassiveHorizontalSliderThumb(Rect a, float b, float c, float d)
    {
        float x = Mathf.Lerp(a.x + 12f, a.xMax - 12f, Mathf.InverseLerp(c, d, b));
        GUI.Box(new Rect(x - 8f, a.y - 1f, 16f, a.height + 2f), GUIContent.none, GUI.skin.horizontalSliderThumb);
    }

    float GetLayoutContentHeight() => Mathf.Max(300f, windowRect.height - 176f);
    Rect GetLayoutArea() { float h = GetLayoutContentHeight(); return GUILayoutUtility.GetRect(1f, h, GUILayout.ExpandWidth(true), GUILayout.Height(h)); }
    static Rect GetLayoutColumn(Rect a, float b) { float w = Mathf.Min(480f, a.width); return new Rect(Mathf.Round(a.x + (a.width - w) * 0.5f), a.y + Mathf.Max(0f, Mathf.Floor((a.height - b) * 0.5f)), w, b); }
    static Rect GetHalfColumnRect(Rect a, float b, bool c) => new Rect(c ? a.xMax - 232f : a.x, b, 232f, 50f);
    static Rect GetBlockControlRect(Rect a) => new Rect(a.x, a.y + 22f, a.width, 28f);
    static GUIStyle CreateCaptionStyle() => Skins.FixBold(new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false });
    static GUIStyle CreateValueBoxStyle() => new GUIStyle(Skins.S("Main", "textfield02")) { alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), overflow = new RectOffset(0, 0, 0, 0), contentOffset = Vector2.zero, fixedHeight = 0f };
    static void DrawLayoutCaption(Rect a, string b) => GUI.Label(new Rect(a.x, a.y, a.width, 20f), b, CreateCaptionStyle());

    static Rect DrawLayoutSliderBlock(Rect a, float b, string c, float d, float e, float f)
    {
        DrawLayoutCaption(new Rect(a.x, b, a.width, 50f), c);
        var r = new Rect(a.x, b + 22f + 5f, 364f, 18f);
        DrawPassiveHorizontalSlider(r, d, e, f);
        return r;
    }

    static Rect GetLayoutValueRect(Rect a, float b, float c) => new Rect(a.x + 376f + 26f, b, 52f, c);

    static GUIStyle GetSensitivityArrowStyle()
    {
        if (sensitivityArrowStyle == null)
            sensitivityArrowStyle = Skins.FixBold(new GUIStyle(Skins.Btn("selectGrid")) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), fixedWidth = 0f, fixedHeight = 0f });
        return sensitivityArrowStyle;
    }

    bool DrawIntValueEditor(Rect a, float b, float c, string d, int e, int f, int g, out int h)
    {
        h = Mathf.Clamp(e, f, g);
        var p1 = new Rect(a.x + 376f, b, 24f, c); var p2 = new Rect(p1.xMax + 2f, b, 52f, c); var p3 = new Rect(p2.xMax + 2f, b, 24f, c);
        if (GUI.GetNameOfFocusedControl() != d || !valueEditorTexts.TryGetValue(d, out var value)) value = h.ToString(CultureInfo.InvariantCulture);
        bool result = false;
        GUI.SetNextControlName(d);
        string text = GUI.TextField(p2, value, 3, CreateValueBoxStyle());
        if (text != value)
        {
            var sb = new StringBuilder(); foreach (char ch in text) if (char.IsDigit(ch)) sb.Append(ch);
            value = sb.ToString();
            if (value.Length > 0 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r2))
            {
                if (r2 > g) { r2 = g; value = g.ToString(CultureInfo.InvariantCulture); }
                if (r2 >= f && r2 != h) { h = r2; result = true; }
            }
        }
        valueEditorTexts[d] = value;
        var st = GetSensitivityArrowStyle();
        if (GUI.Button(p1, "–", st) && h > f) { h--; result = true; GUIUtility.keyboardControl = 0; }
        if (GUI.Button(p3, "+", st) && h < g) { h++; result = true; GUIUtility.keyboardControl = 0; }
        return result;
    }

    static void DrawSectionDivider(Rect a, float b)
    {
        if (Event.current.type != EventType.Repaint) return;
        var r = new Rect(a.x + 24f, Mathf.Round(b) - 1f, Mathf.Max(20f, a.width - 48f), 1f);
        DrawFilledRect(r, new Color(0f, 0f, 0f, 0.2f));
        DrawFilledRect(new Rect(r.x, r.y + 1f, r.width, 1f), new Color(1f, 1f, 1f, 0.25f));
    }

    // ---------------- OnGUI / draw_popup ----------------
    public void Draw()
    {
        var skin = GUI.skin;
        GUI.skin = Skins.Main;
        UpdateWindowRect();
        if (open)
        {
            HoverUI.Enabled = false;
            var bg = Skins.Get("Backgound");
            Skins.Window(1, windowRect, DrawPopup, bg != null ? bg.window : GUI.skin.window);   // 嘄.Backgound.window
        }
        GUI.skin = skin;
    }

    void DrawPopup(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("НАСТРОЙКИ", Skins.Txt("popupTitle"), GUILayout.Height(34));
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("selectGrid"), GUILayout.Height(32));
        selectMenu = GUILayout.Toolbar(selectMenu, new[] { T("CONTROLS"), T("VIDEO"), T("AUDIO"), "ПРИЦЕЛ" }, Skins.Btn("selectGrid"), GUILayout.Height(28), GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        if (selectMenu != 3) { crosshairColorPaletteOpen = false; crosshairPresetRectsValid = false; crosshairWeaponSlotRectsValid = false; crosshairColorButtonRectValid = false; }
        GUILayout.Space(10);
        GUILayout.BeginVertical();
        if (selectMenu == 0) DrawControls(); else if (selectMenu == 1) DrawVideo(); else if (selectMenu == 2) DrawAudio(); else DrawCrosshair();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(T("OK"), Skins.Btn("green"), GUILayout.Width(160), GUILayout.Height(40))) ClickOK();
        GUILayout.Space(10);
        if (GUILayout.Button(T("Cancel"), Skins.Btn("cancel"), GUILayout.Width(160), GUILayout.Height(40))) ClickReset();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        if (GUI.Button(new Rect(windowRect.width - 32f, 2f, 30f, 30f), GUIContent.none, Skins.Btn("popupClose"))) open = false;
    }

    void DrawControls()
    {
        if (!sensitivityTextInitialized) SyncSensitivityTextFields();
        float top = 218f, pad = 3f;
        int rows = Mathf.Clamp(Mathf.FloorToInt((GetLayoutContentHeight() - top + pad) / 30f), 2, 8);
        float listH = rows * 30f - pad;
        var col = GetLayoutColumn(GetLayoutArea(), top + listH);
        float y = col.y;
        DrawSensitivityControl(col, y, "Чувствительность мыши", "MouseSensitivityValue", ref pendingMouseSensitivity, ref mouseSensitivityText, true); y += 58f;
        DrawSensitivityControl(col, y, "Чувствительность прицела", "AimSensitivityValue", ref pendingAimSensitivity, ref aimSensitivityText, false); y += 58f;
        GameOptions.Sensivity = Mathf.Clamp(pendingMouseSensitivity, 0.01f, 10f);
        GameOptions.AimSensitivity = Mathf.Clamp(pendingAimSensitivity, 0.01f, 10f);
        var h1 = GetHalfColumnRect(col, y, false);
        DrawLayoutCaption(h1, "Инвертированная мышь");
        GameOptions.InvertMouseY = GUI.SelectionGrid(GetBlockControlRect(h1), GameOptions.InvertMouseY ? 0 : 1, new[] { T("YES"), T("NO") }, 2, Skins.Btn("selectGrid")) == 0;
        var h2 = GetHalfColumnRect(col, y, true);
        DrawLayoutCaption(h2, "Режим мыши");
        int mm = GameOptions.MouseMode == CameraOrbitMode.Normal ? 1 : 0;   // 噱: 1 = Default (Neutral), 2 = Alternative
        mm = GUI.SelectionGrid(GetBlockControlRect(h2), mm, new[] { T("Default"), T("Alternative") }, 2, Skins.Btn("selectGrid"));
        GameOptions.MouseMode = mm == 1 ? CameraOrbitMode.Normal : CameraOrbitMode.Neutral;
        y += 50f;
        DrawSectionDivider(col, y + 8f);
        y += 16f;
        var cap = CreateCaptionStyle(); cap.alignment = TextAnchor.MiddleLeft; cap.fontSize = Mathf.Max(16, cap.fontSize);
        GUI.Label(new Rect(col.x, y, col.width - 210f - 10f, 28f), "Настройки клавиатуры", cap);
        if (GUI.Button(new Rect(col.xMax - 210f, y, 210f, 28f), "Сбросить настройки", Skins.Btn("selectGrid"))) TRInput.ResetDefaults();
        y += 36f;
        DrawKeyboardControlsList(new Rect(col.x, y, col.width, listH));
        if (!isSetNewButton) return;
        var e = Event.current;
        if (!e.isKey && !e.isMouse) return;
        if (e.isKey) Binds[setButtonKey].set(e.keyCode);
        if (e.isMouse && e.button <= 4) Binds[setButtonKey].set(KeyCode.Mouse0 + e.button);
        isSetNewButton = false;
    }

    void DrawSensitivityControl(Rect a, float b, string c, string d, ref float e, ref string f, bool mouse)
    {
        var sr = DrawLayoutSliderBlock(a, b, c, e, 0.01f, 10f);
        if (mouse) mouseSensitivitySliderScreenRect = WindowRectToScreen(sr); else aimSensitivitySliderScreenRect = WindowRectToScreen(sr);
        float y = b + 22f;
        var p1 = new Rect(a.x + 376f, y, 24f, 28f); var p2 = new Rect(p1.xMax + 2f, y, 52f, 28f); var p3 = new Rect(p2.xMax + 2f, y, 24f, 28f);
        GUI.SetNextControlName(d);
        string text = GUI.TextField(p2, f, 5, CreateValueBoxStyle());
        if (text != f)
        {
            f = FilterSensitivityText(text);
            if (TryParseSensitivity(f, out float v)) { if (v > 10f) { v = 10f; f = "10"; } e = Mathf.Clamp(v, 0.01f, 10f); }
        }
        var st = GetSensitivityArrowStyle();
        if (GUI.Button(p1, "–", st)) { e = Mathf.Clamp(Mathf.Round((e - 0.01f) * 100f) / 100f, 0.01f, 10f); f = FormatSensitivity(e); GUIUtility.keyboardControl = 0; }
        if (GUI.Button(p3, "+", st)) { e = Mathf.Clamp(Mathf.Round((e + 0.01f) * 100f) / 100f, 0.01f, 10f); f = FormatSensitivity(e); GUIUtility.keyboardControl = 0; }
        if (GUI.GetNameOfFocusedControl() != d) f = FormatSensitivity(e);
    }

    void DrawKeyboardControlsList(Rect a)
    {
        var bar = new Rect(a.xMax - 20f, a.y, 20f, Mathf.Max(24f, a.height));
        var view = new Rect(a.x, a.y, Mathf.Max(80f, bar.x - 8f - a.x), a.height);
        keyboardScrollMax = Mathf.Max(0f, KeyboardContentHeight - view.height);
        keybordScroll.y = Mathf.Clamp(keybordScroll.y, 0f, keyboardScrollMax);
        keyboardScrollViewScreenRect = WindowRectToScreen(view);
        float off = Mathf.Min(Mathf.Round(keybordScroll.y / 30f) * 30f, keyboardScrollMax);
        GUI.BeginGroup(view);
        float yy = -off;
        for (int i = 0; i < Binds.Length; i++) { DrawKeyboardControlRow(yy, view.width, i); yy += 30f; }
        GUI.EndGroup();
        var up = new Rect(bar.x, bar.y, 20f, 24f); var down = new Rect(bar.x, bar.yMax - 24f, 20f, 24f);
        var track = new Rect(bar.x, up.yMax, 20f, Mathf.Max(1f, bar.height - 48f));
        GUI.Box(up, GUIContent.none, GUI.skin.verticalScrollbarUpButton);
        GUI.Box(track, GUIContent.none, GUI.skin.verticalScrollbar);
        GUI.Box(down, GUIContent.none, GUI.skin.verticalScrollbarDownButton);
        float th = keyboardScrollMax > 0f ? Mathf.Max(24f, track.height * (view.height / KeyboardContentHeight)) : track.height;
        float free = Mathf.Max(0f, track.height - th), k = keyboardScrollMax > 0f ? off / keyboardScrollMax : 0f;
        GUI.Box(new Rect(track.x, track.y + free * k, track.width, th), GUIContent.none, GUI.skin.verticalScrollbarThumb);
        keyboardScrollTrackScreenRect = WindowRectToScreen(track);
        keyboardScrollUpScreenRect = WindowRectToScreen(up);
        keyboardScrollDownScreenRect = WindowRectToScreen(down);
    }

    void DrawKeyboardControlRow(float a, float b, int idx)
    {
        float lw = Mathf.Min(150f, b * 0.35f);
        var p1 = new Rect(0f, a, lw, 27f);
        float x2 = lw + 10f;
        var p2 = new Rect(x2, a, Mathf.Max(140f, b - x2 - 2f), 27f);
        var st = Skins.FixBold(new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, wordWrap = false });
        GUI.Label(p1, T(Binds[idx].title), st);
        string key = KeyName(Binds[idx].get());
        if (isSetNewButton && setButtonKey == idx) GUI.Label(p2, T("Press key.."), Skins.Btn("active01"));
        else if (isSetNewButton) GUI.Label(p2, key, Skins.Btn("selectGrid"));
        else if (GUI.Button(p2, key, Skins.Btn("selectGrid"))) { isSetNewButton = true; setButtonKey = idx; }
    }

    void UpdateKeyboardScrollFromMouse(float a)
    {
        var r = keyboardScrollTrackScreenRect;
        if (r.height <= 0f || keyboardScrollMax <= 0f) { keybordScroll.y = 0f; return; }
        float th = Mathf.Max(18f, r.height * (keyboardScrollViewScreenRect.height / KeyboardContentHeight));
        keybordScroll.y = Mathf.Clamp01((a - r.y - th * 0.5f) / Mathf.Max(1f, r.height - th)) * keyboardScrollMax;
    }

    void DrawVideo()
    {
        var col = GetLayoutColumn(GetLayoutArea(), 296f);
        float y = col.y;
        var lab = new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        DrawLayoutCaption(new Rect(col.x, y, col.width, 50f), T("Quality Settings").TrimEnd(':', ' '));
        int q = GUI.SelectionGrid(new Rect(Mathf.Round(col.center.x - 116f), y + 22f, 232f, 124f), Quality, new[] { T("Fast"), T("Simple"), T("Good"), T("Beautiful"), T("Fantastic") }, 1, Skins.Btn("selectGrid"));
        if (q != Quality) { Quality = q; QualitySettings.SetQualityLevel(Mathf.Clamp(q, 0, QualitySettings.names.Length - 1), true); }
        y += 146f;
        DrawSectionDivider(col, y + 8f);
        y += 16f;
        var h1 = GetHalfColumnRect(col, y, false);
        DrawLayoutCaption(h1, T("VSync"));
        VSync = GUI.SelectionGrid(GetBlockControlRect(h1), VSync ? 0 : 1, new[] { T("YES"), T("NO") }, 2, Skins.Btn("selectGrid")) == 0;
        if (!VSync) GUI.Label(new Rect(h1.x - 10f, h1.yMax + 1f, h1.width + 20f, 16f), T("Unstable playing is possible"), lab);
        var h2 = GetHalfColumnRect(col, y, true);
        DrawLayoutCaption(h2, T("Game chat"));
        BattleChat = GUI.SelectionGrid(GetBlockControlRect(h2), BattleChat ? 0 : 1, new[] { T("On").ToUpperInvariant(), T("Off").ToUpperInvariant() }, 2, Skins.Btn("selectGrid")) == 0;
        y += 68f;
        DrawSectionDivider(col, y + 8f);
        y += 16f;
        var sr = DrawLayoutSliderBlock(col, y, "Угол обзора (FOV)", GameOptions.FpsFieldOfView, 40f, 95f);
        fovSliderScreenRect = WindowRectToScreen(sr);
        if (DrawIntValueEditor(col, y + 22f, 28f, "FovValue", Mathf.RoundToInt(GameOptions.FpsFieldOfView), 40, 95, out int h)) GameOptions.FpsFieldOfView = h;
    }

    void DrawAudio()
    {
        var col = GetLayoutColumn(GetLayoutArea(), 290f);
        float y = col.y;
        masterVolumeSliderScreenRect = WindowRectToScreen(DrawLayoutSliderBlock(col, y, T("Master Volume"), GameOptions.SoundVolumeMaster, 0f, 1f));
        if (DrawIntValueEditor(col, y + 22f, 28f, "VolumeMasterValue", (int)(GameOptions.SoundVolumeMaster * 100f), 0, 100, out int h)) GameOptions.SoundVolumeMaster = h / 100f;
        y += 58f;
        musicVolumeSliderScreenRect = WindowRectToScreen(DrawLayoutSliderBlock(col, y, T("Music Volume"), GameOptions.SoundVolumeMusic, 0f, 1f));
        if (DrawIntValueEditor(col, y + 22f, 28f, "VolumeMusicValue", (int)(GameOptions.SoundVolumeMusic * 100f), 0, 100, out int h2)) GameOptions.SoundVolumeMusic = h2 / 100f;
        y += 58f;
        effectsVolumeSliderScreenRect = WindowRectToScreen(DrawLayoutSliderBlock(col, y, T("Effects Volume"), GameOptions.SoundVolumeEffect, 0f, 1f));
        if (DrawIntValueEditor(col, y + 22f, 28f, "VolumeEffectsValue", (int)(GameOptions.SoundVolumeEffect * 100f), 0, 100, out int h3)) GameOptions.SoundVolumeEffect = h3 / 100f;
        y += 50f;
        DrawSectionDivider(col, y + 8f);
        y += 16f;
        var a1 = GetHalfColumnRect(col, y, false);
        DrawLayoutCaption(a1, T("Mute audio"));
        MuteAll = GUI.SelectionGrid(GetBlockControlRect(a1), MuteAll ? 0 : 1, new[] { T("YES"), T("NO") }, 2, Skins.Btn("selectGrid")) == 0;
        var a2 = GetHalfColumnRect(col, y, true);
        DrawLayoutCaption(a2, "Микрофон");
        Microphone = GUI.SelectionGrid(GetBlockControlRect(a2), Microphone ? 0 : 1, new[] { "ВКЛ", "ВЫКЛ" }, 2, Skins.Btn("selectGrid")) == 0;
        y += 58f;
        voiceVolumeSliderScreenRect = WindowRectToScreen(DrawLayoutSliderBlock(col, y, "Громк. игроков", PlayersVolume, 0f, 1f));
        if (DrawIntValueEditor(col, y + 22f, 28f, "VolumeVoiceValue", (int)(PlayersVolume * 100f), 0, 100, out int h4)) PlayersVolume = h4 / 100f;
    }

    // ---------------- ПРИЦЕЛ (draw_Crosshair) ----------------
    void DrawCrosshair()
    {
        int num = Mathf.Clamp(crosshairWeaponSlot, 1, 7), s = num - 1;
        var col = GetLayoutColumn(GetLayoutArea(), 378f);
        float gap = Mathf.Floor((col.width - 406f) / 6f), total = 406f + gap * 6f, x0 = Mathf.Round(col.x + (col.width - total) * 0.5f), y = col.y;
        for (int i = 0; i < 7; i++)
        {
            var r = new Rect(x0 + i * (58f + gap), y, 58f, 44f);
            crosshairWeaponSlotScreenRects[i] = WindowRectToScreen(r);
            GUI.Toggle(r, num == i + 1, GUIContent.none, Skins.Btn("selectGrid"));
            var st = Skins.S("PartsWeapon", "ws" + (i + 1));
            var tex = st != null ? st.normal.background : null;
            if (tex != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + 5f, r.width - 12f, r.height - 10f), tex, ScaleMode.ScaleToFit, true);
        }
        crosshairWeaponSlotRectsValid = true;
        float pw = Mathf.Floor((col.width - 22f) / 3f), ptotal = pw * 3f + 22f, px = Mathf.Round(col.x + (col.width - ptotal) * 0.5f), py = y + 44f + 12f;
        if (crosshairColorPaletteOpen) { crosshairPresetRectsValid = false; DrawCrosshairColorPalette(new Rect(px, py, ptotal, 168f), s); }
        else
        {
            for (int k = 0; k < 9; k++)
            {
                var r = new Rect(px + (pw + 11f) * (k % 3), py + 58f * (k / 3), pw, 52f);
                crosshairPresetScreenRects[k] = WindowRectToScreen(r);
                DrawCrosshairPresetButton(r, k - 1, s);
            }
            crosshairPresetRectsValid = true;
        }
        float lx = col.x, sx = lx + 96f + 10f, sw = 258f, sh = 18f, soff = Mathf.Floor((28f - sh) * 0.5f), voff = 2f;
        var lab = Skins.FixBold(new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, wordWrap = false });
        float row = py + 156f + 12f + 14f;
        GUI.Label(new Rect(lx, row, 96f, 28f), "Ширина:", lab);
        CrosshairSlider(new Rect(sx, row + soff, sw, sh), Mathf.Clamp(CrossThickness[s] - 2f, 0f, 12f), 0f, 12f, false, 0);
        if (DrawIntValueEditor(col, row + voff, 24f, "CrosshairWidthValue", Mathf.RoundToInt(Mathf.Clamp(CrossThickness[s] - 2f, 0f, 12f)), 0, 12, out int h)) CrossThickness[s] = h + 2f;
        row += 28f;
        GUI.Label(new Rect(lx, row, 96f, 28f), "Обводка:", lab);
        CrosshairSlider(new Rect(sx, row + soff, sw, sh), CrossOutline[s], 0f, 5f, false, 1);
        if (DrawIntValueEditor(col, row + voff, 24f, "CrosshairOutlineValue", Mathf.RoundToInt(CrossOutline[s]), 0, 5, out int h2)) CrossOutline[s] = h2;
        row += 28f;
        GUI.Label(new Rect(lx, row, 96f, 28f), "Зазор:", lab);
        CrosshairSlider(new Rect(sx, row + soff, sw, sh), CrossGap[s], 0f, 18f, false, 2);
        if (DrawIntValueEditor(col, row + voff, 24f, "CrosshairGapValue", Mathf.RoundToInt(CrossGap[s]), 0, 18, out int h3)) CrossGap[s] = h3;
        row += 28f;
        GUI.Label(new Rect(lx, row, 96f, 28f), "Цвет:", lab);
        CrosshairSlider(new Rect(sx, row + soff, sw, sh), CrossHue[s], 0f, 1f, true, 3);
        var vr = GetLayoutValueRect(col, row + voff, 24f);
        crosshairColorButtonScreenRect = WindowRectToScreen(vr); crosshairColorButtonRectValid = true;
        GUI.Toggle(vr, crosshairColorPaletteOpen, GUIContent.none, Skins.Btn("selectGrid"));
        DrawRoundedColorFill(new Rect(vr.x + 2f, vr.y + 2f, vr.width - 4f, vr.height - 4f), CrossColor[s]);
        row += 28f;
        GUI.Label(new Rect(lx, row, 96f, 28f), "Размер:", lab);
        CrosshairSlider(new Rect(sx, row + soff, sw, sh), Mathf.Clamp(CrossSize[s] - 8f, 0f, 34f), 0f, 34f, false, 4);
        if (DrawIntValueEditor(col, row + voff, 24f, "CrosshairSizeValue", Mathf.RoundToInt(Mathf.Clamp(CrossSize[s] - 8f, 0f, 34f)), 0, 34, out int h4)) CrossSize[s] = h4 + 8f;
    }

    void CrosshairSlider(Rect a, float b, float c, float d, bool hue, int idx)
    {
        GUI.Box(a, GUIContent.none, GUI.skin.horizontalSlider);
        if (hue)
        {
            var hs = GUI.skin.horizontalSlider; var o = hs.overflow;
            float w = hs.fixedWidth > 0f ? hs.fixedWidth : a.width, h = hs.fixedHeight > 0f ? hs.fixedHeight : a.height;
            var r = new Rect(a.x - o.left, a.y - o.top, w + o.left + o.right, h + o.top + o.bottom);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), GetHueTexture(), ScaleMode.StretchToFill, true);
        }
        DrawPassiveHorizontalSliderThumb(a, b, c, d);
        crosshairSliderScreenRects[idx] = WindowRectToScreen(a);
    }

    void DrawCrosshairColorPalette(Rect a, int s)
    {
        GUI.Box(a, GUIContent.none, Skins.Bg("selectGrid"));
        var st = Skins.FixBold(new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        st.normal.textColor = st.hover.textColor = st.active.textColor = st.focused.textColor = Color.white;
        GUI.Label(new Rect(a.x + 8f, a.y + 6f, a.width - 16f, 22f), "Выбор цвета", st);
        float cw = Mathf.Floor((a.width - 24f - 42f) / 8f), ch = Mathf.Floor((a.height - 32f - 12f - 18f) / 4f), tw = 8f * cw + 42f, x0 = Mathf.Round(a.x + (a.width - tw) * 0.5f), y0 = a.y + 32f;
        var e = Event.current;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 8; j++)
            {
                int n = i * 8 + j; bool hue = n >= 2; float hv = 0f;
                Color c = n == 0 ? Color.black : n == 1 ? Color.white : HueToColor(hv = (n - 2) / 30f);
                var r = new Rect(x0 + j * (cw + 6f), y0 + i * (ch + 6f), cw, ch);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                { if (hue) SetHue(s, hv); else CrossColor[s] = c; crosshairColorPaletteOpen = false; GUI.changed = true; }
                bool sel = ColorsClose(CrossColor[s], c);
                if (e.type == EventType.Repaint)
                {
                    float d = Mathf.Floor(Mathf.Min(r.width, r.height));
                    var a2 = new Rect(Mathf.Round(r.center.x - d * 0.5f), Mathf.Round(r.center.y - d * 0.5f), d, d);
                    DrawSmoothCircle(a2, sel ? new Color(1f, 0.6f, 0f, 1f) : new Color(0.04f, 0.04f, 0.04f, 1f));
                    float k = sel ? 3f : 2f;
                    DrawSmoothCircle(new Rect(a2.x + k, a2.y + k, a2.width - k * 2f, a2.height - k * 2f), sel ? new Color(0.08f, 0.08f, 0.08f, 1f) : new Color(0.16f, 0.16f, 0.16f, 1f));
                    float k2 = k + 2f;
                    DrawSmoothCircle(new Rect(a2.x + k2, a2.y + k2, a2.width - k2 * 2f, a2.height - k2 * 2f), c);
                }
            }
    }

    void DrawCrosshairPresetButton(Rect a, int b, int s)
    {
        GUI.Toggle(a, CrossStyle[s] == b, GUIContent.none, Skins.Btn("selectGrid"));
        var r = new Rect(a.x + 18f, a.y + 14f, a.width - 36f, a.height - 28f);
        if (b < 0) { var st = new GUIStyle(Skins.Txt("room")) { alignment = TextAnchor.MiddleCenter, fontSize = 12 }; GUI.Label(r, "РОДНОЙ", st); }
        else DrawAutoCrosshairGraphic(r, b, CrossColor[s], Mathf.Max(1.5f, CrossThickness[s] * 0.42f), Mathf.Max(10f, CrossSize[s] * 0.52f), CrossOutline[s] * 0.55f, CrossGap[s] * 0.55f);
    }

    static bool ColorsClose(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.035f && Mathf.Abs(a.g - b.g) < 0.035f && Mathf.Abs(a.b - b.b) < 0.035f;

    /// <summary>OptionPopup.DrawAutoCrosshairGraphic: preset previews 0..7.</summary>
    public static void DrawAutoCrosshairGraphic(Rect a, int b, Color c, float d, float e, float f, float g)
    {
        var v = new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.5f);
        float gp = Mathf.Max(0f, g), len = Mathf.Max(7f, e * 0.5f), th = Mathf.Max(1.5f, d * 0.6f), ol = Mathf.Max(0f, f), dot = Mathf.Max(2.2f, e * 0.1f);
        Rect Dot(float rr) => new Rect(v.x - rr, v.y - rr, rr * 2f, rr * 2f);
        switch (b)
        {
            case 2:
                if (ol > 0f) DrawCircle(Dot(dot + ol), Color.black);
                DrawCircle(Dot(dot), c);
                break;
            case 3:
            {
                float rad = Mathf.Max(8f, e * 0.48f), rt = Mathf.Max(1.5f, th * 0.75f);
                if (ol > 0f) { DrawCircleRing(v, rad + ol * 0.35f, rt + ol * 2f, Color.black); DrawCircle(Dot(dot + ol), Color.black); }
                DrawCircleRing(v, rad, rt, c); DrawCircle(Dot(dot), c);
                break;
            }
            case 6:
            {
                float l = Mathf.Max(8f, len * 0.9f), o = l * 0.3536f;
                if (ol > 0f) { DrawRotatedRect(v + new Vector2(-o, o), l + ol * 2f, th + ol * 2f, -45f, Color.black); DrawRotatedRect(v + new Vector2(o, o), l + ol * 2f, th + ol * 2f, 45f, Color.black); }
                DrawRotatedRect(v + new Vector2(-o, o), l, th, -45f, c); DrawRotatedRect(v + new Vector2(o, o), l, th, 45f, c);
                break;
            }
            case 7:
            {
                float rr = Mathf.Max(1.3f, e * 0.05f);
                if (ol > 0f) DrawCircle(Dot(rr + ol), Color.black);
                DrawCircle(Dot(rr), c);
                break;
            }
            case 5:
            {
                if (ol > 0f)
                {
                    float t2 = th + ol * 2f;
                    DrawFilledRect(new Rect(v.x - t2 * 0.5f, v.y + gp, t2, len + ol * 2f), Color.black);
                    DrawFilledRect(new Rect(v.x - gp - len - ol * 2f, v.y - t2 * 0.5f, len + ol * 2f, t2), Color.black);
                    DrawFilledRect(new Rect(v.x + gp, v.y - t2 * 0.5f, len + ol * 2f, t2), Color.black);
                }
                float g2 = gp + ol;
                DrawFilledRect(new Rect(v.x - th * 0.5f, v.y + g2, th, len), c);
                DrawFilledRect(new Rect(v.x - g2 - len, v.y - th * 0.5f, len, th), c);
                DrawFilledRect(new Rect(v.x + g2, v.y - th * 0.5f, len, th), c);
                break;
            }
            case 0:
            case 4:
            {
                if (ol > 0f)
                {
                    float t2 = th + ol * 2f;
                    DrawFilledRect(new Rect(v.x - t2 * 0.5f, v.y - gp - len - ol * 2f, t2, len + ol * 2f), Color.black);
                    DrawFilledRect(new Rect(v.x - t2 * 0.5f, v.y + gp, t2, len + ol * 2f), Color.black);
                    DrawFilledRect(new Rect(v.x - gp - len - ol * 2f, v.y - t2 * 0.5f, len + ol * 2f, t2), Color.black);
                    DrawFilledRect(new Rect(v.x + gp, v.y - t2 * 0.5f, len + ol * 2f, t2), Color.black);
                }
                float g2 = gp + ol;
                DrawFilledRect(new Rect(v.x - th * 0.5f, v.y - g2 - len, th, len), c);
                DrawFilledRect(new Rect(v.x - th * 0.5f, v.y + g2, th, len), c);
                DrawFilledRect(new Rect(v.x - g2 - len, v.y - th * 0.5f, len, th), c);
                DrawFilledRect(new Rect(v.x + g2, v.y - th * 0.5f, len, th), c);
                if (b == 4) { if (ol > 0f) DrawCircle(Dot(dot + ol), Color.black); DrawCircle(Dot(dot), c); }
                break;
            }
            default:
            {
                float o = gp * 0.85f, l = len * 0.95f;
                if (ol > 0f)
                {
                    float l2 = l + ol * 2f, t2 = th + ol * 2f, k = o + l2 * 0.5f;
                    DrawRotatedRect(v + new Vector2(-k, -k), l2, t2, 45f, Color.black); DrawRotatedRect(v + new Vector2(k, -k), l2, t2, -45f, Color.black);
                    DrawRotatedRect(v + new Vector2(-k, k), l2, t2, -45f, Color.black); DrawRotatedRect(v + new Vector2(k, k), l2, t2, 45f, Color.black);
                }
                float q = o + ol + l * 0.5f;
                DrawRotatedRect(v + new Vector2(-q, -q), l, th, 45f, c); DrawRotatedRect(v + new Vector2(q, -q), l, th, -45f, c);
                DrawRotatedRect(v + new Vector2(-q, q), l, th, -45f, c); DrawRotatedRect(v + new Vector2(q, q), l, th, 45f, c);
                break;
            }
        }
    }

    static void DrawFilledRect(Rect a, Color b) { var c = GUI.color; GUI.color = b; GUI.DrawTexture(a, Texture2D.whiteTexture); GUI.color = c; }
    static void DrawRotatedRect(Vector2 a, float b, float c, float d, Color e) { var m = GUI.matrix; GUIUtility.RotateAroundPivot(d, a); DrawFilledRect(new Rect(a.x - b * 0.5f, a.y - c * 0.5f, b, c), e); GUI.matrix = m; }

    static Texture2D MakeTex(int w, int h, System.Func<int, int, Color> px, bool bilinear = true)
    {
        var t = new Texture2D(w, h, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point };
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, px(x, y));
        t.Apply(false, false);
        return t;
    }

    static void DrawRoundedColorFill(Rect a, Color b)
    {
        if (roundedColorMaskTexture == null)
            roundedColorMaskTexture = MakeTex(64, 32, (x, y) => { float px = x + 0.5f, py = y + 0.5f, dx = px - Mathf.Clamp(px, 8f, 56f), dy = py - Mathf.Clamp(py, 8f, 24f); return dx * dx + dy * dy <= 64f ? Color.white : new Color(0, 0, 0, 0); });
        var c = GUI.color; GUI.color = b; GUI.DrawTexture(a, roundedColorMaskTexture, ScaleMode.StretchToFill, true); GUI.color = c;
    }

    static Texture2D GetHueTexture()
    {
        if (hueTexture == null)
            hueTexture = MakeTex(256, 20, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f, dx = px - Mathf.Clamp(px, 10f, 246f), dy = py - 10f;
                float al = Mathf.Clamp01(10.5f - Mathf.Sqrt(dx * dx + dy * dy));
                if (al <= 0f) return new Color(0, 0, 0, 0);
                var col = HueToColor(x / 255f); col.a = al; return col;
            });
        return hueTexture;
    }

    static void DrawSmoothCircle(Rect a, Color b)
    {
        if (smoothCircleTexture == null)
            smoothCircleTexture = MakeTex(64, 64, (x, y) => { float dx = x + 0.5f - 32f, dy = y + 0.5f - 32f; return new Color(1, 1, 1, Mathf.Clamp01(31.5f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)); });
        var c = GUI.color; GUI.color = b; GUI.DrawTexture(a, smoothCircleTexture, ScaleMode.StretchToFill, true); GUI.color = c;
    }

    static void DrawCircle(Rect a, Color b)
    {
        if (circleTexture == null)
            circleTexture = MakeTex(64, 64, (x, y) => { float dx = x - 31.5f, dy = y - 31.5f; return Mathf.Sqrt(dx * dx + dy * dy) / 31.5f <= 1f ? Color.white : new Color(0, 0, 0, 0); }, false);
        var c = GUI.color; GUI.color = b; GUI.DrawTexture(a, circleTexture); GUI.color = c;
    }

    static void DrawCircleRing(Vector2 a, float b, float c, Color d)
    {
        if (circleRingTexture == null)
            circleRingTexture = MakeTex(64, 64, (x, y) => { float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy) / 31.5f; return r <= 1f && r >= 0.78f ? Color.white : new Color(0, 0, 0, 0); });
        float rr = b + Mathf.Max(0f, c - 1.5f) * 0.25f;
        var col = GUI.color; GUI.color = d; GUI.DrawTexture(new Rect(a.x - rr, a.y - rr, rr * 2f, rr * 2f), circleRingTexture, ScaleMode.StretchToFill, true); GUI.color = col;
    }

    /// <summary>喞.CrosshairHueToColor.</summary>
    public static Color HueToColor(float a)
    {
        float n = Mathf.Repeat(a, 1f) * 6f; int i = Mathf.FloorToInt(n); float f = n - i, q = 1f - f;
        switch (i)
        {
            case 0: return new Color(1f, f, 0f, 1f);
            case 1: return new Color(q, 1f, 0f, 1f);
            case 2: return new Color(0f, 1f, f, 1f);
            case 3: return new Color(0f, q, 1f, 1f);
            case 4: return new Color(f, 0f, 1f, 1f);
            default: return new Color(1f, 0f, q, 1f);
        }
    }

    // ---------------- sensitivity text / OK / Cancel ----------------
    void SyncSensitivityTextFields()
    {
        pendingMouseSensitivity = Mathf.Clamp(GameOptions.Sensivity, 0.01f, 10f);
        pendingAimSensitivity = Mathf.Clamp(GameOptions.AimSensitivity, 0.01f, 10f);
        mouseSensitivityText = FormatSensitivity(pendingMouseSensitivity);
        aimSensitivityText = FormatSensitivity(pendingAimSensitivity);
        sensitivityTextInitialized = true;
    }

    static string FormatSensitivity(float a) => Mathf.Clamp(a, 0.01f, 10f).ToString("0.00", CultureInfo.InvariantCulture);

    static string FilterSensitivityText(string a)
    {
        var sb = new StringBuilder(); bool dot = false; int dec = 0;
        foreach (char c in a)
        {
            if (char.IsDigit(c)) { if (dot) { if (dec >= 2) continue; dec++; } sb.Append(c); }
            else if ((c == '.' || c == ',') && !dot) { dot = true; sb.Append('.'); }
        }
        return sb.ToString();
    }

    static bool TryParseSensitivity(string a, out float b) { b = 0f; return !string.IsNullOrEmpty(a) && float.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out b); }

    void ClickReset()
    {
        GameOptions.Reload();
        SyncSensitivityTextFields();
        crosshairColorPaletteOpen = false;
        open = false;
        HoverUI.Enabled = true;
    }

    void ClickOK()
    {
        if (!sensitivityTextInitialized) SyncSensitivityTextFields();
        if (TryParseSensitivity(mouseSensitivityText, out float b)) pendingMouseSensitivity = Mathf.Clamp(b, 0.01f, 10f);
        if (TryParseSensitivity(aimSensitivityText, out float b2)) pendingAimSensitivity = Mathf.Clamp(b2, 0.01f, 10f);
        GameOptions.Sensivity = Mathf.Round(pendingMouseSensitivity * 100f) / 100f;
        GameOptions.AimSensitivity = Mathf.Round(pendingAimSensitivity * 100f) / 100f;
        SyncSensitivityTextFields();
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        AudioListener.volume = GameOptions.SoundVolumeMaster;   // pause is set by SoundIsMute
        GameOptions.Save();
        crosshairColorPaletteOpen = false;
        open = false;
        HoverUI.Enabled = true;
    }
}
