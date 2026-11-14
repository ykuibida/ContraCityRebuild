// Port of the shot geometry of the New Era client: static class 啫 (NE_Scripts_Decrypted/啫.cs) = Penta 2016 ShotCalculator.cs.
// Stage 2: StraightShot (pistols, rifles, sniper rifles, gatling), CastPreciseShot, the dead-player-skipping casts, StraightScan
// (crosshair state) and GunShot. Line numbers: NE 啫.cs.
// Targets: a hit on an actor collider (Bip01 …, Ship…) belongs to a CombatPlayer; this offline build has no other fighters yet,
// so GetCombatPlayerFromHitTransform finds none and every shot is a wall shot (bots / players: later). Dropped: the anti-cheat
// requests (SendEnterBaseRequest 26 / 28, CheckNickname) and the RTTX debug lines.
using System;
using System.Collections.Generic;
using UnityEngine;

public static class ShotCalculator
{
    static readonly HashSet<string> ActorHitNames = new HashSet<string>
    {
        "ShipModel", "ShipBase", "ShipEngine", "ShipCabin", "Bip01", "Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2",
        "Bip01 Spine3", "Bip01 Neck", "Bip01 Head", "Bip01 R Calf", "Bip01 L Calf", "Bip01 R Thigh", "Bip01 L Thigh", "Bip01 R UpperArm",
        "Bip01 L UpperArm", "Bip01 R Forearm", "Bip01 L Forearm", "Bip01 R Hand", "Bip01 L Hand", "Bip01 R Foot", "Bip01 L Foot",
        "Bip01 R Toe0", "Bip01 L Toe0",
    };

    static bool IsActorHitName(string a) => a != null && (ActorHitNames.Contains(a) || a == "Monster");   // :306-349

    /// <summary>GetCombatPlayerFromHitTransform (:709-737): the fighter that owns an actor collider — none offline yet.</summary>
    static Component GetCombatPlayerFromHitTransform(Transform a) => null;

    static bool IsDeadPlayerHit(Transform a) => false;   // :351-355 (no other fighters)

    static bool RaycastIgnoringDeadPlayers(Ray a, out RaycastHit b, float c, int d)   // :357-385
    {
        if (!Physics.Raycast(a, out b, c, d)) return false;
        if (b.transform == null || !IsDeadPlayerHit(b.transform)) return true;
        var all = Physics.RaycastAll(a, c, d);
        if (all == null || all.Length == 0) { b = default; return false; }
        Array.Sort(all, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in all) if (h.transform != null && !IsDeadPlayerHit(h.transform)) { b = h; return true; }
        b = default;
        return false;
    }

    static bool SphereCastIgnoringDeadPlayers(Ray a, float b, out RaycastHit c, float d, int e)   // :387-415
    {
        if (!Physics.SphereCast(a, b, out c, d, e)) return false;
        if (c.transform == null || !IsDeadPlayerHit(c.transform)) return true;
        var all = Physics.SphereCastAll(a.origin, b, a.direction, d, e);
        if (all == null || all.Length == 0) { c = default; return false; }
        Array.Sort(all, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in all) if (h.transform != null && !IsDeadPlayerHit(h.transform)) { c = h; return true; }
        c = default;
        return false;
    }

    /// <summary>CastPreciseShot (:417-429): the 0.2 sphere cast, unless the exact ray hits an actor collider no farther than 1.5 behind it.</summary>
    static bool CastPreciseShot(Ray a, float b, int c, out RaycastHit d)
    {
        bool sphere = SphereCastIgnoringDeadPlayers(a, b, out var c2, float.PositiveInfinity, c);
        if (RaycastIgnoringDeadPlayers(a, out var b2, float.PositiveInfinity, c) && b2.transform != null && IsActorHitName(b2.transform.name) && (!sphere || b2.distance <= c2.distance + 1.5f))
        {
            d = b2;
            return true;
        }
        d = c2;
        return sphere;
    }

    /// <summary>StraightScan (:783-839): the id of the fighter under the ray, -1 if none. (New Era read hit.transform.name even when
    /// nothing was hit; here a miss is simply -1.)</summary>
    public static int StraightScan(Ray a, bool b)
    {
        int mask = ~(b ? 516 : 772);
        if (!RaycastIgnoringDeadPlayers(a, out var hit, float.PositiveInfinity, mask) || hit.transform == null) return -1;
        if (ActorHitNames.Contains(hit.transform.name) && GetCombatPlayerFromHitTransform(hit.transform) != null) return 0;
        return -1;
    }

    /// <summary>StraightShot (:912-1080): layers 2, 8, 9 ignored (bots: 2, 9), the precise cast, then the hit name decides the target
    /// (actor collider → player, "Grenade" / "Mine" / "Turret" → player item, "Monster" → bot); the shot lands at the hit point with its
    /// normal, null when nothing is hit or the hit is farther than <paramref name="d"/> (0 = unlimited).</summary>
    public static Shot StraightShot(Ray a, Transform b, int c, int d, bool e)
    {
        int mask = ~(e ? 516 : 772);
        if (!CastPreciseShot(a, 0.2f, mask, out var hit)) return null;
        int targetId = -1;
        ShotTargetType targetType = ShotTargetType.NONE;
        Transform targetTransform = null;
        HitZone hitZone = HitZone.BASE;
        // the target switch (:932-1060) needs other fighters / their items: none offline yet
        if (IsActorHitName(hit.transform.name)) { var p = GetCombatPlayerFromHitTransform(hit.transform); if (p != null) { targetId = 0; targetType = ShotTargetType.PLAYER; targetTransform = p.transform; } }
        Shot shot = null;
        if (hit.distance < d || d == 0)
        {
            shot = new Shot(hit.point, hit.normal, (byte)c);
            if (targetId != -1) shot.Targets.Add(new ShotTarget { TargetID = targetId, TargetType = targetType, TargetTransform = targetTransform, HitZone = hitZone, ItemTimeStamp = -1 });
        }
        return shot;
    }

    /// <summary>GunShot (NE 啫.cs GunShot): a team-mate target is dropped — no targets offline yet, so the shot stays as it is.</summary>
    public static void GunShot(ShotController a, Shot b) { }

    /// <summary>SegmentShot (:2173-2262): the cone in front of the fighter (shotgun: the direct target or one assist target) — the
    /// cone looks for other fighters and their items, none offline yet.</summary>
    public static void SegmentShot(ShotController a, Shot b) { }

    /// <summary>PlayerManager.Scan (NE PlayerManager.cs:10195-10221): the first live fighter other than the local one within <paramref name="b"/>.</summary>
    public static int Scan(Vector3 a, float b) => -1;

    // ---------------- projectiles (stage 3) ----------------
    /// <summary>RocketShot (:1437-1475): aim through the crosshair (220 if nothing is hit), a 0.5 sphere cast from the muzzle; the rocket
    /// flies from NetworkTime + 150 at max(velocity, 20) × 1.15 (≤ +8) per 100 ms, at least 120 ms.</summary>
    public static Shot RocketShot(Ray a, Transform b, int c, float d)
    {
        int mask = ~772;
        Vector3 from = b != null ? b.position : a.origin;
        Vector3 dir = a.direction;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();
        bool hit = RaycastIgnoringDeadPlayers(a, out var h, float.PositiveInfinity, mask);
        Vector3 aim = !hit ? a.origin + dir * 220f : h.point;
        Vector3 v = aim - from;
        if (v.sqrMagnitude < 4f) v = dir;
        v.Normalize();
        var r2 = new Ray(from, v);
        float len = !hit ? 220f : Mathf.Max(5f, (aim - from).magnitude + 2f);
        Vector3 end = from + v * len;
        Vector3 normal = -v;
        if (SphereCastIgnoringDeadPlayers(r2, 0.5f, out h, len, mask)) { end = h.point; normal = h.normal; }
        float speed = Mathf.Max(d, 20f);
        float speed2 = Mathf.Min(speed * 1.15f, speed + 8f);
        long t0 = Shot.NetworkTime + 150;
        long dt = Math.Max(120L, (long)(100f * (end - r2.origin).magnitude / speed2));
        var s = new Shot(end, normal, (byte)c);
        s.Launch(from, v, t0, t0 + dt);
        s.addPoint(r2.origin);
        return s;
    }

    /// <summary>GrenadeShot (:1592-1660): aim through the crosshair (80 if nothing is hit), steps of velocity (grenade launcher: 0.35 ×
    /// below 15, else 9..18) with gravity 0.16 per step, bounces × 0.2 (stops below 0.45, or at once for sticky bombs), life / 50 steps
    /// (22..60), 20 steps per second from NetworkTime + 200.</summary>
    public static Shot GrenadeShot(Ray a, Transform b, int c, float d, float e, bool f)
    {
        int mask = ~772;
        Vector3 from = b != null ? b.position : a.origin;
        Vector3 dir = a.direction;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();
        Vector3 aim = !RaycastIgnoringDeadPlayers(a, out var h, float.PositiveInfinity, mask) ? a.origin + dir * 80f : h.point;
        Vector3 v = aim - from;
        if (v.sqrMagnitude < 9f) v = dir;
        v.Normalize();
        float speed = Mathf.Max(d, 9f);
        if ((byte)c == 9) speed = !(d >= 15f) ? Mathf.Clamp(d * 0.35f, 9f, 9f) : Mathf.Clamp(d, 9f, 18f);
        int steps = Mathf.RoundToInt(e / 50f);
        if (steps < 22) steps = 22;
        if (steps > 60) steps = 60;
        var r2 = new Ray(from, v);
        Vector3 step = v * speed;
        var path = new Shot(r2.origin, r2.direction, (byte)c);
        path.addPoint(r2.origin);
        int i;
        for (i = 1; i < steps; i++)
        {
            if (step.sqrMagnitude > 0.0001f)
            {
                if (RaycastIgnoringDeadPlayers(r2, out h, step.magnitude, mask))
                {
                    r2.origin = h.point;
                    step = Vector3.Reflect(step, h.normal) * 0.2f;
                    if (f || step.magnitude < 0.45f) { step = Vector3.zero; path.addPoint(r2.origin); break; }
                }
                else
                {
                    r2.origin += step;
                    step += new Vector3(0f, -0.16f, 0f);
                }
            }
            r2.direction = !(step.sqrMagnitude > 0.0001f) ? v : step.normalized;
            path.addPoint(r2.origin);
        }
        long t0 = Shot.NetworkTime + 200;
        long t1 = t0 + (long)(1000f * i / 20f);
        var s = new Shot(r2.origin, !(step.sqrMagnitude > 0.0001f) ? v : step.normalized, (byte)c);
        s.Launch(from, v * speed, t0, t1);
        s.Trajectory = path.Trajectory;
        return s;
    }

    static bool VorchunGrenadeRaycast(Ray a, out RaycastHit b, float c, int d)   // :1477-1500 (no fighters: the nearest hit)
    {
        b = default;
        var all = Physics.RaycastAll(a, c, d);
        if (all == null || all.Length == 0) return false;
        Array.Sort(all, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in all) if (h.transform != null && GetCombatPlayerFromHitTransform(h.transform) == null) { b = h; return true; }
        return false;
    }

    /// <summary>VorchunGrenadeShot (:1507-1590): «Ворчун» (GL_GrenadeLauncher03) and «Гадюка» (GL_Milkor_A): no shot without a hit; steps of
    /// max(2, velocity × 0.6), gravity 0.2, bounces keep 0.3 of the step (stop below 0.5), life / 100 steps; a target closer than 5
    /// ends at the first step.</summary>
    public static Shot VorchunGrenadeShot(Ray a, Transform b, int c, float d, float e, bool f, bool g)
    {
        int mask = -773;
        if (!VorchunGrenadeRaycast(a, out var h, float.PositiveInfinity, mask) || h.transform == null) return null;
        Vector3 from = b != null ? b.position : a.origin;
        Vector3 direction = h.point - from;
        float dist = direction.magnitude;
        if (direction.sqrMagnitude <= 0.0001f) direction = a.direction;
        var r2 = new Ray(from, direction);
        int steps = Mathf.RoundToInt(e / 100f);
        if (steps <= 0) steps = 2;
        float vel = d;
        if (vel <= 0f) vel = 2f;
        float k = Mathf.Max(2f, vel * 0.6f);
        Vector3 step = Vector3.Scale(r2.direction, new Vector3(k, k, k));
        Vector3 launchDir = step;
        Vector3 rayDir = step;
        var path = new Shot(r2.origin, rayDir, (byte)c);
        path.addPoint(r2.origin);
        int i = 1;
        while (i < steps)
        {
            bool hit = false;
            if (step.sqrMagnitude > 0f)
            {
                hit = VorchunGrenadeRaycast(new Ray(r2.origin, rayDir), out h, step.magnitude, mask);
                if (hit)
                {
                    float m = step.magnitude;
                    step = Vector3.Reflect(step, h.normal);
                    step.Normalize();
                    float n5 = 0.3f * (m - h.distance);
                    step = Vector3.Scale(step, new Vector3(n5, n5, n5));
                    r2.origin = h.point;
                    step.Normalize();
                    m *= 0.3f;
                    step = Vector3.Scale(step, new Vector3(m, m, m));
                    if (m < 0.5f) { r2.origin = h.point; rayDir = Vector3.zero; step = Vector3.zero; }
                }
                else
                {
                    r2.origin += step;
                    step += new Vector3(0f, -0.2f, 0f);
                }
            }
            bool stop = i == 1 && dist < 5f;
            if (!stop && f && hit && h.transform != null) stop = true;   // !IsVorchunPlayerHit: no fighters offline
            rayDir = step;
            path.addPoint(r2.origin);
            i++;
            if (stop) break;
        }
        long t0 = Shot.NetworkTime + 200;
        long t1 = t0 + (long)(1000f * i / 20f);
        var s = new Shot(r2.origin, rayDir, (byte)c);
        s.Launch(from, launchDir, t0, t1);
        s.Trajectory = path.Trajectory;
        return s;
    }

    // ---------------- explosions (:212-305, 1739-1866, 2105-2128) ----------------
    static float GetRadialDamageRadius(WeaponType a, float b)
    {
        float r, d;
        switch (a)
        {
            case WeaponType.ROCKET_LAUNCHER: r = 13.5f; d = 350f; break;
            case WeaponType.GRENADE_LAUNCHER: r = 9f; d = 350f; break;
            case WeaponType.BOMB_LAUNCHER: r = 8f; d = 300f; break;
            default: return Mathf.Clamp(b * 0.12f, 4f, 12f);
        }
        if (b > d) r *= Mathf.Clamp(b / d, 1f, 1.18f);
        return r;
    }

    static bool IsVorchunGrenadeLauncher(CombatWeapon a) => a != null && a.Type == WeaponType.GRENADE_LAUNCHER && (a.WeaponID == 104 || string.Equals(a.SystemName, "GL_GrenadeLauncher03", StringComparison.OrdinalIgnoreCase));
    static bool IsGadyukaGrenadeLauncher(CombatWeapon a) => a != null && a.Type == WeaponType.GRENADE_LAUNCHER && (a.WeaponID == 45 || string.Equals(a.SystemName, "GL_Milkor_A", StringComparison.OrdinalIgnoreCase));

    /// <summary>GetExplosionRadius (:291-304): «Гадюка» 20, «Ворчун» 13, else GetRadialDamageRadius (clan enhancers 150 / 151: ×1.1 — no clan).</summary>
    public static float GetExplosionRadius(CombatWeapon a, WeaponType b)
    {
        float dist = a == null ? 0f : a.Distance;
        // Owner's decision 12.11.2026 (deviation from NE :291-304, where «Гадюка» has 20): «Гадюка» blasts like «Гранатин», the
        // plain grenade-launcher radius, so «Ворчун» (13) stays the strongest. IsGadyukaGrenadeLauncher is kept for reference.
        return b != WeaponType.GRENADE_LAUNCHER || !IsVorchunGrenadeLauncher(a) ? GetRadialDamageRadius(b, dist) : 13f;
    }

    static short GetProjectileFallbackDamage(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER ? (short)51 : a == WeaponType.GRENADE_LAUNCHER || a == WeaponType.BOMB_LAUNCHER ? (short)44 : (short)0;   // :1739-1752

    static void EnsureProjectileTargetDamage(Shot a, ShotTarget b)   // :1754-1765
    {
        if (a != null && b != null && b.HealthDamage + b.EnergyDamage <= 0)
        {
            short dmg = GetProjectileFallbackDamage(a.WeaponType);
            if (dmg > 0) { b.HealthDamage = 0; b.EnergyDamage = dmg; }
        }
    }

    static bool IsSelfDamageWeapon(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER || a == WeaponType.GRENADE_LAUNCHER || a == WeaponType.BOMB_LAUNCHER;

    /// <summary>AddSelfExplosionTarget (:2105-2128): the shooter within the explosion radius of the blast (feet + 2) is a target too,
    /// with the fallback damage (rocket 51, grenade / sticky bomb 44). IsNoSelfDamageRocketWeapon is always false in New Era.</summary>
    static void AddSelfExplosionTarget(ShotController a, Shot b, CombatWeapon c)
    {
        if (a == null || b == null || !IsSelfDamageWeapon(b.WeaponType)) return;
        var vitals = a.GetComponent<PlayerVitals>();
        if (vitals == null || vitals.Dead) return;
        foreach (var x in b.Targets) if (x.TargetID == GameFlow.LocalId && x.TargetType == ShotTargetType.PLAYER) return;
        Vector3 me = a.transform.position + new Vector3(0f, 2f, 0f);
        if ((b.Origin - me).magnitude <= GetExplosionRadius(c, b.WeaponType))
        {
            var target = new ShotTarget { TargetID = GameFlow.LocalId, TargetType = ShotTargetType.PLAYER, TargetTransform = a.transform };
            EnsureProjectileTargetDamage(b, target);
            b.Targets.Add(target);
        }
    }

    /// <summary>RadialShot (:1767-1866): the direct target, then the shooter (self damage), the opponents' items and opponents in the
    /// radius — no other fighters offline yet, so only the shooter.</summary>
    public static void RadialShot(ShotController a, Shot b, bool c)
    {
        var w = a != null ? a.GetWeaponByType((int)b.WeaponType) : null;
        AddSelfExplosionTarget(a, b, w);
    }
}
