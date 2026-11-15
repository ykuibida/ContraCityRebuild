// Bots steps 2–4 (map lab): what the lab knows about a map. The markers the map already has (team spawns, pickups, flags, control
// points, base gates) and the WALK GRID: where a fighter can stand and walk, found with the same physics the fighter uses, not with
// Unity's NavMesh. (The NavMesh dropped floor a fighter walks on: bumpy snow, slopes, the space near garages — the owner's walk on
// Форпост, 15.11.2026: 488 of 1773 steps were on floor the NavMesh did not have.)
//   • a column every 1 unit over the map: a ray from the top finds every floor in it (floors, roofs, crates, beams, pipes);
//   • a floor is a cell if the fighter's capsule fits standing on it (PlayerRig CharacterController: radius 1.3, height 6.5, sitting
//     skinWidth 0.45 above the floor; checked with radius 1.15 so a fighter can stand by a wall or on a beam narrower than itself);
//   • neighbouring cells (8 directions) are walk-connected when the height step is within what the motor climbs: stepOffset 1.5, or
//     the slope (the CharacterController's slopeLimit is 90: the fighter walks up steep slopes);
//   • walk regions = connected cells; a region's edge cells (missing neighbours) are where the jump search starts.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public class LabWorld
{
    public enum MarkerKind { SpawnT0, SpawnT1, SpawnT2, Health, Ammo, Armor, Flag, ControlPoint, Gate, Other }

    public struct Marker { public MarkerKind kind; public string name; public Vector3 position; public bool onNavMesh; public int region; }

    public static LabWorld Current;   // the map open in the lab (LabSim asks it what is walkable)

    public readonly List<Marker> markers = new List<Marker>();
    public Bounds bounds;
    public Mesh navMesh;
    public float navArea, bakeMs, navMinY, navMaxY; public int cellCount, columns;

    // the grid
    public const float Cell = 1f;
    const float StandRadius = 1.15f, Skin = 0.45f, Height = 6.5f, Step = 1.5f;
    float x0, z0; int nx, nz;
    int[] colStart, colLen;                 // per column: its cells [colStart, colStart + colLen) in the arrays below
    public Vector3[] cells;                 // feet position on the floor
    float[] cellNy;                         // floor normal's y (slope)
    public int[] cellRegion;
    public int regionCount; public float[] regionArea;
    public struct Edge { public Vector3 a, b, outward; public int region; }
    public readonly List<Edge> boundary = new List<Edge>();

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
    static int Mask => ~((1 << CharacterMotor.GhostLayer) | (1 << 31) | (1 << 30) | (1 << 29));   // not simulated fighters, UI, previews

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
            anim.playAutomatically = false; anim.enabled = false;   // otherwise it plays its default (closing) clip again
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

    // ---------------- the walk grid ----------------

    /// <summary>Builds the walk grid from the map's colliders (see the header), then the regions and their edges.</summary>
    public void BakeNavMesh()
    {
        Current = this;
        var sw = Stopwatch.StartNew();
        // the extent: solid colliders of the map, without the GlobalCollider (every map's huge boundary box) — it is switched off
        // while baking, so rays from above are not stopped by its top and the capsule checks do not touch it
        var global = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.name == "GlobalCollider" && c.enabled).ToList();
        foreach (var g in global) g.enabled = false;
        var cols = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.enabled && !c.isTrigger && !(c is CharacterController) && ((1 << c.gameObject.layer) & Mask) != 0 && c.bounds.extents.magnitude < 2000f).ToList();
        var ext = cols.Count > 0 ? cols[0].bounds : new Bounds(bounds.center, Vector3.one * 100f);
        foreach (var c in cols) ext.Encapsulate(c.bounds);
        x0 = Mathf.Floor(ext.min.x) - 2f; z0 = Mathf.Floor(ext.min.z) - 2f;
        nx = Mathf.CeilToInt(ext.size.x / Cell) + 4; nz = Mathf.CeilToInt(ext.size.z / Cell) + 4;
        float top = ext.max.y + 5f, depth = ext.size.y + 10f;
        columns = nx * nz;
        colStart = new int[columns]; colLen = new int[columns];
        var pos = new List<Vector3>(columns); var ny = new List<float>(columns);
        var hits = new RaycastHit[64]; var floors = new List<RaycastHit>(16);
        for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                int col = iz * nx + ix; colStart[col] = pos.Count;
                float x = x0 + (ix + 0.5f) * Cell, z = z0 + (iz + 0.5f) * Cell;
                int n = Physics.RaycastNonAlloc(new Vector3(x, top, z), Vector3.down, hits, depth, Mask, QueryTriggerInteraction.Ignore);
                floors.Clear();
                for (int i = 0; i < n; i++) if (hits[i].normal.y >= 0.25f) floors.Add(hits[i]);   // up to ~75°: the motor stands on steep ground
                floors.Sort((a, b) => b.point.y.CompareTo(a.point.y));
                float lastY = float.MaxValue;
                foreach (var h in floors)
                {
                    var p = h.point;
                    if (lastY - p.y < 0.5f) continue;   // the same floor hit twice (two colliders)
                    lastY = p.y;
                    // the fighter's capsule standing here (its bottom Skin above the floor)
                    var a = p + Vector3.up * (Skin + StandRadius + 0.05f); var b = p + Vector3.up * (Skin + Height - StandRadius);
                    if (Physics.CheckCapsule(a, b, StandRadius, Mask, QueryTriggerInteraction.Ignore)) continue;
                    pos.Add(p); ny.Add(h.normal.y);
                }
                colLen[col] = pos.Count - colStart[col];
            }
        foreach (var g in global) g.enabled = true;
        cells = pos.ToArray(); cellNy = ny.ToArray(); cellCount = cells.Length;

        // regions: union-find over walk-connected neighbours
        var parent = new int[cellCount]; for (int i = 0; i < cellCount; i++) parent[i] = i;
        int Find(int v) { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; }
        for (int c = 0; c < cellCount; c++)
            foreach (int d in Neighbours(c)) { int ra = Find(c), rb = Find(d); if (ra != rb) parent[ra] = rb; }
        var ids = new Dictionary<int, int>(); cellRegion = new int[cellCount];
        for (int c = 0; c < cellCount; c++) { int r = Find(c); if (!ids.TryGetValue(r, out int id)) { id = ids.Count; ids[r] = id; } cellRegion[c] = id; }
        regionCount = ids.Count; regionArea = new float[regionCount];
        navMinY = float.MaxValue; navMaxY = float.MinValue;
        for (int c = 0; c < cellCount; c++) { regionArea[cellRegion[c]] += Cell * Cell; navMinY = Mathf.Min(navMinY, cells[c].y); navMaxY = Mathf.Max(navMaxY, cells[c].y); }
        navArea = cellCount * Cell * Cell;

        // edges: cells with a missing neighbour; outward = the mean direction of the missing ones; every other edge cell (≈ 2 units)
        boundary.Clear();
        for (int c = 0; c < cellCount; c++)
        {
            ColumnOf(c, out int ix, out int iz);
            if (((ix + iz) & 1) != 0) continue;
            var outward = Vector3.zero; int missing = 0;
            for (int k = 0; k < 8; k++) if (Neighbour(c, k) < 0) { outward += new Vector3(DX[k], 0f, DZ[k]).normalized; missing++; }
            if (missing == 0 || outward.sqrMagnitude < 0.01f) continue;
            outward.Normalize();
            boundary.Add(new Edge { a = cells[c] + outward * 1.3f, b = cells[c] + outward * 1.3f, outward = outward, region = cellRegion[c] });
        }
        bakeMs = (float)sw.Elapsed.TotalMilliseconds;
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            m.region = RegionAt(m.position, 3f);
            m.onNavMesh = m.region >= 0;
            markers[i] = m;
        }
        BuildMesh(null);
    }

    bool InGrid(int ix, int iz) => ix >= 0 && iz >= 0 && ix < nx && iz < nz;
    void ColumnOf(int c, out int ix, out int iz)
    {
        ix = Mathf.FloorToInt((cells[c].x - x0) / Cell); iz = Mathf.FloorToInt((cells[c].z - z0) / Cell);
    }

    /// <summary>The cell in direction k (0..7) that this cell walks to, -1 if none (a wall, a drop, a too high step).</summary>
    int Neighbour(int c, int k)
    {
        ColumnOf(c, out int ix, out int iz);
        int jx = ix + DX[k], jz = iz + DZ[k];
        if (!InGrid(jx, jz)) return -1;
        int col = jz * nx + jx; float y = cells[c].y;
        // the step the motor takes: stepOffset 1.5; on a slope more (the climb of the slope over one cell, CharacterController slopeLimit 90)
        float dist = k < 4 ? Cell : Cell * 1.4142f;
        int best = -1; float bestDy = float.MaxValue;
        for (int i = colStart[col]; i < colStart[col] + colLen[col]; i++)
        {
            float dy = Mathf.Abs(cells[i].y - y);
            float slopeNy = Mathf.Min(cellNy[c], cellNy[i]);
            float allowed = Mathf.Max(Step, dist * Mathf.Tan(Mathf.Acos(Mathf.Clamp(slopeNy, 0.26f, 1f))) + 0.3f);
            if (dy <= allowed && dy < bestDy) { bestDy = dy; best = i; }
        }
        return best;
    }

    IEnumerable<int> Neighbours(int c) { for (int k = 0; k < 8; k++) { int d = Neighbour(c, k); if (d >= 0) yield return d; } }

    /// <summary>The cell under this point (feet): its column or the next ones, the floor within tolerance. -1 if none.</summary>
    public int CellAt(Vector3 p, float tolerance = 1.5f)
    {
        if (cells == null) return -1;
        int ix = Mathf.FloorToInt((p.x - x0) / Cell), iz = Mathf.FloorToInt((p.z - z0) / Cell);
        int best = -1; float bestD = float.MaxValue;
        for (int r = 0; r <= 1 && best < 0; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int jx = ix + dx, jz = iz + dz; if (!InGrid(jx, jz)) continue;
                    int col = jz * nx + jx;
                    for (int i = colStart[col]; i < colStart[col] + colLen[col]; i++)
                    {
                        float dy = Mathf.Abs(cells[i].y - p.y); if (dy > tolerance) continue;
                        float d = dy + LabSim.Horizontal(cells[i] - p);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
        return best;
    }

    /// <summary>The walk region under this point (feet), -1 if there is no walkable floor within tolerance.</summary>
    public int RegionAt(Vector3 p, float tolerance = 1.5f) { int c = CellAt(p, tolerance); return c < 0 ? -1 : cellRegion[c]; }

    /// <summary>The nearest walkable cell within maxDist (horizontally; floor within maxDist vertically).</summary>
    public bool Snap(Vector3 p, float maxDist, out Vector3 q)
    {
        q = p; if (cells == null) return false;
        int ix = Mathf.FloorToInt((p.x - x0) / Cell), iz = Mathf.FloorToInt((p.z - z0) / Cell), R = Mathf.CeilToInt(maxDist / Cell);
        int best = -1; float bestD = float.MaxValue;
        for (int dz = -R; dz <= R; dz++)
            for (int dx = -R; dx <= R; dx++)
            {
                int jx = ix + dx, jz = iz + dz; if (!InGrid(jx, jz)) continue;
                int col = jz * nx + jx;
                for (int i = colStart[col]; i < colStart[col] + colLen[col]; i++)
                {
                    float d = Vector3.Distance(cells[i], p); if (d > maxDist) continue;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
        if (best < 0) return false;
        q = cells[best]; return true;
    }

    /// <summary>Is there walkable floor right under this point (feet)?</summary>
    public static bool OnWalkable(Vector3 feet, float tolerance = 1.5f) => Current != null && Current.CellAt(feet, tolerance) >= 0;

    /// <summary>A walking path over the grid (A*, 8 directions), as points every few cells; null if the cells are not walk-connected.</summary>
    public List<Vector3> FindPath(Vector3 a, Vector3 b)
    {
        int s = CellAt(a, 2f), t = CellAt(b, 2f);
        if (s < 0 || t < 0 || cellRegion[s] != cellRegion[t]) return null;
        var g = new Dictionary<int, float> { [s] = 0f }; var prev = new Dictionary<int, int>();
        var open = new SortedSet<(float f, int c)>(); open.Add((Vector3.Distance(cells[s], cells[t]), s));
        var closed = new HashSet<int>();
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur); int c = cur.c;
            if (c == t) break;
            if (!closed.Add(c)) continue;
            for (int k = 0; k < 8; k++)
            {
                int d = Neighbour(c, k); if (d < 0 || closed.Contains(d)) continue;
                float ng = g[c] + Vector3.Distance(cells[c], cells[d]);
                if (!g.TryGetValue(d, out float og) || ng < og) { g[d] = ng; prev[d] = c; open.Add((ng + Vector3.Distance(cells[d], cells[t]), d)); }
            }
        }
        if (!g.ContainsKey(t)) return null;
        var chain = new List<int>(); for (int c = t; ; c = prev[c]) { chain.Add(c); if (c == s) break; }
        chain.Reverse();
        var pts = new List<Vector3>();
        for (int i = 0; i < chain.Count; i++) if (i % 3 == 0 || i == chain.Count - 1) pts.Add(cells[chain[i]]);
        return pts;
    }

    public float WalkDistance(Vector3 a, Vector3 b)
    {
        var p = FindPath(a, b); if (p == null) return float.PositiveInfinity;
        float l = 0f; for (int i = 1; i < p.Count; i++) l += Vector3.Distance(p[i - 1], p[i]); return l;
    }

    /// <summary>The cells for drawing: reachable regions coloured by height (cyan → green → yellow → magenta), the rest grey.</summary>
    public void BuildMesh(HashSet<int> reachable)
    {
        if (navMesh == null) navMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        navMesh.Clear();
        float y0 = navMinY, y1 = navMaxY;
        if (reachable != null && reachable.Count > 0)
        {
            y0 = float.MaxValue; y1 = float.MinValue;
            for (int c = 0; c < cellCount; c++) if (reachable.Contains(cellRegion[c])) { y0 = Mathf.Min(y0, cells[c].y); y1 = Mathf.Max(y1, cells[c].y); }
        }
        float span = Mathf.Max(1f, Mathf.Min(y1 - y0, 60f));
        Color Height(float y)
        {
            float t = Mathf.Clamp01((y - y0) / span);
            var a = new Color(0.1f, 0.8f, 1f); var b = new Color(0.3f, 1f, 0.3f); var c = new Color(1f, 0.9f, 0.2f); var d = new Color(1f, 0.3f, 0.9f);
            return t < 0.33f ? Color.Lerp(a, b, t / 0.33f) : t < 0.66f ? Color.Lerp(b, c, (t - 0.33f) / 0.33f) : Color.Lerp(c, d, (t - 0.66f) / 0.34f);
        }
        // grey (unreachable) floor is drawn only up to a jump above the highest reachable floor: higher up are the maps' invisible
        // ceilings (Форпост: a 30 000-cell plane at +44 over the whole arena) that would cover the view
        float greyTop = reachable != null && reachable.Count > 0 ? y1 + 12f : float.MaxValue;
        var draw = Enumerable.Range(0, cellCount).Where(c => reachable == null || reachable.Contains(cellRegion[c]) || cells[c].y <= greyTop).ToArray();
        var v = new Vector3[draw.Length * 4]; var col = new Color[draw.Length * 4]; var idx = new int[draw.Length * 6];
        float h = Cell * 0.5f;
        for (int q = 0; q < draw.Length; q++)
        {
            int c = draw[q];
            var p = cells[c] + Vector3.up * 0.2f;
            bool ok = reachable == null || reachable.Contains(cellRegion[c]);
            var cc = ok ? Height(cells[c].y) : new Color(0.25f, 0.25f, 0.25f, 0.35f);
            v[q * 4] = p + new Vector3(-h, 0, -h); v[q * 4 + 1] = p + new Vector3(-h, 0, h); v[q * 4 + 2] = p + new Vector3(h, 0, h); v[q * 4 + 3] = p + new Vector3(h, 0, -h);
            for (int k = 0; k < 4; k++) col[q * 4 + k] = cc;
            idx[q * 6] = q * 4; idx[q * 6 + 1] = q * 4 + 1; idx[q * 6 + 2] = q * 4 + 2; idx[q * 6 + 3] = q * 4; idx[q * 6 + 4] = q * 4 + 2; idx[q * 6 + 5] = q * 4 + 3;
        }
        navMesh.vertices = v; navMesh.colors = col; navMesh.triangles = idx;
        navMesh.RecalculateBounds();
    }
}
