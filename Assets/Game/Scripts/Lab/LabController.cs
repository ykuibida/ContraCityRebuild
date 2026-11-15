// Bots steps 2–4: the map lab. ContraCity.exe -lab [map] opens a map without a battle. The lab searches the whole map for jumps by
// itself (LabLinks; cached in LabData/<map>.links.json next to the game), greys out what cannot be reached from the spawns, and
// shows routes: left click where the bot starts, left click where it must get to — the bot (the real fighter model) finds the way
// with running and jumps and goes it. The owner watches (behind the bot or through its eyes) and marks the route real or not
// (Docs/bots/lab_marks/<map>.jsonl in the project).
// ContraCity.exe -labtest map1,map2,... runs the same automatically (log "[labtest] ...", screenshots uitest/lab_<map>_*.png).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LabDraw
{
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    /// <summary>A transparent unlit colour (legacy Particles/Alpha Blended: output = 2 × tint × vertex colour).</summary>
    public static Material Mat(Color c)
    {
        if (mats.TryGetValue(c, out var m) && m != null) return m;
        m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
        mats[c] = m; return m;
    }

    public static LineRenderer Line(string name, IList<Vector3> pts, Color c, float width, Transform parent)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(parent, false);
        lr.sharedMaterial = Mat(new Color(c.r, c.g, c.b, 1f)); lr.startColor = lr.endColor = Color.white;
        lr.startWidth = lr.endWidth = width; lr.numCapVertices = 2;
        lr.positionCount = pts.Count; lr.SetPositions(pts.ToArray());
        return lr;
    }

    /// <summary>Many coloured polylines in one mesh (thin GPU lines): the link arcs.</summary>
    public static GameObject Lines(string name, IEnumerable<(IList<Vector3> pts, Color c)> lines, Transform parent)
    {
        var v = new List<Vector3>(); var col = new List<Color>(); var idx = new List<int>();
        foreach (var (pts, c) in lines)
            for (int i = 0; i < pts.Count; i++) { if (i > 0) { idx.Add(v.Count - 1); idx.Add(v.Count); } v.Add(pts[i] + Vector3.up * 0.4f); col.Add(c); }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetColors(col); mesh.SetIndices(idx, MeshTopology.Lines, 0); mesh.RecalculateBounds();
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = Mat(Color.white);
        return go;
    }

    public static GameObject Primitive(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Color c, Transform parent)
    {
        var g = GameObject.CreatePrimitive(t); Object.Destroy(g.GetComponent<Collider>());
        g.name = name; g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = Mat(c);
        return g;
    }
}

public class LabController : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.FindIndex(args, a => a == "-lab" || a == "-labtest");
        if (i < 0) return;
        var go = new GameObject("Lab"); DontDestroyOnLoad(go);
        var lab = go.AddComponent<LabController>();
        lab.automated = args[i] == "-labtest";
        lab.maps = (i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "Bit_map").Split(',');
    }

    static void Log(string s) => Debug.Log("[labtest] " + s);

    static readonly Dictionary<string, string> RuMap = new Dictionary<string, string>
    {
        { "Bit_map", "Комиссариат" }, { "Arena_3lvl", "Ангар, задний двор" }, { "ShipConteyner", "Монетный двор" }, { "ArenaRing", "Форпост" },
        { "Angar", "Ангар" }, { "7000", "Хоздвор" }, { "Arena_well", "Перестройполис" }, { "Cache", "Промзона" }, { "Inferno", "Урбан" },
        { "LegoTurnament", "Рубеж" }, { "Sniper", "Враг у ворот" }, { "Sniper_night", "Ночь у ворот" }, { "TF3_Arena_map", "Складчина" }, { "Faccility", "Предприятие" },
    };

    bool automated; string[] maps; string map;
    LabWorld world; LabLinks links;
    Camera cam; Transform root, markerRoot, routeRoot; GameObject navObj, linksObj, nearObj, ghost, fighter;
    bool showMarkers = true, showNav = true, showLinks = false, ready, searching;
    string status = "загрузка…";
    float statsMultiplier = 1f; int gatesOpened, doorsOpened;

    // the route on screen
    Vector3? startPt, targetPt; LabLinks.Route route; LabSim.Result played; string routeText = "";
    float playT; float speed = 1f; int camMode = 1;   // 0 free, 1 behind the bot, 2 the bot's eyes
    string marksNote = "", snapNote = "";
    int allowedMask = ~0;   // bit per LabSim.Move: which moves the routes and the drawn jumps may use
    BodyWeapons fighterWeapons; WeaponType shownWeapon = WeaponType.NONE;

    // ---- walk yourself (G): the owner plays a normal fighter; the steps show where the lab is wrong and teach it ----
    GameObject walker; float walkNext; readonly List<(Vector3 p, bool grounded, int region, float t)> walkLog = new List<(Vector3, bool, int, float)>();
    int walkLearned; string walkNote = "";
    readonly List<Vector3> walkRun = new List<Vector3>(); LineRenderer walkLine; bool walkRunOk; Vector3 walkLast;

    // the owner's jumps in walk mode (15.11.2026: «дай мне в руки дробовик — покажу, как можно запрыгнуть»): every time in the air
    // from a take-off to a landing that holds 0.3 s, with what he did in the air. Only recorded (next to the walk file, with what the
    // lab knows about each): they show where the search misses ways, and the search is fixed for all maps — the lab does not learn
    // single jumps into its cache (the owner: keep it simple)
    class AirEp { public Vector3 takeoff, landing; public float t0, t1, speed0; public int shotgun, rockets, hops = 1; public bool bat, jumped; public LabSim.Move move; public string result = ""; }
    readonly List<AirEp> walkEps = new List<AirEp>(); AirEp curEp; float groundedSince = -1f, lastRocketT = -10f; Vector3 lastGround;
    int walkDamage;
    static WeaponType Held => ShotController.Instance != null && ShotController.Instance.CurrentWeapon != null ? ShotController.Instance.CurrentWeapon.Type : WeaponType.NONE;

    void OnWalkShot(WeaponType t)
    {
        if (!walker) return;
        if (t == WeaponType.SHOT_GUN && curEp != null) curEp.shotgun++;
        if (t == WeaponType.ROCKET_LAUNCHER) { if (curEp != null) curEp.rockets++; else lastRocketT = Time.time; }
    }

    void TrackAir()
    {
        var m = walker.GetComponent<CharacterMotor>(); var p = walker.transform.position;
        if (m.grounded)
        {
            if (curEp == null) { lastGround = p; return; }
            if (groundedSince < 0f) groundedSince = Time.time;
            if (Time.time - groundedSince < 0.3f) return;
            var e = curEp; curEp = null; groundedSince = -1f; lastGround = p;
            e.landing = p; e.t1 = Time.time;
            e.move = e.rockets > 0 ? (e.shotgun > 0 ? LabSim.Move.RocketShotgun : LabSim.Move.Rocket) : e.shotgun > 0 ? LabSim.Move.Shotgun
                : e.bat && e.jumped ? LabSim.Move.Bat : e.hops >= 2 && e.jumped ? LabSim.Move.Bunnyhop : e.jumped ? (e.speed0 > 8f ? LabSim.Move.RunJump : LabSim.Move.Jump) : LabSim.Move.Walk;
            if (LabSim.Horizontal(e.landing - e.takeoff) < 1.5f && Mathf.Abs(e.landing.y - e.takeoff.y) < 1f) return;   // up and down on the spot
            walkEps.Add(e);
            LabDraw.Line("OwnerJump", new List<Vector3> { e.takeoff + Vector3.up * 0.5f, (e.takeoff + e.landing) / 2f + Vector3.up * (2f + Mathf.Max(0f, e.landing.y - e.takeoff.y)), e.landing + Vector3.up * 0.5f }, LabSim.ColorOf(e.move), 0.6f, routeRoot);
            return;
        }
        if (curEp == null)
        {
            var v = m.movement.velocity; var hv = v; hv.y = 0f;
            curEp = new AirEp { takeoff = lastGround, t0 = Time.time, bat = Held == WeaponType.ONE_HANDED_COLD_ARMS, jumped = v.y > 2f, speed0 = hv.magnitude, rockets = Time.time - lastRocketT < 0.6f ? 1 : 0 };
        }
        else if (groundedSince >= 0f) { curEp.hops++; groundedSince = -1f; if (m.movement.velocity.y > 2f) curEp.jumped = true; }
    }

    /// <summary>After the walk: the owner's jumps next to the walk file, each with what the lab knows (the bot's route there and its
    /// time, or why none), and the places he stood that the grid has no cell for (what the grid saw there).</summary>
    void WriteJumps(string walkFile)
    {
        int unknown = 0;
        foreach (var e in walkEps)
        {
            int ra = world.RegionAt(e.takeoff, 2f), rb = world.RegionAt(e.landing, 2f);
            if (ra < 0 || rb < 0) { e.result = ra < 0 ? "отрыв не на сетке" : "приземление не на сетке"; unknown++; continue; }
            var r = links.reachable.Contains(rb) ? links.FindRoute(e.takeoff, e.landing, allowedMask) : null;
            if (r == null) { e.result = $"лаборатория туда пути не знает (участок {rb} {(links.reachable.Contains(rb) ? "достижим" : "серый")})"; unknown++; }
            else if (r.time > (e.t1 - e.t0) + 2f) { e.result = $"у бота путь дольше: {r.Describe()}"; unknown++; }
            else e.result = $"бот умеет: {r.Describe()}";
        }
        walkNote += $"\nТвоих прыжков: {walkEps.Count}, из них лаборатория не знала {unknown}. Записал, разберу.";
        // the episodes and the places he stood that the grid does not have, next to the walk file
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {map}: the owner's jumps ({walkEps.Count}), damage taken {walkDamage}");
        foreach (var e in walkEps) sb.AppendLine($"{e.move} {e.takeoff} -> {e.landing} air {e.t1 - e.t0:F2} s, shotgun {e.shotgun}, rockets {e.rockets}, hops {e.hops}, bat {e.bat}, run {e.speed0:F1}: {e.result}");
        sb.AppendLine("# grounded where the grid has no cell (what the grid saw there)");
        var shown = new List<Vector3>();
        foreach (var w in walkLog.Where(x => x.grounded && x.region < 0))
        {
            if (shown.Any(q => Vector3.Distance(q, w.p) < 3f) || shown.Count >= 15) continue;
            shown.Add(w.p);
            sb.AppendLine($"{w.p}:{world.WhyApart(w.p + Vector3.left * 1.5f, w.p + Vector3.right * 1.5f)}");
        }
        if (walkFile != null) File.WriteAllText(Path.ChangeExtension(walkFile, ".jumps.txt"), sb.ToString());
    }

    void StartWalk()
    {
        if (walker) return;
        Vector3 feet; float yaw = cam.transform.eulerAngles.y;
        if (startPt != null) feet = startPt.Value;
        else
        {
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
            feet = Physics.Raycast(ray, out var h, 3000f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore) ? h.point : world.markers.First(m => LabWorld.IsSpawn(m.kind)).position;
        }
        ClearRoute();
        walker = PlayerRig.Create(feet + Vector3.up * 0.5f, yaw, cam);
        // as the battle spawns him (GameFlow.ApplyProfileToPlayer): the profile's speed, health, armor; weapons work without the battle
        // HUD; damage (own rockets, falls) is counted but he never dies
        walker.GetComponent<SoldierController>().ForceBattleMovementValues(statsMultiplier);
        var vit = walker.GetComponent<PlayerVitals>();
        try { var bs = BattleStats.Compute(Profile.Current); vit.MaxHealth = bs.MaxHealth; vit.ResetForSpawn(); vit.Armor = vit.MaxArmor = Mathf.Max(bs.MaxArmor, 0); vit.FallDamageMultiplier = bs.FallDamageMultiplier; } catch { }
        walkDamage = 0;
        vit.Damaged += d => { walkDamage += d; vit.Health = vit.MaxHealth; vit.Armor = vit.MaxArmor; };
        ShotController.LabControl = true; ShotController.LocalShotSent -= OnWalkShot; ShotController.LocalShotSent += OnWalkShot;
        walkEps.Clear(); curEp = null; groundedSince = -1f; lastRocketT = -10f; lastGround = feet;
        cam.enabled = false; var fly = cam.GetComponent<FlyCamera>(); if (fly) fly.enabled = false;
        var al = cam.GetComponent<AudioListener>(); if (al) al.enabled = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        walkLog.Clear(); walkRun.Clear(); walkLine = null; walkLearned = 0; walkNote = "";
    }

    void StopWalk()
    {
        if (!walker) return;
        var last = walker.transform.position;
        Destroy(walker); walker = null;
        ShotController.LabControl = false; ShotController.LocalShotSent -= OnWalkShot;
        cam.enabled = true; var fly = cam.GetComponent<FlyCamera>(); if (fly) fly.enabled = true;
        var al = cam.GetComponent<AudioListener>(); if (al) al.enabled = true;
        cam.transform.position = last + Vector3.up * 14f - cam.transform.forward * 12f; SyncFly();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        // learn: every grounded step from a reachable region into another region is a proven walk link
        int greyPoints = walkLog.Count(x => x.grounded && x.region >= 0 && !links.reachable.Contains(x.region));
        for (int i = 1; i < walkLog.Count; i++)
        {
            var a = walkLog[i - 1]; var b = walkLog[i];
            if (!a.grounded || !b.grounded || a.region < 0 || b.region < 0 || a.region == b.region) continue;
            if (links.AddObserved(a.p, b.p, b.t - a.t)) walkLearned++;
        }
        // regions reachable through the newly learned links in turn (the owner walked on through them)
        bool grew = true; while (grew) { grew = false; foreach (var l in links.links.Where(l => l.observed)) if (links.reachable.Contains(l.fromRegion) && links.reachable.Add(l.toRegion)) { grew = true; walkLearned++; } }
        if (walkLearned > 0) { world.BuildMesh(links.reachable); }
        var file = SaveWalk(greyPoints);
        walkNote = $"Записано {walkLog.Count} шагов. По серому ты прошёл {greyPoints} шагов: это ошибки лаборатории. Новых доступных участков после твоего прохода: {walkLearned}.";
        WriteJumps(file);
    }

    void RecordWalk()
    {
        if (!walker || Time.time < walkNext) return;
        walkNext = Time.time + 0.1f;
        var m = walker.GetComponent<CharacterMotor>(); var p = walker.transform.position;
        int r = m.grounded ? world.RegionAt(p, 2f) : -1;
        walkLog.Add((p, m.grounded, r, Time.time));
        if (!m.grounded) return;
        bool ok = r >= 0 && links.reachable.Contains(r);
        // one line per stretch of the same verdict; a new stretch on a change or a gap (a jump, a fall)
        if (walkLine == null || ok != walkRunOk || Vector3.Distance(p, walkLast) > 4f) { walkRun.Clear(); if (walkLine != null && walkRun.Count == 0 && ok == walkRunOk) { } walkLine = null; walkRunOk = ok; }
        walkRun.Add(p + Vector3.up * 0.3f); walkLast = p;
        if (walkLine) Destroy(walkLine.gameObject);
        if (walkRun.Count > 1) walkLine = LabDraw.Line(ok ? "WalkGood" : "WalkBad", walkRun, ok ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.2f, 0.2f), ok ? 0.5f : 0.7f, routeRoot);
    }

    string SaveWalk(int greyPoints)
    {
        var projectDocs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs", "bots"));
        var dir = Directory.Exists(projectDocs) && !automated ? Path.Combine(projectDocs, "lab_walks") : Path.Combine(Path.GetDirectoryName(Application.dataPath), "LabData", "walks");
        Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {map} {System.DateTime.Now:yyyy-MM-dd HH:mm}: {walkLog.Count} steps, {greyPoints} grounded steps on grey, learned {walkLearned}");
        sb.AppendLine("t,x,y,z,grounded,region,reachable");
        foreach (var s in walkLog) sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2},{4},{5},{6}", s.t, s.p.x, s.p.y, s.p.z, s.grounded ? 1 : 0, s.region, s.region >= 0 && links.reachable.Contains(s.region) ? 1 : 0));
        var file = Path.Combine(dir, $"{map}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(file, sb.ToString());
        return file;
    }

    IEnumerator Start()
    {
        while (UIRoot.Instance == null || GameFlow.Instance == null) yield return null;
        yield return new WaitForSeconds(0.5f);
        { var keep = new GameObject("tmp"); Dresser.CreateFighter(keep.transform); Destroy(keep); }   // keep the fighter template for the map scene
        ShotController.Template();   // and the weapon container of the lobby scene (DontDestroyOnLoad): without it the walker's 7 slots are empty
        var L = UIRoot.Instance.Lobby;
        if (L != null && L.Preview) L.Preview.Active = false;
        UIRoot.Instance.enabled = false;
        foreach (var m in maps)
        {
            map = m;
            if (!GameFlow.MapAvailable(map)) { status = "нет карты " + map; Log(status); continue; }
            ready = false;
            SceneManager.LoadScene(map + "_Viewer");
            yield return null; yield return null;
            Setup();
            if (!links.TryLoad()) { searching = true; yield return links.Search(automated ? 400f : 25f); searching = false; }
            AfterSearch();
            if (!automated) yield break;
            yield return AutoTest();
        }
        if (automated) Application.Quit();
    }

    void Setup()
    {
        root = new GameObject("LabObjects").transform;
        markerRoot = new GameObject("Markers").transform; markerRoot.SetParent(root, false);
        routeRoot = new GameObject("Route").transform; routeRoot.SetParent(root, false);
        cam = FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.gameObject.scene.name == map + "_Viewer").OrderByDescending(c => c.GetComponent<FlyCamera>() != null).FirstOrDefault();
        if (cam == null) cam = new GameObject("LabCamera").AddComponent<Camera>();
        cam.enabled = true; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
        var fly = cam.GetComponent<FlyCamera>(); if (fly == null) fly = cam.gameObject.AddComponent<FlyCamera>();
        fly.enabled = true; fly.moveSpeed = 25f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        try { statsMultiplier = BattleStats.Compute(Profile.Current).SpeedMultiplier; } catch { statsMultiplier = 1f; }

        world = new LabWorld(); world.Scan(); gatesOpened = world.OpenGates(); doorsOpened = world.OpenDoors(); world.BakeNavMesh();
        foreach (var m in world.markers)
        {
            var c = LabWorld.ColorOf(m.kind);
            LabDraw.Primitive(PrimitiveType.Cylinder, "Pole_" + m.name, m.position + Vector3.up * 4f, new Vector3(0.35f, 4f, 0.35f), new Color(c.r, c.g, c.b, 0.8f), markerRoot);
            LabDraw.Primitive(PrimitiveType.Sphere, "Head_" + m.name, m.position + Vector3.up * 8.5f, Vector3.one * 1.6f, new Color(c.r, c.g, c.b, 0.95f), markerRoot);
        }
        navObj = new GameObject("WalkableSurface"); navObj.transform.SetParent(root, false);
        navObj.AddComponent<MeshFilter>().sharedMesh = world.navMesh;
        navObj.AddComponent<MeshRenderer>().sharedMaterial = LabDraw.Mat(new Color(1f, 1f, 1f, 0.3f));   // vertex colours = height

        ghost = LabSim.CreateGhost("LabBot", statsMultiplier);
        fighter = MakeFighter();
        links = new LabLinks(world, ghost, map);
        startPt = targetPt = null; route = null; played = null; routeText = "";
        Overview();
        ready = true;
    }

    void AfterSearch()
    {
        world.BuildMesh(links.reachable);
        if (linksObj) Destroy(linksObj);
        linksObj = LabDraw.Lines("Links", links.links.Select(l => ((IList<Vector3>)l.path, LabSim.ColorOf(l.move))), root);
        linksObj.SetActive(showLinks);
        status = links.state;
    }

    GameObject MakeFighter()
    {
        var holder = new GameObject("LabFighter");
        var f = Dresser.CreateFighter(holder.transform);
        if (f != null)
        {
            var outfit = new Dictionary<int, GameItem>();
            foreach (var kv in Profile.Current.EquippedWear) { var wi = ItemDB.Get(ItemKind.Wear, kv.Value); if (wi != null) outfit[kv.Key] = wi; }
            Dresser.Dress(f, outfit);
            var a = f.GetComponentInChildren<Animation>(); if (a && a["Idle"] != null) a.CrossFade("Idle");
            // the weapons, as the stage 5 body: the bot shows what it jumps with (shotgun, rocket launcher, bat, rifle when running)
            var ba = f.AddComponent<BodyAnimator>(); ba.Anim = a;
            fighterWeapons = f.AddComponent<BodyWeapons>(); fighterWeapons.Animator = ba;
            foreach (var kv in BattleWeapons.Build(Profile.Current)) fighterWeapons.InitWeapon(kv.Key, kv.Value);
            shownWeapon = WeaponType.NONE;
        }
        else LabDraw.Primitive(PrimitiveType.Capsule, "Body", Vector3.up * 3.65f, new Vector3(2.6f, 3.25f, 2.6f), new Color(0.6f, 1f, 0.6f, 0.6f), holder.transform);
        holder.SetActive(false);
        return holder;
    }

    void Overview()
    {
        var b = world.bounds; var c = new Vector3(b.center.x, world.navMinY, b.center.z);
        float size = Mathf.Max(b.size.x, b.size.z);
        cam.transform.position = c + new Vector3(0f, size * 0.55f, -size * 0.45f);
        cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position);
        SyncFly();
    }

    void SyncFly()
    {
        var fly = cam.GetComponent<FlyCamera>(); if (!fly) return;
        var e = cam.transform.eulerAngles;
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(FlyCamera).GetField("yaw", bf)?.SetValue(fly, e.y);
        typeof(FlyCamera).GetField("pitch", bf)?.SetValue(fly, e.x > 180f ? e.x - 360f : e.x);
    }

    // ---------------- picking start / target ----------------

    void Pick(Vector3 p)
    {
        string moved = "";
        if (world.RegionAt(p, 1.5f) < 0 && world.Snap(p, 6f, out var near)) { moved = $" (точка сдвинута на ближайшую поверхность, {Vector3.Distance(p, near):F1})"; p = near; }
        snapNote = moved;
        if (startPt == null || targetPt != null) { ClearRoute(); startPt = p; DrawFlags(); ShowNear(p); return; }
        targetPt = p; DrawFlags(); if (nearObj) Destroy(nearObj);
        MakeRoute();
    }

    void ClearRoute()
    {
        foreach (Transform t in routeRoot) Destroy(t.gameObject);
        if (nearObj) Destroy(nearObj);
        startPt = targetPt = null; route = null; played = null; routeText = ""; marksNote = "";
        if (fighter) fighter.SetActive(false);
    }

    void DrawFlags()
    {
        foreach (Transform t in routeRoot) if (t.name.StartsWith("Flag")) Destroy(t.gameObject);
        if (startPt != null) { LabDraw.Primitive(PrimitiveType.Cylinder, "FlagA", startPt.Value + Vector3.up * 6f, new Vector3(0.5f, 6f, 0.5f), new Color(0.2f, 1f, 0.3f, 0.9f), routeRoot); LabDraw.Primitive(PrimitiveType.Cylinder, "FlagA2", startPt.Value + Vector3.up * 0.1f, new Vector3(3f, 0.05f, 3f), new Color(0.2f, 1f, 0.3f, 0.9f), routeRoot); }
        if (targetPt != null) { LabDraw.Primitive(PrimitiveType.Cylinder, "FlagB", targetPt.Value + Vector3.up * 6f, new Vector3(0.5f, 6f, 0.5f), new Color(1f, 0.2f, 0.2f, 0.9f), routeRoot); LabDraw.Primitive(PrimitiveType.Cylinder, "FlagB2", targetPt.Value + Vector3.up * 0.1f, new Vector3(3f, 0.05f, 3f), new Color(1f, 0.2f, 0.2f, 0.9f), routeRoot); }
    }

    /// <summary>The jumps that start near the picked point (within 15, same region).</summary>
    void ShowNear(Vector3 p)
    {
        if (nearObj) Destroy(nearObj);
        int r = world.RegionAt(p, 3f);
        var near = links.links.Where(l => l.fromRegion == r && Allowed(l.move) && LabSim.Horizontal(l.from - p) < 15f).ToList();
        nearObj = LabDraw.Lines("Near", near.Select(l => ((IList<Vector3>)l.path, LabSim.ColorOf(l.move))), routeRoot);
        routeText = r < 0 ? "Здесь нет проходимой поверхности: щёлкни на пол, ящик, крышу." : near.Count > 0 ? $"Отсюда рядом {near.Count} прыжков (цветные дуги). Теперь щёлкни, куда боту нужно попасть." : "Рядом прыжков нет. Теперь щёлкни, куда боту нужно попасть.";
    }

    void MakeRoute()
    {
        route = links.FindRoute(startPt.Value, targetPt.Value, allowedMask);
        if (route == null)
        {
            int ra = world.RegionAt(startPt.Value, 3f), rb = world.RegionAt(targetPt.Value, 3f);
            routeText = ra < 0 || rb < 0 ? "Точка не на проходимой поверхности." : !links.reachable.Contains(rb) ? "Туда не попасть ни бегом, ни найденными прыжками (серая зона)." : "Пути не найдено. Возможно, нужен приём, который сейчас выключен.";
            return;
        }
        played = links.Play(route, startPt.Value);
        bool reached = LabSim.Horizontal(played.landing - targetPt.Value) < 3f && Mathf.Abs(played.landing.y - targetPt.Value.y) < 3f;
        routeText = (reached ? "<b>Маршрут</b> " : "<b>Маршрут</b> (бот не дошёл до цели!) ") + route.Describe();
        LabDraw.Line("RoutePath", played.path.Where((x, i) => i % 6 == 0).Select(x => x + Vector3.up * 0.5f).ToList(), new Color(1f, 1f, 1f), 0.35f, routeRoot);
        foreach (var leg in route.legs.Where(l => l.link != null))
            LabDraw.Line("RouteJump", leg.link.path.Select(x => x + Vector3.up * 0.6f).ToList(), LabSim.ColorOf(leg.link.move), 0.6f, routeRoot);
        playT = 0f; if (fighter) fighter.SetActive(true); marksNote = ""; shownWeapon = WeaponType.NONE;
    }

    bool Allowed(LabSim.Move m) => (allowedMask & (1 << (int)m)) != 0;

    /// <summary>After the move filter changed: redraw the jumps and rebuild the route on screen.</summary>
    void FilterChanged()
    {
        if (linksObj) Destroy(linksObj);
        linksObj = LabDraw.Lines("Links", links.links.Where(l => Allowed(l.move)).Select(l => ((IList<Vector3>)l.path, LabSim.ColorOf(l.move))), root);
        linksObj.SetActive(showLinks);
        if (startPt != null && targetPt != null) { var a = startPt; var b = targetPt; foreach (Transform t in routeRoot) if (!t.name.StartsWith("Flag")) Destroy(t.gameObject); MakeRoute(); }
        else if (startPt != null) ShowNear(startPt.Value);
    }

    void SaveMark(bool real)
    {
        if (route == null) return;
        var projectDocs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs", "bots"));
        var dir = Directory.Exists(projectDocs) ? Path.Combine(projectDocs, "lab_marks") : Path.Combine(Path.GetDirectoryName(Application.dataPath), "LabData", "marks");
        Directory.CreateDirectory(dir);
        var line = JsonUtility.ToJson(new Mark
        {
            when = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), map = map, real = real, from = startPt.Value, to = targetPt.Value, route = route.Describe(),
            jumps = route.legs.Where(l => l.link != null).Select(l => $"{l.link.move} {l.link.from} -> {l.link.to} yaw {l.link.yaw:F0}").ToArray(),
        });
        File.AppendAllText(Path.Combine(dir, map + ".jsonl"), line + "\n");
        marksNote = real ? "Отмечено: реально." : "Отмечено: нереально. Разберусь, что не так.";
    }
    [System.Serializable] class Mark { public string when, map, route; public bool real; public Vector3 from, to; public string[] jumps; }

    // ---------------- every frame ----------------

    void Update()
    {
        if (!ready) return;
        if (markerRoot) markerRoot.gameObject.SetActive(showMarkers);
        if (navObj) navObj.SetActive(showNav);
        if (linksObj) linksObj.SetActive(showLinks);
        if (walker) { RecordWalk(); TrackAir(); }
        if (automated) return;
        bool overUi = new Rect(0, 0, 380, Screen.height).Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        if (Input.GetMouseButtonDown(0) && !overUi && !searching && !walker)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 3000f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore)) Pick(hit.point);
        }
        if (Input.GetKeyDown(KeyCode.G)) { if (walker) StopWalk(); else StartWalk(); }
        if (walker) return;
        if (Input.GetKeyDown(KeyCode.Escape)) ClearRoute();
        AnimateFighter(Time.deltaTime * speed);
    }

    void LateUpdate()
    {
        if (played == null || camMode == 0 || automated) return;
        int i = Mathf.Clamp(Mathf.FloorToInt(playT / played.dt), 0, played.path.Count - 1);
        var p = played.path[i]; var v = played.views[i];
        if (camMode == 2) { cam.transform.position = LabSim.Eye(p, v.x); cam.transform.rotation = Quaternion.Euler(v.y, v.x, 0f); }
        else
        {
            var back = Quaternion.Euler(0f, v.x, 0f) * new Vector3(0f, 0f, -16f);
            var want = p + back + Vector3.up * 11f;
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            cam.transform.rotation = Quaternion.LookRotation(p + Vector3.up * 4f - cam.transform.position);
        }
        SyncFly();
    }

    string lastClip;
    void AnimateFighter(float dt)
    {
        if (played == null || played.path.Count == 0 || fighter == null) return;
        playT += dt;
        float end = (played.path.Count - 1) * played.dt;
        if (playT > end + 1.5f) playT = 0f;   // loop with a short pause at the end
        int i = Mathf.Clamp(Mathf.FloorToInt(playT / played.dt), 0, played.path.Count - 1);
        var p = played.path[i]; var v = played.views[i];
        var vel = i > 0 ? (played.path[i] - played.path[i - 1]) / played.dt : Vector3.zero; vel.y = 0f;
        fighter.transform.position = p;
        if (fighterWeapons != null && played.weapons.Count > i && played.weapons[i] != shownWeapon && fighter.activeInHierarchy)
        {
            for (int sl = 1; sl <= BodyWeapons.WeaponNum; sl++)
            {
                var w = fighterWeapons.GetWeapon(sl);
                if (w != null && w.Type == played.weapons[i]) { fighterWeapons.OnChangeWeapon(sl); break; }
            }
            shownWeapon = played.weapons[i];
        }
        fighter.transform.rotation = Quaternion.Euler(0f, v.x, 0f);   // the body faces where the bot looks (backwards runs for shotgun jumps)
        var anim = fighter.GetComponentInChildren<Animation>();
        if (!anim) return;
        string clip = played.air[i] ? "Jump" : vel.magnitude > 2f ? (Vector3.Dot(vel, fighter.transform.forward) < -1f ? "RunBackwards" : "Run2") : "Idle";
        if (anim[clip] == null) clip = played.air[i] ? "Jump" : "Idle";
        if (clip != lastClip && anim[clip] != null) { anim.CrossFade(clip, 0.15f); lastClip = clip; }
        foreach (AnimationState s in anim) s.speed = speed;
    }

    // ---------------- panel ----------------
    GUIStyle box, label, small, big;
    void OnGUI()
    {
        if (automated) return;
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            big = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
        }
        GUILayout.BeginArea(new Rect(8, 8, 364, Screen.height - 16), box);
        GUILayout.Label($"<b>Лаборатория</b> — {(RuMap.TryGetValue(map ?? "", out var ru) ? ru : map)}", big);
        if (searching) { GUILayout.Label($"Ищу прыжки по всей карте: {links.progress * 100f:F0}%\n{links.state}\n\nОдин раз для карты, дальше берётся из кеша.", label); GUILayout.EndArea(); return; }
        if (!ready) { GUILayout.Label(status, label); GUILayout.EndArea(); return; }
        if (walker)
        {
            GUILayout.Label("<b>Ты идёшь сам</b> (обычный боец: WASD, мышь, пробел; оружие 1–7 или колесо, стрельба — левая кнопка; не умираешь).\nЗелёный след — лаборатория знала, что сюда можно. <color=#ff5555><b>Красный</b></color> — она считала, что нельзя. Дуги — твои прыжки.\n<b>G</b> — закончить и сохранить.", label);
            var cw = ShotController.Instance != null ? ShotController.Instance.CurrentWeapon : null;
            GUILayout.Label($"В руках: <b>{(cw != null ? cw.SystemName : "—")}</b>\nПрыжков записано: {walkEps.Count}" + (walkEps.Count > 0 ? $" (последний: {LabSim.RuName(walkEps[walkEps.Count - 1].move).ToLowerInvariant()})" : "") + (walkDamage > 0 ? $"\nУрон по тебе (не считается): {walkDamage}" : ""), label);
            GUILayout.EndArea();
            GUI.Label(new Rect(Screen.width / 2f - 10f, Screen.height / 2f - 12f, 20f, 24f), "<b>+</b>", big);
            return;
        }
        if (GUILayout.Button("Пройтись самому (G)", GUILayout.Height(26))) StartWalk();
        if (walkNote != "") GUILayout.Label(walkNote, small);
        GUILayout.Label("<b>1.</b> Щёлкни левой кнопкой, <b>откуда</b> бот стартует.\n<b>2.</b> Щёлкни, <b>куда</b> ему нужно попасть.\nБот сам найдёт путь с прыжками и пройдёт его.", label);
        GUILayout.Space(4);
        if (routeText != "") GUILayout.Label(routeText + snapNote, small);
        if (played != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Скорость:", small, GUILayout.Width(70));
            foreach (var s in new[] { 1f, 0.5f, 0.25f }) if (GUILayout.Toggle(Mathf.Approximately(speed, s), s == 1f ? "1×" : s == 0.5f ? "½×" : "¼×", "Button")) speed = s;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Камера:", small, GUILayout.Width(70));
            string[] cams = { "свободная", "за ботом", "глазами" };
            for (int i = 0; i < 3; i++) if (GUILayout.Toggle(camMode == i, cams[i], "Button")) camMode = i;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("С начала")) playT = 0f;
            GUILayout.Space(4);
            GUILayout.Label("Так бывает в игре?", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✓ Реально", GUILayout.Height(28))) SaveMark(true);
            if (GUILayout.Button("✗ Нереально", GUILayout.Height(28))) SaveMark(false);
            GUILayout.EndHorizontal();
            if (marksNote != "") GUILayout.Label(marksNote, small);
        }
        if (startPt != null && GUILayout.Button("Сбросить (Esc)")) ClearRoute();
        GUILayout.FlexibleSpace();
        GUILayout.Label("<b>Бот может использовать</b>", small);
        {
            var moves = (LabSim.Move[])System.Enum.GetValues(typeof(LabSim.Move)); int before = allowedMask;
            for (int i = 0; i < moves.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, moves.Length); j++)
                {
                    var m = moves[j]; bool on = Allowed(m);
                    bool now = GUILayout.Toggle(on, $"<color=#{ColorUtility.ToHtmlStringRGB(LabSim.ColorOf(m))}>■</color> {LabSim.RuName(m).ToLowerInvariant()}", new GUIStyle(GUI.skin.toggle) { richText = true, fontSize = 12 }, GUILayout.Width(170));
                    if (now != on) allowedMask ^= 1 << (int)m;
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Без ракет")) allowedMask &= ~((1 << (int)LabSim.Move.Rocket) | (1 << (int)LabSim.Move.RocketShotgun));
            if (allowedMask != before) FilterChanged();
        }
        GUILayout.Label("<b>Показывать</b>", small);
        GUILayout.BeginHorizontal();
        showNav = GUILayout.Toggle(showNav, "поверхность"); showLinks = GUILayout.Toggle(showLinks, "все прыжки"); showMarkers = GUILayout.Toggle(showMarkers, "метки");
        GUILayout.EndHorizontal();
        GUILayout.Label("Поверхность: голубая — низ, малиновая — верх, серая — туда не попасть.", small);
        GUILayout.Label($"<color=#aaaaaa>{status}</color>", small);
        GUILayout.Label("<color=#aaaaaa>Камера: правая кнопка + WASD, Q / E, Shift — быстрее.</color>", small);
        GUILayout.EndArea();
    }

    static string Legend()
    {
        string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        return string.Join("  ", ((LabSim.Move[])System.Enum.GetValues(typeof(LabSim.Move))).Select(m => $"<color=#{Hex(LabSim.ColorOf(m))}>■</color> {LabSim.RuName(m).ToLowerInvariant()}"))
            + "\nПоверхность: голубая — низ, малиновая — верх, серая — туда не попасть.";
    }

    // ---------------- automated run (-labtest) ----------------
    IEnumerator Shot(string name)
    {
        yield return null; yield return null; yield return new WaitForEndOfFrame();
        var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "uitest"); Directory.CreateDirectory(dir);
        var t = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG()); Destroy(t);
        Log("picture: uitest/" + name + ".png");
    }

    /// <summary>Why grey: for the biggest grey regions near the reachable floor, the closest pair of cells (grey, reachable), what lies
    /// between them (LabWorld.WhyApart) and a picture; the spawns that are not on the grid, what is under them.</summary>
    IEnumerator GreyDiagnostics()
    {
        var reach = new List<int>(); for (int c = 0; c < world.cellCount; c++) if (links.reachable.Contains(world.cellRegion[c])) reach.Add(c);
        float topReach = reach.Count > 0 ? reach.Max(c => world.cells[c].y) : 0f;
        var grey = Enumerable.Range(0, world.regionCount).Where(r => !links.reachable.Contains(r)).OrderByDescending(r => world.regionArea[r]).ToList();
        int shots = 0;
        foreach (int r in grey)
        {
            if (shots >= 8) break;
            var rc = Enumerable.Range(0, world.cellCount).Where(c => world.cellRegion[c] == r).ToList();
            if (rc.Count < 30 || rc.Average(c => world.cells[c].y) > topReach + 12f) continue;
            // the closest pair: horizontal distance plus the height difference
            int bg = -1, bq = -1; float bd = float.MaxValue;
            for (int i = 0; i < rc.Count; i += 2)
            {
                var g = world.cells[rc[i]];
                foreach (int q in reach) { var d = world.cells[q] - g; float s = Mathf.Abs(d.x) + Mathf.Abs(d.z); if (s > bd) continue; s += Mathf.Abs(d.y); if (s < bd) { bd = s; bg = rc[i]; bq = q; } }
            }
            if (bg < 0 || bd > 30f) continue;
            var a = world.cells[bg]; var b = world.cells[bq];
            Log($"{map}: WHY grey region {r} ({rc.Count} cells, y {rc.Min(c => world.cells[c].y):F1}..{rc.Max(c => world.cells[c].y):F1}): closest grey {a} to reachable {b} (region {world.cellRegion[bq]}), dy {b.y - a.y:+0.0;-0.0}{world.WhyApart(a, b)}");
            // a picture: from the reachable side, 35° down, the grey cell marked red, the reachable one green
            var p1 = LabDraw.Primitive(PrimitiveType.Cylinder, "DiagGrey", a + Vector3.up * 5f, new Vector3(0.5f, 5f, 0.5f), Color.red, routeRoot);
            var p2 = LabDraw.Primitive(PrimitiveType.Cylinder, "DiagReach", b + Vector3.up * 5f, new Vector3(0.5f, 5f, 0.5f), Color.green, routeRoot);
            var mid = (a + b) / 2f; var side = b - a; side.y = 0f; if (side.sqrMagnitude < 0.01f) side = Vector3.forward; side.Normalize();
            cam.orthographic = false;
            cam.transform.position = a - side * 7f + Vector3.up * 5f; cam.transform.rotation = Quaternion.LookRotation(b + Vector3.up * 2f - cam.transform.position);
            yield return Shot($"lab_{map}_why_{shots}_r{r}");
            Destroy(p1); Destroy(p2); shots++;
        }
        foreach (var m in world.markers.Where(m => LabWorld.IsSpawn(m.kind) && m.region < 0))
        {
            var all = Physics.RaycastAll(m.position + Vector3.up * 2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Take(6);
            Log($"{map}: spawn {m.kind} {m.name} at {m.position} is not on the grid; below: {string.Join("; ", all.Select(h => $"'{h.collider.name}' layer {h.collider.gameObject.layer} trigger {h.collider.isTrigger} y {h.point.y:F2} ny {h.normal.y:F2}"))}{world.WhyApart(m.position + Vector3.left * 1.5f, m.position + Vector3.right * 1.5f)}");
        }
    }

    /// <summary>Walk mode with weapons: the fighter put in the air fires the shotgun under himself — the shot must reach the lab and
    /// push him; then the lab repeats a real shotgun link from the cache as if the owner had made it.</summary>
    IEnumerator WalkTest()
    {
        var spawn = world.markers.First(m => LabWorld.IsSpawn(m.kind) && m.region >= 0);
        startPt = spawn.position; StartWalk(); startPt = null;
        yield return new WaitForSeconds(1f);
        var sc = ShotController.Instance;
        int slot = sc != null && sc.Weapons != null ? System.Array.FindIndex(sc.Weapons, w => w != null && w.Type == WeaponType.SHOT_GUN) : -1;
        Log($"{map}: walk test: weapons {(sc != null && sc.Weapons != null ? string.Join(", ", sc.Weapons.Select(w => w == null ? "-" : w.SystemName)) : "none")}, shotgun slot {slot + 1}");
        if (slot >= 0) sc.SetWeapon(slot + 1);
        yield return new WaitForSeconds(1.5f);
        var cc = walker.GetComponent<CharacterController>(); var m = walker.GetComponent<CharacterMotor>();
        cc.enabled = false; walker.transform.position += Vector3.up * 10f; cc.enabled = true;
        var look = walker.GetComponent<SoldierCamera>();
        yield return null; yield return null;
        if (look) look.SetPitch(80f);   // + = down (SoldierCamera: y -= mouse Y)
        var before = m.movement.velocity; ShotController.TestFire = true;
        yield return new WaitForSeconds(0.1f);
        ShotController.TestFire = false;
        var after = m.movement.velocity;
        Log($"{map}: walk test: held {Held}, airborne {!m.grounded}, episode shotgun shots {(curEp != null ? curEp.shotgun : -1)}, velocity {before} -> {after}");
        yield return new WaitForSeconds(3f);
        StopWalk();
        Log($"{map}: walk test: {walkNote.Replace("\n", " | ")}; episodes: {string.Join("; ", walkEps.Select(e => $"{e.move} shots {e.shotgun} {e.result}"))}");
        // the lab repeats a real shotgun link of the cache from its ends, as if the owner had made it
        var probe = links.links.FirstOrDefault(l => l.move == LabSim.Move.Shotgun && !l.taught && l.fromRegion != l.toRegion && l.rise > 4f);
        if (probe != null)
        {
            LabLinks.Link got = null; string why = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            yield return links.Reproduce(probe.from, probe.to, LabSim.Move.Shotgun, (l, w) => { got = l; why = w; });
            Log($"{map}: reproduce test: shotgun {probe.from} -> {probe.to} (+{probe.rise:F1}, {probe.time:F2} s, {probe.Params().Recipe()}): {(got != null ? $"{got.move} {got.Params().Recipe()} lands {got.to}, {got.time:F2} s" : "none")}; {why}; {sw.Elapsed.TotalSeconds:F1} s");
            if (got != null) links.Forget(got);
        }
    }

    /// <summary>Diagnostics (-labdiag): the owner's jumps the lab did not know or knew only a longer way for
    /// (Docs/bots/lab_walks/&lt;map&gt;_*.jumps.txt): what the grid has at his take-off and landing, and whether the simulation repeats
    /// the jump from his take-off spot (LabLinks.Reproduce; the link it finds is taken out again, the cache stays as it was).</summary>
    IEnumerator OwnerJumpsDiag()
    {
        var walkDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs", "bots", "lab_walks"));
        if (!Directory.Exists(walkDir)) yield break;
        var rx = new System.Text.RegularExpressions.Regex(@"^(\w+) \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\) -> \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\).*? run [\d,]+: (.*)$");
        float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        string Where(Vector3 p)
        {
            int c = world.CellAt(p, 2f); if (c < 0) return "no cell within 2";
            int r = world.cellRegion[c];
            return $"cell {world.cells[c]} region {r} ({world.regionArea[r]:F0} sq, {(links.reachable.Contains(r) ? "reachable" : "GREY")}, {world.boundary.Count(e => e.region == r)} edge spots, links from it {links.links.Count(l => l.fromRegion == r)})";
        }
        int n = 0;
        foreach (var f in Directory.GetFiles(walkDir, map + "_*.jumps.txt").OrderBy(x => x))
            foreach (var line in File.ReadAllLines(f))
            {
                var m = rx.Match(line); if (!m.Success || n >= 12) continue;
                string res = m.Groups[8].Value;
                if (res.StartsWith("бот умеет")) continue;
                if (!System.Enum.TryParse<LabSim.Move>(m.Groups[1].Value, out var mv)) continue;
                var a = new Vector3(F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value));
                var b = new Vector3(F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value));
                n++;
                Log($"{map}: owner jump {n} ({Path.GetFileName(f)}) {mv} {a} -> {b} (+{b.y - a.y:F1}, {LabSim.Horizontal(b - a):F1} across): {res}");
                Log($"{map}: owner jump {n} take-off: {Where(a)}; landing: {Where(b)}");
                var side = Vector3.Cross(Vector3.up, (b - a).normalized); if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                if (world.CellAt(a, 2f) < 0) Log($"{map}: owner jump {n} take-off grid:{world.WhyApart(a - side * 2f, a + side * 2f)}");
                if (world.CellAt(b, 2f) < 0) Log($"{map}: owner jump {n} landing grid:{world.WhyApart(b - side * 2f, b + side * 2f)}");
                LabLinks.Link got = null; string why = ""; var sw = System.Diagnostics.Stopwatch.StartNew();
                int rb = world.RegionAt(b, 2f); bool wasReachable = rb >= 0 && links.reachable.Contains(rb);
                yield return links.Reproduce(a, b, mv, (l, w) => { got = l; why = w; });
                Log($"{map}: owner jump {n} repeated: {(got != null ? $"{got.move} {got.Params().Recipe()} from {got.from} lands {got.to}, {got.time:F2} s" : "no")}; {why}; {sw.Elapsed.TotalSeconds:F0} s");
                if (got != null) { if (!wasReachable) links.reachable.Remove(rb); links.Forget(got); }
            }
    }

    /// <summary>Diagnostics (-labdiag): how quickly each way covers flat open ground (a floor far under the map, no walls), from a
    /// standstill: running with the rifle, running with the bat (×1.25), bunnyhops, bat hops, shotgun hops.</summary>
    void FlatSpeedDiag()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "DiagFlat";
        floor.transform.position = new Vector3(0f, -5000f, 0f); floor.transform.localScale = new Vector3(4000f, 2f, 4000f);
        Physics.SyncTransforms();
        var feet = new Vector3(0f, -4999f, -1500f);
        void One(string what, LabSim.Params p)
        {
            p.feet = feet; p.yaw = 0f; p.fps = LabSim.BotFps; p.maxTime = 20f;
            var r = LabSim.Run(ghost, p);
            float t = r.landingT > 0f ? r.landingT : r.time, d = LabSim.Horizontal((r.landed ? r.landing : ghost.transform.position) - feet);
            Log($"{map}: flat {what}: {d:F1} in {t:F2} s = {d / Mathf.Max(0.01f, t):F2}/s (landed {r.landed})");
        }
        foreach (float s in new[] { 2f, 5f, 9f })
        {
            One($"run rifle {s} s", new LabSim.Params { move = LabSim.Move.Walk, runUp = s });
            One($"run bat {s} s", new LabSim.Params { move = LabSim.Move.Walk, runUp = s, hold = WeaponType.ONE_HANDED_COLD_ARMS });
        }
        foreach (int h in new[] { 3, 6, 12 })
        {
            One($"bunnyhop rifle {h} hops", new LabSim.Params { move = LabSim.Move.Bunnyhop, runUp = 0.3f, hops = h });
            One($"bunnyhop bat {h} hops", new LabSim.Params { move = LabSim.Move.Bat, runUp = 0.3f, hops = h });
            One($"bunnyhop shotgun-in-hand {h} hops", new LabSim.Params { move = LabSim.Move.Bunnyhop, runUp = 0.3f, hops = h, hold = WeaponType.SHOT_GUN });
        }
        foreach (int sh in new[] { 1, 2, 3 })
            foreach (float pi in new[] { 30f, 50f, 70f })
                One($"shotgun {sh} shots {pi:F0}° back", new LabSim.Params { move = LabSim.Move.Shotgun, runUp = 0.3f, shots = sh, pitch = pi, backwards = true, fireDelay = 0.15f });
        Destroy(floor);
    }

    IEnumerator AutoTest()
    {
        if (System.Environment.GetCommandLineArgs().Contains("-labdiag"))
        {
            FlatSpeedDiag();
            yield return OwnerJumpsDiag();
            Log($"{map}: diag done");
            yield break;
        }
        Log($"=== {map}: walk grid {world.cellCount} cells of {LabWorld.Cell}×{LabWorld.Cell} ({world.columns} columns), {world.regionCount} regions, built in {world.bakeMs:F0} ms; heights {world.navMinY:F1}..{world.navMaxY:F1}");
        float reachArea = links.reachable.Sum(r => world.regionArea[r]);
        Log($"{map}: reachable from the spawns {links.reachable.Count} regions, {reachArea:F0} sq units ({100f * reachArea / Mathf.Max(1f, world.navArea):F0}% of the surface); spawns on the surface {world.markers.Count(m => LabWorld.IsSpawn(m.kind) && m.region >= 0)}/{world.markers.Count(m => LabWorld.IsSpawn(m.kind))}");
        Log($"{map}: search {links.state}; base gate colliders opened {gatesOpened}, automatic doors opened {doorsOpened}");
        // diagnostics: the biggest grey (unreachable) regions — where they are and how close the reachable floor is
        {
            var reachCells = new List<int>(); for (int c = 0; c < world.cellCount; c += 2) if (links.reachable.Contains(world.cellRegion[c])) reachCells.Add(c);
            var grey = Enumerable.Range(0, world.regionCount).Where(r => !links.reachable.Contains(r)).OrderByDescending(r => world.regionArea[r]).Take(12).ToList();
            float greyArea = Enumerable.Range(0, world.regionCount).Where(r => !links.reachable.Contains(r)).Sum(r => world.regionArea[r]);
            Log($"{map}: grey (unreachable) {greyArea:F0} sq units in {world.regionCount - links.reachable.Count} regions");
            foreach (int r in grey)
            {
                var rc = Enumerable.Range(0, world.cellCount).Where(c => world.cellRegion[c] == r).ToList();
                var mid = rc.Aggregate(Vector3.zero, (acc, c) => acc + world.cells[c]) / rc.Count;
                float nearH = 999f, nearV = 0f;
                foreach (int c in reachCells) { var d = world.cells[c] - mid; float h = new Vector2(d.x, d.z).magnitude; if (h < nearH) { nearH = h; nearV = d.y; } }
                float up = Physics.Raycast(mid + Vector3.up * 0.5f, Vector3.up, out var hu, 50f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore) ? hu.distance + 0.5f : 99f;
                Log($"{map}: grey region {r}: {rc.Count} cells, centre {mid}, y {rc.Min(c => world.cells[c].y):F1}..{rc.Max(c => world.cells[c].y):F1}, ceiling {up:F1}, nearest reachable floor {nearH:F1} away, dy {nearV:+0.0;-0.0}");
            }
            // the owner's recorded walks: how many of his grounded steps the grid has, and whether they are reachable
            var walkDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs", "bots", "lab_walks"));
            if (Directory.Exists(walkDir))
                foreach (var f in Directory.GetFiles(walkDir, map + "_*.csv"))
                {
                    int steps = 0, onGrid = 0, reach = 0;
                    foreach (var line in File.ReadAllLines(f).Skip(2))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"^(-?\d+[.,]\d\d),(-?\d+[.,]\d\d),(-?\d+[.,]\d\d),(-?\d+[.,]\d\d),(\d),");
                        if (!m.Success || m.Groups[5].Value != "1") continue;
                        float F(int g) => float.Parse(m.Groups[g].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                        var pt = new Vector3(F(2), F(3), F(4)); steps++;
                        int rr = world.RegionAt(pt, 2f); if (rr >= 0) { onGrid++; if (links.reachable.Contains(rr)) reach++; }
                    }
                    Log($"{map}: owner's walk {Path.GetFileName(f)}: {steps} grounded steps, on the walk grid {onGrid} ({100f * onGrid / Mathf.Max(1, steps):F0}%), reachable {reach} ({100f * reach / Mathf.Max(1, steps):F0}%)");
                }
        }
        yield return GreyDiagnostics();
        // overview: all links over the reachable surface
        showLinks = true; linksObj.SetActive(true);
        var b = world.bounds; cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(b.extents.x / cam.aspect, b.extents.z) * 0.8f;
        cam.transform.SetPositionAndRotation(new Vector3(b.center.x, world.navMaxY + 60f, b.center.z), Quaternion.Euler(90f, 0f, 0f));
        yield return Shot($"lab_{map}_links");
        showLinks = false; linksObj.SetActive(false); cam.orthographic = false;

        // routes from a spawn to the places the search found hardest to reach: the highest landings of different regions
        var spawn = world.markers.FirstOrDefault(m => LabWorld.IsSpawn(m.kind) && m.region >= 0 && links.reachable.Contains(m.region));
        var targets = links.links.Where(l => !l.shortcut).OrderByDescending(l => l.to.y).GroupBy(l => l.toRegion).Select(g => g.First()).Take(3).ToList();
        int n = 0;
        foreach (var t in targets)
        {
            ClearRoute(); startPt = spawn.position; targetPt = t.to; DrawFlags();
            var rsw = System.Diagnostics.Stopwatch.StartNew(); var probe = links.FindRoute(startPt.Value, targetPt.Value, allowedMask); float routeMs = (float)rsw.Elapsed.TotalMilliseconds;
            MakeRoute();
            Log($"{map}: route {n} search {routeMs:F0} ms");
            bool reached = played != null && LabSim.Horizontal(played.landing - t.to) < 3f && Mathf.Abs(played.landing.y - t.to.y) < 3f;
            Log($"{map}: route {n} from {spawn.kind} {spawn.position} to {t.to} (region {t.toRegion}, +{t.to.y - spawn.position.y:F1}): {(route == null ? "none: " + routeText : $"{route.legs.Count} legs, {route.legs.Count(l => l.link != null)} jumps, reached {reached}, {routeText}")}");
            if (played != null) foreach (var e in played.events.Where(e => e.what.StartsWith("MISS"))) Log($"{map}: route {n} {e.what}");
            if (route != null && !reached) for (int li = 0; li < route.legs.Count; li++) { var lg = route.legs[li]; Log($"{map}: route {n} leg {li}: {(lg.link != null ? $"{lg.link.move} {lg.link.from} -> {lg.link.to} yaw {lg.link.yaw:F0} observed {lg.link.observed}" : $"walk to {lg.to}")}"); }
            if (played != null && n == 0)
            {
                // from above over the whole route, then behind the bot in its first jump, then through its eyes
                var pts = played.path; var rb = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) rb.Encapsulate(p);
                cam.orthographic = true; cam.orthographicSize = Mathf.Max(rb.extents.x / cam.aspect, rb.extents.z) + 12f;
                cam.transform.SetPositionAndRotation(new Vector3(rb.center.x, rb.max.y + 8f, rb.center.z), Quaternion.Euler(90f, 0f, 0f));
                fighter.SetActive(true); playT = (played.path.Count - 1) * played.dt; AnimateFighter(0f);
                yield return Shot($"lab_{map}_route_top");
                cam.orthographic = false;
                int ja = played.air.IndexOf(true); if (ja < 0) ja = played.path.Count / 2;
                int k = Mathf.Min(ja + 30, played.path.Count - 1);
                playT = k * played.dt; AnimateFighter(0f);
                var p0 = played.path[k]; var v0 = played.views[k];
                cam.transform.position = p0 + Quaternion.Euler(0f, v0.x, 0f) * new Vector3(6f, 0f, -18f) + Vector3.up * 9f;
                cam.transform.rotation = Quaternion.LookRotation(p0 + Vector3.up * 3f - cam.transform.position);
                yield return Shot($"lab_{map}_route_jump");
                fighter.SetActive(false);
                cam.transform.position = LabSim.Eye(p0, v0.x); cam.transform.rotation = Quaternion.Euler(Mathf.Max(v0.y, 10f), v0.x, 0f);
                yield return Shot($"lab_{map}_route_eyes");
            }
            n++;
        }
        // the same first target without rockets (the owner's filter)
        if (targets.Count > 0)
        {
            allowedMask = ~((1 << (int)LabSim.Move.Rocket) | (1 << (int)LabSim.Move.RocketShotgun));
            ClearRoute(); startPt = spawn.position; targetPt = targets[0].to; DrawFlags(); MakeRoute();
            Log($"{map}: route 0 without rockets: {(route == null ? "none: " + routeText : $"{route.legs.Count(l => l.link != null)} jumps, {routeText}")}");
            allowedMask = ~0;
        }
        ClearRoute();
        Log($"{map}: done");
        yield return WalkTest();
    }
}
