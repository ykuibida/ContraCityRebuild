// The local player's weapons = port of the New Era ShotController (NE_Scripts_Decrypted/ShotController.cs) and its base class
// WeaponController (NE WeaponController.cs); Penta 2016 ShotController.cs / WeaponController.cs are the reference.
// Weapons stage 1: the first-person models (the original Fighter_locomotion/Fighter/MainCamera/Weapon container cloned under the eye
// camera, with the WeaponCamera that draws layer 8), the 7 slots, switching (1–7, wheel, Q), the container's change animation,
// the model's Reset clip and the HUD weapon bar / ammo.
// Weapons stage 2: the fire loop of Update, DoShot for pistols / rifles / gatling / sniper rifles (melee, shotgun, rockets, grenades,
// sticky bombs: stage 3), OnShot (ammo), auto-reload, Reload with the model's reload clips and sounds, the sequential shell reload
// interrupt, the sniper zoom flag and the 3D crosshair (texture, colour, rotation).
// Line numbers: NE ShotController.cs unless noted.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShotController : MonoBehaviour
{
    public static int WeaponNum = 7;                     // WeaponController.WeaponNum
    public static ShotController Instance { get; private set; }

    CombatWeapon weapon;
    readonly CombatWeapon[] weapons = new CombatWeapon[WeaponNum];
    bool shot, shotAlt, zoom, isTaunt, weaponBlocked;
    readonly System.Random random = new System.Random();
    float reloadAnimationBlockUntil;
    CombatWeapon reloadAnimationWeapon, pendingAutoReloadWeapon;
    bool shotgunReloadInterruptArmed, vorchunReloadClickQueued, closeGrenadeQueuedFire;
    int vorchunPredictedShotAcks;
    static readonly long CheckWeaponStateInterval = 500000L;   // 50 ms
    static long lastCheckWeaponState = DateTime.Now.Ticks;
    float nextWeaponSwitchTime;
    Coroutine weaponChangeRoutine;
    int previousWeapon = 1;
    Camera fpsCamera, weaponCamera;
    Transform weaponContainer;

    public CombatWeapon CurrentWeapon => weapon;
    public CombatWeapon[] Weapons => weapons;
    public Camera WeaponCamera => weaponCamera;
    public int Ammo => weapon == null ? 0 : weapon.LoadedAmmo;
    public int MaxAmmo => weapon == null ? 0 : weapon.MaxLoadedAmmo;
    public int AmmoReserve => weapon == null ? 0 : weapon.AmmoReserve;
    public bool IsShooting => shot;
    public bool IsTaunt { get => isTaunt; set => isTaunt = value; }
    public string GetAmmoCountString() => weapon == null ? "0/0" : weapon.LoadedAmmo + "/" + weapon.AmmoReserve;   // :275-282

    public bool IsShootingAlt => shotAlt;
    public bool IsBlocked => CurrentWeapon.IsWeaponBlocked();

    /// <summary>The Zoom setter (:142-162): GameHUD.Zoom for the sniper rifle (or whenever the zoom goes off).</summary>
    public bool Zoom
    {
        get => zoom;
        set
        {
            zoom = value;
            if (weapon == null) { if (!zoom && Hud != null) Hud.Zoom(false); }
            else if (weapon.Type == WeaponType.SNIPER_RIFLE || !zoom) { if (Hud != null) Hud.Zoom(zoom); }
        }
    }

    // ---------------- set-up (prefab Fighter_locomotion + CombatPlayer.InitWeapon) ----------------
    static GameObject template;
    /// <summary>The hands' shared materials with the prefab texture. HandTextureLoader writes the glove texture into the shared material
    /// (NE), which New Era loaded fresh with the fighter; our container lives for the whole session, so every new set of weapons starts
    /// from the prefab texture again — otherwise a life without gloves would keep the last glove texture (Penta used per-renderer copies).</summary>
    static readonly Dictionary<Material, Texture> prefabHandTexture = new Dictionary<Material, Texture>();
    /// <summary>The Weapon container of the lobby scene ("WeaponTemplate", inactive), kept across scene loads.</summary>
    public static GameObject Template()
    {
        if (template) return template;
        var holder = GameObject.Find("WeaponTemplate");
        if (!holder || holder.transform.childCount == 0) return null;
        DontDestroyOnLoad(holder);
        template = holder.transform.GetChild(0).gameObject;
        foreach (var h in template.GetComponentsInChildren<HandTextureLoader>(true))
        {
            var r = h.GetComponent<Renderer>();
            if (r && r.sharedMaterial && !prefabHandTexture.ContainsKey(r.sharedMaterial)) prefabHandTexture[r.sharedMaterial] = r.sharedMaterial.mainTexture;
        }
        return template;
    }

    /// <summary>Builds MainCamera/Weapon + MainCamera/WeaponCamera of the prefab under <paramref name="fps"/>, then
    /// CombatPlayer.InitWeapon (NE CombatPlayer.cs:1615-1635): InitWeapon(i, weaponInfo[i]) for slots 0..6.</summary>
    public static ShotController Attach(GameObject player, Camera fps, FighterBody body)
    {
        var sc = player.AddComponent<ShotController>();
        sc.fpsCamera = fps;
        Instance = sc;
        var src = Template();
        if (!src) { Debug.LogWarning("[ShotController] no weapon template"); return sc; }
        var w = Instantiate(src, fps.transform, false);   // the template is inactive: nothing starts before InitWeapon
        w.name = "Weapon";
        sc.weaponContainer = w.transform;
        // prefab MainCamera/WeaponCamera: clear Depth, culling layer 8 only, FOV 60, depth 1, near 0.1, far 300 (GUILayer dropped);
        // 嗃.ApplyCameraRig (NE 嗃.cs:39-60): far ≥ 1000 for the eye camera, weapon camera depth above it, two weapon fill lights
        var camGo = new GameObject("WeaponCamera");
        camGo.layer = 8;
        camGo.transform.SetParent(fps.transform, false);
        var wc = camGo.AddComponent<Camera>();
        wc.clearFlags = CameraClearFlags.Depth; wc.backgroundColor = Color.clear;
        wc.cullingMask = 256; wc.fieldOfView = 60f; wc.nearClipPlane = 0.1f; wc.farClipPlane = 300f; wc.depth = 1f;
        if (wc.depth <= fps.depth) wc.depth = fps.depth + 1f;
        if (fps.farClipPlane < 1000f) fps.farClipPlane = 1000f;
        FillLight(wc.transform, "__WeaponFillLight", new Color(1f, 0.98f, 0.94f, 1f), 0.95f, Quaternion.Euler(18f, -32f, 0f));       // 嗃.cs:149-161
        FillLight(wc.transform, "__WeaponBackFillLight", new Color(0.78f, 0.84f, 0.92f, 1f), 0.35f, Quaternion.Euler(-12f, 140f, 0f)); // 嗃.cs:163-175
        sc.weaponCamera = wc;

        // hands: PlayerCustomisator.SetHandTextureID during InitWear (before the models start); arms: ArmClothModel.Sync
        var outfit = new Dictionary<int, GameItem>();
        foreach (var kv in Profile.Current.EquippedWear) { var it = ItemDB.Get(ItemKind.Wear, kv.Value); if (it != null) outfit[kv.Key] = it; }
        var fighter = body ? body.Body : null;
        foreach (var kv in prefabHandTexture) if (kv.Key) kv.Key.mainTexture = kv.Value;
        string hand = Dresser.HandTextureID(fighter, outfit);
        if (hand != "") foreach (var h in w.GetComponentsInChildren<HandTextureLoader>(true)) h.textureID = hand;

        var info = BattleWeapons.Build(Profile.Current);
        for (int i = 0; i < WeaponNum; i++) if (info.TryGetValue(i, out var ht)) sc.InitWeapon(i, new CombatWeapon(i, ht));
        if (fighter)
        {
            var current = Dresser.CurrentClothes(fighter, outfit);
            ArmClothModel.Sync(w.transform, current[4], current[3]);
        }
        w.SetActive(true);
        return sc;
    }

    static void FillLight(Transform cam, string name, Color color, float intensity, Quaternion rot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(cam, false);
        go.hideFlags = HideFlags.DontSave;
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional; l.color = color; l.intensity = intensity; l.shadows = LightShadows.None;
        l.cullingMask = 256; l.renderMode = LightRenderMode.ForcePixel;
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = rot;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>base.transform.GetComponentsInChildren&lt;WeaponLook&gt;(true) of the New Era local fighter, whose only WeaponLook nodes are
    /// the first-person ones; our soldier body is the remote fighter prefab with its own third-person nodes, so only the container counts.</summary>
    WeaponLook[] WeaponLooks() => weaponContainer ? weaponContainer.GetComponentsInChildren<WeaponLook>(true) : new WeaponLook[0];
    public Transform Container => weaponContainer;

    /// <summary>ShotController.InitWeapon (:1024-1103): the type node (WeaponLook named getName(), or +"L" / +"R"), the model named
    /// SystemName kept (shown for slot 0), every other model hidden and destroyed (喩.Destroy_Geometry = true) — only hidden for cold
    /// arms in ZOMBIE mode.</summary>
    public int InitWeapon(int a, CombatWeapon b)
    {
        weapons[a] = b;
        if (weapons[a] == null || weapons[a].Transform == null || !string.Equals(weapons[a].SystemName, "OHCA_Zombie", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var wl in WeaponLooks())
                if (wl.transform.name == weapons[a].GetName() || wl.transform.name == weapons[a].GetName() + "L" || wl.transform.name == weapons[a].GetName() + "R")
                    weapons[a].Transform = wl.transform;
        }
        if (weapons[a].Type == WeaponType.TWO_HANDED_COLD_ARMS) weapons[a].SwitchWeaponTexture(0);
        bool zombieMode = GameFlow.Instance != null && GameFlow.Instance.Room != null && GameFlow.Instance.Room.Mode == GameMode.ZOMBIE;
        var t = weapons[a].Transform;
        if (a == 0)
        {
            weapon = weapons[a];
            SetCrossHair();
            if (t != null)
                for (int j = 0; j < t.childCount; j++)
                {
                    var child = t.GetChild(j);
                    if (child.gameObject.name == weapon.SystemName) { child.gameObject.SetActive(true); setWeaponVisible(child, true); continue; }
                    setWeaponVisible(child, false);
                    if (!zombieMode || (weapon.Type != WeaponType.ONE_HANDED_COLD_ARMS && weapon.Type != WeaponType.TWO_HANDED_COLD_ARMS)) Destroy(child.gameObject);
                    else child.gameObject.SetActive(false);
                }
            ResetReloadWeaponAnimation(weapon.Transform);
        }
        else if (t != null)
        {
            for (int k = 0; k < t.childCount; k++)
            {
                var child = t.GetChild(k);
                if (child.gameObject.name == weapons[a].SystemName) { child.gameObject.SetActive(true); setWeaponVisible(child, false); continue; }
                setWeaponVisible(child, false);
                if (!zombieMode || (weapons[a].Type != WeaponType.ONE_HANDED_COLD_ARMS && weapons[a].Type != WeaponType.TWO_HANDED_COLD_ARMS)) Destroy(child.gameObject);
                else child.gameObject.SetActive(false);
            }
        }
        shot = false; shotAlt = false;
        return weapons[a].GetType();
    }

    // ---------------- input (Update :1194-1496, stage 1 part) ----------------
    /// <summary>Test hook (-uicompare): holds Fire1 like the mouse button.</summary>
    public static bool TestFire;
    static bool testFireWas, testFireDown, testFireUp;
    static bool FireHeld => Input.GetKey(TRInput.Fire1) || TestFire;
    bool FireDown => Input.GetKeyDown(TRInput.Fire1) || testFireDown;
    bool FireUp => Input.GetKeyUp(TRInput.Fire1) || testFireUp;
    static BattleUI Hud => UIRoot.Instance ? UIRoot.Instance.Battle : null;
    /// <summary>Rebuild (bots lab, walk mode): the lab has no battle HUD, so weapons would never fire or switch; true lets them.</summary>
    public static bool LabControl;
    static bool HudActive => LabControl || (Hud != null && Hud.IsHudActive);
    /// <summary>Rebuild (bots lab): every shot the local fighter sends (its weapon type), so the lab knows which moves the owner made.</summary>
    public static event System.Action<WeaponType> LocalShotSent;

    void Update()
    {
        testFireDown = TestFire && !testFireWas; testFireUp = !TestFire && testFireWas; testFireWas = TestFire;   // test hook edges
        // F12 toggled the camera's ColorCorrectionCurves (none on our camera); 喩.CheckAim debug dummy: dropped
        if (FireUp)   // :1208-1221
        {
            bool vorchun = IsVorchunGrenadeLauncher(weapon);
            if (!vorchun || !vorchunReloadClickQueued)
            {
                shot = false;
                if (vorchun) { closeGrenadeQueuedFire = false; weapon.CancelQueuedSequentialReloadFire(); }
            }
            shotgunReloadInterruptArmed = false;
        }
        if (Input.GetKeyUp(KeyCode.Mouse1)) shotAlt = false;
        if (!HudActive) return;            // GameHUD.isActive (NE GameHUD.cs:545-556)
        // 嚍.IsWrite (chat input) — there is no battle chat input
        if (isTaunt) return;                                    // the taunt runs to its end (owner's rule: never interrupted)
        if (GameFlow.Instance != null && GameFlow.Instance.Player) Hud.ShowWeapon();   // :1246-1250 GameHUD.UpdateHealth + ShowWeapon every frame
        if (weapon == null) return;
        weapon.CalcMode();
        if (!FireHeld && !vorchunReloadClickQueued && !closeGrenadeQueuedFire && !weapon.HasQueuedSequentialReloadFire) shot = false;
        for (int n = 1; n <= 7; n++) if (IsWeaponKeyDown(n)) { SetWeapon(n); return; }
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel > 0f) { SetPrevWeapon(); return; }
        if (wheel < 0f) { SetNextWeapon(); return; }
        if (Input.GetKeyDown(TRInput.QuickChange)) { QuickChange(); return; }
        if (Input.GetKeyDown(TRInput.Reload))   // :1307-1315
        {
            bool closeAndFire = IsCloseBeforeShotGrenadeLauncher(weapon) && FireHeld;
            Reload(weapon);
            if (!closeAndFire) return;
        }
        if (FireDown)   // :1316-1340
        {
            if (IsVorchunGrenadeLauncher(weapon) && (weapon.WeaponMode == WeaponMode.Reloading || weapon.WeaponMode == WeaponMode.ReloadingReady || (reloadAnimationWeapon == weapon && Time.time < reloadAnimationBlockUntil)))
            {
                if (weapon.LoadedAmmo > 0) { vorchunReloadClickQueued = true; shot = true; }
                else { vorchunReloadClickQueued = false; closeGrenadeQueuedFire = false; weapon.CancelQueuedSequentialReloadFire(); shot = false; }
            }
            else shot = true;
            if (weapon != null && weapon.Type == WeaponType.SHOT_GUN) shotgunReloadInterruptArmed = true;
        }
        if (Input.GetKeyDown(KeyCode.Mouse1)) shotAlt = true;
        // KickManager voting (Y / N) and the Equals-key enemy animation test: dropped; the taunt keys are read by BattleUI (only while !shot)
        if (IsVorchunGrenadeLauncher(weapon))   // :1371-1383
        {
            bool key = FireHeld;
            if (!shot && key && weapon.LoadedAmmo > 0) shot = true;
            if (vorchunReloadClickQueued || (shot && key)) TryInterruptSequentialReloadForFire();
        }
        else if (shot || (weapon != null && weapon.HasQueuedSequentialReloadFire))
        {
            if (weapon.Type == WeaponType.SHOT_GUN)
            {
                bool key2 = FireHeld;
                if ((shotgunReloadInterruptArmed || key2) && TryInterruptSequentialReloadForFire()) shotgunReloadInterruptArmed = false;
            }
            else TryInterruptSequentialReloadForFire();
        }
        if (shot && !IsReloadAnimationBlocked())   // :1398-1440
        {
            if (weapon.IsWeaponBlocked())
            {
                if (EffectManager.Instance) EffectManager.Instance.weaponBlockEffect(this, weapon.Type);
                if (weapon.LoadedAmmo <= 0 && weapon.Type == WeaponType.GATLING_GUN && weapon.Stop()) DoStop();
            }
            else if (weapon.LoadedAmmo > 0 || weapon.Type == WeaponType.ONE_HANDED_COLD_ARMS || weapon.Type == WeaponType.TWO_HANDED_COLD_ARMS)
            {
                if ((weapon.Type == WeaponType.GATLING_GUN || weapon.Type == WeaponType.FLAMER || weapon.Type == WeaponType.ACID_THROWER || weapon.Type == WeaponType.SNOW_GUN) && weapon.Launch()) DoLaunch();
                else if (weapon.Fire())
                {
                    if (weapon.Type == WeaponType.SHOT_GUN) shotgunReloadInterruptArmed = false;
                    DoShot();
                    if (IsVorchunGrenadeLauncher(weapon)) vorchunReloadClickQueued = false;
                    if (closeGrenadeQueuedFire) { closeGrenadeQueuedFire = false; if (!FireHeld) shot = false; }
                }
            }
        }
        else if ((weapon.Type == WeaponType.FLAMER || weapon.Type == WeaponType.ACID_THROWER || weapon.Type == WeaponType.SNOW_GUN) && weapon.Stop()) DoStop();
        if (!shot && shotAlt && weapon.CanLaunch)   // :1445-1466
        {
            if (weapon.IsWeaponBlocked())
            {
                if (EffectManager.Instance) EffectManager.Instance.weaponBlockEffect(this, weapon.Type);
                if (weapon.LoadedAmmo <= 0 && weapon.Type == WeaponType.GATLING_GUN && weapon.Stop()) DoStop();
            }
            else if ((weapon.LoadedAmmo > 0 || weapon.Type == WeaponType.ONE_HANDED_COLD_ARMS || weapon.Type == WeaponType.TWO_HANDED_COLD_ARMS) && weapon.Type == WeaponType.GATLING_GUN)
            {
                if (weapon.Launch()) DoLaunch();
                else if (weapon.CanFire()) DoSpin();
            }
        }
        if (shotAlt && weapon.Type == WeaponType.BOMB_LAUNCHER) { DoBlow(); shotAlt = false; }
        if (!shot && (!shotAlt || !weapon.CanLaunch) && weapon.Type == WeaponType.GATLING_GUN && weapon.Stop()) DoStop();
        if (Input.GetKeyDown(TRInput.Zoom)) DoShot2x();
        if (lastCheckWeaponState + CheckWeaponStateInterval < DateTime.Now.Ticks)
        {
            lastCheckWeaponState = DateTime.Now.Ticks;
            CheckWeaponState();
        }
    }


    /// <summary>DoLaunch (:2331-2359): the gatling spin-up — a LAUNCH shot (no ammo), the crosshair starts turning, the model plays
    /// Start then Load, "M134_Launch"; the stream weapons play Start / Load and "Flamer_Launch" (their stream: stage 4).</summary>
    void DoLaunch()
    {
        if (weapon.Type == WeaponType.GATLING_GUN)
        {
            var s = new Shot(transform.position, Vector3.zero, (byte)weapon.Type);
            s.LaunchMode = ShotLaunchMode.LAUNCH;
            AnimateCrossHair(true);
            SendShot(s);
        }
        var t = weapon.Transform;
        if (t == null) return;
        if (weapon.Type == WeaponType.GATLING_GUN)
        {
            ShotWeaponAnimation("Load", t);
            if (EffectManager.Instance) EffectManager.Instance.gatlingGunEffect(ShotLaunchMode.LAUNCH, SoundManager.LocalPlayerAudio, true);
        }
        else if (weapon.Type == WeaponType.FLAMER || weapon.Type == WeaponType.ACID_THROWER || weapon.Type == WeaponType.SNOW_GUN)
        {
            ShotWeaponAnimation("Load", t);
            SoundManager.Play(SoundManager.LocalPlayerAudio, "Flamer_Launch", AudioPlayMode.Play);
        }
    }

    /// <summary>DoSpin (:2418-2436): the right button keeps the gatling spinning — Load clip and the "M134_Spin" loop.</summary>
    void DoSpin()
    {
        if (weapon.Type == WeaponType.GATLING_GUN) AnimateCrossHair(true);   // the LAUNCH shot built here is not sent in the original
        var t = weapon.Transform;
        if (t != null && weapon.Type == WeaponType.GATLING_GUN)
        {
            ShotWeaponAnimation("Spin", t);
            if (EffectManager.Instance) EffectManager.Instance.gatlingGunEffect(ShotLaunchMode.SPIN, SoundManager.LocalPlayerAudio, true);
        }
    }

    /// <summary>DoStop (:2438-2469): a BLOW shot (no ammo), the crosshair slows down, End clip, "M134_Stop" with PlayStop (which ends
    /// the shot loop); stream weapons play End.</summary>
    /// <summary>ResetWeapon (:3076-3105), on the local fighter's death (PlayerManager.KillMe :7388) and infection: the gatling and
    /// the stream weapons go quiet, the slot falls back to the first weapon (WeaponController.ResetWeapon), DoStop, the crosshair
    /// rotation stops.</summary>
    public void ResetWeapon()
    {
        var em = EffectManager.Instance;
        if (weapon != null)
        {
            if (weapon.Type == WeaponType.GATLING_GUN && em) em.gatlingGunEffect(ShotLaunchMode.BLOW, SoundManager.LocalPlayerAudio, true);
            if ((weapon.Type == WeaponType.FLAMER || weapon.Type == WeaponType.ACID_THROWER || weapon.Type == WeaponType.SNOW_GUN) && em) em.flamerEffect(ShotLaunchMode.BLOW, this, true);
        }
        else Debug.LogError("[ShotController.cs] Weapon in null!");
        if (weapons != null) weapon = weapons[0];
        if (weapon != null) DoStop();
        var ch = EnsureCrossHair();
        var an = ch ? ch.GetComponent<Animation>() : null;
        if (an != null && an["CrossHairRotationAnimation"] != null)
        {
            an["CrossHairRotationAnimation"].speed = 0f;
            an["CrossHairRotationAnimation"].time = 0f;
            ch.localEulerAngles = Vector3.zero;
        }
    }

    void DoStop()
    {
        if (weapon == null) return;
        if (weapon.Type == WeaponType.GATLING_GUN)
        {
            var s = new Shot(transform.position, Vector3.zero, (byte)weapon.Type);
            s.LaunchMode = ShotLaunchMode.BLOW;
            AnimateCrossHair(false);
            SendShot(s);
        }
        var t = weapon.Transform;
        if (t == null) return;
        if (weapon.Type == WeaponType.GATLING_GUN)
        {
            ShotWeaponAnimation("Stop", t);
            if (EffectManager.Instance) EffectManager.Instance.gatlingGunEffect(ShotLaunchMode.BLOW, SoundManager.LocalPlayerAudio, true);
        }
        else if (weapon.Type == WeaponType.FLAMER || weapon.Type == WeaponType.ACID_THROWER || weapon.Type == WeaponType.SNOW_GUN) ShotWeaponAnimation("Stop", t);
    }

    /// <summary>DoBlow (:2361-2387): the right button detonates every own sticky bomb.</summary>
    void DoBlow()
    {
        if (weapon.Type != WeaponType.BOMB_LAUNCHER) return;
        var list = new List<BombTracer>();
        foreach (var v in registeredItems.Values) if (v is BombTracer bt) list.Add(bt);
        foreach (var item in list) item.InitBlow();
    }

    /// <summary>CheckBlow (:2389-2416): with 3 sticky bombs out, the oldest one blows up before the next is thrown.</summary>
    void CheckBlow()
    {
        if (weapon.Type != WeaponType.BOMB_LAUNCHER) return;
        int n = 0; BombTracer oldest = null;
        foreach (var v in registeredItems.Values)
            if (v is BombTracer bt) { n++; if (oldest == null || oldest.TimeStamp > bt.TimeStamp) oldest = bt; }
        if (n >= 3 && oldest != null) oldest.InitBlow();
    }

    // ---------------- the fighter's items (CombatPlayer.RegisterItem / UnregisterItem, NE CombatPlayer.cs:1449-1468) ----------------
    readonly Dictionary<long, ItemTracer> registeredItems = new Dictionary<long, ItemTracer>();
    public Dictionary<long, ItemTracer> RegisteredItems => registeredItems;
    public void RegisterItem(ItemTracer a) { if (!registeredItems.ContainsKey(a.TimeStamp)) registeredItems.Add(a.TimeStamp, a); }
    public void UnregisterItem(long a)
    {
        if (!registeredItems.TryGetValue(a, out var it)) return;
        if (it != null) it.Destroy();
        registeredItems.Remove(a);
    }
    void DoShot2x() { }   // :2471-2473 (empty in the original)

    bool IsConfiguredWeaponSlotKeyDown(KeyCode a, KeyCode b, KeyCode c)   // :1147-1158
    {
        if (a == KeyCode.None) return false;
        if (Input.GetKeyDown(a)) return true;
        return a == b && Input.GetKeyDown(c);
    }

    bool IsWeaponKeyDown(int a)   // :1160-1182
    {
        switch (a)
        {
            case 1: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon1, KeyCode.Alpha1, KeyCode.Keypad1);
            case 2: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon2, KeyCode.Alpha2, KeyCode.Keypad2);
            case 3: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon3, KeyCode.Alpha3, KeyCode.Keypad3);
            case 4: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon4, KeyCode.Alpha4, KeyCode.Keypad4);
            case 5: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon5, KeyCode.Alpha5, KeyCode.Keypad5);
            case 6: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon6, KeyCode.Alpha6, KeyCode.Keypad6);
            case 7: return IsConfiguredWeaponSlotKeyDown(TRInput.Weapon7, KeyCode.Alpha7, KeyCode.Keypad7);
            default: return false;
        }
    }

    // ---------------- switching ----------------
    bool CanSelectWeapon(int a)   // :1105-1125 (New Era: every filled slot; Penta also needed ammo except slot 1 / sticky bombs)
    {
        if (weapons == null || a < 1 || a > weapons.Length) return false;
        return weapons[a - 1] != null;
    }

    /// <summary>SetWeapon (:288-325).</summary>
    public void SetWeapon(int a)
    {
        if (!CanSelectWeapon(a)) { if (Hud != null) Hud.ShowWeapon(); return; }
        var w = weapons[a - 1];
        if (weapon != null && weapon == w) { if (Hud != null) Hud.ShowWeapon(); return; }
        if (weapon != null && weapon.Type == WeaponType.GATLING_GUN && weapon.Stop()) DoStop();
        OnChangeWeapon(a);
        // NetworkManager.SendChange → PlayerManager.ChangeWeapon (the third-person body's weapon pose: stage 5)
        if (Hud != null) Hud.ShowWeapon();
    }

    /// <summary>OnChangeWeapon (:327-378).</summary>
    public WeaponType OnChangeWeapon(int a)
    {
        if (weapons == null || a < 1 || a > weapons.Length) return WeaponType.NONE;
        var w = weapons[a - 1];
        if (w == null) return WeaponType.NONE;
        if (weapon != null && weapon == w) return weapon.Type;
        pendingAutoReloadWeapon = null;
        if (weapon != null) weapon.CancelQueuedSequentialReloadFire();
        vorchunReloadClickQueued = false; closeGrenadeQueuedFire = false; shotgunReloadInterruptArmed = false;
        shot = FireHeld;
        previousWeapon = weapon == null ? a : weapon.GetIndex();
        weapon = w;
        weapon.Change();
        if (previousWeapon > 0)
        {
            if (weaponChangeRoutine != null) { StopCoroutine(weaponChangeRoutine); weaponChangeRoutine = null; }
            weaponChangeRoutine = StartCoroutine(Change(previousWeapon, a));
        }
        else { HideWeapons(); ShowCurrentWeaponTransform(); }
        if (weapon.LoadedAmmo == 0 && weapon.AmmoReserve > 0 && ShouldAutoReloadWhenEmpty(weapon)) Reload(weapon);
        SetCrossHair();
        if (Hud != null) Hud.ShowWeapon();
        return weapon.Type;
    }

    /// <summary>The Change coroutine (:2623-2671): Zoom off, WeaponChangeAnimation on the container, Reset on the new model,
    /// every model hidden, then the new one shown at once (New Era; Penta waited 0.2 s before hiding the old one).</summary>
    IEnumerator Change(int a, int b)
    {
        Zoom = false;
        var anim = weaponContainer ? weaponContainer.GetComponent<Animation>() : null;
        if (anim != null) ChangeWeaponAnimation(anim);
        else Debug.Log("Weapon Container Animation is NULL");
        var t = weapons[b - 1].Transform;
        if (t != null) ResetReloadWeaponAnimation(t);
        HideWeapons();
        t = weapons[b - 1].Transform;
        if (weapons[b - 1].Type == WeaponType.TWO_HANDED_COLD_ARMS) weapons[b - 1].SwitchWeaponTexture(0);
        bool zombieMode = GameFlow.Instance != null && GameFlow.Instance.Room != null && GameFlow.Instance.Room.Mode == GameMode.ZOMBIE;
        if (t != null)
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child.gameObject.name == weapons[b - 1].SystemName) { setWeaponVisible(child, true); continue; }
                setWeaponVisible(child, false);
                if (!zombieMode) Destroy(child.gameObject);
                else child.gameObject.SetActive(false);
            }
        weaponChangeRoutine = null;
        yield break;
    }

    void QuickChange()   // :2609-2621
    {
        if (weapon == null || weapons == null) return;
        int index = weapon.GetIndex();
        int num = previousWeapon;
        if (num > 0 && num <= weapons.Length && num != index && CanSelectWeapon(num)) { nextWeaponSwitchTime = 0f; SetWeapon(num); }
    }

    void SetNextWeapon()   // :1718-1737
    {
        if (weapons == null || weapon == null) return;
        int num = weapon.Index + 1;
        for (int i = 1; i <= weapons.Length; i++)
        {
            int n = num + i;
            while (n > weapons.Length) n -= weapons.Length;
            if (CanSelectWeapon(n)) { SetWeapon(n); break; }
        }
    }

    void SetPrevWeapon()   // :1739-1758
    {
        if (weapons == null || weapon == null) return;
        int num = weapon.Index + 1;
        for (int i = 1; i <= weapons.Length; i++)
        {
            int n = num - i;
            while (n < 1) n += weapons.Length;
            if (CanSelectWeapon(n)) { SetWeapon(n); break; }
        }
    }

    // ---------------- model visibility (NE WeaponController.cs) ----------------
    /// <summary>WeaponController.HideWeapons (:172-202).</summary>
    public void HideWeapons()
    {
        foreach (var wl in WeaponLooks())
            if (wl != null) foreach (var r in wl.transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var w in weapons)
            if (w != null && w.Transform != null) foreach (var r in w.Transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    /// <summary>WeaponController.HideWeapon (:204-215).</summary>
    public void HideWeapon()
    {
        if (weapon == null || weapon.Transform == null) return;
        foreach (var r in weapon.Transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    /// <summary>WeaponController.ShowWeapon (:217-224).</summary>
    public void ShowWeapon()
    {
        if (weapon == null || weapon.Transform == null) return;
        HideWeapons();
        ShowOnlyWeaponModel(weapon);
    }

    void ShowOnlyWeaponModel(CombatWeapon a)   // WeaponController :249-283
    {
        if (a == null || a.Transform == null) return;
        var t = a.Transform;
        if (t.childCount <= 0) { setWeaponVisible(t, true); return; }
        bool found = false;
        for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.name == a.SystemName) { found = true; break; }
        for (int j = 0; j < t.childCount; j++)
        {
            var child = t.GetChild(j);
            bool on = child.gameObject.name == a.SystemName;
            if (!found && !string.IsNullOrEmpty(a.SystemName)) on = child.gameObject.name.StartsWith(a.SystemName);
            setWeaponVisible(child, on);
        }
    }

    void ShowCurrentWeaponTransform()   // :1127-1145
    {
        if (weapon == null || weapon.Transform == null) return;
        bool any = false;
        for (int i = 0; i < weapon.Transform.childCount; i++)
        {
            var child = weapon.Transform.GetChild(i);
            bool on = child.gameObject.name == weapon.SystemName;
            setWeaponVisible(child, on);
            child.gameObject.SetActive(true);
            any |= on;
        }
        if (!any) ShowWeapon();
    }

    public void setWeaponVisible(Transform a, bool b)   // WeaponController :303-310
    {
        foreach (var r in a.GetComponentsInChildren<Renderer>(true)) r.enabled = b;
    }

    // ---------------- ActorAnimator (first-person part) ----------------
    /// <summary>ActorAnimator.ResetReloadWeaponAnimation (NE ActorAnimator.cs:1471-1500): "Reset" on the model's Animation.</summary>
    public static void ResetReloadWeaponAnimation(Transform a)
    {
        if (a == null) return;
        var anim = a.GetComponentInChildren<Animation>();
        if (anim != null) { anim.Play("Reset"); return; }
        anim = a.GetComponent<Animation>();
        if (anim != null) { anim.Play("Reset"); return; }
        for (int i = 0; i < a.childCount; i++)
        {
            anim = a.GetChild(i).GetComponent<Animation>();
            if (anim != null) anim.Play("Reset");
        }
    }

    /// <summary>ActorAnimator.ChangeWeaponAnimation(Animation) (NE ActorAnimator.cs:1587-1601).</summary>
    public static void ChangeWeaponAnimation(Animation a)
    {
        a.Play("WeaponChangeAnimation", PlayMode.StopSameLayer);
        var fb = GameFlow.Instance && GameFlow.Instance.Player ? GameFlow.Instance.Player.GetComponent<FighterBody>() : null;
        bool running = fb != null && fb.IsRunning;   // ActorAnimator.isRunning
        a.PlayQueued(running ? "WeaponAnimation_ShotgunRun" : "WeaponAnimation_ShotgunIdle", QueueMode.CompleteOthers);
    }

    /// <summary>ActorAnimator.RunWeaponAnimation / IdleWeaponAnimation (NE ActorAnimator.cs:1087-1109).</summary>
    public static void RunOrIdleWeaponAnimation(Animation a, bool running)
    {
        string clip = running ? "WeaponAnimation_ShotgunRun" : "WeaponAnimation_ShotgunIdle";
        if (a.IsPlaying("WeaponChangeAnimation")) a.PlayQueued(clip, QueueMode.CompleteOthers);
        else a.CrossFade(clip, 0.2f);
    }

    /// <summary>GetSpeedMultiplier (:3108-3125): max(1, weapon speed) × 1.25 for one-handed melee or the "R" attribute
    /// (3rd "_" token of the system name).</summary>
    public float GetSpeedMultiplier()
    {
        float result = 1f;
        if (weapon == null) return result;
        result = Mathf.Max(1f, weapon.SpeedValue);
        if (weapon.Type == WeaponType.ONE_HANDED_COLD_ARMS) result = Mathf.Max(1f, weapon.SpeedValue) * 1.25f;
        if (weapon.Attributes.Contains("R")) result = Mathf.Max(1f, weapon.SpeedValue) * 1.25f;
        return result;
    }

    // ---------------- firing (DoShot :1892-2188) ----------------
    public CombatWeapon GetWeaponByType(int a)   // WeaponController.GetWeaponByType (NE WeaponController.cs:312-327)
    {
        foreach (var w in weapons) if (w != null && w.GetType() == a) return w;
        if (a == 203) return new CombatWeapon(1, 996, WeaponType.KAMIKADZE, "OHCA_Kamikadze");
        return null;
    }

    Transform GetWeaponModelTransform(CombatWeapon a)   // :2205-2245
    {
        if (a == null || a.Transform == null) return null;
        var t = a.Transform;
        if (!string.IsNullOrEmpty(a.SystemName))
        {
            var m = t.Find(a.SystemName);
            if (m != null) return m;
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c != null && c.name == a.SystemName) return c; }
        }
        for (int j = 0; j < t.childCount; j++) { var c = t.GetChild(j); if (c != null && c.gameObject.activeInHierarchy) return c; }
        if (t.childCount > 0) return t.GetChild(0);
        return t;
    }

    Transform GetWeaponTargetTransform(CombatWeapon a)   // :2190-2203
    {
        var model = GetWeaponModelTransform(a);
        if (model == null) return null;
        var t = model.Find("Target");
        return t != null ? t : model;
    }

    void ShotAnimationOnNode(string nodeName)
    {
        foreach (var wl in WeaponLooks()) if (wl.gameObject.name == nodeName) ShotWeaponAnimation("Shot", wl.transform);
    }

    /// <summary>DoShot: spread = Deviation / 500 of the viewport (0 for a zoomed sniper rifle), a square ±r/2 (the shotgun's disc is
    /// stage 3), the ray through the viewport point, then per weapon type the shot, the target filter, the local effect, the model's
    /// "Shot" clip and the camera shake. The shot is dispatched offline like NetworkManager.SendShot → PlayerManager.PlayerShot:
    /// for pistols / rifles / gatling / sniper rifles ShotEffect(ME_AFTER_SERVER) answers true → OnShot (ammo).</summary>
    void DoShot()
    {
        var random = new System.Random();
        var weaponTarget = GetWeaponTargetTransform(weapon);
        if (weaponTarget == null) weaponTarget = transform;
        const float num = 500f;
        float r = Mathf.Max(0f, weapon.Deviation) / num;
        if (weapon.Type == WeaponType.SNIPER_RIFLE && Zoom) r = 0f;
        float rx, ry;
        if (weapon.Type == WeaponType.SHOT_GUN)
        {
            float disc = Mathf.Min(r / 2f, 0.014f);
            float f = Convert.ToSingle(random.NextDouble() * Math.PI * 2.0);
            float d = Mathf.Sqrt(Convert.ToSingle(random.NextDouble())) * disc;
            rx = Mathf.Cos(f) * d; ry = Mathf.Sin(f) * d;
        }
        else
        {
            rx = Convert.ToSingle(random.NextDouble() * r) - r / 2f;
            ry = Convert.ToSingle(random.NextDouble() * r) - r / 2f;
        }
        var cam = fpsCamera;
        Ray a = cam.ViewportPointToRay(new Vector3(0.5f + rx, 0.5f + ry, 0f));
        var em = EffectManager.Instance;
        switch ((byte)weapon.GetType())
        {
            case 3:
            {
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.GunShot(this, s);
                SendShot(s);
                if (em) em.machineGunEffect(s, this, true);
                ShotAnimationOnNode("HandGunWeapon");
                if (weapon.Attributes.Contains("S") || weapon.IsShaking)
                {
                    var fc = fpsCamera.GetComponent<FPSCamera>();
                    int k = this.random.Next(-1, 2);
                    if (k == 0) fc.Shake(new Vector3(-1f, 0f, 0f)); else fc.Shake(new Vector3(0f, k, 0f));
                }
                AnimateCrossHair();
                break;
            }
            case 4:
            {
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.GunShot(this, s);
                SendShot(s);
                if (em) em.machineGunEffect(s, this, true);
                ShotAnimationOnNode("MachineGunWeapon");
                AnimateCrossHair();
                break;
            }
            case 6:
            {
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.GunShot(this, s);
                SendShot(s);
                if (em) em.machineGunEffect(s, this, true);
                if (weapon.Type == WeaponType.GATLING_GUN && weapon.Transform != null) ShotWeaponAnimation("Shot", weapon.Transform);
                AnimateCrossHair();
                break;
            }
            case 10:
            {
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.GunShot(this, s);
                SendShot(s);
                if (em) em.machineGunEffect(s, this, true);
                ShotAnimationOnNode("SniperRifleWeapon");
                AnimateCrossHair();
                var fc = fpsCamera.GetComponent<FPSCamera>();
                if (fc != null) fc.Shake(new Vector3(-1f, -0.35f, 0f), true);
                break;
            }
            case 1:
            case 2:
            {
                // melee (:1957-1970): the swing is a LAUNCH shot ("Bat_Launch"), the hit comes 0.2 s later (DelayedShot)
                var s = new Shot(a.origin + a.direction * weapon.Distance, a.direction, (byte)weapon.GetType());
                s.Launch(a.origin + a.direction * weapon.Distance, a.direction, s.TimeStamp, s.TimeStamp + 200);
                SendShot(s);
                StartCoroutine(DelayedShot(weaponTarget, weapon));
                if (s.LaunchMode == ShotLaunchMode.LAUNCH) ShotWeaponAnimation("Shot", weapon.Transform);
                AnimateCrossHair();
                break;
            }
            case 7:
            {
                // shotgun (:2073-2106): the shot, the cone, 6 visual pellets, the "Shot" clip, the jump impulse in the air, shake (-2, 0, 0)
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.SegmentShot(this, s);
                SendShot(s);
                if (em) em.shotGunEffect(s, this, true);
                ShotAnimationOnNode("ShotGunWeapon");
                AnimateCrossHair();
                var motor = GetComponentInChildren<CharacterMotor>();
                if (motor != null && !motor.grounded && !weapon.IsWeaponBlockedChange() && CanApplyShotgunJumpImpulse())
                {
                    float jump = LocalJump * 0.9310345f;   // ShotgunJumpPowerScale
                    if (weapon.SystemName.Contains("SG_Novapump")) jump *= 0.7f;
                    motor.SetExplosionForce(s.Origin, jump);
                }
                var fcs = fpsCamera.GetComponent<FPSCamera>();
                if (fcs != null) fcs.Shake(new Vector3(-2f, 0f, 0f));
                break;
            }
            case 8:
            {
                // rocket launcher (:2110-2120): RocketShot, the rocket (RocketTracer), "RPG26_Shot"
                var s = ShotCalculator.RocketShot(a, weaponTarget, weapon.GetType(), weapon.Velocity);
                if (s == null) break;
                SendShot(s);
                if (em) em.rocketLauncherEffect(s, this, true);
                AnimateCrossHair();
                break;
            }
            case 9:
            {
                // grenade launcher (:2122-2138): the lob ray; «Ворчун» (104) / Milkor_A (45) take the Vorchun trajectory, others GrenadeShot.
                // Owner's request 12.11.2026: «Гранатин» (44, GL_Milkor) flies exactly like «Гадюка», and «Павлик М.» (46) too — New Era sold no
                // grenade launcher on the plain GrenadeShot path, and with the rebuild data it rose twice as high as «Ворчун» (test `logic arc`).
                a = GetFovIndependentLobRay(rx, ry);
                bool vorchun = weapon != null && (weapon.WeaponID == 104 || string.Equals(weapon.SystemName, "GL_GrenadeLauncher03", StringComparison.OrdinalIgnoreCase));
                bool milkor = weapon != null && (weapon.WeaponID == 45 || weapon.WeaponID == 44 || weapon.WeaponID == 46 || string.Equals(weapon.SystemName, "GL_Milkor_A", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(weapon.SystemName, "GL_Milkor", StringComparison.OrdinalIgnoreCase) || string.Equals(weapon.SystemName, "GL_SnowLauncher", StringComparison.OrdinalIgnoreCase));
                var s = !vorchun && (weapon == null || !milkor)
                    ? ShotCalculator.GrenadeShot(a, weaponTarget, weapon.GetType(), weapon.Velocity, weapon.Life, false)
                    : ShotCalculator.VorchunGrenadeShot(a, weaponTarget, weapon.GetType(), weapon.Velocity, weapon.Life, false, false);
                if (s == null) break;
                if (vorchun) PredictVorchunShotAmmo();
                SendShot(s);
                if (em) em.grenadeLauncherEffect(s, this, true);
                AnimateCrossHair();
                break;
            }
            case 15:
            {
                // sticky-bomb launcher (:2170-2182): CheckBlow, the lob ray, GrenadeShot(stick) → BombTracer
                CheckBlow();
                a = GetFovIndependentLobRay(rx, ry);
                var s = ShotCalculator.GrenadeShot(a, weaponTarget, weapon.GetType(), weapon.Velocity, weapon.Life, true);
                if (s == null) break;
                SendShot(s);
                if (em) em.bombLauncherEffect(s, weapon, this, true, false);
                AnimateCrossHair();
                break;
            }
            case 5:
            case 11:
            case 12:
            {
                // stream weapons (:2035-2048): flamer / acid thrower / snow gun - every interval a straight shot, the segment
                // through the stream (fighters only), the Flamer stream on for 0.3 s. PlayerManager.WaterBlock: no water offline.
                var s = ShotCalculator.StraightShot(a, weaponTarget, weapon.GetType(), 0, false);
                if (s == null) break;
                ShotCalculator.SegmentShot(this, s);
                SendShot(s);
                if (em) em.flamerEffect(s, this, true);
                AnimateCrossHair();
                break;
            }
            // 13 / 14 shockers: nothing in the original
        }
        // RegisterWeaponShotStat (accuracy in the battle results): with the round rewards, later
    }

    /// <summary>NetworkManager.SendShot offline (NE NetworkManager.cs:221-231) → PlayerManager.PlayerShot(local, shot) (NE PlayerManager.cs:
    /// 9377-9536): the targets first (offline only the shooter himself — own explosions), then ShotEffect(ME_AFTER_SERVER) → OnShot.</summary>
    public void SendShot(Shot s)
    {
        LocalShotSent?.Invoke(s.WeaponType);
        if (s.HasTargets)
            foreach (var target in s.Targets)
            {
                if (target.TargetType != ShotTargetType.PLAYER || target.TargetID != GameFlow.LocalId) continue;
                var vitals = GetComponent<PlayerVitals>();
                if (vitals == null) continue;
                // the local player hit by his own shot (:9440-9478): armor first, KillMe at 0; GameHUD.ShotMe + "Bat_Shot_Enemy"
                if (!vitals.Dead) vitals.ApplyDamageArmorFirst(target.HealthDamage, target.EnergyDamage, s.WeaponType.ToString());
                if (s.WeaponType != WeaponType.TURRET_TESLA && s.WeaponType != WeaponType.TURRET_MACHINE_GUN)
                    SoundManager.Play(SoundManager.LocalPlayerAudio, "Bat_Shot_Enemy", AudioPlayMode.Play);   // GameHUD.ShotMe: PlayerVitals.Damaged → BattleUI.ShotMe
                if (target.HealthDamage + target.EnergyDamage > 0 && EffectManager.Instance)
                    EffectManager.Instance.HitEffect(s, transform, SoundManager.LocalPlayerAudio);
                // owner's request (New Era runs periodic impacts only on opponents offline): the weapon's effect hits the shooter too
                var li = GetComponent<LocalImpacts>();
                if (li != null && target.HealthDamage + target.EnergyDamage > 0) li.Apply(GetWeaponByType((int)s.WeaponType), s.WeaponType);
                target.TargetTransform = transform;
                // ShotDamage (the damage number) / GameHUD.IndicateDamage: with the damage HUD, later
            }
        if (ShotEffect(s)) OnShot(s.WeaponType);
    }

    /// <summary>PlayerManager.ShotEffect(shot, local, ME_AFTER_SERVER) (NE PlayerManager.cs:9724-9947): melee → batEffect; hitscan shots →
    /// true (their effect was played in DoShot); gatling LAUNCH / BLOW → false; launchers: LAUNCH → the type node's "Shot" clip and
    /// true, BLOW → launcherEffect (explosion, knockback) and false.</summary>
    bool ShotEffect(Shot a)
    {
        var em = EffectManager.Instance;
        switch (a.WeaponType)
        {
            case WeaponType.ONE_HANDED_COLD_ARMS:
            case WeaponType.TWO_HANDED_COLD_ARMS:
                if (em) em.batEffect(a, this, true);
                return false;
            case WeaponType.GATLING_GUN:
                return a.LaunchMode != ShotLaunchMode.LAUNCH && a.LaunchMode != ShotLaunchMode.BLOW;
            case WeaponType.SHOT_GUN:
                return true;
            case WeaponType.HAND_GUN: case WeaponType.MACHINE_GUN: case WeaponType.SNIPER_RIFLE:
            case WeaponType.FLAMER: case WeaponType.SNOW_GUN: case WeaponType.ACID_THROWER:
                return a.LaunchMode != ShotLaunchMode.BLOW;
            case WeaponType.ROCKET_LAUNCHER:
                if (a.LaunchMode == ShotLaunchMode.LAUNCH) { ShotAnimationOnNode("RocketLauncherWeapon"); return true; }
                if (em) em.launcherEffect(a, this, this, true);
                return false;
            case WeaponType.GRENADE_LAUNCHER:
                if (a.LaunchMode == ShotLaunchMode.LAUNCH) { ShotAnimationOnNode("GrenadeLauncherWeapon"); ShotAnimationOnNode("SnowLauncherWeapon"); return true; }
                if (em) em.launcherEffect(a, this, this, true);
                return false;
            case WeaponType.BOMB_LAUNCHER:
                if (a.LaunchMode == ShotLaunchMode.LAUNCH) { ShotAnimationOnNode("BombLauncherWeapon"); return true; }
                if (em) em.launcherEffect(a, this, this, true);
                return false;
        }
        return true;
    }

    /// <summary>DelayedShot (:2247-2289): 0.2 s after the swing, the straight shot through the screen centre up to the weapon distance
    /// (the cone fallback for a miss needs other fighters), the cone, then the SHOT (spark + "Bat_Shot_Stone" on a wall).</summary>
    IEnumerator DelayedShot(Transform a, CombatWeapon b)
    {
        yield return new WaitForSeconds(0.2f);
        if (!HudActive || b == null || fpsCamera == null) yield break;
        Ray ray = fpsCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 20f));
        AnimateCrossHair();
        var type = (WeaponType)b.GetType();
        if (type != WeaponType.ONE_HANDED_COLD_ARMS && type != WeaponType.TWO_HANDED_COLD_ARMS) yield break;
        var s = ShotCalculator.StraightShot(ray, fpsCamera.transform, b.GetType(), (int)b.Distance, false);
        if (s != null)
        {
            s.Targets.Clear();
            ShotCalculator.SegmentShot(this, s);
            SendShot(s);
        }
    }

    /// <summary>GetFovIndependentLobRay (:1881-1890): the lob ray 0.15 above the crosshair, scaled to the field of view (55° reference).</summary>
    Ray GetFovIndependentLobRay(float a, float b)
    {
        var main = fpsCamera;
        float num = Mathf.Tan(0.47996554f);
        float num2 = Mathf.Tan(main.fieldOfView * ((float)Math.PI / 180f) * 0.5f);
        float k = num / num2;
        return main.ViewportPointToRay(new Vector3(0.5f + a * k, 0.5f + (0.14999998f + b) * k, 20f));
    }

    float nextShotgunJumpImpulseTime = -1000f;
    bool CanApplyShotgunJumpImpulse()   // :1827-1835 (0.45 s cooldown)
    {
        if (Time.time < nextShotgunJumpImpulseTime) return false;
        nextShotgunJumpImpulseTime = Time.time + 0.45f;
        return true;
    }

    /// <summary>CombatPlayer.Jump of the local fighter: the offline actor info (NE PlayerManager.cs:3377-3379) is always 28.</summary>
    const int LocalJump = 28;

    /// <summary>PredictVorchunShotAmmo (:2880-2908): «Ворчун» takes its round at once; the OnShot of that shot is then skipped.</summary>
    void PredictVorchunShotAmmo()
    {
        var w = weapon;
        if (!IsVorchunGrenadeLauncher(w) || w.LoadedAmmo <= 0) return;
        w.ShootAmmo();
        vorchunPredictedShotAcks++;
        if (Hud != null) Hud.ShowWeapon();
        if (w.LoadedAmmo != 0 || w != weapon) return;
        if (w.AmmoReserve > 0) { if (ShouldAutoReloadWhenEmpty(w)) QueueAutoReloadAfterFinalShot(w); }
        else SetNextWeapon();
    }

    /// <summary>OnShot (:2920-2969): one round out of the clip; an empty clip reloads by itself after the shot (reserve left), else the
    /// next weapon is taken (not for sticky bombs).</summary>
    public void OnShot(WeaponType a)
    {
        if (a == WeaponType.ONE_HANDED_COLD_ARMS || a == WeaponType.TWO_HANDED_COLD_ARMS) return;
        var w = weapon;
        bool predicted = false;
        if (a == WeaponType.GRENADE_LAUNCHER && vorchunPredictedShotAcks > 0)
        {
            var v = FindVorchunWeapon();
            if (v != null) { w = v; vorchunPredictedShotAcks--; predicted = true; }
        }
        if (w == null || w.Type != a) w = GetWeaponByType((int)a);
        if (w == null) return;
        if (!predicted) w.ShootAmmo();
        if (w == weapon && Hud != null) Hud.ShowWeapon();
        if (w.LoadedAmmo != 0 || w != weapon) return;
        // Owner's request (deviation from both clients): the gatling's shot loop kept playing through the reload (Penta / New Era call
        // DoStop only while the barrel is Shooting / Launching, and the reload sets Reloading first) — the empty clip stops it like a release.
        if (w.Type == WeaponType.GATLING_GUN && w.Stop()) DoStop();
        if (w.AmmoReserve > 0) { if (ShouldAutoReloadWhenEmpty(w)) QueueAutoReloadAfterFinalShot(w); }
        else if (a != WeaponType.BOMB_LAUNCHER) SetNextWeapon();
    }

    void QueueAutoReloadAfterFinalShot(CombatWeapon a)   // :2971-2978
    {
        if (a != null && pendingAutoReloadWeapon != a) { pendingAutoReloadWeapon = a; StartCoroutine(AutoReloadAfterFinalShot(a)); }
    }

    IEnumerator AutoReloadAfterFinalShot(CombatWeapon a)   // :2980-2997
    {
        yield return new WaitForEndOfFrame();
        while (a != null && a == weapon && a.LoadedAmmo == 0 && a.AmmoReserve > 0)
        {
            a.CalcMode();
            if (a.CanReload()) { Reload(a); break; }
            yield return null;
        }
        if (pendingAutoReloadWeapon == a) pendingAutoReloadWeapon = null;
    }

    static bool ShouldAutoReloadWhenEmpty(WeaponType a) => a != WeaponType.ONE_HANDED_COLD_ARMS && a != WeaponType.TWO_HANDED_COLD_ARMS;   // :2910-2918
    static bool ShouldAutoReloadWhenEmpty(CombatWeapon a) => a != null && ShouldAutoReloadWhenEmpty(a.Type);

    static bool IsCloseBeforeShotGrenadeLauncher(CombatWeapon a) => a != null && a.IsCloseBeforeShotGrenadeLauncher();   // :2839-2851
    static bool IsVorchunGrenadeLauncher(CombatWeapon a) => a != null && a.Type == WeaponType.GRENADE_LAUNCHER && (a.WeaponID == 104 || string.Equals(a.SystemName, "GL_GrenadeLauncher03", StringComparison.OrdinalIgnoreCase));   // :2853-2860

    CombatWeapon FindVorchunWeapon()   // :2862-2876
    {
        foreach (var w in weapons) if (IsVorchunGrenadeLauncher(w)) return w;
        return null;
    }

    // ---------------- reload (:2475-2607) ----------------
    /// <summary>Reload: not for melee, only when CanReload; the model plays Reload (or ReloadStart, ReloadAmmo × n, ReloadEnd for the
    /// complex reload) at the reload speed multiplier, an empty clip or a sequential reload blocks firing for the clip length, then the
    /// reload sound (EffectManager.reloadEffect).</summary>
    void Reload(CombatWeapon a)
    {
        if (a == null || a.Type == WeaponType.ONE_HANDED_COLD_ARMS || a.Type == WeaponType.TWO_HANDED_COLD_ARMS || !a.CanReload()) return;
        bool empty = a.LoadedAmmo <= 0;
        ConfigureSequentialReloadTiming(a);
        int n = a.Reload();
        // NetworkManager.SendReload: offline nothing is sent (NE NetworkManager.cs:343-361)
        float len = ReloadWeaponAnimation(a.Transform, a.ComplexReload, n, GetReloadAnimationSpeedMultiplier(a));
        if ((empty || a.SequentialReload) && len > 0f) { reloadAnimationWeapon = a; reloadAnimationBlockUntil = Time.time + len; }
        if (EffectManager.Instance) EffectManager.Instance.reloadEffect(a.Type, SoundManager.LocalPlayerAudio, a.SystemName, n);
        if (Hud != null) Hud.ShowWeapon();
    }

    static Animation ModelAnimation(Transform t)
    {
        if (t == null) return null;
        var anim = t.GetComponentInChildren<Animation>();
        return anim != null ? anim : t.GetComponent<Animation>();
    }

    void ConfigureSequentialReloadTiming(CombatWeapon a)   // :2506-2527
    {
        if (a == null || !a.SequentialReload || a.Transform == null) return;
        var anim = ModelAnimation(a.Transform);
        if (anim == null) return;
        var start = anim["ReloadStart"]; var ammo = anim["ReloadAmmo"];
        if (start == null || ammo == null) return;
        float k = GetReloadAnimationSpeedMultiplier(a);
        a.ConfigureSequentialReloadTiming((start.length + ammo.length) / k, ammo.length / k);
    }

    float GetReloadAnimationSpeedMultiplier(CombatWeapon a)   // :2529-2607
    {
        if (a != null && a.HasExactReloadOverride && !a.ComplexReload)
        {
            var anim = ModelAnimation(a.Transform);
            if (anim != null)
            {
                var st = anim["Reload"];
                float sec = a.ReloadTime / 10000000f;
                if (st != null && st.length > 0.01f && sec > 0.01f) return Mathf.Clamp(st.length / sec, 0.1f, 5f);
            }
        }
        if (a != null && a.Type == WeaponType.ROCKET_LAUNCHER && (a.WeaponID == 59 || string.Equals(a.SystemName, "RL_RPG7B02", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(a.SystemName) && a.SystemName.IndexOf("RPG7B02", StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            var anim = ModelAnimation(a.Transform);
            if (anim != null)
            {
                var st = anim["Reload"];
                float sec = a.ReloadTime / 10000000f;
                if (st != null && sec > 0.01f && st.length > 0.01f) return Mathf.Clamp(st.length / sec, 0.1f, 5f);
            }
            return 1f;
        }
        if (a != null && a.Type == WeaponType.SHOT_GUN)
        {
            if (a.WeaponID == 109 || string.Equals(a.SystemName, "SG_Remington", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(a.SystemName) && a.SystemName.IndexOf("Remington", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var anim = ModelAnimation(a.Transform);
                if (anim != null)
                {
                    var s1 = anim["ReloadStart"]; var s2 = anim["ReloadAmmo"]; var s3 = anim["ReloadEnd"];
                    if (s1 != null && s2 != null && s3 != null)
                    {
                        int n = Math.Max(1, a.MaxLoadedAmmo);
                        float total = s1.length + s2.length * n + s3.length;
                        if (total > 0.01f) return Mathf.Clamp(total / 2.41f, 0.1f, 5f);
                    }
                }
                return 0.92f;
            }
            return 0.92f;
        }
        return 1f;
    }

    bool TryInterruptSequentialReloadForFire()   // :2753-2822
    {
        if (weapon == null) return false;
        bool vorchun = IsVorchunGrenadeLauncher(weapon);
        bool closeGl = IsCloseBeforeShotGrenadeLauncher(weapon);
        bool vorchunBlocked = vorchun && reloadAnimationWeapon == weapon && Time.time < reloadAnimationBlockUntil;
        if (!weapon.InterruptSequentialReloadForFire() && (!vorchunBlocked || weapon.LoadedAmmo <= 0)) return false;
        if (closeGl && !vorchun)
        {
            closeGrenadeQueuedFire = true;
            shot = true;
            var anim = ModelAnimation(weapon.Transform);
            var end = anim != null ? anim["ReloadEnd"] : null;
            if (end != null)
            {
                float k = Mathf.Clamp(GetReloadAnimationSpeedMultiplier(weapon), 0.1f, 5f);
                anim.Stop();
                end.speed = k; end.time = 0f;
                if (!PlayEmptyReloadEndSubstitute(anim, k, false)) anim.Play("ReloadEnd", PlayMode.StopAll);
                reloadAnimationWeapon = weapon;
                reloadAnimationBlockUntil = Time.time + end.length / k;
                if (Hud != null) Hud.ShowWeapon();
                return true;
            }
        }
        var anim2 = ModelAnimation(weapon.Transform);
        if (anim2 != null) anim2.Stop();
        if (reloadAnimationWeapon == weapon) { reloadAnimationWeapon = null; reloadAnimationBlockUntil = 0f; }
        if (Hud != null) Hud.ShowWeapon();
        return true;
    }

    /// <summary>Rebuild fix (playtest 12.11.2026): the Milkor model («Гадюка», «Гранатин») has an EMPTY ReloadEnd clip in every export
    /// (New Era bundles and cache, the Penta-era web cache: 1 s, no curves), so the launcher stayed in the open loading pose after a
    /// per-round reload. When ReloadEnd has no curves, the opening (ReloadStart) is played backwards instead; the timing stays
    /// ReloadEnd's (the reload / fire block lengths are unchanged).</summary>
    static bool PlayEmptyReloadEndSubstitute(Animation anim, float k, bool queued)
    {
        var end = anim["ReloadEnd"]; var start = anim["ReloadStart"];
        if (end == null || end.clip == null || !end.clip.empty || start == null) return false;
        AnimationState st;
        if (queued) st = anim.PlayQueued("ReloadStart", QueueMode.CompleteOthers);
        else { anim.Play("ReloadStart", PlayMode.StopAll); st = start; }
        if (st == null) return false;
        st.speed = -k;
        st.time = st.length;
        return true;
    }

    bool IsReloadAnimationBlocked()   // :2824-2837
    {
        if (reloadAnimationWeapon == null) return false;
        if (weapon != reloadAnimationWeapon || Time.time >= reloadAnimationBlockUntil) { reloadAnimationWeapon = null; reloadAnimationBlockUntil = 0f; return false; }
        return true;
    }

    // ---------------- zoom (:3026-3046) ----------------
    public bool zoomAllowed() => weapon != null && (weapon.Type == WeaponType.SNIPER_RIFLE || weapon.Attributes.Contains("O"));

    public float GetSniperZoomFactor()
    {
        if (weapon.Type == WeaponType.SNIPER_RIFLE) return weapon.Attributes.Contains("D") ? 0.15f : 0.2f;
        if (weapon.Attributes.Contains("O")) return 0.5f;
        return 1f;
    }

    // ---------------- first-person clips (NE ActorAnimator.cs) ----------------
    /// <summary>ActorAnimator.ShotWeaponAnimation(string, Transform) (NE :1502-1565): "Stop" → End, "Load" → Start then Load, "Spin" → Load,
    /// otherwise the clip at speed 1.5 from 0 (StopSameLayer), then Idle queued (loop) if the model has one; the Taurus queues Reset.</summary>
    public static void ShotWeaponAnimation(string a, Transform b)
    {
        if (b == null) return;
        var anim = b.GetComponentInChildren<Animation>();
        if (anim == null) return;
        if (a == "Stop") { if (anim["End"] != null) { anim["End"].speed = 1.5f; anim.Play("End", PlayMode.StopSameLayer); } return; }
        if (a == "Load")
        {
            if (anim["Start"] != null) { anim["Start"].speed = 1.5f; anim.Play("Start", PlayMode.StopSameLayer); }
            if (anim["Load"] != null) { anim["Load"].speed = 1.5f; anim.PlayQueued("Load", QueueMode.CompleteOthers); }
            return;
        }
        if (a == "Spin") { if (anim["Load"] != null) { anim["Load"].speed = 1.5f; anim.Play("Load", PlayMode.StopSameLayer); } return; }
        if (anim[a] == null) return;
        anim[a].speed = 1.5f;
        anim[a].time = 0f;
        anim.Play(a, PlayMode.StopSameLayer);
        bool taurus = a == "Shot" && IsTaurusWeaponTransform(b);
        if (taurus) QueueTaurusVisualReset(anim);
        if (anim["Idle"] != null)
        {
            anim["Idle"].wrapMode = WrapMode.Loop;
            anim.PlayQueued("Idle", QueueMode.CompleteOthers);   // Play("Idle", AnimationPlayMode.Queue)
        }
    }

    /// <summary>ActorAnimator.ReloadWeaponAnimation(Transform, bool, int, float) (NE :1407-1469); returns the clip time in seconds.</summary>
    public static float ReloadWeaponAnimation(Transform a, bool complex, int c, float d)
    {
        if (a == null) return 0f;
        var anim = ModelAnimation(a);
        if (anim == null) { Debug.LogWarning("[ActorAnimator] Reload animation component not found for " + a.name); return 0f; }
        float k = Mathf.Clamp(d, 0.1f, 5f);
        if (complex)
        {
            var s1 = anim["ReloadStart"]; var s2 = anim["ReloadAmmo"]; var s3 = anim["ReloadEnd"];
            if (s1 == null || s2 == null || s3 == null) { Debug.LogWarning("[ActorAnimator] Complex reload clips are incomplete for " + a.name); return 0f; }
            int n = Mathf.Max(0, c);
            s1.speed = k; s2.speed = k; s3.speed = k;
            anim.Stop();
            s1.time = 0f; s2.time = 0f; s3.time = 0f;
            anim.Play("ReloadStart", PlayMode.StopAll);
            for (int i = 0; i < n; i++) anim.PlayQueued("ReloadAmmo", QueueMode.CompleteOthers);
            if (!PlayEmptyReloadEndSubstitute(anim, k, true)) anim.PlayQueued("ReloadEnd", QueueMode.CompleteOthers);
            return (s1.length + s2.length * n + s3.length) / k;
        }
        var st = anim["Reload"];
        if (st == null) return 0f;
        st.speed = k;
        anim.Stop();
        st.time = 0f;
        anim.Play("Reload", PlayMode.StopAll);
        if (IsTaurusWeaponTransform(a))
        {
            QueueTaurusVisualReset(anim);
            if (anim["Idle"] != null) { anim["Idle"].wrapMode = WrapMode.Loop; anim.PlayQueued("Idle", QueueMode.CompleteOthers); }
        }
        return st.length / k;
    }

    static bool IsTaurusWeaponTransform(Transform a)   // NE ActorAnimator.cs:1367-1390
    {
        if (a == null) return false;
        foreach (var t in a.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !string.Equals(t.name, "HG_Taurus", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled && r.gameObject.activeInHierarchy) return true;
        }
        return false;
    }

    static void QueueTaurusVisualReset(Animation a)   // NE ActorAnimator.cs:1392-1400
    {
        if (a == null || a["Reset"] == null) return;
        a["Reset"].speed = 1f;
        a["Reset"].wrapMode = WrapMode.Once;
        a.PlayQueued("Reset", QueueMode.CompleteOthers);
    }

    // ---------------- crosshair (:398-839, 1760-1782) ----------------
    // The 3D crosshair of the map scene: GameHUD.CrossHairs = GUICamera/Crosshairs, its child CrossHair (quad, material, Animation with
    // CrossHairAnimation / CrossHairRotationAnimation). New Era's configurable procedural crosshair (option styles, CrosshairAlphaTint)
    // is an addition of the rebuild with no option in our settings: the native texture path is used, as with the option off.
    enum CrossHairMode { Ready, Blocked, ReadyToFire }
    CrossHairMode currentCrossHairMode;
    Transform crossHair;
    bool rotateCursor;

    static Transform GetCrossHairRoot()   // :803-819
    {
        if (BattleUI.CrossHairs != null) return BattleUI.CrossHairs;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t != null && t.name == "CrossHairs") return t;
        return null;
    }

    Transform EnsureCrossHair()   // :821-839
    {
        if (crossHair != null) return crossHair;
        var root = GetCrossHairRoot();
        if (root == null) return null;
        var t = root.Find("CrossHair");
        if (t == null && root.childCount > 0) t = root.GetChild(0);
        crossHair = t;
        return crossHair;
    }

    /// <summary>SetCrossHair (:733-794): texture crosshair_0&lt;type&gt; (else crosshair_00), root scale 0.65 (1 for the gatling), the
    /// rotation clip stopped at 0, then the current colour mode.</summary>
    void SetCrossHair()
    {

        var t = EnsureCrossHair();
        if (t == null || weapon == null) return;
        var r = t.GetComponent<Renderer>();
        if (r == null || r.material == null) return;
        r.enabled = true;
        var tex = (Texture)Resources.Load("GUI/Battle/Crosshairs/crosshair_0" + weapon.GetType());
        if (tex == null) tex = (Texture)Resources.Load("GUI/Battle/Crosshairs/crosshair_00");
        if (tex != null)
        {
            if (r.material.HasProperty("_MainTex")) r.material.SetTexture("_MainTex", tex);
            r.material.mainTexture = tex;
        }
        var root = GetCrossHairRoot();
        if (root != null) root.localScale = weapon.GetType() != 6 ? new Vector3(0.65f, 0.65f, 0.65f) : new Vector3(1f, 1f, 1f);
        var anim = t.GetComponent<Animation>();
        if (anim != null && anim["CrossHairRotationAnimation"] != null)
        {
            anim["CrossHairRotationAnimation"].speed = 0f;
            anim["CrossHairRotationAnimation"].time = 0f;
            t.localEulerAngles = Vector3.zero;
        }
        SetCrossHairMode(currentCrossHairMode);
    }

    /// <summary>SetCrossHairMode (:460-485): white (Ready), grey 0.5 (Blocked), red (1, 0.25, 0.25) with a fighter under it.</summary>
    void SetCrossHairMode(CrossHairMode a)
    {
        currentCrossHairMode = a;
        var t = EnsureCrossHair();
        if (t == null) return;
        var r = t.GetComponent<Renderer>();
        if (r == null || r.material == null || !r.material.HasProperty("_Color")) return;
        switch (a)
        {
            case CrossHairMode.Ready: r.material.color = new Color(1f, 1f, 1f, 1f); break;
            case CrossHairMode.Blocked: r.material.color = new Color(0.5f, 0.5f, 0.5f, 1f); break;
            case CrossHairMode.ReadyToFire: r.material.color = new Color(1f, 0.25f, 0.25f, 1f); break;
        }
    }

    /// <summary>AnimateCrossHair() (:444-458): the shot kick — CrossHairAnimation rewound and blended at weight 1, speed 1.</summary>
    void AnimateCrossHair()
    {
        var t = EnsureCrossHair();
        var anim = t != null ? t.GetComponent<Animation>() : null;
        if (anim == null || anim["CrossHairAnimation"] == null) return;
        anim["CrossHairAnimation"].weight = 1f;
        anim["CrossHairAnimation"].speed = 1f;
        anim.Rewind("CrossHairAnimation");
        anim.Blend("CrossHairAnimation");
    }

    void LateUpdate() { RotateCursor(); }   // :1189-1192

    /// <summary>AnimateCrossHair(bool) (:398-418): the gatling spin turns the crosshair (rotation clip from speed 0 when it starts,
    /// back to speed 2 when stopping while it still turns).</summary>
    void AnimateCrossHair(bool a)
    {
        var t = EnsureCrossHair();
        var anim = t != null ? t.GetComponent<Animation>() : null;
        if (anim == null || anim["CrossHairRotationAnimation"] == null) return;
        var s = anim["CrossHairRotationAnimation"];
        if ((a && s.speed == 0f) || !anim.IsPlaying("CrossHairRotationAnimation"))
        {
            s.speed = 0f;
            anim.Blend("CrossHairRotationAnimation");
        }
        else if (!a && s.speed > 0f) s.speed = 2f;
        rotateCursor = a;
    }

    void RotateCursor()   // :420-442
    {
        var t = EnsureCrossHair();
        var anim = t != null ? t.GetComponent<Animation>() : null;
        if (anim == null || anim["CrossHairRotationAnimation"] == null) return;
        var s = anim["CrossHairRotationAnimation"];
        if (rotateCursor && s.speed < 2f) s.speed += 0.025f;
        else if (!rotateCursor && s.speed > 0f) { s.speed -= 0.04f; if (s.speed < 0f) s.speed = 0f; }
    }

    /// <summary>CheckWeaponState (:1760-1782, every 50 ms): Blocked, else Ready / ReadyToFire by StraightScan through the screen centre.</summary>
    void CheckWeaponState()
    {
        if (weapon == null) return;
        weaponBlocked = weapon.IsWeaponBlocked();
        if (weaponBlocked) SetCrossHairMode(CrossHairMode.Blocked);
        else if (fpsCamera != null)
        {
            Ray a = fpsCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 20f));
            SetCrossHairMode(ShotCalculator.StraightScan(a, false) == -1 ? CrossHairMode.Ready : CrossHairMode.ReadyToFire);
        }
    }

    // ---------------- taunts (LaunchTaunt :1558-1606 / FinishTaunt :1668-1716) ----------------
    public void OnTauntStart()
    {
        isTaunt = true; shot = false; shotAlt = false;
        Zoom = false;
        if (weapon != null) ResetReloadWeaponAnimation(weapon.Transform);
    }

    public void OnTauntEnd()
    {
        isTaunt = false;
        if (weapon != null && weapon.Transform != null) ResetReloadWeaponAnimation(weapon.Transform);
    }

    /// <summary>PlayerManager.SyncLocalWeaponCamera (NE :8126-8143): the weapon camera is on only with the eye camera.</summary>
    public void SyncWeaponCamera(bool fpsActive)
    {
        if (!weaponCamera) return;
        weaponCamera.gameObject.SetActive(fpsActive);
        weaponCamera.enabled = fpsActive;
    }
}
