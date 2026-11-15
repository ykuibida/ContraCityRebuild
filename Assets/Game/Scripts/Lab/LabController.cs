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
        SaveWalk(greyPoints);
        walkNote = $"Записано {walkLog.Count} шагов. По серому ты прошёл {greyPoints} шагов: это ошибки лаборатории. Новых доступных участков после твоего прохода: {walkLearned}.";
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

    void SaveWalk(int greyPoints)
    {
        var projectDocs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Docs", "bots"));
        var dir = Directory.Exists(projectDocs) ? Path.Combine(projectDocs, "lab_walks") : Path.Combine(Path.GetDirectoryName(Application.dataPath), "LabData", "walks");
        Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {map} {System.DateTime.Now:yyyy-MM-dd HH:mm}: {walkLog.Count} steps, {greyPoints} grounded steps on grey, learned {walkLearned}");
        sb.AppendLine("t,x,y,z,grounded,region,reachable");
        foreach (var s in walkLog) sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2},{4},{5},{6}", s.t, s.p.x, s.p.y, s.p.z, s.grounded ? 1 : 0, s.region, s.region >= 0 && links.reachable.Contains(s.region) ? 1 : 0));
        File.WriteAllText(Path.Combine(dir, $"{map}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv"), sb.ToString());
    }

    IEnumerator Start()
    {
        while (UIRoot.Instance == null || GameFlow.Instance == null) yield return null;
        yield return new WaitForSeconds(0.5f);
        { var keep = new GameObject("tmp"); Dresser.CreateFighter(keep.transform); Destroy(keep); }   // keep the fighter template for the map scene
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
        if (automated) return;
        bool overUi = new Rect(0, 0, 380, Screen.height).Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        if (Input.GetMouseButtonDown(0) && !overUi && !searching && !walker)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 3000f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore)) Pick(hit.point);
        }
        if (Input.GetKeyDown(KeyCode.G)) { if (walker) StopWalk(); else StartWalk(); }
        if (walker) { RecordWalk(); return; }
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
            GUILayout.Label("<b>Ты идёшь сам</b> (обычный боец: WASD, мышь, пробел).\nЗелёный след — лаборатория знала, что сюда можно. <color=#ff5555><b>Красный</b></color> — она считала, что нельзя.\n<b>G</b> — закончить и сохранить.", label);
            GUILayout.EndArea(); return;
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

    IEnumerator AutoTest()
    {
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
            ClearRoute(); startPt = spawn.position; targetPt = t.to; DrawFlags(); MakeRoute();
            bool reached = played != null && LabSim.Horizontal(played.landing - t.to) < 3f && Mathf.Abs(played.landing.y - t.to.y) < 3f;
            Log($"{map}: route {n} from {spawn.kind} {spawn.position} to {t.to} (region {t.toRegion}, +{t.to.y - spawn.position.y:F1}): {(route == null ? "none: " + routeText : $"{route.legs.Count} legs, {route.legs.Count(l => l.link != null)} jumps, reached {reached}, {routeText}")}");
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
    }
}
