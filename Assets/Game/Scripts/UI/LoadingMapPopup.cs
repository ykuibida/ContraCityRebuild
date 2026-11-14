// New Era LoadingMapPopup: window 678x515 (windowLoading) over everything while the map loads —
// «Загрузка карты...» + status text, «Совет» block (啓 tips, change every 10 s, N = next), map picture with its name,
// the 315x326 invite banner (server image, cropped from a New Era screenshot) and the pb2 progress bar (visual speed 125 %/s).
using UnityEngine;

public static class LoadingMapPopup
{
    public static bool Show { get; private set; }
    static float progress, targetProgress, nextTip;
    static int tip;
    static string msg = "";
    static MapInfo map;
    static Rect windowRect = new Rect(0, 0, 678, 515);
    static Texture2D invite;

    public static bool IsVisualProgressComplete => targetProgress >= 99.99f && progress >= 99.99f;

    public static void Init(MapInfo m)
    {
        Show = true; progress = targetProgress = 0f; map = m; nextTip = 0f;
        msg = LanguageManager.GetText("Map initialization...");
    }

    public static void Progress(float p) { p = Mathf.Clamp(p, 0f, 100f); if (p > targetProgress) targetProgress = p; }
    public static void Complete() => Show = false;

    public static void Update()
    {
        if (!Show) return;
        progress = Mathf.MoveTowards(progress, targetProgress, 125f * Time.unscaledDeltaTime);
        if (IsVisualProgressComplete && map != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == map.SystemName + "_Viewer") Complete();
    }

    /// <summary>啓.TipByNumber (17 tips, rotate every 10 s).</summary>
    static string Tip()
    {
        if (Time.time > nextTip) { nextTip = Time.time + 10f; tip = Random.Range(0, 17); }
        switch (tip)
        {
            case 0: return LanguageManager.GetText("To enter the full screen mode please press \"Alt + F\"");
            case 1: return LanguageManager.GetText("During the game you can change the weapon with the mouse scroll");
            case 2: case 3: return LanguageManager.GetText("Press \"TAB\" to check the statistics of the round");
            case 4: return LanguageManager.GetText("You will get the money for completing the achievements!");
            case 5: return LanguageManager.GetText("You can always buy stronger weapon in the SHOP");
            case 6: return LanguageManager.GetText("Invite your friends and get money for that!");
            case 7: return LanguageManager.GetText("You can get unique abilities in Training hall of your Headquaters!");
            case 8: return LanguageManager.GetText("Sniper rifles deal as much damage as your target is far away");
            case 9: return LanguageManager.GetText("Ammunition ended and there is no time for reload? Change the weapon!");
            case 10: return LanguageManager.GetText("Shooting from shotgun gives you ability to jump thanks to its reciol");
            case 11: return LanguageManager.GetText("Remember - machine gun needs some time to spin before shooting!");
            case 12: return LanguageManager.GetText("Improve yourself in the Training hall of your Headquaters!");
            case 13: return LanguageManager.GetTextFormat("Press \"{0}\" to salute your killed enemy and other players", TRInput.Taunt1);
            case 14: return LanguageManager.GetText("If there is some lags during the game try to descrease the video settings");
            case 15: return LanguageManager.GetText("Complete the achievements and get the money for that!");
            default: return LanguageManager.GetText("NEVER give the links of your game pages to ANYONE. Also don't give the links which can be copied in the game by pressing the right button of the mouse!");
        }
    }

    public static void Draw()
    {
        if (!Show) return;
        int depth = GUI.depth; GUI.depth = -10000;
        windowRect.x = Screen.width * 0.5f - windowRect.width * 0.5f;
        windowRect.y = Screen.height * 0.5f - windowRect.height * 0.5f;
        GUI.Button(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);   // swallow clicks
        Skins.Window(3, windowRect, DrawWindow, Skins.Bg("windowLoading"));
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.N) tip = (tip + 1) % 17;
        GUI.depth = depth;
    }

    static void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(52));
        GUILayout.Space(7);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive2"));
        string title = LanguageManager.GetText("Map loading...");
        Skins.AutoLabel(title, Skins.Txt("partActive"));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), title, Skins.Txt("partActive"), Skins.Txt("partActiveShadow"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(); GUILayout.Label(msg, Skins.S("BattleText", "txt1Value")); GUILayout.Space(15); GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(15);
        GUILayout.BeginHorizontal();
        GUILayout.Space(15);
        GUILayout.BeginVertical(GUILayout.Width(320));
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(316), GUILayout.Height(126));
        GUILayout.Label(LanguageManager.GetText("Hint"), Skins.Txt("mapName"));
        GUILayout.Space(1);
        GUILayout.Label(Tip(), Skins.Txt("mapDesc"));
        GUILayout.EndVertical();
        GUILayout.Space(12);
        if (map != null)
        {
            GUILayout.Label(map.Ico, GUIStyle.none, GUILayout.Width(316), GUILayout.Height(188));
            Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), map.Name.ToUpper(), Skins.Txt("mapNameBig"), Skins.Txt("mapNameBigShadow"));
        }
        else GUILayout.Label(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(316), GUILayout.Height(188));
        GUILayout.EndVertical();
        GUILayout.Space(9);
        if (invite == null) invite = Resources.Load<Texture2D>("gui/promo/invite_small");
        if (invite != null) GUILayout.Label(invite, GUIStyle.none, GUILayout.Width(315), GUILayout.Height(326));
        else GUILayout.Label(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(315), GUILayout.Height(326));
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.Space(15);
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(644), GUILayout.Height(80));
        GUILayout.Space(19);
        GUILayout.BeginHorizontal();
        GUILayout.Space(15);
        ProgressBar(590f, 100f, progress, "pb2");
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    /// <summary>GUIProgressBar.ProgressBar over the Progressbar skin.</summary>
    static void ProgressBar(float w, float max, float val, string e)
    {
        var line = Skins.S("Progressbar", e + "Line");
        float n = Mathf.FloorToInt(w * val / max), b = line.border.left + line.border.right;
        n = Mathf.Clamp(n, b, w - b);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.S("Progressbar", e + "Background"), GUILayout.Width(w));
        GUILayout.Label(GUIContent.none, line, GUILayout.Width(n));
        GUILayout.EndHorizontal();
    }
}
