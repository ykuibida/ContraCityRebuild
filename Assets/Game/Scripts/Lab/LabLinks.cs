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
    public const int Version = 1;   // bump when the search or LabSim changes: cached files of another version are ignored

    [System.Serializable]
    public class Link
    {
        public LabSim.Move move; public Vector3 from, to; public float yaw, runUp, pitch, fireDelay; public int hops, shots; public bool backwards;
        public float time, rise, peak, distance; public int fromRegion, toRegion; public bool shortcut;
        public Vector3[] path;   // decimated, for drawing
        [System.NonSerialized] public int id;
        public LabSim.Params Params() => new LabSim.Params { move = move, feet = from, yaw = yaw, runUp = runUp, pitch = pitch, fireDelay = fireDelay, hops = hops, shots = shots, backwards = backwards, fps = LabSim.BotFps };
    }

    [System.Serializable] class File_ { public int version; public string map; public float seconds; public int sims; public List<Link> links; public List<int> reachable; }

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
            if (f == null || f.version != Version || f.links == null) return false;
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
        File.WriteAllText(p, JsonUtility.ToJson(new File_ { version = Version, map = map, seconds = seconds, sims = sims, links = links, reachable = reachable.ToList() }));
    }

    // the moves tried at every start spot: (move, run-up, extra)
    static IEnumerable<LabSim.Params> Variants(Vector3 feet, float yaw)
    {
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
    static float RunUpLength(LabSim.Params p) => p.runUp <= 0.01f ? 0f : p.runUp * (p.move == LabSim.Move.Bat ? 20f : 16f) * 0.85f;

    public IEnumerator Search(float frameBudgetMs = 25f)
    {
        var sw = Stopwatch.StartNew(); var frame = Stopwatch.StartNew();
        links.Clear(); reachable.Clear(); sims = 0; done = false; thinned = null;
        var queue = new Queue<int>();
        foreach (var m in world.markers) if (LabWorld.IsSpawn(m.kind) && m.region >= 0 && reachable.Add(m.region)) queue.Enqueue(m.region);
        var best = new Dictionary<(int, int, int, int, int, int), Link>();
        int edgesDone = 0, edgesTotal = world.boundary.Count;
        var path = new NavMeshPath();
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
                    var spot = onEdge - e.outward * 1.6f;   // just inside the edge (radius 1.3 + a little)
                    if (world.RegionAt(spot) != region) continue;
                    float baseYaw = Quaternion.LookRotation(e.outward).eulerAngles.y;
                    foreach (float dy in new[] { 0f, -30f, 30f })
                    {
                        float yaw = baseYaw + dy; var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        foreach (var p in Variants(spot, yaw))
                        {
                            float back = RunUpLength(p);
                            var from = spot - dir * back;
                            if (back > 0f && (world.RegionAt(from) != region || Physics.CapsuleCast(from + Vector3.up * 2.6f, from + Vector3.up * 5.4f, 1.2f, dir, back, ~(1 << CharacterMotor.GhostLayer), QueryTriggerInteraction.Ignore))) continue;
                            if (!NavMesh.SamplePosition(from, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                            p.feet = hit.position; p.yaw = yaw; p.fps = LabSim.BotFps; p.maxTime = 5f;
                            var r = LabSim.Run(ghost, p); sims++;
                            if (!r.landed || !r.onWalkable || r.fallDamage > 0) continue;
                            int to = world.RegionAt(r.landing);
                            if (to < 0) continue;
                            bool shortcut = false;
                            if (to == region)
                            {
                                if (r.Rise < 1.6f && LabSim.Horizontal(r.landing - p.feet) < 12f) continue;
                                float walk = NavMesh.CalculatePath(p.feet, r.landing, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? PathLength(path) : 9999f;
                                if (walk - LabSim.Horizontal(r.landing - p.feet) < 25f) continue;
                                shortcut = true;
                            }
                            var link = new Link
                            {
                                move = p.move, from = p.feet, to = r.landing, yaw = yaw, runUp = p.runUp, pitch = p.pitch, fireDelay = p.fireDelay, hops = p.hops, shots = p.shots, backwards = p.backwards,
                                time = r.time, rise = r.Rise, peak = r.maxRise, distance = LabSim.Horizontal(r.landing - p.feet), fromRegion = region, toRegion = to, shortcut = shortcut,
                                path = r.path.Where((x, i) => i % 8 == 0 || i == r.path.Count - 1).ToArray(),
                            };
                            var key = (Mathf.RoundToInt(p.feet.x / 6f), Mathf.RoundToInt(p.feet.z / 6f), Mathf.RoundToInt(r.landing.x / 4f), Mathf.RoundToInt(r.landing.y / 2f), Mathf.RoundToInt(r.landing.z / 4f), region);
                            if (!best.TryGetValue(key, out var old) || link.time < old.time) best[key] = link;
                            if (reachable.Add(to)) queue.Enqueue(to);
                        }
                    }
                    if (frame.Elapsed.TotalMilliseconds > frameBudgetMs)
                    {
                        progress = edgesTotal > 0 ? (float)edgesDone / edgesTotal : 0f;
                        state = $"ищу прыжки: участков {reachable.Count}, опытов {sims}, найдено {best.Count}";
                        yield return null; frame.Restart();
                    }
                }
            }
        }
        links.AddRange(best.Values);
        for (int i = 0; i < links.Count; i++) links[i].id = i;
        seconds = (float)sw.Elapsed.TotalSeconds; progress = 1f; done = true;
        state = $"готово: {links.Count} прыжков, {reachable.Count} участков достижимы, {sims} опытов за {seconds:F0} с";
        Save();
    }

    public static float PathLength(NavMeshPath p) { float l = 0f; for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]); return l; }

    // ---------------- routes ----------------

    public class Route
    {
        public readonly List<(Vector3 from, Vector3 to, Link link)> legs = new List<(Vector3, Vector3, Link)>();   // link == null: walk
        public float time;
        public string Describe()
        {
            var parts = legs.Select(l => l.link == null ? $"бег {LabSim.Horizontal(l.to - l.from):F0}" : LabSim.RuName(l.link.move).ToLowerInvariant() + $" ({l.link.Params().Recipe()})");
            return $"≈{time:F1} с: " + string.Join(" → ", parts);
        }
    }

    const float RunSpeed = 16.12f;

    float WalkTime(Vector3 a, Vector3 b, NavMeshPath path) =>
        NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? PathLength(path) / RunSpeed : float.PositiveInfinity;

    /// <summary>A cheap walking estimate inside one region (straight line × 1.3); the played route walks the real NavMesh path.</summary>
    static float WalkEstimate(Vector3 a, Vector3 b) => Vector3.Distance(a, b) * 1.3f / RunSpeed;

    Dictionary<int, List<Link>> thinned;
    /// <summary>Per start region: for every region it leads to, the quickest link and then up to 7 more spread over the start region
    /// (each the farthest from those already taken), so routes can start a jump near wherever the bot is.</summary>
    Dictionary<int, List<Link>> Thinned()
    {
        if (thinned != null) return thinned;
        thinned = new Dictionary<int, List<Link>>();
        foreach (var g in links.GroupBy(l => (l.fromRegion, l.toRegion)))
        {
            var all = g.OrderBy(l => l.time).ToList(); var pick = new List<Link> { all[0] };
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
    public Route FindRoute(Vector3 a, Vector3 b)
    {
        int ra = world.RegionAt(a, 3f), rb = world.RegionAt(b, 3f);
        if (ra < 0 || rb < 0) return null;
        var byRegion = Thinned();
        var nearA = links.Where(l => l.fromRegion == ra && LabSim.Horizontal(l.from - a) < 25f).ToList();   // all jumps right next to the start
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
                float nd = d + WalkEstimate(pos, l.from) + l.time;
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
            all.path.AddRange(r.path); all.views.AddRange(r.views); all.air.AddRange(r.air);
        }
        all.landing = all.path.Count > 0 ? all.path[all.path.Count - 1] : a;
        all.time = all.path.Count * all.dt;
        return all;
    }
}
