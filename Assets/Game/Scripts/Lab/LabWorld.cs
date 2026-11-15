// Bots step 2 (map lab): what the lab knows about a map. The markers the map already has (team spawns, pickups, flags, control
// points, base gates) and the walkable surface for the fighter's size (Unity NavMesh baked at runtime from the map's colliders:
// radius 1.3, height 6.5, step 1.5 — the CharacterController of PlayerRig).
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class LabWorld
{
    public enum MarkerKind { SpawnT0, SpawnT1, SpawnT2, Health, Ammo, Armor, Flag, ControlPoint, Gate, Other }

    public struct Marker { public MarkerKind kind; public string name; public Vector3 position; public bool onNavMesh; }

    public readonly List<Marker> markers = new List<Marker>();
    public Bounds bounds;
    public NavMeshData navData; NavMeshDataInstance navInstance;
    public Mesh navMesh;
    public float navArea, bakeMs, navMinY, navMaxY; public int navTriangles, sources;

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
            markers.Add(new Marker { kind = k.Value, name = n, position = t.position });
        }
        markers.Sort((x, y) => string.CompareOrdinal(x.kind + x.name + x.position, y.kind + y.name + y.position));   // a stable order between runs
    }

    /// <summary>Bakes the walkable surface from every solid collider of the map (triggers and simulated fighters left out).</summary>
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
        navData = NavMeshBuilder.BuildNavMeshData(Agent, src, b, Vector3.zero, Quaternion.identity);
        navInstance = NavMesh.AddNavMeshData(navData);
        bakeMs = (float)sw.Elapsed.TotalMilliseconds;
        var tri = NavMesh.CalculateTriangulation();
        navTriangles = tri.indices.Length / 3;
        navArea = 0f;
        for (int i = 0; i < tri.indices.Length; i += 3)
            navArea += Vector3.Cross(tri.vertices[tri.indices[i + 1]] - tri.vertices[tri.indices[i]], tri.vertices[tri.indices[i + 2]] - tri.vertices[tri.indices[i]]).magnitude * 0.5f;
        navMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        navMesh.vertices = tri.vertices.Select(v => v + Vector3.up * 0.25f).ToArray();
        navMesh.triangles = tri.indices;
        // colour by height so floors, roofs and ledges read apart: low cyan → green → yellow → high magenta
        float y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var v in tri.vertices) { y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y); }
        Color HeightColor(float y)
        {
            float t = y1 > y0 ? Mathf.Clamp01((y - y0) / Mathf.Min(y1 - y0, 60f)) : 0f;
            var a = new Color(0.1f, 0.8f, 1f); var b = new Color(0.3f, 1f, 0.3f); var c = new Color(1f, 0.9f, 0.2f); var d = new Color(1f, 0.3f, 0.9f);
            return t < 0.33f ? Color.Lerp(a, b, t / 0.33f) : t < 0.66f ? Color.Lerp(b, c, (t - 0.33f) / 0.33f) : Color.Lerp(c, d, (t - 0.66f) / 0.34f);
        }
        navMesh.colors = tri.vertices.Select(v => HeightColor(v.y)).ToArray();
        navMinY = y0; navMaxY = y1;
        navMesh.RecalculateBounds();
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            m.onNavMesh = NavMesh.SamplePosition(m.position, out var hit, 3f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - m.position.y) < 3f;
            markers[i] = m;
        }
    }

    /// <summary>Is there walkable surface right under this point (feet)?</summary>
    public static bool OnWalkable(Vector3 feet, float tolerance = 1.5f) =>
        NavMesh.SamplePosition(feet, out var hit, tolerance, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - feet.y) < tolerance;
}
