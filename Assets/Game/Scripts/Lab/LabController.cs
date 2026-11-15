// Bots step 2: the map lab. ContraCity.exe -lab [map] opens a map without a battle: a free camera, the map's markers, the walkable
// surface for the fighter, and single movement experiments on a simulated fighter (LabSim). ContraCity.exe -labtest [map] runs the
// same lab automatically (log "[labtest] ...", screenshots uitest/lab_*.png) and quits.
//   Camera: right mouse + WASD / Q E (Shift faster).   Ctrl + left click: put the bot there, facing where the camera looks.
//   Alt + left click: turn the bot towards that point.   Space: run the experiment.   C: clear paths.   F: camera to the bot.
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
        lab.map = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "Bit_map";
    }

    static void Log(string s) => Debug.Log("[labtest] " + s);

    bool automated; string map = "Bit_map";
    readonly LabWorld world = new LabWorld();
    Camera cam; Transform root, markerRoot, pathRoot; GameObject navObj, ghost;
    bool showMarkers = true, showLabels = true, showNav = true, showPaths = true, ready;
    readonly LabSim.Params prm = new LabSim.Params();
    readonly List<(LabSim.Result r, LabSim.Params p)> runs = new List<(LabSim.Result, LabSim.Params)>();
    string status = "загрузка…";
    float statsMultiplier = 1f;

    IEnumerator Start()
    {
        while (UIRoot.Instance == null || GameFlow.Instance == null) yield return null;
        yield return new WaitForSeconds(0.5f);
        var L = UIRoot.Instance.Lobby;
        if (L != null && L.Preview) L.Preview.Active = false;
        UIRoot.Instance.enabled = false;
        if (!GameFlow.MapAvailable(map)) { status = "нет карты " + map; Log(status); if (automated) Application.Quit(); yield break; }
        SceneManager.LoadScene(map + "_Viewer");
        yield return null; yield return null;
        Setup();
        if (automated) { yield return AutoTest(); Application.Quit(); }
    }

    void Setup()
    {
        root = new GameObject("LabObjects").transform;
        markerRoot = new GameObject("Markers").transform; markerRoot.SetParent(root, false);
        pathRoot = new GameObject("Paths").transform; pathRoot.SetParent(root, false);
        cam = FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.gameObject.scene.name == map + "_Viewer").OrderByDescending(c => c.GetComponent<FlyCamera>() != null).FirstOrDefault();
        if (cam == null) cam = new GameObject("LabCamera").AddComponent<Camera>();
        cam.enabled = true; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
        var fly = cam.GetComponent<FlyCamera>();
        if (fly == null) fly = cam.gameObject.AddComponent<FlyCamera>();
        fly.enabled = true; fly.moveSpeed = 25f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        try { statsMultiplier = BattleStats.Compute(Profile.Current).SpeedMultiplier; } catch { statsMultiplier = 1f; }

        world.Scan();
        world.BakeNavMesh();
        foreach (var m in world.markers)
        {
            var c = LabWorld.ColorOf(m.kind);
            LabDraw.Primitive(PrimitiveType.Cylinder, "Pole_" + m.name, m.position + Vector3.up * 4f, new Vector3(0.35f, 4f, 0.35f), new Color(c.r, c.g, c.b, 0.8f), markerRoot);
            LabDraw.Primitive(PrimitiveType.Sphere, "Head_" + m.name, m.position + Vector3.up * 8.5f, Vector3.one * 1.6f, new Color(c.r, c.g, c.b, 0.95f), markerRoot);
        }
        navObj = new GameObject("WalkableSurface");
        navObj.transform.SetParent(root, false);
        navObj.AddComponent<MeshFilter>().sharedMesh = world.navMesh;
        navObj.AddComponent<MeshRenderer>().sharedMaterial = LabDraw.Mat(new Color(1f, 1f, 1f, 0.3f));   // vertex colours = height

        ghost = LabSim.CreateGhost("LabBot", statsMultiplier, true);
        var first = world.markers.Where(m => m.onNavMesh && (m.kind == LabWorld.MarkerKind.SpawnT1 || m.kind == LabWorld.MarkerKind.SpawnT0)).Select(m => (Vector3?)m.position).FirstOrDefault();
        PlaceBot(first ?? world.bounds.center, 0f);
        FocusBot();
        ready = true;
        status = $"готово: проходимая поверхность {world.navArea:F0} кв. ед., {world.navTriangles} треугольников, {world.bakeMs:F0} мс";
    }

    void PlaceBot(Vector3 feet, float yaw)
    {
        if (Physics.Raycast(feet + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore)) feet = hit.point;
        prm.feet = feet; prm.yaw = yaw;
        ghost.GetComponent<CharacterMotor>().RestoreState(new CharacterMotor.State { position = feet, rotation = Quaternion.Euler(0f, yaw, 0f), grounded = true, groundNormal = Vector3.up, lastGroundNormal = Vector3.up, lastButtonDownTime = -100f });
    }

    void FocusBot()
    {
        var f = Quaternion.Euler(0f, prm.yaw, 0f) * Vector3.forward;
        cam.transform.position = prm.feet - f * 30f + Vector3.up * 22f;
        cam.transform.rotation = Quaternion.LookRotation(prm.feet + f * 15f - cam.transform.position);
        SyncFly();
    }

    void SyncFly()
    {
        var fly = cam.GetComponent<FlyCamera>(); if (!fly) return;
        // FlyCamera keeps its own yaw / pitch (set in Start): put them to the camera's new rotation
        var e = cam.transform.eulerAngles;
        var fYaw = typeof(FlyCamera).GetField("yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fPitch = typeof(FlyCamera).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fYaw?.SetValue(fly, e.y); fPitch?.SetValue(fly, e.x > 180f ? e.x - 360f : e.x);
    }

    LabSim.Result RunExperiment(LabSim.Params p, bool draw = true)
    {
        var r = LabSim.Run(ghost, p);
        runs.Add((r, p.Clone()));
        if (draw)
        {
            var c = LabSim.ColorOf(p.move);
            LabDraw.Line("Path_" + runs.Count, r.path.Select(x => x + Vector3.up * 0.4f).ToList(), c, 0.45f, pathRoot);
            if (r.landed) LabDraw.Primitive(PrimitiveType.Cylinder, "Landing_" + runs.Count, r.landing + Vector3.up * 0.1f, new Vector3(2.6f, 0.08f, 2.6f), new Color(c.r, c.g, c.b, 0.9f), pathRoot);
            foreach (var e in r.events.Where(e => e.what.StartsWith("дробовик: толчок") || e.what.StartsWith("взрыв")))
                LabDraw.Primitive(PrimitiveType.Sphere, "Hit_" + runs.Count, e.at, Vector3.one * 0.9f, new Color(1f, 0.2f, 0.1f, 0.9f), pathRoot);
        }
        // the bot stays where it was put
        ghost.GetComponent<CharacterMotor>().RestoreState(new CharacterMotor.State { position = p.feet, rotation = Quaternion.Euler(0f, p.yaw, 0f), grounded = true, groundNormal = Vector3.up, lastGroundNormal = Vector3.up, lastButtonDownTime = -100f });
        status = LabSim.RuName(p.move) + ": " + r.Summary();
        return r;
    }

    void ClearPaths() { foreach (Transform t in pathRoot) Destroy(t.gameObject); runs.Clear(); }

    void Update()
    {
        if (!ready || automated) return;
        if (markerRoot) markerRoot.gameObject.SetActive(showMarkers);
        if (navObj) navObj.SetActive(showNav);
        if (pathRoot) pathRoot.gameObject.SetActive(showPaths);
        bool overUi = new Rect(0, 0, 360, Screen.height).Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        if (Input.GetMouseButtonDown(0) && !overUi && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftAlt)))
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 3000f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore))
            {
                if (Input.GetKey(KeyCode.LeftControl)) { var f = cam.transform.forward; f.y = 0f; PlaceBot(hit.point, f.sqrMagnitude > 0.01f ? Quaternion.LookRotation(f).eulerAngles.y : prm.yaw); }
                else { var d = hit.point - prm.feet; d.y = 0f; if (d.sqrMagnitude > 0.01f) PlaceBot(prm.feet, Quaternion.LookRotation(d).eulerAngles.y); }
            }
        }
        if (Input.GetKeyDown(KeyCode.Space)) RunExperiment(prm);
        if (Input.GetKeyDown(KeyCode.C)) ClearPaths();
        if (Input.GetKeyDown(KeyCode.F)) FocusBot();
    }

    // ---------------- panel ----------------
    GUIStyle box, label, small;
    void OnGUI()
    {
        if (automated) return;
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        }
        GUILayout.BeginArea(new Rect(8, 8, 344, Screen.height - 16), box);
        GUILayout.Label($"<b>Лаборатория</b> — {map}", new GUIStyle(label) { richText = true, fontSize = 15 });
        GUILayout.Label(status, small);
        if (!ready) { GUILayout.EndArea(); return; }
        GUILayout.Label($"Метки: {world.markers.Count} (на проходимом {world.markers.Count(m => m.onNavMesh)}); скорость бойца ×{statsMultiplier:F2}", small);
        GUILayout.BeginHorizontal();
        showMarkers = GUILayout.Toggle(showMarkers, "метки"); showLabels = GUILayout.Toggle(showLabels, "подписи");
        showNav = GUILayout.Toggle(showNav, "проходимость"); showPaths = GUILayout.Toggle(showPaths, "пути");
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label("<b>Опыт</b>", new GUIStyle(label) { richText = true });
        var moves = (LabSim.Move[])System.Enum.GetValues(typeof(LabSim.Move));
        int sel = System.Array.IndexOf(moves, prm.move);
        sel = GUILayout.SelectionGrid(sel, moves.Select(LabSim.RuName).ToArray(), 2);
        prm.move = moves[sel];
        float Slider(string name, float v, float a, float b, string fmt) { GUILayout.Label($"{name}: {v.ToString(fmt)}", small); return GUILayout.HorizontalSlider(v, a, b); }
        prm.yaw = Mathf.Repeat(Slider("Направление, °", prm.yaw, 0f, 360f, "F0"), 360f);
        if (prm.move != LabSim.Move.Jump) prm.runUp = Slider("Разбег, с", prm.runUp, 0f, 2f, "F2");
        if (prm.move == LabSim.Move.Bunnyhop || prm.move == LabSim.Move.Bat) prm.hops = Mathf.RoundToInt(Slider("Прыжков", prm.hops, 1f, 10f, "F0"));
        if (prm.move == LabSim.Move.Shotgun || prm.move == LabSim.Move.Rocket)
        {
            prm.pitch = Slider("Наклон взгляда, ° (+ вниз)", prm.pitch, -30f, 89f, "F0");
            prm.fireDelay = Slider("Выстрел после отрыва, с", prm.fireDelay, 0f, 0.8f, "F2");
        }
        if (prm.move == LabSim.Move.Shotgun)
        {
            prm.shots = Mathf.RoundToInt(Slider("Выстрелов в воздухе", prm.shots, 1f, 3f, "F0"));
            prm.backwards = GUILayout.Toggle(prm.backwards, "спиной к цели, назад на S");
        }
        GUILayout.Label($"Кадров в секунду симуляции: {prm.fps:F0}", small);
        GUILayout.BeginHorizontal();
        foreach (var f in new[] { 30f, 60f, 144f, 250f }) if (GUILayout.Button(f.ToString("F0"))) prm.fps = f;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Пуск (Space)")) RunExperiment(prm);
        if (GUILayout.Button("Очистить (C)")) ClearPaths();
        if (GUILayout.Button("К боту (F)")) FocusBot();
        GUILayout.EndHorizontal();
        if (runs.Count > 0)
        {
            var (r, p) = runs[runs.Count - 1];
            GUILayout.Label("<b>Результат</b>", new GUIStyle(label) { richText = true });
            GUILayout.Label(LabSim.RuName(p.move) + ": " + r.Summary(), small);
            foreach (var e in r.events.Take(8)) GUILayout.Label($"{e.t:F2} с — {e.what}", small);
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("Камера: ПКМ + WASD / Q E, Shift быстрее\nCtrl + ЛКМ — поставить бота (смотрит туда же, куда камера)\nAlt + ЛКМ — повернуть бота к точке", small);
        GUILayout.EndArea();

        if (showLabels && showMarkers && cam)
            foreach (var m in world.markers)
            {
                var sp = cam.WorldToScreenPoint(m.position + Vector3.up * 10f);
                if (sp.z <= 0f || sp.z > 250f) continue;
                var c = LabWorld.ColorOf(m.kind);
                GUI.color = c; GUI.Label(new Rect(sp.x - 60, Screen.height - sp.y - 18, 160, 20), LabWorld.RuName(m.kind) + (m.onNavMesh ? "" : " (вне)"), small); GUI.color = Color.white;
            }
    }

    // ---------------- automated run (-labtest) ----------------
    IEnumerator Shot(string name)
    {
        yield return null; yield return null; yield return new WaitForEndOfFrame();
        var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "uitest"); Directory.CreateDirectory(dir);
        var t = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG()); Destroy(t);
        Log("picture: uitest/" + name + ".png");
    }

    /// <summary>Of 24 directions, the one with the longest free run for the fighter's capsule (so the test moves are not into a wall).</summary>
    static float OpenestYaw(Vector3 feet)
    {
        float best = 0f, bestYaw = 0f;
        for (int i = 0; i < 24; i++)
        {
            float yaw = i * 15f; var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float free = Physics.CapsuleCast(feet + Vector3.up * 2.5f, feet + Vector3.up * 5.5f, 1.2f, dir, out var h, 120f, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore) ? h.distance : 120f;
            if (free > best + 0.01f) { best = free; bestYaw = yaw; }
        }
        return bestYaw;
    }

    IEnumerator AutoTest()
    {
        Log($"map {map}: bounds {world.bounds.center} size {world.bounds.size}, colliders used {world.sources}");
        Log($"walkable surface: {world.navArea:F0} sq units, heights {world.navMinY:F1}..{world.navMaxY:F1}, {world.navTriangles} triangles, baked in {world.bakeMs:F0} ms (agent r {LabWorld.Agent.agentRadius} h {LabWorld.Agent.agentHeight} step {LabWorld.Agent.agentClimb} slope {LabWorld.Agent.agentSlope} voxel {LabWorld.Agent.voxelSize})");
        foreach (var g in world.markers.GroupBy(m => m.kind)) Log($"markers {g.Key}: {g.Count()}, on the walkable surface {g.Count(m => m.onNavMesh)}");
        Log($"weapons: shotgun {LabSim.WeaponName(WeaponType.SHOT_GUN)}, rocket {LabSim.WeaponName(WeaponType.ROCKET_LAUNCHER)}, melee {LabSim.WeaponName(WeaponType.ONE_HANDED_COLD_ARMS)}, stats speed ×{statsMultiplier:F2}");

        // overview from above
        var oc = cam.orthographic; var op = cam.transform.position; var orot = cam.transform.rotation; var os = cam.orthographicSize;
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(world.bounds.extents.x / cam.aspect, world.bounds.extents.z) * 0.75f;
        cam.transform.SetPositionAndRotation(new Vector3(world.bounds.center.x, world.bounds.max.y + 50f, world.bounds.center.z), Quaternion.Euler(90f, 0f, 0f));
        yield return Shot("lab_overview");

        // experiments from three spawns, towards the middle of the map
        var spawns = world.markers.Where(m => m.onNavMesh && (m.kind == LabWorld.MarkerKind.SpawnT1 || m.kind == LabWorld.MarkerKind.SpawnT2 || m.kind == LabWorld.MarkerKind.SpawnT0)).Take(3).ToList();
        int si = 0;
        foreach (var sp in spawns)
        {
            float yaw = OpenestYaw(sp.position);
            PlaceBot(sp.position, yaw);
            foreach (var mv in (LabSim.Move[])System.Enum.GetValues(typeof(LabSim.Move)))
            {
                var p = new LabSim.Params { move = mv, feet = prm.feet, yaw = yaw, fps = 60f };
                if (mv == LabSim.Move.Rocket) { p.pitch = 85f; p.runUp = 0.4f; p.fireDelay = 0.1f; }
                if (mv == LabSim.Move.Shotgun) { p.pitch = 50f; p.runUp = 0.3f; p.shots = 2; p.backwards = true; }
                var r = RunExperiment(p, si == 0);
                Log($"spawn {si} {sp.kind} {sp.position} yaw {yaw:F0}: {mv}: {(r.landed ? $"air {r.landingT - r.takeoffT:F2} s, distance {r.Distance:F1}, rise {r.Rise:F1}, peak +{r.maxRise:F1}, walkable {r.onWalkable}, fall damage {r.fallDamage}" : $"no landing in {r.time:F1} s, peak +{r.maxRise:F1}")}, events {string.Join(" | ", r.events.Select(e => $"{e.t:F2} {e.what}"))}");
            }
            // frame-rate sensitivity: the same running jump at 30 / 60 / 144 / 250 fps
            if (si == 0)
                foreach (var f in new[] { 30f, 60f, 144f, 250f })
                {
                    var r = LabSim.Run(ghost, new LabSim.Params { move = LabSim.Move.RunJump, feet = prm.feet, yaw = yaw, fps = f });
                    var b = LabSim.Run(ghost, new LabSim.Params { move = LabSim.Move.Bunnyhop, feet = prm.feet, yaw = yaw, fps = f, hops = 4 });
                    Log($"fps {f}: running jump distance {r.Distance:F2} air {r.landingT - r.takeoffT:F3} s peak +{r.maxRise:F2}; bunnyhop ×4 covered {Vector3.Distance(new Vector3(b.landing.x, 0, b.landing.z), new Vector3(prm.feet.x, 0, prm.feet.z)):F1} in {b.landingT:F2} s");
                }
            si++;
        }

        // the first spawn's experiments: from behind and above, then from straight above
        cam.orthographic = false;
        var s0 = spawns.Count > 0 ? spawns[0].position : world.bounds.center;
        var d0 = world.bounds.center - s0; d0.y = 0f; d0.Normalize();
        d0 = Quaternion.Euler(0f, OpenestYaw(s0), 0f) * Vector3.forward;
        PlaceBot(s0, OpenestYaw(s0));
        cam.transform.position = s0 - d0 * 35f + Vector3.up * 30f + Vector3.Cross(Vector3.up, d0) * 15f;
        cam.transform.rotation = Quaternion.LookRotation(s0 + d0 * 25f - cam.transform.position);
        yield return Shot("lab_moves");
        var pts = runs.Where((x, i) => i < 6).SelectMany(x => x.r.path).ToList();
        if (pts.Count > 0)
        {
            var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p);
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(b.extents.x / cam.aspect, b.extents.z) + 10f;
            cam.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + 6f, b.center.z), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 0.3f;
            yield return Shot("lab_moves_top");
        }
        cam.orthographic = oc; cam.transform.SetPositionAndRotation(op, orot); cam.orthographicSize = os;
        Log("done");
    }
}
