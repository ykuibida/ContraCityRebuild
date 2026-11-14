// Battle interface = port of New Era GameHUD (decrypted client, W:/Contra City Extract/NE_Scripts_Decrypted/GameHUD.cs):
// OnGUI order — DrawUpTimer, DrawUpTeamScores (team modes), DrawFrags (kill feed), DrawMenu (Esc), DrawSelectTeam,
// player states Play / Dead / DeadReady / TimeOver, DrawStateDead (tips + killer card + bottom strip), DrawScore (TAB / results).
// 3D blocks health / armor / ammo = the original BattleGUI prefab. Real screen pixels, original skins and language file.
using System.Linq;
using UnityEngine;

public class BattleUI
{
    public enum PlayerStateKind { Load, Play, Dead, DeadReady, TimeOver }

    readonly OptionsWindow options;
    public BattleUI(OptionsWindow o) { options = o; }

    public bool MenuOpen;                 // IsShowMenu
    public bool ShowSelectTeam;           // showSelectTeam
    public bool ShowScore;                // TAB held
    public bool DebugShowScore;           // test hook (-uitest): TAB without the key
    public PlayerStateKind PlayerState = PlayerStateKind.Load;
    /// <summary>GameHUD.isActive (NE GameHUD.cs:545-556): Play state, no menu.</summary>
    public bool IsHudActive => PlayerState == PlayerStateKind.Play && !MenuOpen;
    static CombatWeapon CurrentWeapon => ShotController.Instance ? ShotController.Instance.CurrentWeapon : null;
    const float RespawnDelay = 7f;        // GameHUD.respawnDelay
    const float TeamSwitchCooldown = 90f; // server value, not in the client: ~90 s observed (00:31 left 61 s after joining)
    float timeOfDeath, hideWeaponTime, timeOverStarted = -1f, teamSwitchUntil, lastSpawnRequestTime = -10f, newGameAt = -1f;
    BattleGUI hud;

    static GameFlow Flow => GameFlow.Instance;
    static RoomSettings Room => Flow.Room;
    static BattleScore Score => Flow.Score;
    static string T(string k) => LanguageManager.GetText(k);
    static GUIStyle BB(string n) => Skins.S("BattleBackgound", n);
    static GUIStyle BT(string n) => Skins.S("BattleText", n);
    static GUIStyle BI(string n) => Skins.S("BattleIco", n);

    // ---------------- flow (NetworkManager.InitSpawn / GameHUD state changes) ----------------
    /// <summary>GameHUD.CheatMessagePersistent: the under-map warning (language "cheat_msg"), drawn until the battle is left.</summary>
    string cheatMessage = "";
    bool hooks;
    void EnsureHooks()
    {
        if (hooks) return;
        hooks = true;
        CharacterMotor.UnderMapWarning = () => cheatMessage = LanguageManager.GetText("cheat_msg");
        // CharacterMotor.IsRoundIntermissionMovementLocked: TimeOver / TimeOverReady / RoundStarting
        CharacterMotor.RoundIntermissionLocked = () => PlayerState == PlayerStateKind.TimeOver;
    }

    /// <summary>GameHUD.Awake: a fresh HUD per battle (no cheat message, no pending new round).</summary>
    public void ResetForBattle() { cheatMessage = ""; newGameAt = -1f; PlayerState = PlayerStateKind.Load; }

    float bloodOverlayAmount;
    /// <summary>GameHUD.ShotMe / ShotMePeriodic (NE GameHUD.cs:3957-3975): the blood overlay at full strength; it holds 0.5 s and fades
    /// over the next 0.5 s (Update :3771-3788). Every hit resets it, whatever the damage.</summary>
    public void ShotMe() { bloodOverlayAmount = 1f; if (hud != null) hud.SetBloodOverlay(1f); }
    public void ShotMePeriodic() => ShotMe();
    /// <summary>GameHUD.ClearTransientBattleEffects (:656-672): blood overlay and state icons off (respawn / death).</summary>
    public void ClearTransientBattleEffects() { bloodOverlayAmount = 0f; if (hud != null) hud.HideAllImpacts(); }
    public void ShowImpact(BattleGUIImpact.ImpactType a, float b) { if (hud != null) hud.ShowImpact(a, b); }

    void UpdateBloodOverlay()
    {
        float v = 0f;
        if (bloodOverlayAmount > 0f)
        {
            bloodOverlayAmount = Mathf.Max(0f, bloodOverlayAmount - Time.deltaTime);
            v = bloodOverlayAmount > 0.5f ? 1f : Mathf.Clamp01(bloodOverlayAmount / 0.5f);
        }
        if (hud != null) hud.SetBloodOverlay(v);
    }

    public void InitSpawn()
    {
        EnsureHooks();
        MenuOpen = false; ShowScore = false; timeOverStarted = -1f;
        ShowCursor();
        if (Room.Mode.IsTeam() && Room.Mode != GameMode.ZOMBIE) { Flow.LocalTeam = -1; ShowSelectTeam = true; PlayerState = PlayerStateKind.Load; }
        else { ShowSelectTeam = false; Flow.LocalTeam = 0; BeginRespawnCountdown(); }
    }

    public void BeginRespawnCountdown()
    {
        PlayerState = PlayerStateKind.Dead;
        timeOfDeath = Time.time;
    }

    public void JoinSelectedBattleTeam(short team)
    {
        ShowSelectTeam = false;
        Flow.LocalTeam = team;
        if (Score.ContainsUser(GameFlow.LocalId)) Score[GameFlow.LocalId].Team = team;
        teamSwitchUntil = Time.realtimeSinceStartup + TeamSwitchCooldown;
        EnsureHud(); if (hud) hud.SetTeam(team, Room.Mode);
        HideCursor();
        BeginRespawnCountdown();
    }

    /// <summary>NetworkManager.SendSpawnRequest after «Нажмите огонь».</summary>
    public void RequestRespawn()
    {
        if (Time.time - lastSpawnRequestTime < 0.75f) return;   // GameHUD.RequestRespawn debounce (:3876)
        lastSpawnRequestTime = Time.time;
        int team = Room.Mode == GameMode.DEATHMATCH || Room.Mode == GameMode.TOWER_DEFENSE ? 0 : Flow.LocalTeam;
        if (team < 0) { InitSpawn(); return; }
        Flow.Spawn(team);
        EnsureHud(); if (hud) hud.SetTeam((short)team, Room.Mode);
        PlayerState = PlayerStateKind.Play;
        ShowWeapon();
    }

    /// <summary>OfflineMatchController.CheckFragLimit (:198-229).</summary>
    bool CheckFragLimit()
    {
        if (Room.Mode == GameMode.DEATHMATCH) return Score.Users.Values.Any(u => u.Kill >= Room.Frags);
        if (Room.Mode == GameMode.TEAM_DEATHMATCH) return Score.GetTeamKills(1) >= Room.Frags || Score.GetTeamKills(2) >= Room.Frags;
        return false;
    }

    /// <summary>OfflineMatchController.OnTimeOver (:662-693) → PlayerManager.TimeOver (:8165-8216): the team with more points gets a win,
    /// round stats are committed, everybody is killed (no frag), results for 10 s, the new round after 8 s.</summary>
    void OnTimeOver()
    {
        if (Room.Mode.IsTeam())
        {
            int r = Score.GetTeamPoints(1), b = Score.GetTeamPoints(2);
            if (r > b) Score.AddTeamWin(1); else if (b > r) Score.AddTeamWin(2);
        }
        Flow.CommitRound(true);
        PlayerState = PlayerStateKind.TimeOver; timeOverStarted = -1f;
        ShowSelectTeam = false; ShowScore = false;
        ShowCursor();
        Flow.KillLocalForTimeOver();
        SoundManager.PlayAnnouncer("Announcer_Winner");   // SoundManager.AnnounceWinner (the Windows speech of the winner's name is left out)
        newGameAt = Time.time + 8f;
    }

    /// <summary>PlayerManager.NewGame + SpawnMe in the same team (OfflineMatchController.NewGameAfterDelay).</summary>
    void NewGame()
    {
        newGameAt = -1f;
        Flow.RestartRound();
        int team = Room.Mode == GameMode.DEATHMATCH || Room.Mode == GameMode.TOWER_DEFENSE ? 0 : Flow.LocalTeam;
        if (team < 0) { InitSpawn(); return; }
        MenuOpen = false; HideCursor();
        lastSpawnRequestTime = -10f;
        RequestRespawn();
    }

    public void ShowWeapon() => hideWeaponTime = Time.time + 2f;    // offsetShowWeapon

    static void HideCursor() { Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked; }
    static void ShowCursor() { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }

    void EnsureHud()
    {
        if (hud != null) return;
        var pf = Resources.Load<GameObject>("gui/battle/prefab/BattleGUI");
        if (pf == null) return;
        var go = Object.Instantiate(pf); go.name = "BattleGUI";
        hud = go.GetComponent<BattleGUI>();
        hud.SetTeam(0, Room.Mode);
        InitSceneHud();
    }

    // ---------------- the map scene's GameHUD objects (GUICamera) ----------------
    /// <summary>GameHUD.CrossHairs: GUICamera/Crosshairs of the map scene (the 3D crosshair that ShotController drives).</summary>
    public static Transform CrossHairs { get; private set; }
    static bool isCrossHairsVisible;
    bool zoom;

    /// <summary>The original map scenes keep their GUICamera (orthographic, depth 1) with the crosshair and the damage indicator; the
    /// map-viewer build step had switched every scene camera off, so it is switched on again for the battle (Contra/_Scenes: enabled).
    /// The damage indicator starts at the end of its clip (GameHUD.ResetDamageIndicatorVisual, NE GameHUD.cs:4105-4124).</summary>
    void InitSceneHud()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        CrossHairs = null; isCrossHairsVisible = true;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "GUICamera") continue;
            var cam = root.GetComponent<Camera>(); if (cam) cam.enabled = true;
            CrossHairs = root.transform.Find("Crosshairs");
            var di = root.transform.Find("DamageIndicator");
            var anim = di ? di.GetComponent<Animation>() : null;
            if (anim != null && anim.clip != null)
            {
                anim.Stop();
                var st = anim[anim.clip.name];
                if (st != null) { st.enabled = true; st.weight = 1f; st.time = Mathf.Max(0f, st.length); anim.Sample(); st.enabled = false; }
            }
        }
    }

    /// <summary>GameHUD.Zoom (NE GameHUD.cs:520-524): the zoom flag (hides the crosshair) and BattleGUI.SetZoom (the scope overlay).</summary>
    public void Zoom(bool a)
    {
        zoom = a;
        if (hud != null) hud.SetZoom(a);
    }

    /// <summary>GameHUD.SetCrossHairVisible (NE GameHUD.cs:526-533), called every frame with "FPS camera active and no zoom" (:3763).</summary>
    static void SetCrossHairVisible(bool a)
    {
        if (CrossHairs == null || isCrossHairsVisible == a) return;
        isCrossHairsVisible = a;
        CrossHairs.gameObject.SetActive(a);
    }

    float SecondsLeft()
    {
        float total = Mathf.Clamp(Room.EffectiveTimeLimit, 1, 60) * 60f;
        float el = Time.time - Flow.BattleStartTime;
        bool up = Room.Mode == GameMode.CONTROL_POINTS || Room.Mode == GameMode.CAPTURE_THE_FLAG;   // these count up
        return Mathf.Clamp(up ? el : total - el, 0f, total);
    }

    int NextRoundSecondsLeft()
    {
        if (timeOverStarted < 0f) timeOverStarted = Time.realtimeSinceStartup;
        return Mathf.Clamp(Mathf.CeilToInt(10f - (Time.realtimeSinceStartup - timeOverStarted)), 0, 10);
    }

    // ---------------- Update (GameHUD.Update) ----------------
    /// <summary>CombatPlayer.GetTauntID: the taunt put in the slot (owned, not expired); the empty first slot plays the free taunt 1.</summary>
    static int TauntInSlot(int i)
    {
        var p = Profile.Current;
        int id = i < p.TauntSlots.Length ? p.TauntSlots[i] : 0;
        if (id > 0) { var it = ItemDB.Get(ItemKind.Taunt, id); if (it == null || !p.Owns(it)) id = 0; }
        return id > 0 ? id : i == 0 ? 1 : 0;
    }

    public void Update()
    {
        var flow = Flow;
        if (flow == null || flow.Current != GameFlow.State.Battle)
        {
            if (hud != null) { Object.Destroy(hud.gameObject); hud = null; }
            return;
        }
        EnsureHud();
        UpdateBloodOverlay();
        {
            var fpsCam = flow.Player ? flow.Player.transform.Find("FPSCamera")?.GetComponent<Camera>() : null;
            SetCrossHairVisible(fpsCam != null && fpsCam.enabled && fpsCam.gameObject.activeInHierarchy && !zoom);
        }
        // New Era OfflineMatchController.Update (:160-180): the round ends when the time limit is over (every mode, CTF/CP count up
        // to their 60 min) or by the frag limit (DM: a player's kills, TDM: a team's kills ≥ Frags) → OnTimeOver → TimeOver
        if (PlayerState != PlayerStateKind.TimeOver && PlayerState != PlayerStateKind.Load && (Time.time - Flow.BattleStartTime >= Mathf.Clamp(Room.EffectiveTimeLimit, 1, 60) * 60f || (Room.Frags > 0 && CheckFragLimit())))
            OnTimeOver();
        // NewGameAfterDelay(8 s) (:695-726): new round, the local player respawns at once in the same team (SpawnMe 100/100)
        if (PlayerState == PlayerStateKind.TimeOver && newGameAt > 0f && Time.time >= newGameAt) NewGame();
        if (PlayerState == PlayerStateKind.Dead && (long)((timeOfDeath + RespawnDelay - Time.time)) <= 0 && Time.time - timeOfDeath >= RespawnDelay) PlayerState = PlayerStateKind.DeadReady;

        ShowScore = Input.GetKey(KeyCode.Tab) || DebugShowScore;
        if (Input.GetKeyUp(KeyCode.Escape)) { MenuOpen = true; ShowCursor(); if (Screen.fullScreen) Screen.fullScreen = false; }   // GameHUD :3806-3814
        else if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !MenuOpen && !ShowSelectTeam && !options.Open && PlayerState != PlayerStateKind.TimeOver) HideCursor();
        if (PlayerState == PlayerStateKind.DeadReady && !MenuOpen && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(TRInput.Fire1))) RequestRespawn();   // IsRespawnClick
        if (Input.GetKeyDown(KeyCode.F) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) { MenuOpen = false; HideCursor(); Screen.fullScreen = true; }

        bool blocked = MenuOpen || options.Open || ShowSelectTeam || PlayerState != PlayerStateKind.Play;
        var body = flow.Player ? flow.Player.GetComponent<FighterBody>() : null;
        if (body)
        {
            // Penta 2016 ShotController: E / T / Y launch the taunt of slot 1 / 2 / 3; while it plays ShotController.Update returns
            // early (no shot, no weapon change, no other taunt) until FinishTaunt — nothing cancels it (only death switches the camera)
            if (PlayerState != PlayerStateKind.Play) body.CancelTaunt();
            else if (!blocked && !body.Taunting && !(ShotController.Instance && ShotController.Instance.IsShooting))   // ShotController: taunt keys only while !shot
            {
                if (Input.GetKeyDown(TRInput.Taunt1)) body.LaunchTaunt(TauntInSlot(0));
                else if (Input.GetKeyDown(TRInput.Taunt2)) body.LaunchTaunt(TauntInSlot(1));
                else if (Input.GetKeyDown(TRInput.Taunt3)) body.LaunchTaunt(TauntInSlot(2));
            }
        }
        if (flow.Player)
        {
            var sc = flow.Player.GetComponent<SoldierController>(); if (sc) sc.enabled = !blocked;
            var look = flow.Player.GetComponent<SoldierCamera>(); if (look) look.enabled = !(MenuOpen || options.Open || ShowSelectTeam);
            var motor = flow.Player.GetComponent<CharacterMotor>(); if (motor && blocked) motor.inputMoveDirection = Vector3.zero;
            // the eye camera's field of view: FPSCamera.ApplyFieldOfView (option × zoom)
        }
        // 3D HUD values (DrawUserStats → UpdateHealth / SetAmmo); hidden while not playing (HideBattleGUI)
        if (hud == null) return;
        var v = flow.Player ? flow.Player.GetComponent<PlayerVitals>() : null;
        bool hudOn = PlayerState == PlayerStateKind.Play && !ShowSelectTeam && v != null;
        hud.SetCameraEnabled(hudOn);
        if (hudOn)
        {
            hud.SetHealth(v.Health, v.MaxHealth);
            hud.SetArmor(v.Armor, v.MaxArmor);
            // DrawUserStats (NE GameHUD.cs:897-918): melee → 0 / 0 / 0, else loaded / max loaded / reserve of the current weapon
            var cw = CurrentWeapon;
            var sh = ShotController.Instance;
            if (cw != null)
            {
                if (cw.Type == WeaponType.ONE_HANDED_COLD_ARMS || cw.Type == WeaponType.TWO_HANDED_COLD_ARMS) hud.SetAmmo(0, 0, 0);
                else hud.SetAmmo(sh.Ammo, sh.MaxAmmo, sh.AmmoReserve);
            }
        }
    }

    // ---------------- OnGUI (GameHUD.OnGUI) ----------------
    public void Draw()
    {
        var flow = Flow;
        if (flow == null || flow.Current != GameFlow.State.Battle) return;
        GUI.skin = Skins.Main;
        float W = Screen.width, H = Screen.height;
        DrawUpTimer(W);
        if (Room.Mode.IsTeam()) DrawUpTeamScores(W);
        DrawFrags(W);
        if (cheatMessage != "") Skins.ShadowLabel(new Rect(0f, 45f, W, H - 146f), cheatMessage, BT("cheatMsg"), BT("cheatMsgBorder"));   // GameHUD :3253-3256
        if (MenuOpen) { DrawMenu(W, H); options.Draw(); return; }
        if (ShowSelectTeam)
        {
            if (ShowScore) DrawScore(W, H, false); else DrawSelectTeam(W, H);
            options.Draw();
            return;
        }
        switch (PlayerState)
        {
            case PlayerStateKind.Play:
                // the crosshair is the scene's 3D CrossHair (ShotController.SetCrossHair), not drawn here
                if (CurrentWeapon != null && Time.time < hideWeaponTime) WeaponBar(W, H);
                break;
            case PlayerStateKind.Dead:
                DrawStateDead(W, H, (long)Mathf.Max(0f, Mathf.Floor(RespawnDelay - (Time.time - timeOfDeath))));   // GameHUD :3470 integer division: 6…0
                break;
            case PlayerStateKind.DeadReady:
                DrawStateDead(W, H, 0);
                break;
            case PlayerStateKind.TimeOver:
                DrawScore(W, H, true);
                break;
        }
        if (ShowScore && (PlayerState == PlayerStateKind.Play || PlayerState == PlayerStateKind.Dead || PlayerState == PlayerStateKind.DeadReady)) DrawScore(W, H, false);   // GameHUD :3510
        options.Draw();
    }

    void DrawUpTimer(float W)
    {
        bool tdmSelect = ShowSelectTeam && Room.Mode == GameMode.TEAM_DEATHMATCH;
        float left = PlayerState == PlayerStateKind.TimeOver || (PlayerState == PlayerStateKind.Load && !tdmSelect) ? 0f : SecondsLeft();   // GameHUD :843-848
        long ms = (long)(left * 1000f);
        long m = ms / 60000, s = ms / 1000 - m * 60;
        Skins.ShadowLabel(new Rect(W / 2 - 20, 7, 40, 20), $"{m}:{s:00}", BT("upTime"), BT("upTimeShadow"));
    }

    void DrawUpTeamScores(float W)
    {
        int red = Score.GetTeamPoints(1), blue = Score.GetTeamPoints(2);
        BT("upScoreRed").fontSize = red < 100 ? 35 : red < 1000 ? 28 : 18;
        BT("upScoreBlue").fontSize = blue < 100 ? 35 : blue < 1000 ? 28 : 18;
        bool big = Room.Mode == GameMode.CAPTURE_THE_FLAG || Room.Mode == GameMode.CONTROL_POINTS;
        float num = big ? 175f : 145f;
        GUILayout.BeginArea(new Rect(W * 0.5f - num - 40f, 0f, num, 41f), GUIContent.none, BB("leftScore"));
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (big) GUILayout.Label($"{Room.Frags}\\", BT("upScoreRedSmall"));
        GUILayout.Label(red.ToString(), BT("upScoreRed"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace(); GUILayout.EndVertical();
        GUILayout.Label(GUIContent.none, BI(Room.Mode == GameMode.CAPTURE_THE_FLAG ? "upScoreCTFRed_0" : Room.Mode == GameMode.ZOMBIE ? "upScoreZombie" : "upScoreRed"), GUILayout.Width(37), GUILayout.Height(33));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(W * 0.5f + 40f, 0f, num, 41f), GUIContent.none, BB("rightScore"));
        GUILayout.BeginHorizontal();
        GUILayout.Label(GUIContent.none, BI(Room.Mode == GameMode.CAPTURE_THE_FLAG ? "upScoreCTFBlue_0" : Room.Mode == GameMode.ZOMBIE ? "upScoreHuman" : "upScoreBlue"), GUILayout.Width(37), GUILayout.Height(33));
        GUILayout.BeginVertical(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.Label(blue.ToString(), BT("upScoreBlue"));
        if (big) GUILayout.Label($"/{Room.Frags}", BT("upScoreBlueSmall"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace(); GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    static GUIStyle FragName(Frag f, bool killer)
    {
        if (f.KillerID == GameFlow.LocalId) return BT("fragLocal");
        int team = killer ? f.KillerTeam : f.KilledTeam;
        return BT(team == 1 ? "fragRed" : team == 2 ? "fragBlue" : "fragDefault");
    }

    void DrawFrags(float W)
    {
        GUILayout.BeginArea(new Rect(0f, 5f, W, 300f), GUIStyle.none);
        var skin = Skins.Get("BattleFrags");
        foreach (var f in Score.Frags)
        {
            bool local = f.KillerID == GameFlow.LocalId;
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal(GUIContent.none, BB(local ? "fragLineLocal" : "fragLine"));
            GUILayout.Label(f.KillerShowedName, FragName(f, true));
            if (f.KillerID == f.KilledID) GUILayout.Label(GUIContent.none, Skins.S("BattleFrags", local ? "fragSuicideLocal" : "fragSuicide"));
            else
            {
                string suf = local ? "Local" : "";
                var st = !string.IsNullOrEmpty(f.WeaponSystemName) && skin ? skin.FindStyle("frag" + f.WeaponSystemName + suf) : null;
                GUILayout.Label(GUIContent.none, st ?? Skins.S("BattleFrags", "fragNone"));
                if (f.Head) { var h = skin ? skin.FindStyle("fragHead" + suf) : null; if (h != null) GUILayout.Label(GUIContent.none, h); }
                GUILayout.Label(f.KilledShowedName, FragName(f, false));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();
    }

    // ---------------- team choice (DrawSelectTeam) ----------------
    void DrawSelectTeam(float W, float H)
    {
        Skins.BeginArea(new Rect((W - 449f) * 0.5f, (H - 448f) * 0.5f, 449f, 448f), BB("selectTeam"));
        GUILayout.Label(T("Choose a team"), BB("tabPart"));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), T("Choose a team"), BT("tabPart"), BT("tabPartShadow"));
        GUILayout.BeginHorizontal();
        TeamColumn("selectRed", 1);
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Hor"), GUILayout.Width(1));
        TeamColumn("selectBlue", 2);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(GUILayout.Height(75));
        GUILayout.Space(15);
        int r = Score.GetTeamCount(1), b = Score.GetTeamCount(2), max = Room.MaxPlayersTeam;
        GUILayout.BeginVertical(GUILayout.Width(193)); GUILayout.Space(13);
        if (r < max && r - b < 2 && GUILayout.Button(T("Join Red"), Skins.Btn("joinRed"), GUILayout.Width(193), GUILayout.Height(47))) JoinSelectedBattleTeam(1);
        GUILayout.EndVertical();
        GUILayout.Space(30);
        GUILayout.BeginVertical(GUILayout.Width(193)); GUILayout.Space(13);
        if (b < max && b - r < 2 && GUILayout.Button(T("Join Blue"), Skins.Btn("joinBlue"), GUILayout.Width(193), GUILayout.Height(47))) JoinSelectedBattleTeam(2);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void TeamColumn(string style, int team)
    {
        GUILayout.BeginVertical(GUIContent.none, BB(style), GUILayout.Width(222), GUILayout.Height(323));
        GUILayout.Space(13);
        GUILayout.BeginHorizontal(); GUILayout.Space(9);
        GUILayout.Label(GUIContent.none, BI(style), GUILayout.Width(204), GUILayout.Height(204));
        GUILayout.EndHorizontal();
        GUILayout.Space(20);
        GUILayout.BeginHorizontal(); GUILayout.Space(15);
        GUILayout.BeginVertical(GUIContent.none, BB("tabItem"), GUILayout.Width(193), GUILayout.Height(73));
        GUILayout.FlexibleSpace();
        Centered(T("Players"), BT("txt1"));
        Centered($"{Score.GetTeamCount(team)}/{Room.MaxPlayersTeam}", BT("txt24"));
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    // ---------------- death (DrawStateDead + DrawBottomMessage) ----------------
    void DrawStateDead(float W, float H, long left)
    {
        GUILayout.BeginArea(new Rect(0f, H * 0.5f - 157f, 244f, 92f + 23f), GUIContent.none, BB("tips"));
        foreach (var (k, txt) in new[] { ("ALT + F", T("Fullscreen")), ("Tab", T("Combat Stats")), (T("RMB"), T("Change camera")), ("Esc", T("Menu")) })
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(k, BT("tips01"), GUILayout.Width(50));
            GUILayout.Label(txt, BT("tips02"));
            GUILayout.EndHorizontal();
            GUILayout.Label(GUIContent.none, Skins.Sep("transHor"), GUILayout.Height(1));
        }
        GUILayout.EndArea();
        var f = Score.ContainsUser(GameFlow.LocalId) ? Score[GameFlow.LocalId].LastFrag : null;
        if (f != null && f.KillerID != f.KilledID)
        {
            var weapon = ItemDB.Weapons.FirstOrDefault(w => w.SName == f.WeaponSystemName);
            string wname = weapon != null ? weapon.Name : string.IsNullOrEmpty(f.WeaponSystemName) ? "—" : f.WeaponSystemName;
            GUILayout.BeginArea(new Rect(W - 227f, H - 293f, 227f, 183f), GUIContent.none, BB("killerInfo"));
            GUILayout.BeginHorizontal(GUILayout.Height(20)); GUILayout.Space(2);
            Skins.AutoLabel(T("Killer:"), BT("txt1")); GUILayout.Space(2); Skins.AutoLabel(f.KillerShowedName, BT("txt1Value"));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal(GUILayout.Height(33));
            GUILayout.Label(GUIContent.none, BI("healthSmall"), GUILayout.Width(22), GUILayout.Height(22)); GUILayout.Space(2);
            GUILayout.Label(f.KillerHealth.ToString(), BT("healthSmall"), GUILayout.MinWidth(47));
            GUILayout.Label(GUIContent.none, BI("armorSmall"), GUILayout.Width(22), GUILayout.Height(22));
            GUILayout.Label(f.KillerEnergy.ToString(), BT("healthSmall"), GUILayout.MinWidth(47));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            GUILayout.BeginHorizontal(); GUILayout.Space(2);
            Skins.AutoLabel(T("Weapon:"), BT("txt1")); GUILayout.Space(2); Skins.AutoLabel(wname, BT("txt1Value"));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            if (weapon != null && weapon.Icon != null) GUILayout.Label(weapon.Icon, Skins.Bg("itemLeft2"), GUILayout.Width(101), GUILayout.Height(90));
            else GUILayout.Space(90);
            GUILayout.EndArea();
        }
        if (left <= 0) DrawBottomMessage(W, H, T("PRESS"), T("FIRE"), T("TO RESPAWN"));
        else DrawBottomMessage(W, H, T("RESPAWN IN"), left.ToString(), T("SECONDS"));
    }

    static void DrawBottomMessage(float W, float H, string a, string b, string c)
    {
        GUILayout.BeginArea(new Rect(0f, H - 80f, W, 43f), GUIContent.none, BB("respawnLine"));
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.Label(a, BT("respawn01")); GUILayout.Label(b, BT("respawn02")); GUILayout.Label(c, BT("respawn01"));
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---------------- TAB / results (DrawScore) ----------------
    void DrawScore(float W, float H, bool end)
    {
        var p = Profile.Current;
        var me = Score.ContainsUser(GameFlow.LocalId) ? Score[GameFlow.LocalId] : null;
        bool team = Room.Mode.IsTeam();
        var size = new Vector2(774f, end ? 640f : 610f);
        Skins.BeginArea(new Rect(W * 0.5f - size.x * 0.5f, H * 0.5f - size.y * 0.5f, size.x, size.y), BB(end ? "tabEnd" : "tab"));
        if (end)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(50));
            GUILayout.Label(T("Combat Results"), BB("tabPart"));
            Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), T("Combat Results"), BT("tabPart"), BT("tabPartShadow"));
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(); GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            Skins.AutoLabel(T("Next round starts in:"), BT("txt1Value")); GUILayout.Space(4);
            GUILayout.Label(NextRoundSecondsLeft().ToString(), BT("txt1ValueActive")); GUILayout.Space(4);
            Skins.AutoLabel(T("sec"), BT("txt1Value")); GUILayout.Space(15);
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.BeginHorizontal(GUIContent.none, BB("tabTitleOffset"), GUILayout.Height(54));
            GUILayout.BeginVertical();
            Pair(T("Room name:"), Room.Name); GUILayout.Space(5); Pair(T("Server:"), "Local");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            Pair(T("Map:"), Room.MapName); GUILayout.Space(5); Pair(T("Mode:"), Room.Mode.GetFullName());
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUILayout.Width(286));
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUIContent.none, BB("tabItem"), GUILayout.Width(273), GUILayout.Height(73));
        GUILayout.FlexibleSpace();
        Centered(T("Rank"), BT("txt1"));
        if (me != null) { Score.Sorted(me.Team); Centered($"{me.Position}/{(team ? Room.MaxPlayersTeam : Room.MaxPlayers)}", BT("txt24")); }
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        GUILayout.Space(6);
        GUILayout.BeginHorizontal(GUIContent.none, BB("tabItem"), GUILayout.Width(273), GUILayout.MinHeight(83));
        GUILayout.Space(14);
        GUILayout.BeginVertical();
        GUILayout.Space(4);
        int exp = me != null ? me.Exp : 0, maxExp = p.MaxExp;
        Pair(T("Level:"), p.Level.ToString()); GUILayout.Space(1);
        Pair(T("Exp:"), $"{p.Exp + exp}/{maxExp}"); GUILayout.Space(1);
        Pair(T("Experience earned:"), exp.ToString(), true); GUILayout.Space(1);
        Pair(T("EXP to the next level:"), Mathf.Max(0, maxExp - p.Exp - exp).ToString());
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.BeginHorizontal(GUIContent.none, BB("tabItem"), GUILayout.Width(273), GUILayout.MinHeight(165));
        GUILayout.Space(14);
        GUILayout.BeginVertical();
        GUILayout.Space(11);
        DrawTabWeaponStats(me);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.BeginVertical(GUIContent.none, BB("tabItem"), GUILayout.Width(273), GUILayout.MinHeight(157));
        GUILayout.Space(15);
        Centered(T("Earned achievements:"), BT("txt1"));
        GUILayout.Space(9);
        GUILayout.EndVertical();
        GUILayout.EndVertical();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Hor"), GUILayout.Width(1));
        GUILayout.BeginVertical();
        TeamHeader(team ? T("Red") : T("Players"), team ? "txtTitle19boldRed" : "txtTitle19bold", 0, team);
        DrawUsers(team ? 1 : 0, end, 212f);
        if (team)
        {
            GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
            TeamHeader(T("Blue"), "txtTitle19boldBlue", 1, true);
            DrawUsers(2, end, 0f);
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        if (end)
        {
            GUILayout.BeginVertical(GUILayout.Height(73));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            GUILayout.Label(LanguageManager.GetTextFormat("Next round starts in: {0} sec", NextRoundSecondsLeft()), BT("tabPartSmall"));
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        else
        {
            GUILayout.BeginHorizontal(GUILayout.Height(42));
            GUILayout.FlexibleSpace();
            Skins.AutoLabel(T("Experience earned:"), BT("txt1"), GUILayout.Height(42)); GUILayout.Space(6);
            GUILayout.Label(exp.ToString(), BT("txt1ValueActive"), GUILayout.Height(42));
            GUILayout.Space(24);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();
    }

    void TeamHeader(string name, string style, int idx, bool team)
    {
        GUILayout.BeginHorizontal(GUIContent.none, BB("tabTitle"), GUILayout.Height(36));
        GUILayout.Label(name, BT(style), GUILayout.Width(124));
        GUILayout.BeginVertical(); GUILayout.Space(10);
        GUILayout.Label($"{Score.GetTeamCount(team ? idx + 1 : 0)}/{(team ? Room.MaxPlayersTeam : Room.MaxPlayers)}", BT("txt1Value"));
        GUILayout.EndVertical();
        if (team) { GUILayout.FlexibleSpace(); GUILayout.Label(Score.GetTeamPoints(idx + 1).ToString(), BT(style)); GUILayout.Space(10); }
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Hor"), GUILayout.Width(1));
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
    }

    static void Pair(string k, string v, bool active = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(k, BT("txt1")); GUILayout.Space(5); GUILayout.Label(v, BT(active ? "txt1ValueActive" : "txt1Value"));
        GUILayout.EndHorizontal();
    }

    static void Centered(string s, GUIStyle st)
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); GUILayout.Label(s, st); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    static readonly string[] TabWeaponHeaders = { "Убито", "Получено", "Доля в %" };

    static void DrawTabWeaponStats(ScoreUser a)
    {
        // EnsureTabWeaponLayout: inner width 273 - padding - 22, label column = widest category name
        var tabItem = BB("tabItem");
        float inner = Mathf.Max(150f, 273f - (tabItem.padding.left + tabItem.padding.right) - 14f - 8f);
        var lab = new GUIStyle(BT("txt1Value")) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        var val = new GUIStyle(BT("txt1ValueActive")) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
        val.padding = new RectOffset(0, 0, val.padding.top, val.padding.bottom);
        float lw = ScoreUser.DamageCategoryNames.Max(n => lab.CalcSize(new GUIContent(n)).x);
        lw = Mathf.Min(Mathf.Ceil(lw) + 4f, Mathf.Floor(inner * 0.45f));
        float rest = inner - lw, kw = Mathf.Floor(rest * 0.26f), pw = Mathf.Floor(rest * 0.34f), tw = rest - kw - pw;
        var head = new GUIStyle(BT("txt1")) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow, fontStyle = FontStyle.Normal };
        head.padding = new RectOffset(0, 0, head.padding.top, head.padding.bottom);
        float[] cols = { kw, tw, pw };
        for (int fs = head.fontSize > 0 ? head.fontSize : 12; fs >= 8; fs--)
        {
            head.fontSize = fs;
            if (Enumerable.Range(0, 3).All(j => head.CalcSize(new GUIContent(TabWeaponHeaders[j])).x <= cols[j] - 2f)) break;
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Width(lw));
        GUILayout.Label(TabWeaponHeaders[0], head, GUILayout.Width(kw));
        GUILayout.Label(TabWeaponHeaders[1], head, GUILayout.Width(tw));
        GUILayout.Label(TabWeaponHeaders[2], head, GUILayout.Width(pw));
        GUILayout.EndHorizontal();
        GUILayout.Space(1);
        int total = a != null ? a.DamageTakenTotal : 0;
        for (int i = 0; i < 7; i++)
        {
            int k = a != null ? a.KillsByCategory[i] : 0, d = a != null ? a.DamageTaken[i] : 0;
            int pc = total > 0 && d > 0 ? Mathf.Clamp(Mathf.RoundToInt(d * 100f / total), 0, 100) : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(ScoreUser.DamageCategoryNames[i], lab, GUILayout.Width(lw));
            GUILayout.Label(k.ToString(), val, GUILayout.Width(kw));
            GUILayout.Label(ScoreUser.FormatDamage(d), val, GUILayout.Width(tw));
            GUILayout.Label(pc + "%", val, GUILayout.Width(pw));
            GUILayout.EndHorizontal();
            GUILayout.Space(1);
        }
    }

    void DrawUsers(int team, bool end, float minH)
    {
        GUILayout.BeginVertical(GUIContent.none, BB("tabUListOffset"), GUILayout.MinHeight(minH));
        GUILayout.BeginHorizontal();
        var st = BT(end ? "txtTabTitleEnd" : "txtTabTitle");
        GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Width(27));
        GUILayout.Label(T("Rank"), st, GUILayout.Width(38));
        GUILayout.Label(T("Name"), st, GUILayout.Width(179));
        GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Width(50));
        GUILayout.Label("Урон", st, GUILayout.Width(44));
        GUILayout.Label(T("Kills"), st, GUILayout.Width(46));
        GUILayout.Label(T("Deaths"), st, GUILayout.Width(46));
        GUILayout.Label(T("Ping"), st, GUILayout.Width(38));
        GUILayout.EndHorizontal();
        foreach (var u in Score.Sorted(team))
        {
            bool local = u.UserID == GameFlow.LocalId;
            GUILayout.BeginHorizontal(GUIContent.none, BB(local ? "tabLineActive" : "tabLine"));
            GUILayout.Space(2);
            GUILayout.Label(GUIContent.none, u.IsDead && !end ? BI("tabDead") : GUIStyle.none, GUILayout.Width(25));
            bool act = local || end;
            GUILayout.Label(u.Position.ToString(), BT(act ? "tabItemPosActive" : "tabItemPos"), GUILayout.Width(38));
            var name = new GUIStyle(BT(act ? "tabItemNameActive" : "tabItemName")) { wordWrap = false, clipping = TextClipping.Clip };
            GUILayout.Label(u.UserName, name, GUILayout.Width(179));
            GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Width(25));
            GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Width(25));
            var item = BT(act ? "tabItemActive" : "tabItem");
            GUILayout.Label(ScoreUser.FormatDamage(u.Damage), item, GUILayout.Width(44));
            GUILayout.Label(u.Kill.ToString(), item, GUILayout.Width(46));
            GUILayout.Label(u.Death.ToString(), item, GUILayout.Width(46));
            GUILayout.Label(u.Ping.ToString(), BT("tabItemPing"), GUILayout.Width(38));
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    // ---------------- Esc menu (DrawMenu) ----------------
    static GUIStyle redBtn, blueBtn;

    public static GUIStyle TeamColorButtonPublic(int team) => TeamColorButton(team);

    /// <summary>NotificationWindow.GetTeamColorButtonStyle: the green button re-tinted red / blue.</summary>
    static GUIStyle TeamColorButton(int team)
    {
        if (team != 1 && team != 2) return Skins.Btn("green");
        if (team == 1 && redBtn != null) return redBtn;
        if (team == 2 && blueBtn != null) return blueBtn;
        var g = Skins.Btn("green");
        Color32 n = team == 1 ? new Color32(220, 45, 45, 255) : new Color32(45, 105, 220, 255);
        Color32 h = team == 1 ? new Color32(235, 60, 60, 255) : new Color32(60, 125, 235, 255);
        Color32 a = team == 1 ? new Color32(185, 30, 30, 255) : new Color32(30, 80, 185, 255);
        var st = new GUIStyle(g);
        st.normal.background = Recolor(g.normal.background, n); st.hover.background = Recolor(g.hover.background, h);
        st.active.background = Recolor(g.active.background, a); st.focused.background = Recolor(g.focused.background, n);
        st.onNormal.background = Recolor(g.onNormal.background, n); st.onHover.background = Recolor(g.onHover.background, h);
        st.onActive.background = Recolor(g.onActive.background, a); st.onFocused.background = Recolor(g.onFocused.background, n);
        foreach (var s in new[] { st.normal, st.hover, st.active, st.focused, st.onNormal, st.onHover, st.onActive, st.onFocused }) s.textColor = Color.white;
        if (team == 1) redBtn = st; else blueBtn = st;
        return st;
    }

    /// <summary>NotificationWindow.CreateColoredButtonTexture: brightness of the green button × the team colour.</summary>
    static Texture2D Recolor(Texture2D src, Color32 c)
    {
        if (src == null) return null;
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt); RenderTexture.active = rt;
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = t.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            float k = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)) / 255f;
            px[i] = new Color32((byte)Mathf.RoundToInt(c.r * k), (byte)Mathf.RoundToInt(c.g * k), (byte)Mathf.RoundToInt(c.b * k), px[i].a);
        }
        t.SetPixels32(px); t.Apply();
        t.wrapMode = src.wrapMode; t.filterMode = src.filterMode; t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    static GUIStyle timerText, timerShadow;

    void DrawMenu(float W, float H)
    {
        bool was = GUI.enabled;
        if (options.Open) GUI.enabled = false;
        // TryGetMenuTeamSwitchState: team modes, alive, playing
        bool teamSwitch = (Room.Mode == GameMode.TEAM_DEATHMATCH || Room.Mode == GameMode.CAPTURE_THE_FLAG || Room.Mode == GameMode.CONTROL_POINTS || Room.Mode == GameMode.ESCORT)
                          && PlayerState == PlayerStateKind.Play && (Flow.LocalTeam == 1 || Flow.LocalTeam == 2);
        short target = (short)(Flow.LocalTeam == 1 ? 2 : 1);
        int cooldown = Mathf.Max(0, Mathf.CeilToInt(teamSwitchUntil - Time.realtimeSinceStartup));
        float h = 248f + (teamSwitch ? 41f : 0f);
        Skins.BeginArea(new Rect((W - 226f) / 2f, (H - 194f) / 2f, 226f, h), Skins.Bg("window"));
        GUILayout.BeginHorizontal();
        GUILayout.Label(T("Menu"), Skins.Txt("popupTitle"), GUILayout.Height(34));
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        if (GUILayout.Button(T("Resume"), Skins.Btn("green"), GUILayout.Width(191), GUILayout.Height(37))) { MenuOpen = false; if (!ShowSelectTeam) HideCursor(); }
        GUILayout.Space(4);
        if (teamSwitch)
        {
            var st = TeamColorButton(target);
            var r = GUILayoutUtility.GetRect(GUIContent.none, st, GUILayout.Width(191), GUILayout.Height(37));
            bool free = Score.GetTeamCount(target) < Room.MaxPlayersTeam;
            if (cooldown > 0)
            {
                GUI.Label(r, GUIContent.none, st);
                string time = $"{cooldown / 60:00}:{cooldown % 60:00}";
                if (timerText == null)
                {
                    var font = Resources.Load<Font>("gui/fonts/tahoma") ?? Resources.Load<Font>("gui/fonts/tahomabd");
                    timerText = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, richText = true, padding = new RectOffset(3, 3, 0, 0) };
                    timerText.normal.textColor = Color.white;
                    timerShadow = new GUIStyle(timerText) { richText = false }; timerShadow.normal.textColor = new Color(0f, 0f, 0f, 0.68f);
                }
                var pr = new Rect(r.x + 5f, r.y, r.width - 10f, r.height);
                int fs = 16; timerText.fontSize = timerShadow.fontSize = fs;
                while (fs > 12 && timerText.CalcSize(new GUIContent("Смена через " + time)).x > pr.width) { fs--; timerText.fontSize = timerShadow.fontSize = fs; }
                bool e = GUI.enabled; GUI.enabled = true;
                GUI.Label(new Rect(pr.x + 1, pr.y + 1, pr.width, pr.height), "Смена через " + time, timerShadow);
                GUI.Label(pr, "<color=#F2F2F2FF>Смена через </color><color=#FFD24AFF>" + time + "</color>", timerText);
                GUI.enabled = e;
            }
            else
            {
                bool e = GUI.enabled; GUI.enabled = e && free;
                if (GUI.Button(r, "Сменить сторону", st) && free)
                {
                    // New Era: a spawn request into the other team — the fighter respawns there at once, no death, no countdown
                    MenuOpen = false; HideCursor();
                    Flow.LocalTeam = target;
                    if (Score.ContainsUser(GameFlow.LocalId)) Score[GameFlow.LocalId].Team = target;
                    lastSpawnRequestTime = -10f;
                    RequestRespawn();
                }
                GUI.enabled = e;
            }
            GUILayout.Space(4);
        }
        if (GUILayout.Button(T("Fullscreen"), Skins.Btn("cancel"), GUILayout.Width(191), GUILayout.Height(37))) { MenuOpen = false; HideCursor(); Screen.fullScreen = true; }
        GUILayout.Space(4);
        if (GUILayout.Button(T("Options"), Skins.Btn("cancel"), GUILayout.Width(191), GUILayout.Height(37))) options.Open = true;
        GUILayout.Space(12);
        if (GUILayout.Button(T("Exit to My HQ"), Skins.Btn("cancel"), GUILayout.Width(191), GUILayout.Height(37))) { MenuOpen = false; Flow.ExitToLobby(); }
        GUILayout.EndArea();
        GUI.enabled = was;
    }

    // ---------------- crosshair / weapon bar ----------------
    void WeaponBar(float W, float H)
    {
        var area = W < 1015 ? new Rect(W / 2 - 325, H - 155, 650, 56) : new Rect(W / 2 - 250, H - 60, 650, 56);
        KeyCode[] keys = { TRInput.Weapon1, TRInput.Weapon2, TRInput.Weapon3, TRInput.Weapon4, TRInput.Weapon5, TRInput.Weapon6, TRInput.Weapon7 };
        int[] widths = { 63, 40, 77, 105, 77, 95, 109 };
        GUILayout.BeginArea(area);
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        for (int n = 1; n <= 7; n++)
        {
            if (n > 1) GUILayout.Space(5);
            bool act = n == CurrentWeapon.GetIndex();
            GUILayout.BeginVertical();
            GUILayout.Label(GUIContent.none, Skins.S("BattleIcoWeapon", act ? $"ws{n}active" : $"ws{n}"), GUILayout.Width(widths[n - 1]), GUILayout.Height(36));
            GUILayout.Label(TRInput.ToDisplayString(keys[n - 1]), Skins.S("BattleIcoWeapon", act ? "wsTxtActive" : "wsTxt"), GUILayout.Width(widths[n - 1]));   // 啛.WeaponN.ToDisplayString()
            GUILayout.EndVertical();
        }
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
