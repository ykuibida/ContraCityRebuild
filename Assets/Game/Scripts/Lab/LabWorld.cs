// Bots steps 2–4 (map lab): what the lab knows about a map. The markers the map already has (team spawns, pickups, flags, control
// points, base gates) and the walkable surface for the fighter's size (Unity NavMesh baked at runtime from the map's colliders:
// radius 1.3, height 6.5, step 1.5 — the CharacterController of PlayerRig). The surface is split into walk regions (triangles that
// share an edge); its outer edges (ledges, wall feet) are where the jump search starts; what is not reachable from the spawns is grey.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class LabWorld
{
    public enum MarkerKind { SpawnT0, SpawnT1, SpawnT2, Health, Ammo, Armor, Flag, ControlPoint, Gate, Other }

    public struct Marker { public MarkerKind kind; public string name; public Vector3 position; public bool onNavMesh; public int region; }

    public readonly List<Marker> markers = new List<Marker>();
    public Bounds bounds;
    NavMeshDataInstance navInstance;
    public Mesh navMesh;
    public float navArea, bakeMs, navMinY, navMaxY; public int navTriangles, sources;

    // walk regions
    public Vector3[] verts; public int[] tris;          // the triangulation (welded)
    public int[] triRegion; public int regionCount;
    public float[] regionArea;
    public struct Edge { public Vector3 a, b, outward; public int region; }
    public readonly List<Edge> boundary = new List<Edge>();
    readonly Dictionary<long, List<int>> triGrid = new Dictionary<long, List<int>>();
    const float GridCell = 8f;

    public static readonly NavMeshBuildSettings Agent = MakeAgent();
    static NavMeshBuildSettings MakeAgent()
    {
        var s = NavMesh.GetSettingsByIndex(0);
        s.agentRadius = 1.3f; s.agentHeight = 6.5f; s.agentClimb = 1.5f; s.agentSlope = 60f;
        s.overrideVoxelSize = true; s.voxelSize = 0.4f;            // radius / 3 ≈ 0.43: the default resolution for this agent
        s.minRegionArea = 4f;
        return s;
    }

    public static Color ColorOf(MarkerKind k)
    {
        switch (k)
        {
            case MarkerKind.SpawnT0: return new Color(0.9f, 0.9f, 0.9f);
            case MarkerKind.SpawnT1: return new Color(1f, 0.25f, 0.2f);
            case MarkerKind.SpawnT2: return new Color(0.25f, 0.5f, 1f);
            case MarkerKind.Health: return new Color(0.2f, 1f, 0.3f);
            case MarkerKind.Ammo: return new Color(1f, 0.85f, 0.2f);
            case MarkerKind.Armor: return new Color(0.3f, 0.95f, 1f);
            case MarkerKind.Flag: return new Color(1f, 0.4f, 1f);
            case MarkerKind.ControlPoint: return new Color(1f, 0.6f, 0.1f);
            case MarkerKind.Gate: return new Color(0.6f, 0.6f, 0.6f);
            default: return Color.white;
        }
    }

    public static string RuName(MarkerKind k)
    {
        switch (k)
        {
            case MarkerKind.SpawnT0: return "респ DM"; case MarkerKind.SpawnT1: return "респ красных"; case MarkerKind.SpawnT2: return "респ синих";
            case MarkerKind.Health: return "аптечка"; case MarkerKind.Ammo: return "патроны"; case MarkerKind.Armor: return "броня";
            case MarkerKind.Flag: return "флаг"; case MarkerKind.ControlPoint: return "точка"; case MarkerKind.Gate: return "ворота";
            default: return "метка";
        }
    }

    public static bool IsSpawn(MarkerKind k) => k == MarkerKind.SpawnT0 || k == MarkerKind.SpawnT1 || k == MarkerKind.SpawnT2;

    /// <summary>The map's bounds (renderers of the map scene, as MapBrowser) and its markers.</summary>
    public void Scan()
    {
        var rs = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.extents.magnitude < 1500f).ToArray();
        bounds = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one * 100f);
        foreach (var r in rs) bounds.Encapsulate(r.bounds);
        markers.Clear();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = t.name;
            MarkerKind? k = null;
            if (n.StartsWith("RespawnPoint") || n.StartsWith("Respawn_T"))
            {
                // the "RespawnPoint" children of Respawn_T<team>, or a bare Respawn_T root (GameFlow.CollectSafeSceneRespawnPoints)
                string team = null;
                for (var p = t; p != null; p = p.parent) if (p.name.StartsWith("Respawn_T")) { team = p.name.Substring(9, 1); break; }
                if (n.StartsWith("Respawn_T") && t.Cast<Transform>().Any(c => c.name.StartsWith("RespawnPoint"))) continue;   // a root with children
                k = team == "1" ? MarkerKind.SpawnT1 : team == "2" ? MarkerKind.SpawnT2 : MarkerKind.SpawnT0;
            }
            else if (t.GetComponent<ItemMapXML>() != null)
            {
                var pk = ItemMapXML.KindFromName(n);
                k = pk == PickupKind.Health ? MarkerKind.Health : pk == PickupKind.Ammo ? MarkerKind.Ammo : pk == PickupKind.Armor ? MarkerKind.Armor : (MarkerKind?)null;
                if (k == null && n.StartsWith("FlagPoint")) k = MarkerKind.Flag;
            }
            else if (n.StartsWith("FlagPoint")) k = MarkerKind.Flag;
            else if (n.StartsWith("ControlPoint")) k = MarkerKind.ControlPoint;
            else if ((n.StartsWith("GatesRed") || n.StartsWith("GatesBlue")) && !n.Contains("Trigger") && t.GetComponent<Collider>() != null) k = MarkerKind.Gate;
            if (k == null) continue;
            markers.Add(new Marker { kind = k.Value, name = n, position = t.position, region = -1 });
        }
        markers.Sort((x, y) => string.CompareOrdinal(x.kind + x.name + x.position, y.kind + y.name + y.position));   // a stable order between runs
    }

    /// <summary>Automatic doors (AutomaticDoor: base doors on Angar, ArenaRing, Arena_3lvl, …) open in the game when a player who is
    /// not of the other team comes within touchDistance; in the lab nobody comes, so they would stay shut and seal the bases. The lab
    /// puts every door at the end of its open animation and stops the door script. Returns how many doors were opened.</summary>
    public int OpenDoors()
    {
        int n = 0;
        foreach (var d in Object.FindObjectsByType<AutomaticDoor>(FindObjectsSortMode.None))
        {
            var anim = d.transform.parent != null ? d.transform.parent.GetComponentInChildren<Animation>() : d.GetComponentInChildren<Animation>();
            if (anim == null || string.IsNullOrEmpty(d.OpenAnimationName) || anim[d.OpenAnimationName] == null) continue;
            var st = anim[d.OpenAnimationName];
            st.enabled = true; st.weight = 1f; st.normalizedTime = 1f; anim.Sample(); st.enabled = false;
            d.enabled = false; n++;
        }
        Physics.SyncTransforms();
        return n;
    }

    /// <summary>The base gates (GatesRed / GatesBlue) let their own team through (GameFlow: layer 12 vs the player's layer 2) and stop
    /// the other team. The lab opens them for everyone: the search and the routes see the bases as part of the map; which team may
    /// pass is a rule for the bots later. Returns how many gate colliders were switched off.</summary>
    public int OpenGates()
    {
        int n = 0;
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c.isTrigger || !c.enabled) continue;
            for (var t = c.transform; t != null; t = t.parent)
                if ((t.name.StartsWith("GatesRed") || t.name.StartsWith("GatesBlue")) && !t.name.Contains("Trigger")) { c.enabled = false; n++; break; }
        }
        return n;
    }

    /// <summary>Bakes the walkable surface from every solid collider of the map (triggers and simulated fighters left out), then
    /// splits it into walk regions and finds its outer edges.</summary>
    public void BakeNavMesh()
    {
        var sw = Stopwatch.StartNew();
        var src = new List<NavMeshBuildSource>();
        var b = bounds; b.Expand(20f);
        NavMeshBuilder.CollectSources(b, ~(1 << CharacterMotor.GhostLayer), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), src);
        // triggers, fighters, and the GlobalCollider (every map's huge boundary box around the level) are not floor
        src.RemoveAll(s => s.component is Collider c && (c.isTrigger || c is CharacterController || c.name == "GlobalCollider"));
        sources = src.Count;
        if (navInstance.valid) navInstance.Remove();
        var data = NavMeshBuilder.BuildNavMeshData(Agent, src, b, Vector3.zero, Quaternion.identity);
        navInstance = NavMesh.AddNavMeshData(data);
        bakeMs = (float)sw.Elapsed.TotalMilliseconds;

        // weld the triangulation (polygons of neighbouring tiles repeat vertices)
        var tri = NavMesh.CalculateTriangulation();
        var map = new Dictionary<(int, int, int), int>(); var vl = new List<Vector3>(); var remap = new int[tri.vertices.Length];
        for (int i = 0; i < tri.vertices.Length; i++)
        {
            var v = tri.vertices[i]; var key = (Mathf.RoundToInt(v.x * 20f), Mathf.RoundToInt(v.y * 20f), Mathf.RoundToInt(v.z * 20f));
            if (!map.TryGetValue(key, out int id)) { id = vl.Count; vl.Add(v); map[key] = id; }
            remap[i] = id;
        }
        verts = vl.ToArray(); tris = tri.indices.Select(i => remap[i]).ToArray();
        navTriangles = tris.Length / 3;

        // regions: triangles sharing an edge (union-find); boundary: edges with one triangle
        int n = navTriangles; var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var edgeOwner = new Dictionary<(int, int), int>(); var edgeCount = new Dictionary<(int, int), int>();
        for (int t = 0; t < n; t++)
            for (int e = 0; e < 3; e++)
            {
                int a = tris[t * 3 + e], c = tris[t * 3 + (e + 1) % 3]; var key = a < c ? (a, c) : (c, a);
                if (edgeOwner.TryGetValue(key, out int o)) { int ra = Find(o), rb = Find(t); if (ra != rb) parent[ra] = rb; edgeCount[key]++; }
                else { edgeOwner[key] = t; edgeCount[key] = 1; }
            }
        var ids = new Dictionary<int, int>(); triRegion = new int[n];
        for (int t = 0; t < n; t++) { int r = Find(t); if (!ids.TryGetValue(r, out int id)) { id = ids.Count; ids[r] = id; } triRegion[t] = id; }
        regionCount = ids.Count; regionArea = new float[regionCount];
        navArea = 0f; navMinY = float.MaxValue; navMaxY = float.MinValue;
        triGrid.Clear();
        for (int t = 0; t < n; t++)
        {
            Vector3 p0 = verts[tris[t * 3]], p1 = verts[tris[t * 3 + 1]], p2 = verts[tris[t * 3 + 2]];
            float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f; navArea += area; regionArea[triRegion[t]] += area;
            navMinY = Mathf.Min(navMinY, p0.y, p1.y, p2.y); navMaxY = Mathf.Max(navMaxY, p0.y, p1.y, p2.y);
            int x0 = Mathf.FloorToInt(Mathf.Min(p0.x, p1.x, p2.x) / GridCell), x1 = Mathf.FloorToInt(Mathf.Max(p0.x, p1.x, p2.x) / GridCell);
            int z0 = Mathf.FloorToInt(Mathf.Min(p0.z, p1.z, p2.z) / GridCell), z1 = Mathf.FloorToInt(Mathf.Max(p0.z, p1.z, p2.z) / GridCell);
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) { long k = ((long)x << 32) ^ (uint)z; if (!triGrid.TryGetValue(k, out var l)) triGrid[k] = l = new List<int>(); l.Add(t); }
        }
        boundary.Clear();
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue;
            int t = edgeOwner[kv.Key]; Vector3 a = verts[kv.Key.Item1], c = verts[kv.Key.Item2];
            var centroid = (verts[tris[t * 3]] + verts[tris[t * 3 + 1]] + verts[tris[t * 3 + 2]]) / 3f;
            var along = c - a; along.y = 0f; if (along.sqrMagnitude < 0.01f) continue;
            var outward = Vector3.Cross(Vector3.up, along).normalized;
            var mid = (a + c) * 0.5f; var toC = centroid - mid; toC.y = 0f;
            if (Vector3.Dot(outward, toC) > 0f) outward = -outward;
            boundary.Add(new Edge { a = a, b = c, outward = outward, region = triRegion[t] });
        }
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            m.region = RegionAt(m.position, 3f);
            m.onNavMesh = m.region >= 0;
            markers[i] = m;
        }
        BuildMesh(null);
    }

    /// <summary>The walk region under this point (feet), -1 if there is no walkable surface within tolerance.</summary>
    public int RegionAt(Vector3 p, float tolerance = 1.5f)
    {
        if (!NavMesh.SamplePosition(p, out var hit, tolerance, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - p.y) > tolerance) return -1;
        var q = hit.position;
        long k = ((long)Mathf.FloorToInt(q.x / GridCell) << 32) ^ (uint)Mathf.FloorToInt(q.z / GridCell);
        if (!triGrid.TryGetValue(k, out var list)) return -1;
        int best = -1; float bestDy = 1.0f;
        foreach (int t in list)
        {
            Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
            // barycentric in XZ
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-6f) continue;
            float w1 = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.z - c.z)) / d, w2 = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.z - c.z)) / d, w3 = 1f - w1 - w2;
            if (w1 < -0.02f || w2 < -0.02f || w3 < -0.02f) continue;
            float y = w1 * a.y + w2 * b.y + w3 * c.y, dy = Mathf.Abs(y - q.y);
            if (dy < bestDy) { bestDy = dy; best = triRegion[t]; }
        }
        return best;
    }

    /// <summary>The surface mesh for drawing: reachable regions coloured by height (cyan → green → yellow → magenta), the rest grey.</summary>
    public void BuildMesh(HashSet<int> reachable)
    {
        if (navMesh == null) navMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        navMesh.Clear();
        // per-triangle vertices so a region keeps its own colour
        var v = new Vector3[tris.Length]; var col = new Color[tris.Length]; var idx = new int[tris.Length];
        float y0 = navMinY, y1 = navMaxY;
        if (reachable != null && reachable.Count > 0)
        {
            y0 = float.MaxValue; y1 = float.MinValue;
            for (int t = 0; t < tris.Length / 3; t++) if (reachable.Contains(triRegion[t])) for (int k = 0; k < 3; k++) { float y = verts[tris[t * 3 + k]].y; y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        }
        float span = Mathf.Max(1f, Mathf.Min(y1 - y0, 60f));
        Color Height(float y)
        {
            float t = Mathf.Clamp01((y - y0) / span);
            var a = new Color(0.1f, 0.8f, 1f); var b = new Color(0.3f, 1f, 0.3f); var c = new Color(1f, 0.9f, 0.2f); var d = new Color(1f, 0.3f, 0.9f);
            return t < 0.33f ? Color.Lerp(a, b, t / 0.33f) : t < 0.66f ? Color.Lerp(b, c, (t - 0.33f) / 0.33f) : Color.Lerp(c, d, (t - 0.66f) / 0.34f);
        }
        for (int i = 0; i < tris.Length; i++)
        {
            var p = verts[tris[i]]; v[i] = p + Vector3.up * 0.25f; idx[i] = i;
            bool ok = reachable == null || reachable.Contains(triRegion[i / 3]);
            col[i] = ok ? Height(p.y) : new Color(0.35f, 0.35f, 0.35f, 0.25f);
        }
        navMesh.vertices = v; navMesh.colors = col; navMesh.triangles = idx;
        navMesh.RecalculateBounds();
    }

    /// <summary>Is there walkable surface right under this point (feet)?</summary>
    public static bool OnWalkable(Vector3 feet, float tolerance = 1.5f) =>
        NavMesh.SamplePosition(feet, out var hit, tolerance, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - feet.y) < tolerance;
}
