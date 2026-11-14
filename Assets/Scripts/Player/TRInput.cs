// Port of the original Contra City (Penta 2016) key bindings: TRInput defaults + OptionsManager PlayerPrefs keys ("cb*").
using UnityEngine;

public static class TRInput
{
    /// <summary>KeyCode.ToDisplayString (NE 啙.cs:128-165): ЛКМ / ПКМ / СКМ from the language file, Mouse 4 / 5, digits, else the KeyCode name.</summary>
    public static string ToDisplayString(KeyCode a)
    {
        switch (a)
        {
            case KeyCode.Mouse0: return LanguageManager.GetText("Left Mouse");
            case KeyCode.Mouse1: return LanguageManager.GetText("Right Mouse");
            case KeyCode.Mouse2: return LanguageManager.GetText("Middle Mouse");
            case KeyCode.Mouse3: return "Mouse 4";
            case KeyCode.Mouse4: return "Mouse 5";
            case KeyCode.Alpha1: return "1"; case KeyCode.Alpha2: return "2"; case KeyCode.Alpha3: return "3"; case KeyCode.Alpha4: return "4";
            case KeyCode.Alpha5: return "5"; case KeyCode.Alpha6: return "6"; case KeyCode.Alpha7: return "7"; case KeyCode.Alpha8: return "8";
            case KeyCode.Alpha9: return "9"; case KeyCode.Alpha0: return "0";
            default: return a.ToString();
        }
    }

    public static KeyCode Forward = KeyCode.W;
    public static KeyCode Backward = KeyCode.S;
    public static KeyCode LeftStrafe = KeyCode.A;
    public static KeyCode RightStrafe = KeyCode.D;
    public static KeyCode Jump = KeyCode.Space;
    public static KeyCode Crouch = KeyCode.LeftControl;
    public static KeyCode Weapon1 = KeyCode.Alpha1;
    public static KeyCode Weapon2 = KeyCode.Alpha2;
    public static KeyCode Weapon3 = KeyCode.Alpha3;
    public static KeyCode Weapon4 = KeyCode.Alpha4;
    public static KeyCode Weapon5 = KeyCode.Alpha5;
    public static KeyCode Weapon6 = KeyCode.Alpha6;
    public static KeyCode Weapon7 = KeyCode.Alpha7;
    public static KeyCode EnterChat = KeyCode.Return;
    public static KeyCode CallTechnic = KeyCode.F;
    public static KeyCode Fire1 = KeyCode.Mouse0;
    public static KeyCode QuickChange = KeyCode.Q;
    public static KeyCode Taunt1 = KeyCode.E;
    public static KeyCode Taunt2 = KeyCode.T;
    public static KeyCode Taunt3 = KeyCode.Y;
    public static KeyCode ScreenShot = KeyCode.P;
    public static KeyCode Enhancer1 = KeyCode.Alpha8;
    public static KeyCode Enhancer2 = KeyCode.Alpha9;
    public static KeyCode Zoom = KeyCode.Mouse1;
    public static KeyCode Reload = KeyCode.R;
    public static KeyCode RollIn = KeyCode.PageUp;
    public static KeyCode RollOut = KeyCode.PageDown;

    static readonly (string key, System.Func<KeyCode> get, System.Action<KeyCode> set)[] Binds =
    {
        ("cbForward", () => Forward, k => Forward = k), ("cbBackward", () => Backward, k => Backward = k),
        ("cbLeftStrafe", () => LeftStrafe, k => LeftStrafe = k), ("cbRightStrafe", () => RightStrafe, k => RightStrafe = k),
        ("cbJump", () => Jump, k => Jump = k), ("cbCrouch", () => Crouch, k => Crouch = k),
        ("cbWeapon1", () => Weapon1, k => Weapon1 = k), ("cbWeapon2", () => Weapon2, k => Weapon2 = k),
        ("cbWeapon3", () => Weapon3, k => Weapon3 = k), ("cbWeapon4", () => Weapon4, k => Weapon4 = k),
        ("cbWeapon5", () => Weapon5, k => Weapon5 = k), ("cbWeapon6", () => Weapon6, k => Weapon6 = k),
        ("cbWeapon7", () => Weapon7, k => Weapon7 = k), ("cbEnterChat", () => EnterChat, k => EnterChat = k),
        ("cbCallTechnic", () => CallTechnic, k => CallTechnic = k), ("cbFire1", () => Fire1, k => Fire1 = k),
        ("cbQuickChange", () => QuickChange, k => QuickChange = k), ("cbTaunt1", () => Taunt1, k => Taunt1 = k),
        ("cbTaunt2", () => Taunt2, k => Taunt2 = k), ("cbTaunt3", () => Taunt3, k => Taunt3 = k),
        ("cbScreenShot", () => ScreenShot, k => ScreenShot = k), ("cbEnhancer1", () => Enhancer1, k => Enhancer1 = k),
        ("cbEnhancer2", () => Enhancer2, k => Enhancer2 = k), ("cbZoom", () => Zoom, k => Zoom = k),
        ("cbReload", () => Reload, k => Reload = k),
    };

    /// <summary>啙.ResetDefault: the original key layout (啛 field initialisers).</summary>
    public static void ResetDefaults()
    {
        Forward = KeyCode.W; Backward = KeyCode.S; LeftStrafe = KeyCode.A; RightStrafe = KeyCode.D; Jump = KeyCode.Space; Crouch = KeyCode.LeftControl;
        Weapon1 = KeyCode.Alpha1; Weapon2 = KeyCode.Alpha2; Weapon3 = KeyCode.Alpha3; Weapon4 = KeyCode.Alpha4; Weapon5 = KeyCode.Alpha5; Weapon6 = KeyCode.Alpha6; Weapon7 = KeyCode.Alpha7;
        EnterChat = KeyCode.Return; CallTechnic = KeyCode.F; Fire1 = KeyCode.Mouse0; QuickChange = KeyCode.Q;
        Taunt1 = KeyCode.E; Taunt2 = KeyCode.T; Taunt3 = KeyCode.Y; Enhancer1 = KeyCode.Alpha8; Enhancer2 = KeyCode.Alpha9;
        Zoom = KeyCode.Mouse1; Reload = KeyCode.R; RollIn = KeyCode.PageUp; RollOut = KeyCode.PageDown;
    }

    public static void Load()
    {
        foreach (var b in Binds)
            if (PlayerPrefs.HasKey(b.key)) b.set((KeyCode)PlayerPrefs.GetInt(b.key));
    }

    public static void Save()
    {
        foreach (var b in Binds) PlayerPrefs.SetInt(b.key, (int)b.get());
        PlayerPrefs.Save();
    }
}

/// <summary>Mouse modes of the original options window (Configuration.MouseMode, PlayerPrefs "cmmode").</summary>
public enum CameraOrbitMode : byte { Neutral = 1, Normal = 2, Smooth = 3 }

/// <summary>Subset of the original OptionsManager: mouse sensitivity/invert/mode and sound volumes, same PlayerPrefs keys.</summary>
public static class GameOptions
{
    // Defaults = the player's own saved contra.city settings (registry: cmsensity 1.02, cmaimsensity 1.02, fpsfov 74.7).
    // Penta 2016 defaults were sensitivity 3 and FOV 55. Formula is the same in Penta / contra.city / New Era:
    // degrees per raw mouse unit = Sensivity * 50 * 0.02 (Neutral mode).
    static float sensivity = 1.02f;                      // slider range 0.01..10
    public static float MouseSensityX { get; private set; } = 51f; // = Sensivity * 50
    public static float AimSensitivity = 1.02f;          // contra.city addition: separate sensitivity while aiming ("cmaimsensity")
    static float fov = 74.66f;
    /// <summary>contra.city addition ("fpsfov"): first-person field of view, 40..95 (zombies get x1.5, clamped to 110).</summary>
    public static float FpsFieldOfView { get => fov; set => fov = Mathf.Clamp(value, 40f, 95f); }
    public static bool InvertMouseY;
    public static CameraOrbitMode MouseMode = CameraOrbitMode.Neutral;
    // OptionsManager / 喞: master 0.5 (AudioListener.volume), effects 0.5, music 0 (unset key; nothing in the game uses it)
    static float master = 0.5f;
    public static float SoundVolumeMaster { get => master; set { master = value; AudioListener.volume = value; } }
    public static float SoundVolumeEffect = 0.5f, SoundVolumeMusic = 0f;
    static bool mute;
    /// <summary>OptionsManager.SoundIsMute ("smute"): AudioListener.pause, and SoundManager plays nothing.</summary>
    public static bool SoundIsMute { get => mute; set { mute = value; AudioListener.pause = value; } }
    static bool loaded;

    public static float Sensivity
    {
        get => sensivity;
        set { sensivity = Mathf.Clamp(value, 0.01f, 10f); MouseSensityX = sensivity * 50f; }
    }

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Sensivity = PlayerPrefs.HasKey("cmsensity") ? PlayerPrefs.GetFloat("cmsensity") : sensivity;
        if (PlayerPrefs.HasKey("cmaimsensity")) AimSensitivity = PlayerPrefs.GetFloat("cmaimsensity");
        if (PlayerPrefs.HasKey("fpsfov")) FpsFieldOfView = PlayerPrefs.GetFloat("fpsfov");
        if (PlayerPrefs.HasKey("cminvert") && bool.TryParse(PlayerPrefs.GetString("cminvert"), out var inv)) InvertMouseY = inv;
        if (PlayerPrefs.HasKey("cmmode")) MouseMode = (CameraOrbitMode)PlayerPrefs.GetInt("cmmode");
        if (PlayerPrefs.HasKey("sma")) SoundVolumeMaster = PlayerPrefs.GetFloat("sma");
        if (PlayerPrefs.HasKey("smu")) SoundVolumeMusic = PlayerPrefs.GetFloat("smu");
        if (PlayerPrefs.HasKey("sme")) SoundVolumeEffect = PlayerPrefs.GetFloat("sme");
        if (PlayerPrefs.HasKey("smute") && bool.TryParse(PlayerPrefs.GetString("smute"), out var m)) SoundIsMute = m;
        TRInput.Load();
    }

    /// <summary>«Отмена»: forget unsaved changes (喞.Instance.Load).</summary>
    public static void Reload() { loaded = false; Load(); }

    public static void Save()
    {
        PlayerPrefs.SetFloat("cmsensity", Sensivity);
        PlayerPrefs.SetFloat("cmaimsensity", AimSensitivity);
        PlayerPrefs.SetFloat("fpsfov", FpsFieldOfView);
        PlayerPrefs.SetString("cminvert", InvertMouseY.ToString());
        PlayerPrefs.SetInt("cmmode", (int)MouseMode);
        PlayerPrefs.SetFloat("sma", SoundVolumeMaster);
        PlayerPrefs.SetFloat("smu", SoundVolumeMusic);
        PlayerPrefs.SetFloat("sme", SoundVolumeEffect);
        PlayerPrefs.SetString("smute", SoundIsMute.ToString());
        TRInput.Save();
    }
}
