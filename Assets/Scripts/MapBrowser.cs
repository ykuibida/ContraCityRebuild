// Contra City map browser: pick a map, fly around, check that everything looks right.
// M / Esc  - map list        N - next spawn point     O - overview from above
// F1       - hide/show help  Right mouse + WASD/QE/Shift - fly (FlyCamera)
// G        - play: spawn the fighter on a team spawn point / back to flying
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MapBrowser : MonoBehaviour
{
    public const string MenuScene = "_MapMenu";

    static readonly Dictionary<string, string> RuNames = new Dictionary<string, string>
    {
        { "7000", "Хоздвор" }, { "Angar", "Ангар" }, { "ArenaRing", "Форпост" }, { "Arena_3lvl", "Ангар, задний двор" },
        { "Arena_well", "Перестройполис" }, { "Bit_map", "Комиссариат" }, { "Cache", "Промзона" }, { "Faccility", "Предприятие" },
        { "Inferno", "Урбан" }, { "InfernoZombie", "Урбан. Карантин" }, { "LegoTurnament", "Рубеж" }, { "ShipConteyner", "Монетный двор" },
        { "Sniper", "Враг у ворот" }, { "Sniper_night", "Ночь у ворот" }, { "TF3_Arena_map", "Складчина" }, { "Zombi", "Зона-Z" },
        { "Zombi_2", "Индастриал" },
    };

    static MapBrowser instance;
    List<string> maps = new List<string>();
    bool showMenu = true, showHelp = true;
    Vector2 scroll;
    string currentMap = "";
    List<Vector3> goodSpawns = new List<Vector3>();
    int spawnIndex;
    Bounds mapBounds;
    float fps;

    void Awake()
    {
        if (instance != null) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            if (name.EndsWith("_Viewer")) maps.Add(name.Substring(0, name.Length - "_Viewer".Length));
        }
        maps = maps.OrderBy(m => RuNames.TryGetValue(m, out var r) ? r : m).ToList();
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (System.Environment.GetCommandLineArgs().Contains("-selftest")) StartCoroutine(SelfTest());
        if (System.Environment.GetCommandLineArgs().Contains("-playtest")) StartCoroutine(PlayTest());
    }

    // ContraMaps.exe -batchmode -selftest : loads every map, renders the start view to selftest/<map>.png, quits.
    System.Collections.IEnumerator SelfTest()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "selftest");
        System.IO.Directory.CreateDirectory(dir);
        var log = new System.Text.StringBuilder();
        foreach (var m in maps)
        {
            SceneManager.LoadScene(m + "_Viewer");
            yield return null; yield return null;
            yield return new WaitForSeconds(0.5f);
            var cam = Cam;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, m + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Destroy(tex); Destroy(rt);
            log.AppendLine($"{m}\tspawns={goodSpawns.Count}\tcam={cam.transform.position}");
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "selftest.txt"), log.ToString());
        Application.Quit();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.name.EndsWith("_Viewer")) return;
        currentMap = scene.name.Substring(0, scene.name.Length - "_Viewer".Length);
        showMenu = false;
        pickupIndex = -1; pickupInfo = "";
        player = null;
        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.extents.magnitude < 1500f).ToArray();
        mapBounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one * 50f);
        foreach (var r in renderers) mapBounds.Encapsulate(r.bounds);
        FindGoodSpawns();
        spawnIndex = 0;
        if (goodSpawns.Count > 0) GoToSpawn(0); else GoToOverview();
    }

    // A spawn is good if there is floor right below it and no wall where the head would be.
    void FindGoodSpawns()
    {
        goodSpawns.Clear();
        var spawns = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("RespawnPoint") || t.name.StartsWith("Respawn_"))
            .OrderBy(t => t.name).Select(t => t.position);
        foreach (var p in spawns)
        {
            if (!Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var hit, 6f)) continue;
            Vector3 eye = hit.point + Vector3.up * 1.7f;
            if (Physics.CheckSphere(eye, 0.25f)) continue;
            if (goodSpawns.Any(g => (g - eye).sqrMagnitude < 1f)) continue;
            goodSpawns.Add(eye);
        }
    }

    Camera Cam => Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

    void GoToSpawn(int i)
    {
        var cam = Cam; if (cam == null || goodSpawns.Count == 0) return;
        Vector3 eye = goodSpawns[i % goodSpawns.Count];
        Vector3 look = new Vector3(mapBounds.center.x, eye.y, mapBounds.center.z) - eye;
        if (look.sqrMagnitude < 0.01f) look = Vector3.forward;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look.normalized));
        SyncFly(cam);
    }

    void GoToOverview()
    {
        var cam = Cam; if (cam == null) return;
        float plan = Mathf.Max(mapBounds.size.x, mapBounds.size.z);
        float groundY = goodSpawns.Count > 0 ? goodSpawns[0].y - 1.7f : mapBounds.min.y;
        var ground = new Vector3(mapBounds.center.x, groundY, mapBounds.center.z);
        var pos = ground + new Vector3(0f, plan * 0.45f, -plan * 0.55f);
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((ground - pos).normalized));
        SyncFly(cam);
    }

    static void SyncFly(Camera cam)
    {
        var fly = cam.GetComponent<FlyCamera>();
        if (fly != null) fly.SendMessage("Start", SendMessageOptions.DontRequireReceiver); // re-read yaw/pitch
    }

    int pickupIndex = -1;
    string pickupInfo = "";

    void GoToNextPickup()
    {
        var cam = Cam; if (cam == null) return;
        var items = new List<ItemMapXML>();
        foreach (var m in FindObjectsByType<ItemMapXML>(FindObjectsSortMode.None))
            if (m.Kind != PickupKind.Unknown) items.Add(m);
        if (items.Count == 0) { pickupInfo = "предметов нет"; return; }
        items.Sort((a, b) => string.CompareOrdinal(a.name + a.transform.position, b.name + b.transform.position));
        pickupIndex = (pickupIndex + 1) % items.Count;
        var it = items[pickupIndex];
        Vector3 p = it.transform.position;
        Vector3 eye = p + new Vector3(2.2f, 1.4f, 2.2f);
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((p - eye).normalized));
        SyncFly(cam);
        pickupInfo = $"предмет {pickupIndex + 1}/{items.Count}: {it.name} (+{it.Value}, респавн {it.RespawnDelay / 1000} с)";
    }

    // ---------- play mode ----------
    GameObject player;
    Camera flyCam;
    bool Playing => player != null;

    void StartPlay()
    {
        if (Playing || currentMap == "") return;
        var spawns = PlayerRig.SpawnPoints();
        // Respawn_T1 / T2 are the two teams; T0 points can hang outside the map (Zombi, LegoTurnament) — use them only as a fallback.
        var teams = spawns.FindAll(t => !t.name.StartsWith("Respawn_T0"));
        if (teams.Count > 0) spawns = teams;
        Vector3 pos; float yaw;
        if (spawns.Count > 0)
        {
            var sp = spawns[Random.Range(0, spawns.Count)];
            pos = sp.position + Vector3.up * 0.5f; yaw = sp.eulerAngles.y;
        }
        else if (goodSpawns.Count > 0) { pos = goodSpawns[0] - Vector3.up * 6f; yaw = 0f; }
        else return;
        flyCam = Cam;
        if (flyCam) SetFly(flyCam, false);
        player = PlayerRig.Create(pos, yaw, flyCam);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        showMenu = false;
    }

    void StopPlay()
    {
        if (!Playing) return;
        Destroy(player); player = null;
        if (flyCam) SetFly(flyCam, true);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    static void SetFly(Camera cam, bool on)
    {
        cam.enabled = on;
        var fly = cam.GetComponent<FlyCamera>(); if (fly) fly.enabled = on;
        var al = cam.GetComponent<AudioListener>(); if (al) al.enabled = on;
    }

    // ContraMaps.exe -batchmode -playtest : on every map spawns the fighter, checks the floor, runs and jumps; writes playtest/.
    System.Collections.IEnumerator PlayTest()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "playtest");
        System.IO.Directory.CreateDirectory(dir);
        var log = new System.Text.StringBuilder();
        log.AppendLine("map;spawns;start;after1s;grounded;run1.5s_dist;speed;jump_peak;landed");
        foreach (var m in maps)
        {
            SceneManager.LoadScene(m + "_Viewer");
            yield return null; yield return null;
            {
                var sb = new System.Text.StringBuilder();
                foreach (var spt in PlayerRig.SpawnPoints())
                {
                    bool floor = Physics.Raycast(spt.position + Vector3.up * 3f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore);
                    sb.Append($"{spt.name}@{spt.position.x:0},{spt.position.y:0},{spt.position.z:0}:{(floor ? (spt.position.y - hit.point.y).ToString("0.0") : "NOFLOOR")} ");
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "spawns.txt"), m + ": " + sb + System.Environment.NewLine);
            }
            StopPlay(); StartPlay();
            if (!Playing) { log.AppendLine(m + ";NO SPAWN"); continue; }
            var sc = player.GetComponent<SoldierController>(); var motor = player.GetComponent<CharacterMotor>();
            Vector3 start = player.transform.position;
            yield return new WaitForSeconds(1f);
            Vector3 settled = player.transform.position; bool grounded = motor.grounded;
            sc.testMove = Vector3.forward;
            yield return new WaitForSeconds(1.5f);
            Vector3 ran = player.transform.position;
            float speed = new Vector3(motor.movement.velocity.x, 0, motor.movement.velocity.z).magnitude;
            sc.testMove = Vector3.zero;
            yield return new WaitForSeconds(0.6f);
            float y0 = player.transform.position.y, peak = y0;
            sc.testJump = true; yield return new WaitForSeconds(0.15f); sc.testJump = false;
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { peak = Mathf.Max(peak, player.transform.position.y); yield return null; }
            var cam = player.GetComponentInChildren<Camera>();
            var rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, m + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; Destroy(tex); Destroy(rt);
            float run = Vector3.Distance(new Vector3(settled.x, 0, settled.z), new Vector3(ran.x, 0, ran.z));
            log.AppendLine($"{m};{PlayerRig.SpawnPoints().Count};{start};{settled};{grounded};{run:0.0};{speed:0.0};{peak - y0:0.00};{motor.grounded}");
            StopPlay();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "playtest.txt"), log.ToString());
        Application.Quit();
    }

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
    }

    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (Playing && e.keyCode == KeyCode.Escape) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; e.Use(); }
            else if (e.keyCode == KeyCode.M || e.keyCode == KeyCode.Escape) { if (Playing) StopPlay(); showMenu = !showMenu || currentMap == ""; e.Use(); }
            else if (e.keyCode == KeyCode.N && goodSpawns.Count > 0) { spawnIndex = (spawnIndex + 1) % goodSpawns.Count; GoToSpawn(spawnIndex); e.Use(); }
            else if (e.keyCode == KeyCode.O && currentMap != "") { GoToOverview(); e.Use(); }
            else if (e.keyCode == KeyCode.P && currentMap != "" && !Playing) { GoToNextPickup(); e.Use(); }
            else if (e.keyCode == KeyCode.G && currentMap != "") { if (Playing) StopPlay(); else StartPlay(); e.Use(); }
            else if (e.keyCode == KeyCode.F1) { showHelp = !showHelp; e.Use(); }
        }

        GUI.skin.label.fontSize = 16;
        GUI.skin.button.fontSize = 18;
        if (currentMap != "")
        {
            string title = RuNames.TryGetValue(currentMap, out var ru) ? ru : currentMap;
            string spawnInfo = goodSpawns.Count > 0 ? $"точка {spawnIndex + 1}/{goodSpawns.Count}" : "точек появления не найдено";
            GUI.Label(new Rect(12, 8, 900, 26), $"{title}  ({currentMap})   |   {spawnInfo}   |   {fps:0} FPS" + (pickupInfo != "" ? "   |   " + pickupInfo : ""));
            if (Playing)
            {
                var v = player.GetComponent<PlayerVitals>(); var mo = player.GetComponent<CharacterMotor>();
                float sp = new Vector3(mo.movement.velocity.x, 0, mo.movement.velocity.z).magnitude;
                GUI.Label(new Rect(12, 32, 1500, 26), $"ИГРА  |  Здоровье {v.Health}/{v.MaxHealth}   Броня {v.Armor}/{v.MaxArmor}   |  скорость {sp:0.0}  {(mo.grounded ? "на земле" : "в воздухе")}   |  WASD, пробел, Ctrl — присесть, Shift — шагом, Esc — курсор, G — выйти из игры");
                if (Cursor.lockState != CursorLockMode.Locked && e.type == EventType.MouseDown && !showMenu) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
            else if (showHelp)
                GUI.Label(new Rect(12, 32, 1100, 26), "ПКМ + мышь — обзор,  WASD — движение,  Q/E — вниз/вверх,  Shift — быстрее,  N — следующая точка,  P — следующий предмет,  G — играть,  O — вид сверху,  M — список карт,  F1 — скрыть подсказку");
        }

        if (!showMenu) return;
        float w = 460, h = Mathf.Min(Screen.height - 80, 60 + maps.Count * 46);
        var rect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        GUI.Box(rect, "Контра Сити — выбор карты");
        scroll = GUI.BeginScrollView(new Rect(rect.x + 10, rect.y + 34, w - 20, h - 44), scroll, new Rect(0, 0, w - 40, maps.Count * 46));
        for (int i = 0; i < maps.Count; i++)
        {
            string m = maps[i];
            string label = (RuNames.TryGetValue(m, out var ru) ? ru : m) + (m == currentMap ? "   ◄" : "");
            if (GUI.Button(new Rect(0, i * 46, w - 40, 40), label))
                SceneManager.LoadScene(m + "_Viewer");
        }
        GUI.EndScrollView();
    }
}
