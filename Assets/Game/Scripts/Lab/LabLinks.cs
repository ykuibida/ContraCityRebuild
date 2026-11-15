// Bots step 4 (map lab): the automatic jump search over the whole map, and routes from A to B.
//
// Search: start from the walk regions of the spawns. Along the outer edges of each reachable region (ledges, the feet of walls and
// crates: where jumps start) every few units the ghost tries a set of moves in three directions (straight out, ±30°): standing
// jump, running jump, bunnyhop, shotgun (two variants), rocket, rocket + shotgun, bat. A landing becomes a link when it lands on the
// walkable surface without fall damage and either reaches another region (a crate top, a roof, a ledge) or is a real shortcut
// (walking there is ≥ 25 units longer). Regions reached by links are searched in turn, so only what can be reached from the spawns
// is searched and drawn. Per start spot and landing spot only the quickest link is kept.
//
// Route: Dijkstra over the link ends: from where the bot is, walk (NavMesh path length / run speed) to the start of a link in the
// same region, take the link (its simulated time), and so on, until the target's region. The route is then played on the ghost:
// walking segments by LabSim.WalkTo (the real motor following the NavMesh path), jumps by LabSim.Run from the link's start.
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class LabLinks
{
    public const int Version = 6;   // bump when the search or LabSim changes: cached files of another version are ignored

    [System.Serializable]
    public class Link
    {
        public LabSim.Move move; public Vector3 from, to; public float yaw, runUp, pitch, fireDelay; public int hops, shots; public bool backwards;
        public float time, rise, peak, distance; public int fromRegion, toRegion, fallDamage; public bool shortcut, observed;
        public Vector3[] path;   // decimated, for drawing
        [System.NonSerialized] public int id;
        public LabSim.Params Params() => new LabSim.Params { move = move, feet = from, yaw = yaw, runUp = runUp, pitch = pitch, fireDelay = fireDelay, hops = hops, shots = shots, backwards = backwards, fps = LabSim.BotFps };
    }

    [System.Serializable] class File_ { public int version; public string map; public int triangles, regions; public float seconds; public int sims; public List<Link> links; public List<int> reachable; }

    public readonly List<Link> links = new List<Link>();
    public readonly HashSet<int> reachable = new HashSet<int>();
    public float progress, seconds; public int sims; public bool done; public string state = "";

    readonly LabWorld world; readonly GameObject ghost; readonly string map;
    public LabLinks(LabWorld w, GameObject g, string mapName) { world = w; ghost = g; map = mapName; }

    static string CachePath(string map) => Path.Combine(Path.GetDirectoryName(Application.dataPath), "LabData", map + ".links.json");

    public bool TryLoad()
    {
        var p = CachePath(map);
        if (!File.Exists(p)) return false;
        try
        {
            var f = JsonUtility.FromJson<File_>(File.ReadAllText(p));
            if (f == null || f.version != Version || f.links == null || f.triangles != world.navTriangles || f.regions != world.regionCount) return false;
            thinned = null; links.Clear(); links.AddRange(f.links); reachable.Clear(); foreach (var r in f.reachable) reachable.Add(r);
            if (reachable.Any(r => r >= world.regionCount)) return false;
            for (int i = 0; i < links.Count; i++) links[i].id = i;
            seconds = f.seconds; sims = f.sims; done = true; progress = 1f; state = $"загружено из кеша: {links.Count} прыжков, {reachable.Count} участков достижимы";
            return true;
        }
        catch { return false; }
    }

    void Save()
    {
        var p = CachePath(map); Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, JsonUtility.ToJson(new File_ { version = Version, map = map, triangles = world.navTriangles, regions = world.regionCount, seconds = seconds, sims = sims, links = links, reachable = reachable.ToList() }));
    }

    // the moves tried at every start spot: (move, run-up, extra)
    static IEnumerable<LabSim.Params> Variants(Vector3 feet, float yaw)
    {
        yield return new LabSim.Params { move = LabSim.Move.Walk, runUp = 1.2f };   // onto the next piece of floor / off a ledge (the real motor decides)
        yield return new LabSim.Params { move = LabSim.Move.Jump, runUp = 0f };
        yield return new LabSim.Params { move = LabSim.Move.RunJump, runUp = 0.6f };
        yield return new LabSim.Params { move = LabSim.Move.Bunnyhop, runUp = 0.6f, hops = 2 };
        yield return new LabSim.Params { move = LabSim.Move.Shotgun, runUp = 0.3f, shots = 2, pitch = 50f, backwards = true };
        yield return new LabSim.Params { move = LabSim.Move.Shotgun, runUp = 0f, shots = 1, pitch = 80f, backwards = true };   // straight up
        yield return new LabSim.Params { move = LabSim.Move.Rocket, runUp = 0.3f, fireDelay = 0.1f };
        yield return new LabSim.Params { move = LabSim.Move.RocketShotgun, runUp = 0.3f, fireDelay = 0.1f, shots = 1, pitch = 50f, backwards = true };
        yield return new LabSim.Params { move = LabSim.Move.Bat, runUp = 0.6f, hops = 2 };
    }

    /// <summary>Distance covered by the run-up before the jump (the run starts this far behind the start spot).</summary>
    static float RunUpLength(LabSim.Params p) => p.runUp <= 0.01f || p.move == LabSim.Move.Walk ? 0f : p.runUp * (p.move == LabSim.Move.Bat ? 20f : 16f) * 0.85f;

    public IEnumerator Search(float frameBudgetMs = 25f)
    {
        var sw = Stopwatch.StartNew(); var frame = Stopwatch.StartNew();
        links.Clear(); reachable.Clear(); sims = 0; done = false; thinned = null;
        var queue = new Queue<int>();
        foreach (var m in world.markers) if (LabWorld.IsSpawn(m.kind) && m.region >= 0 && reachable.Add(m.region)) queue.Enqueue(m.region);
        var best = new Dictionary<(int, int, int, int, int, int), Link>();
        int edgesDone = 0, edgesTotal = world.boundary.Count, probes = 0;
        var path = new NavMeshPath();

        // one try: the move p from the spot `spot` of region `region` in the direction yaw; a good landing becomes a link
        int Try(LabSim.Params p, Vector3 spot, float yaw, int region)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float back = RunUpLength(p);
            var from = spot - dir * back;
            if (back > 0f && (world.RegionAt(from) != region || Physics.CapsuleCast(from + Vector3.up * 2.6f, from + Vector3.up * 5.4f, 1.2f, dir, back, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore))) return -1;
            if (!NavMesh.SamplePosition(from, out var hit, 1.5f, NavMesh.AllAreas)) return -1;
            p.feet = hit.position; p.yaw = yaw; p.fps = LabSim.BotFps; p.maxTime = 5f;
            var r = LabSim.Run(ghost, p); sims++;
            if (!r.landed || !r.onWalkable) return -1;   // a landing with fall damage counts too (players drop down); routes pay for the HP
            int to = world.RegionAt(r.landing);
            if (to < 0) return -1;
            bool shortcut = false;
            if (to == region)
            {
                if (r.Rise < 1.6f && LabSim.Horizontal(r.landing - p.feet) < 12f) return -1;
                float walk = NavMesh.CalculatePath(p.feet, r.landing, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? PathLength(path) : 9999f;
                if (walk - LabSim.Horizontal(r.landing - p.feet) < 25f) return -1;
                shortcut = true;
            }
            var link = new Link
            {
                move = p.move, from = p.feet, to = r.landing, yaw = yaw, runUp = p.runUp, pitch = p.pitch, fireDelay = p.fireDelay, hops = p.hops, shots = p.shots, backwards = p.backwards,
                time = r.time, rise = r.Rise, peak = r.maxRise, distance = LabSim.Horizontal(r.landing - p.feet), fromRegion = region, toRegion = to, shortcut = shortcut, fallDamage = r.fallDamage,
                path = r.path.Where((x, k) => k % 24 == 0 || k == r.path.Count - 1).ToArray(),
            };
            var key = (Mathf.RoundToInt(p.feet.x / 6f), Mathf.RoundToInt(p.feet.z / 6f), Mathf.RoundToInt(r.landing.x / 4f), Mathf.RoundToInt(r.landing.y / 2f), Mathf.RoundToInt(r.landing.z / 4f), region);
            if (!best.TryGetValue(key, out var old) || link.time + Cost(link) < old.time + Cost(old)) best[key] = link;
            if (reachable.Add(to)) queue.Enqueue(to);
            return to;
        }

        Vector3 Centroid(int t) => (world.verts[world.tris[t * 3]] + world.verts[world.tris[t * 3 + 1]] + world.verts[world.tris[t * 3 + 2]]) / 3f;

        for (int round = 0; round < 4; round++)
        {
            // phase 1: from the edges of every reachable region, three directions per spot
            while (queue.Count > 0)
            {
                int region = queue.Dequeue();
                foreach (var e in world.boundary.Where(x => x.region == region))
                {
                    edgesDone++;
                    float len = LabSim.Horizontal(e.b - e.a); int samples = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
                    for (int si = 0; si < samples; si++)
                    {
                        var onEdge = Vector3.Lerp(e.a, e.b, (si + 0.5f) / samples);
                        // just inside the edge: 1.6 where there is room (the fighter's radius 1.3 + a little), else 0.4 (a pipe, a beam)
                        var spot = onEdge - e.outward * 1.6f;
                        if (world.RegionAt(spot) != region) { spot = onEdge - e.outward * 0.4f; if (world.RegionAt(spot) != region) continue; }
                        float baseYaw = Quaternion.LookRotation(e.outward).eulerAngles.y;
                        foreach (float dy in new[] { 0f, -30f, 30f })
                            foreach (var p in Variants(spot, baseYaw + dy)) Try(p, spot, baseYaw + dy, region);
                        if (frame.Elapsed.TotalMilliseconds > frameBudgetMs)
                        {
                            progress = edgesTotal > 0 ? Mathf.Min(0.95f, (float)edgesDone / edgesTotal) : 0f;
                            state = $"ищу прыжки: участков {reachable.Count}, опытов {sims}, найдено {best.Count}";
                            yield return null; frame.Restart();
                        }
                    }
                }
            }
            // phase 2: aimed tries at what is still grey. The edge directions can miss a way in (a gap between fences, a beam end): for
            // every unreachable region near the reachable ones, from the 6 nearest reachable spots, every move aimed straight at it.
            int before = reachable.Count;
            var reachTris = new List<int>(); for (int t = 0; t < world.tris.Length / 3; t++) if (reachable.Contains(world.triRegion[t])) reachTris.Add(t);
            var greyRegions = Enumerable.Range(0, world.regionCount).Where(r => !reachable.Contains(r) && world.regionArea[r] >= 20f).ToList();
            foreach (int u in greyRegions)
            {
                if (reachable.Contains(u)) continue;
                var uTris = Enumerable.Range(0, world.tris.Length / 3).Where(t => world.triRegion[t] == u).Select(Centroid).ToList();
                // the reachable spots nearest to the region (horizontally within 60), at least 5 apart
                var cands = new List<(Vector3 from, Vector3 at, float d)>();
                foreach (int t in reachTris)
                {
                    var c = Centroid(t); Vector3 at = uTris[0]; float bd = float.MaxValue;
                    foreach (var q in uTris) { float d = LabSim.Horizontal(q - c); if (d < bd) { bd = d; at = q; } }
                    if (bd < 60f) cands.Add((c, at, bd));
                }
                var starts = new List<(Vector3 from, Vector3 at)>();
                foreach (var c in cands.OrderBy(x => x.d)) { if (starts.All(s0 => LabSim.Horizontal(s0.from - c.from) > 5f)) starts.Add((c.from, c.at)); if (starts.Count >= 6) break; }
                foreach (var (from0, at) in starts)
                {
                    if (!NavMesh.SamplePosition(from0, out var h0, 2f, NavMesh.AllAreas)) continue;
                    int region = world.RegionAt(h0.position); if (region < 0 || !reachable.Contains(region)) continue;
                    var to = at - h0.position; to.y = 0f; if (to.sqrMagnitude < 0.01f) continue;
                    float yaw0 = Quaternion.LookRotation(to).eulerAngles.y;
                    foreach (float dy in new[] { 0f, -15f, 15f })
                        foreach (var p in Variants(h0.position, yaw0 + dy)) { probes++; Try(p, h0.position, yaw0 + dy, region); }
                    if (frame.Elapsed.TotalMilliseconds > frameBudgetMs)
                    {
                        state = $"добираю серые места: участков {reachable.Count}, опытов {sims}, найдено {best.Count}";
                        yield return null; frame.Restart();
                    }
                }
            }
            if (reachable.Count == before) break;   // nothing new: done
        }
        links.AddRange(best.Values);
        for (int i = 0; i < links.Count; i++) links[i].id = i;
        seconds = (float)sw.Elapsed.TotalSeconds; progress = 1f; done = true;
        state = $"готово: {links.Count} прыжков, {reachable.Count} участков достижимы, {sims} опытов ({probes} прицельных) за {seconds:F0} с";
        Save();
    }

    /// <summary>A crossing the owner made himself (lab walk mode) from one region to another: a walk link, proven by a real player
    /// on the real motor. Returns true when it made a grey region reachable.</summary>
    public bool AddObserved(Vector3 from, Vector3 to, float time)
    {
        int ra = world.RegionAt(from, 2f), rb = world.RegionAt(to, 2f);
        if (ra < 0 || rb < 0 || ra == rb) return false;
        var d = to - from; d.y = 0f; if (d.sqrMagnitude < 0.01f) return false;
        var link = new Link { move = LabSim.Move.Walk, from = from, to = to, yaw = Quaternion.LookRotation(d).eulerAngles.y, runUp = Mathf.Max(0.3f, time),
            time = time, rise = to.y - from.y, distance = d.magnitude, fromRegion = ra, toRegion = rb, path = new[] { from, to }, observed = true, id = links.Count };
        links.Add(link); thinned = null;
        bool newly = reachable.Contains(ra) && reachable.Add(rb);
        Save();
        return newly;
    }

    public static float PathLength(NavMeshPath p) { float l = 0f; for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]); return l; }

    // ---------------- routes ----------------

    public class Route
    {
        public readonly List<(Vector3 from, Vector3 to, Link link)> legs = new List<(Vector3, Vector3, Link)>();   // link == null: walk
        public float time;
        public string Describe()
        {
            var parts = legs.Select(l => l.link == null ? $"бег {LabSim.Horizontal(l.to - l.from):F0}" : LabSim.RuName(l.link.move).ToLowerInvariant() + $" ({l.link.Params().Recipe()})" + (l.link.fallDamage > 0 ? $" [−{l.link.fallDamage} HP от падения]" : ""));
            return $"≈{time:F1} с: " + string.Join(" → ", parts);
        }
    }

    const float RunSpeed = 16.12f;

    float WalkTime(Vector3 a, Vector3 b, NavMeshPath path) =>
        NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? PathLength(path) / RunSpeed : float.PositiveInfinity;

    /// <summary>A cheap walking estimate inside one region (straight line × 1.3); the played route walks the real NavMesh path.</summary>
    static float WalkEstimate(Vector3 a, Vector3 b) => Vector3.Distance(a, b) * 1.3f / RunSpeed;

    /// <summary>What a move costs on top of its time when choosing a route: plain running first, a jump only when it is needed or
    /// clearly quicker, rockets last (they cost health and a weapon switch). Seconds.</summary>
    public static float Penalty(LabSim.Move m)
    {
        switch (m)
        {
            case LabSim.Move.Walk: return 0f;
            case LabSim.Move.Jump: return 0.3f; case LabSim.Move.RunJump: return 0.5f; case LabSim.Move.Bunnyhop: return 0.8f; case LabSim.Move.Bat: return 0.8f;
            // rockets: only when nothing else gets there (the owner's rule: self-damage; the spawn-invulnerability boost is a bot rule)
            case LabSim.Move.Shotgun: return 1.5f; case LabSim.Move.Rocket: return 30f; default: return 35f;
        }
    }

    /// <summary>Penalty of the move plus the fall damage: 40 HP ≈ 6 s, 80 HP ≈ 12 s of detour.</summary>
    public static float Cost(Link l) => Penalty(l.move) + l.fallDamage * 0.15f;

    int thinnedMask = -1;
    Dictionary<int, List<Link>> thinned;
    /// <summary>Per start region: for every region it leads to, the quickest link and then up to 7 more spread over the start region
    /// (each the farthest from those already taken), so routes can start a jump near wherever the bot is.</summary>
    Dictionary<int, List<Link>> Thinned(int mask)
    {
        if (thinned != null && thinnedMask == mask) return thinned;
        thinned = new Dictionary<int, List<Link>>(); thinnedMask = mask;
        foreach (var g in links.Where(l => (mask & (1 << (int)l.move)) != 0).GroupBy(l => (l.fromRegion, l.toRegion)))
        {
            var all = g.OrderBy(l => l.time + Cost(l)).ToList(); var pick = new List<Link> { all[0] };
            while (pick.Count < 8 && pick.Count < all.Count)
            {
                var far = all.Where(l => !pick.Contains(l)).OrderByDescending(l => pick.Min(q => (q.from - l.from).sqrMagnitude)).First();
                pick.Add(far);
            }
            if (!thinned.TryGetValue(g.Key.fromRegion, out var list)) thinned[g.Key.fromRegion] = list = new List<Link>();
            list.AddRange(pick);
        }
        return thinned;
    }

    /// <summary>The quickest way from a to b with walking and the found links; null if there is none.</summary>
    public Route FindRoute(Vector3 a, Vector3 b, int mask = ~0)
    {
        int ra = world.RegionAt(a, 3f), rb = world.RegionAt(b, 3f);
        if (ra < 0 || rb < 0) return null;
        var byRegion = Thinned(mask);
        var nearA = links.Where(l => l.fromRegion == ra && (mask & (1 << (int)l.move)) != 0 && LabSim.Horizontal(l.from - a) < 25f).ToList();   // all allowed jumps right next to the start
        var path = new NavMeshPath();
        // states: -1 = a, i = landing of links[i]
        var dist = new Dictionary<int, float> { [-1] = 0f }; var prev = new Dictionary<int, int>(); var done = new HashSet<int>();
        var open = new List<int> { -1 };
        float bestEnd = float.PositiveInfinity; int bestLast = int.MinValue;
        while (open.Count > 0)
        {
            int s = open.OrderBy(x => dist[x]).First(); open.Remove(s);
            if (!done.Add(s)) continue;
            float d = dist[s]; if (d >= bestEnd) break;
            Vector3 pos = s < 0 ? a : links[s].to; int region = s < 0 ? ra : links[s].toRegion;
            if (region == rb) { float end = d + WalkEstimate(pos, b); if (end < bestEnd) { bestEnd = end; bestLast = s; } }
            var outs = byRegion.TryGetValue(region, out var o) ? o : new List<Link>();
            if (s < 0) outs = outs.Concat(nearA).Distinct().ToList();
            foreach (var l in outs)
            {
                int i = l.id; if (done.Contains(i)) continue;
                float nd = d + WalkEstimate(pos, l.from) + l.time + Cost(l);
                if (float.IsInfinity(nd)) continue;
                if (!dist.TryGetValue(i, out float od) || nd < od) { dist[i] = nd; prev[i] = s; open.Add(i); }
            }
        }
        if (bestLast == int.MinValue) return null;
        var route = new Route { time = bestEnd };
        var chain = new List<int>(); for (int s = bestLast; s >= 0; s = prev[s]) chain.Add(s); chain.Reverse();
        Vector3 at = a;
        foreach (int i in chain)
        {
            var l = links[i];
            if (LabSim.Horizontal(l.from - at) > 1f) route.legs.Add((at, l.from, null));
            route.legs.Add((l.from, l.to, l)); at = l.to;
        }
        if (LabSim.Horizontal(b - at) > 1f) route.legs.Add((at, b, null));
        return route;
    }

    /// <summary>Plays the route on the ghost: one recording of the whole way (positions, view, in the air) for the replay.</summary>
    public LabSim.Result Play(Route route, Vector3 a)
    {
        var all = new LabSim.Result { start = a, dt = 1f / LabSim.BotFps };
        LabSim.Place(ghost, a, 0f);
        foreach (var leg in route.legs)
        {
            LabSim.Result r;
            if (leg.link == null) r = LabSim.WalkTo(ghost, leg.to);
            else
            {
                var p = leg.link.Params();
                // the bot walked to the link's start; it starts the move from there, standing (as the search simulated it)
                r = LabSim.Run(ghost, p);
                all.events.Add(new LabSim.Event { t = all.path.Count * all.dt, what = LabSim.RuName(p.move) + ": " + p.Recipe(), at = p.feet });
                if (r.landed) LabSim.Place(ghost, r.landing, ghost.transform.eulerAngles.y);
            }
            all.path.AddRange(r.path); all.views.AddRange(r.views); all.air.AddRange(r.air); all.weapons.AddRange(r.weapons);
        }
        all.landing = all.path.Count > 0 ? all.path[all.path.Count - 1] : a;
        all.time = all.path.Count * all.dt;
        return all;
    }
}
