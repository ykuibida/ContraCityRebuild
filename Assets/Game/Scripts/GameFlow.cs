// Game path: lobby (HQ / shop) -> battle on a map -> back to the lobby. Lives across scenes.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameFlow : MonoBehaviour
{
    public const string LobbyScene = "_Lobby";
    public static GameFlow Instance { get; private set; }

    public enum State { Lobby, Loading, Battle }
    public State Current { get; private set; } = State.Lobby;
    public string BattleMap { get; private set; } = "";
    public float BattleStartTime { get; set; }
    public const float MatchSeconds = 600f;          // New Era battle snapshot: match 10 minutes

    public GameObject Player { get; private set; }
    public int Kills, Deaths;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (SceneManager.GetActiveScene().name != LobbyScene || Instance != null) return;
        var go = new GameObject("GameFlow");
        go.AddComponent<GameFlow>();
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ItemDB.Load();
        GameOptions.Load();
        Application.targetFrameRate = 120;
        gameObject.AddComponent<UIRoot>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public static bool MapAvailable(string map) => Application.CanStreamedLevelBeLoaded(map + "_Viewer");

    // ---- battle room (local server): New Era RoomSettings / GameScore ----
    public RoomSettings Room { get; private set; } = new RoomSettings();
    public BattleScore Score { get; private set; } = new BattleScore();
    public const int LocalId = 1;
    public int LocalTeam = -1;                   // 喞.Team: -1 not chosen, 0 DM, 1 red, 2 blue
    Camera spectator;                            // the map's own camera, shown while not spawned

    /// <summary>«В БОЙ!» / «Играть» on a map: a default room (TDM, 14 players, 10 min).</summary>
    public void StartBattle(string map)
    {
        var r = new RoomSettings();
        var mi = MapList.Get(map);
        if (mi != null) r.Map = mi; else { r.MapId = map; r.MapName = map; }
        StartBattle(r);
    }

    /// <summary>CreateRoom / JoinRoom: LoadingMapPopup over the HQ while the map scene loads asynchronously.</summary>
    public void StartBattle(RoomSettings room)
    {
        if (Current != State.Lobby || !MapAvailable(room.MapId)) { Debug.LogWarning("[GameFlow] cannot start " + room.MapId); return; }
        Room = room;
        BattleMap = room.MapId;
        Current = State.Loading;
        Kills = Deaths = 0;
        LoadingMapPopup.Init(room.Map);
        ShotController.Template();   // keep the first-person weapon container of the lobby scene for the battle
        CharacterPrefabs.Keep();     // and the CharacterManager prefabs (grenades, sticky bombs, crossbow bolts)
        StartCoroutine(LoadMap(room.MapId + "_Viewer"));
    }

    System.Collections.IEnumerator LoadMap(string scene)
    {
        var op = SceneManager.LoadSceneAsync(scene);
        while (!op.isDone) { LoadingMapPopup.Progress(op.progress / 0.9f * 90f); yield return null; }
        LoadingMapPopup.Progress(100f);
    }

    // ---- per-round dossier stats: New Era 喡.TryCommitCurrentRound (喡.cs:416-530) ----
    float roundStart; bool roundCommitted;
    /// <summary>喡.CalculatePlayedMinutes: ceil(minutes since the round start), at least 1, at most TimeLimit + 1; kills / deaths of the
    /// round (ScoreUser), and with <paramref name="winLoss"/> a win or a loss (team: more points than the other team; DM: place 1).</summary>
    public void CommitRound(bool winLoss)
    {
        if (roundCommitted || Score == null || !Score.ContainsUser(LocalId)) return;
        roundCommitted = true;
        var me = Score[LocalId];
        var p = Profile.Current;
        float sec = Time.time - roundStart;
        int minutes = sec <= 0f ? 0 : Mathf.Min(Mathf.Max(1, Mathf.CeilToInt(sec / 60f)), Mathf.Clamp(Room.TimeLimit, 1, 60) + 1);
        int w = 0, l = 0;
        if (winLoss)
        {
            if (Room.Mode == GameMode.DEATHMATCH || Room.Mode == GameMode.TOWER_DEFENSE)
            {
                Score.Sorted(me.Team);
                if (me.Position == 1) w = 1; else if (me.Kill + me.Death + me.Point != 0) l = 1;   // ResolveWinLoss / HasAnyScoreActivity
            }
            else if (me.Team == 1 || me.Team == 2)
            {
                int mine = Score.GetTeamPoints(me.Team), other = Score.GetTeamPoints(me.Team == 1 ? 2 : 1);
                if (mine > other) w = 1; else if (mine < other) l = 1;
            }
        }
        string map = "map." + BattleMap + ".", mode = "mode." + (int)Room.Mode + ".";
        p.AddStat("time", minutes * 60); p.AddStat(map + "time", minutes * 60); p.AddStat(mode + "time", minutes * 60);
        p.AddStat("kill", me.Kill); p.AddStat("death", me.Death);
        p.AddStat("win", w); p.AddStat("lose", l);
        p.AddStat(map + "win", w); p.AddStat(map + "lose", l);
        p.AddStat(mode + "win", w); p.AddStat(mode + "lose", l);
        p.Save();
    }
    void BeginRound() { roundStart = Time.time; roundCommitted = false; }
    void OnApplicationQuit() { if (Current == State.Battle) CommitRound(false); }

    /// <summary>Esc menu «Выход»: GameHUD (:4330-4400) commits the round and returns to «Битвы» → room list.</summary>
    public void ExitToLobby()
    {
        CommitRound(false);
        if (Player) Destroy(Player);
        Player = null;
        Current = State.Lobby;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var lobby = UIRoot.Instance ? UIRoot.Instance.Lobby : null;
        if (lobby != null) { lobby.mainTab = 7; lobby.Battles.Connected = true; lobby.Battles.Select = BattlesUI.Menu.RoomList; }
        SceneManager.LoadScene(LobbyScene);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Current != State.Loading || !scene.name.EndsWith("_Viewer")) return;
        // The map viewer scene comes with a fly camera: it stays as the spectator view (no fly controls) until the spawn.
        // Only cameras of the map scene: the lobby preview cameras live in DontDestroyOnLoad and must not be taken.
        spectator = FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).OrderByDescending(c => c.GetComponent<FlyCamera>() != null).FirstOrDefault();
        if (spectator) { var fc = spectator.GetComponent<FlyCamera>(); if (fc) fc.enabled = false; spectator.enabled = true; }
        var spawns = PlayerRig.SpawnPoints();
        Score = new BattleScore();
        Score.Users[LocalId] = new ScoreUser { UserID = LocalId, UserName = Profile.Current.Name, Team = -1 };
        LocalTeam = -1;
        Current = State.Battle;
        BattleStartTime = Time.time;
        BeginRound();
        InitGates(Room.Mode != GameMode.DEATHMATCH && Room.Mode != GameMode.TOWER_DEFENSE);
        UIRoot.Instance.Battle.ResetForBattle();
        UIRoot.Instance.Battle.InitSpawn();
    }

    /// <summary>Restart the round after the result screen (scores and timer reset, everyone respawns).</summary>
    public void RestartRound()
    {
        Score.ResetRound();
        BattleStartTime = Time.time;
        BeginRound();
        if (Player) { Destroy(Player); Player = null; }
        if (spectator) { spectator.enabled = true; var al = spectator.GetComponent<AudioListener>(); if (al) al.enabled = true; }
    }

    /// <summary>Remove the fighter (team change) and show the map camera until the next spawn.</summary>
    public void Despawn()
    {
        if (Player) { Destroy(Player); Player = null; }
        if (spectator) { spectator.enabled = true; var al = spectator.GetComponent<AudioListener>(); if (al) al.enabled = true; }
        if (Score.ContainsUser(LocalId)) Score[LocalId].IsDead = true;
    }

    /// <summary>Spawn request → New Era MainNetworkController.CreateOfflineSpawnTransform (:985-1030) /
    /// CreateSceneAuthoredSpawnTransform (:1424-1446): the "RespawnPoint" children of Respawn_T&lt;team&gt; (DM: the neutral T0),
    /// sorted by position, taken from a shuffled bag (no immediate repeat of the same spot), snapped to the ground
    /// (GetNearbyGroundedRespawnPosition), yaw of the Respawn_T root; fallbacks own → T0 → T1 → T2 → (0, 5, 0).</summary>
    public void Spawn(int team)
    {
        SpawnTransform(team, out var pos, out var yaw);
        if (Player) Destroy(Player);
        Player = PlayerRig.Create(pos, yaw, spectator ? spectator : Camera.main);
        if (spectator) { spectator.enabled = false; var al = spectator.GetComponent<AudioListener>(); if (al) al.enabled = false; }
        ApplyProfileToPlayer();
        SetupGates(team);   // PlayerManager: SetupGates(LocalPlayer.Team) when the local player spawns (Penta :1324, NE :4163)
        var v = Player.GetComponent<PlayerVitals>();
        v.Died += OnLocalDied;
        // GameHUD.ShotMe on every local damage (fall :8497, own shot :9443-9494, periodic ticks :9605); cleared on (re)spawn
        v.Damaged += _ => { if (UIRoot.Instance != null && UIRoot.Instance.Battle != null) UIRoot.Instance.Battle.ShotMe(); };
        if (!Player.GetComponent<LocalImpacts>()) Player.AddComponent<LocalImpacts>();
        if (UIRoot.Instance != null && UIRoot.Instance.Battle != null) UIRoot.Instance.Battle.ClearTransientBattleEffects();
        if (Score.ContainsUser(LocalId)) { Score[LocalId].IsDead = false; Score[LocalId].Team = team; }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    static readonly List<int> spawnBag = new List<int>(); static string spawnBagKey = ""; static Vector3 lastSpawn; static bool hasLastSpawn;

    void SpawnTransform(int team, out Vector3 pos, out float yaw)
    {
        int t = Room.Mode == GameMode.DEATHMATCH ? 0 : team;
        var points = AuthoredPoints(t);
        if (points.Count == 0 && Room.Mode == GameMode.DEATHMATCH && IsZonaZRoom())
        {
            // CreateOfflineSpawnTransform (:987-1003): no safe neutral point on Zone-Z / Industrial → the legacy DM table (:125-176, :2374-2400)
            var table = IsZombi2Room() ? Zombi2SpawnPointsDM : ZonaZSpawnPointsDM;
            var e = table[System.Math.Abs(GetLocalSpawnSeed() % table.Length)];
            pos = new Vector3(e.x, e.y, e.z); yaw = e.w;
            Debug.LogWarning("[SPAWN] DM scene has no safe neutral T0 points. Falling back to legacy Zone-Z DM table: " + pos);
            return;
        }
        if (points.Count == 0 && Room.Mode != GameMode.DEATHMATCH)
            foreach (int f in new[] { 0, 1, 2 }) { if (f == t) continue; points = AuthoredPoints(f); if (points.Count > 0) break; }
        if (points.Count == 0) { pos = new Vector3(0f, 5f, 0f); yaw = 0f; Debug.LogWarning("[SPAWN] no respawn markers: (0, 5, 0)"); return; }
        points.Sort((a, b) =>
        {
            int c = a.position.x.CompareTo(b.position.x); if (c != 0) return c;
            c = a.position.z.CompareTo(b.position.z); if (c != 0) return c;
            c = a.position.y.CompareTo(b.position.y);
            return c != 0 ? c : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
        });
        // TakeNetworkDeathmatchSpawnBagIndex (:1784-1830): a shuffled bag; the first pick may not repeat the last spot
        string key = SceneManager.GetActiveScene().name + "|" + points.Count + "|TEAM=" + t;
        int idx = 0;
        if (points.Count > 1)
        {
            if (spawnBagKey != key || spawnBag.Count == 0)
            {
                spawnBagKey = key; spawnBag.Clear();
                for (int i = 0; i < points.Count; i++) spawnBag.Add(i);
                for (int n = spawnBag.Count - 1; n > 0; n--) { int r = Random.Range(0, n + 1); int tmp = spawnBag[n]; spawnBag[n] = spawnBag[r]; spawnBag[r] = tmp; }
                if (hasLastSpawn && (points[spawnBag[0]].position - lastSpawn).sqrMagnitude < 0.25f)
                { int r = Random.Range(1, spawnBag.Count); int tmp = spawnBag[0]; spawnBag[0] = spawnBag[r]; spawnBag[r] = tmp; }
            }
            idx = spawnBag[0]; spawnBag.RemoveAt(0);
        }
        var sp = points[idx];
        pos = GroundedRespawnPosition(sp.position);
        yaw = sp.eulerAngles.y;
        for (var p = sp; p != null; p = p.parent)                                  // GetAuthoredRespawnYaw (:1722-1734)
            if (p.name.StartsWith("Respawn_T", System.StringComparison.OrdinalIgnoreCase)) { yaw = p.eulerAngles.y; break; }
        lastSpawn = sp.position; hasLastSpawn = true;
    }

    /// <summary>CollectSafeSceneRespawnPoints (:1486-1572): Respawn_T&lt;team&gt; roots → their "RespawnPoint*" children, else the root;
    /// on Zone-Z / Industrial (IsZonaZRoom) only points with walkable floor under them, the root too when none of its children is safe;
    /// nothing found → standalone "RespawnPoint" markers outside any team root.</summary>
    List<Transform> AuthoredPoints(int team)
    {
        var list = new List<Transform>();
        bool zonaZ = IsZonaZRoom();
        string root = "Respawn_T" + team;
        int rejected = 0;
        foreach (var m in PlayerRig.SpawnPoints())
        {
            if (!m.name.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) continue;
            bool child = false, safeChild = false;
            foreach (var c in m.GetComponentsInChildren<Transform>(true))
            {
                if (c == m || !c.name.StartsWith("RespawnPoint", System.StringComparison.OrdinalIgnoreCase)) continue;
                child = true;
                if (!zonaZ || IsAuthoredRespawnPointSafe(c.position)) { if (!list.Contains(c)) list.Add(c); safeChild = true; }
                else rejected++;
            }
            if (!child || (!safeChild && zonaZ))
            {
                if (!zonaZ || IsAuthoredRespawnPointSafe(m.position)) { if (!list.Contains(m)) list.Add(m); }
                else rejected++;
            }
        }
        if (list.Count == 0)
            foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!string.Equals(tr.name, "RespawnPoint", System.StringComparison.OrdinalIgnoreCase)) continue;
                bool teamParent = false;
                for (var p = tr.parent; p != null; p = p.parent) if (p.name.StartsWith("Respawn_T", System.StringComparison.OrdinalIgnoreCase)) { teamParent = true; break; }
                if (teamParent) continue;
                if (!zonaZ || IsAuthoredRespawnPointSafe(tr.position)) { if (!list.Contains(tr)) list.Add(tr); }
                else rejected++;
            }
        if (rejected > 0) Debug.Log($"[SPAWN] team {team}: {list.Count} safe, {rejected} rejected (no walkable floor within 3.5 m below)");
        return list;
    }

    // ---------------- Zone-Z / Industrial (NE MainNetworkController.cs:2323-2400, tables :125-176) ----------------
    static bool IsZombi2Name(string a) => !string.IsNullOrEmpty(a) && (string.Equals(a, "Zombi_2", System.StringComparison.OrdinalIgnoreCase)
        || a.IndexOf("industrial", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("индастриал", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("индустриал", System.StringComparison.OrdinalIgnoreCase) >= 0);
    static bool IsZonaZName(string a) => !string.IsNullOrEmpty(a) && (string.Equals(a, "Zombi", System.StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Zombie", System.StringComparison.OrdinalIgnoreCase)
        || a.IndexOf("zombi", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("zone_z", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("zona", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("зона", System.StringComparison.OrdinalIgnoreCase) >= 0);
    bool IsZonaZRoom() => IsZonaZName(Room.MapId) || IsZonaZName(Room.MapName) || IsZombi2Name(Room.MapId) || IsZombi2Name(Room.MapName) || IsZonaZName(SceneManager.GetActiveScene().name) || IsZombi2Name(SceneManager.GetActiveScene().name);
    bool IsZombi2Room() => IsZombi2Name(Room.MapId) || IsZombi2Name(Room.MapName) || IsZombi2Name(SceneManager.GetActiveScene().name);

    static readonly Vector4[] ZonaZSpawnPointsDM =
    {
        new Vector4(65.35904f, -18.27801f, 131.0711f, 269.9999f), new Vector4(52.86271f, -18.36037f, 132.4308f, 269.9998f),
        new Vector4(78.50975f, -18.32238f, 130.8622f, 269.9998f), new Vector4(102.7064f, -18.43853f, 121.3816f, 89.99976f),
        new Vector4(112.8006f, -18.5006f, 121.9204f, 89.99989f), new Vector4(64.70058f, -17.62139f, 104.4714f, 89.99982f),
        new Vector4(50.44251f, -18.50912f, 104.6349f, 89.99999f), new Vector4(62.76516f, -18.43002f, 417.8969f, 269.9999f),
        new Vector4(-35.0833f, -18.45129f, 314.7058f, 180f), new Vector4(169.2893f, -18.28423f, 186.1459f, -0.00013f),
        new Vector4(82.40121f, -18.28423f, 119.7288f, -8E-05f), new Vector4(182.1297f, -18.39848f, 244.8759f, -0.0004f),
        new Vector4(107.3994f, -18.43002f, 124.3844f, 89.99968f), new Vector4(3.28523f, -18.28423f, 225.0545f, 134.9998f),
    };
    static readonly Vector4[] Zombi2SpawnPointsDM =
    {
        new Vector4(-70.03333f, -15.71543f, 277.352f, 89.99993f), new Vector4(65.90355f, -15.94499f, 214.2079f, 89.99993f),
        new Vector4(52.83718f, -64.67096f, 323.2963f, 0f), new Vector4(-59.7607f, -64.60795f, 338.4903f, 195f),
        new Vector4(34.60495f, -13.89301f, 337.5584f, 119.9998f), new Vector4(29.13521f, -64.39134f, 219.2933f, 0f),
        new Vector4(-51.10988f, -64.99174f, 216.9893f, 90.00013f), new Vector4(-69.93756f, -64.09576f, 244.3643f, 180f),
    };

    /// <summary>GetLocalSpawnSeed (:2470-2490): Environment.TickCount (+ the player / user ids, 0 offline).</summary>
    static int GetLocalSpawnSeed() => System.Environment.TickCount;

    /// <summary>IsAuthoredRespawnPointSafe (:1658-1697): a walkable floor between 0.2 above and 3.5 below the point.</summary>
    static bool IsAuthoredRespawnPointSafe(Vector3 a)
    {
        foreach (var h in Physics.RaycastAll(new Ray(a + Vector3.up * 2.5f, Vector3.down), 8.5f))
        {
            if (!IsAuthoredRespawnGroundHit(h)) continue;
            float y = h.point.y;
            if (y > a.y + 0.2f) continue;
            float d = a.y - y;
            if (d < -0.2f || d > 3.5f) continue;
            return true;
        }
        return false;
    }

    /// <summary>IsAuthoredRespawnGroundHit (:1699-1720): a solid, not too steep (normal.y ≥ 0.35) collider that is not a fighter and not
    /// under a Respawn… / POINTS… object.</summary>
    static bool IsAuthoredRespawnGroundHit(RaycastHit a)
    {
        if (a.collider == null || a.collider.isTrigger || a.normal.y < 0.35f) return false;
        for (var p = a.collider.transform; p != null; p = p.parent)
        {
            if (p.GetComponent<PlayerVitals>() != null) return false;
            string n = p.name ?? string.Empty;
            if (n.StartsWith("Respawn", System.StringComparison.OrdinalIgnoreCase) || n.StartsWith("POINTS", System.StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>GetNearbyGroundedRespawnPosition (:1613-1645): the highest solid hit of a ray from 2.5 above (8.5 long), between
    /// 0.2 above and 3.5 below the marker, + 0.05; else the marker itself.</summary>
    static Vector3 GroundedRespawnPosition(Vector3 a)
    {
        bool found = false; float best = -1000000f;
        foreach (var h in Physics.RaycastAll(new Ray(a + Vector3.up * 2.5f, Vector3.down), 8.5f))
        {
            if (!IsAuthoredRespawnGroundHit(h)) continue;
            float y = h.point.y;
            if (y > a.y + 0.2f) continue;
            float d = a.y - y;
            if (d < -0.2f || d > 3.5f) continue;
            if (!found || y > best) { found = true; best = y; }
        }
        if (found) a.y = best + 0.05f;
        return a;
    }

    // ---------------- base gates (PlayerManager.InitGates / SetupGates — Penta :667-705, :1073-1125; NE :948, :3856-3910) ----------------
    Transform finish4;
    Transform Finish4()
    {
        if (finish4) return finish4;
        foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name == "finish4" && tr.gameObject.scene == SceneManager.GetActiveScene()) { finish4 = tr; break; }
        return finish4;
    }
    System.Collections.Generic.IEnumerable<Transform> Gates(string prefix)
    {
        var f = Finish4();
        if (!f) yield break;
        for (int i = 1; ; i++) { var g = f.Find(prefix + i); if (!g) yield break; yield return g; }
    }

    /// <summary>InitGates(on): in DM / Tower Defense the gates and their triggers are switched off — everyone passes.</summary>
    void InitGates(bool on)
    {
        finish4 = null;
        if (on) return;
        foreach (var p in new[] { "GatesRed", "GatesBlue" })
            foreach (var g in Gates(p)) { var r = g.GetComponent<Renderer>(); if (r) r.enabled = false; g.gameObject.SetActive(false); }
        foreach (var p in new[] { "GatesRedTrigger", "GatesBlueTrigger" })
            foreach (var g in Gates(p)) g.gameObject.SetActive(false);
    }

    /// <summary>SetupGates(team): the own team's gates go to layer 12 (the player's layer 2 does not collide with it — passable),
    /// the other team's stay on layer 0 (solid); own triggers on layer 2, the others on 0. Not in DM / Tower Defense.</summary>
    void SetupGates(int team)
    {
        if (Room.Mode == GameMode.DEATHMATCH || Room.Mode == GameMode.TOWER_DEFENSE) return;
        foreach (var g in Gates("GatesRed")) g.gameObject.layer = team == 2 || team == 0 ? 0 : 12;
        foreach (var g in Gates("GatesBlue")) g.gameObject.layer = team == 1 || team == 0 ? 0 : 12;
        foreach (var g in Gates("GatesRedTrigger")) g.gameObject.layer = team == 2 || team == 0 ? 0 : 2;
        foreach (var g in Gates("GatesBlueTrigger")) g.gameObject.layer = team == 1 || team == 0 ? 0 : 2;
    }

    /// <summary>PlayerManager.TimeOver → KillMe(true): the local fighter dies without a frag and the map camera is shown
    /// (ActivateDeathCamera finds no target → fallback camera).</summary>
    public void KillLocalForTimeOver()
    {
        if (!Player) return;
        var v = Player.GetComponent<PlayerVitals>();
        if (v) { v.Died -= OnLocalDied; v.Kill(); }
        var body = Player.GetComponent<FighterBody>(); if (body) body.CancelTaunt();
        foreach (var b in new Behaviour[] { Player.GetComponent<SoldierController>(), Player.GetComponent<SoldierCamera>() }) if (b) b.enabled = false;
        var fps = Player.transform.Find("FPSCamera");
        if (fps) { var c = fps.GetComponent<Camera>(); if (c) c.enabled = false; var al = fps.GetComponent<AudioListener>(); if (al) al.enabled = false; }
        if (spectator) { spectator.enabled = true; var al = spectator.GetComponent<AudioListener>(); if (al) al.enabled = true; }
        if (Score.ContainsUser(LocalId)) Score[LocalId].IsDead = true;
    }

    void OnLocalDied(string killer, string weapon)
    {
        Deaths++;
        if (string.IsNullOrEmpty(killer)) { var p = Profile.Current; p.AddStat("suicide", 1); p.Save(); }   // dossier «Самоубийств» (deaths: per round)
        var me = Score.ContainsUser(LocalId) ? Score[LocalId] : null;
        bool suicide = string.IsNullOrEmpty(killer);
        Score.AddFrag(new Frag
        {
            KillerID = suicide ? LocalId : 0, KilledID = LocalId,
            KillerShowedName = suicide ? Profile.Current.Name : killer, KilledShowedName = Profile.Current.Name,
            KillerTeam = me != null ? me.Team : 0, KilledTeam = me != null ? me.Team : 0, WeaponSystemName = weapon,
        });
        // PlayerManager.KillMe(false) (:7382-7425): controllers off, motor reset, clothes shown, ActorAnimator.Defeat, TPS camera
        if (ShotController.Instance) ShotController.Instance.ResetWeapon();   // KillMe :7388
        if (Player) { var li = Player.GetComponent<LocalImpacts>(); if (li) li.Clear(); }
        if (Player)
        {
            foreach (var b in new Behaviour[] { Player.GetComponent<SoldierController>(), Player.GetComponent<SoldierCamera>() }) if (b) b.enabled = false;
            var m = Player.GetComponent<CharacterMotor>(); if (m) { m.SetExplosionForce(Vector3.zero, 0f); m.ResetMotion(); }
            var body = Player.GetComponent<FighterBody>(); if (body) body.Defeat(Vector3.zero);
        }
        UIRoot.Instance.Battle.BeginRespawnCountdown();
    }

    /// <summary>Clothes + complete sets + training hall + boosters → the fighter (BattleStats, New Era client formulas).</summary>
    public BattleStats Stats { get; private set; }
    void ApplyProfileToPlayer()
    {
        Stats = BattleStats.Compute(Profile.Current);
        var v = Player.GetComponent<PlayerVitals>();
        v.MaxHealth = Stats.MaxHealth;
        v.ResetForSpawn();
        v.Armor = Stats.MaxArmor;
        v.DamageTakenReduction = Stats.DamageTakenReduction;
        v.FallDamageMultiplier = Stats.FallDamageMultiplier;
        Player.GetComponent<SoldierController>().ForceBattleMovementValues(Stats.SpeedMultiplier);   // NE SpawnMe → RefreshBattleMovementValues
        Debug.Log("[BattleStats] " + Stats);
    }
}
