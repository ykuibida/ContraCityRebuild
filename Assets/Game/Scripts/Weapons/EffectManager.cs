// Port of the original EffectManager (New Era NE_Scripts_Decrypted/EffectManager.cs; Penta 2016 EffectManager.cs). Same class name,
// fields and script GUID (9a8e04050779e497b7f4a13a3e01001c): every map scene has one with its effect prefabs wired.
// Stage 2: machineGunEffect (pistols, rifles, sniper rifles, gatling shot), muzzleSparkEffect / muzzleSparkEffectAtPosition,
// sparkEffect, tracerEffect, reloadEffect, weaponBlockEffect and the prefab fallbacks; gatlingGunEffect.
// Stage 3: batEffect, shotGunEffect, rocket / grenade / bomb launcher effects, launcherEffect (explosion, knockback), the crossbow bolt
// (arrowTracerEffect) and HitEffect. Line numbers: NE EffectManager.cs.
// The fighter of the original calls (CombatPlayer b) is the local player here: b.Audio = SoundManager.LocalPlayerAudio, its weapon
// nodes = ShotController.Container (the New Era local fighter has only the first-person WeaponLook nodes).
// Not ported: bulletImpactMarkEffect (:953-995) — a New Era addition that is never called; GetBattleRemoteTracerOrigin / Target
// (:1494-1537) return the plain origin / target offline (no HTTP battle).
using System;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    public Material frozenMaterial;
    public GameObject laserGunPrefab, laserSparkPrefab, laserShaftSparkPrefab, grenadeLauncherShaftSparkPrefab, plasmaSparkPrefab,
        plasmaShaftSparkPrefab, rocketLauncherShaftSparkPrefab, machineGunShaftSparkPrefab, machineGunSparkPrefab, machineGunPrefab,
        railPrefab, blizzRailPrefab, blizzRailArcPrefab, blizzRailSparkPrefab, plasmaPrefab, exploPrefab, exploDeactivationPrefab,
        minePrefab, mineTimePrefab, whizbangPrefab, mortarWhizbangPrefab, mineRemotePrefab, turretTeslaMontagePrefab, turretTeslaPrefab,
        turretTeslaRedPrefab, turretTeslaBluePrefab, energyShieldPrefab, healthHitPrefab, ultraPrefab, ultraSparkPrefab, ultraShaftSparkPrefab,
        tripleRocketPrefab, spiralRailPrefab, powerLaserPrefab, powerLaserSparkPrefab, powerLaserShaftSparkPrefab, blizzChainArcPrefab,
        blizzChainSparkPrefab, ultraSonicPrefab, mineElectricPrefab, electricExploPrefab, energyBallPrefab, energyBallExploPrefab,
        turretMachineGunMontagePrefab, turretMachineGunPrefab, turretMachineGunRedPrefab, turretMachineGunBluePrefab;

    static EffectManager instance;
    public static EffectManager Instance => instance;
    readonly System.Random rnd = new System.Random();
    public System.Random RandomGenerator => rnd;
    readonly Dictionary<int, Dictionary<string, Transform>> weaponTransformCache = new Dictionary<int, Dictionary<string, Transform>>();
    readonly HashSet<string> missingEffectPrefabs = new HashSet<string>();

    public GameObject MachineGunShaftSparkPrefab { get => machineGunShaftSparkPrefab; set => machineGunShaftSparkPrefab = value; }
    public GameObject MachineGunSparkPrefab { get => machineGunSparkPrefab; set => machineGunSparkPrefab = value; }
    public GameObject MachineGunPrefab { get => machineGunPrefab; set => machineGunPrefab = value; }

    void Awake()   // :822-826
    {
        instance = this;
        EnsureFallbackPrefabs();
    }

    void EnsureFallbackPrefabs()   // :997-1007
    {
        machineGunSparkPrefab = ResolveEffectPrefab(machineGunSparkPrefab, "MachineGunSpark");
        machineGunShaftSparkPrefab = ResolveEffectPrefab(machineGunShaftSparkPrefab, "MachineGunShaftSpark");
        grenadeLauncherShaftSparkPrefab = ResolveEffectPrefab(grenadeLauncherShaftSparkPrefab, "grenadeLaucherShaftSpark");
        rocketLauncherShaftSparkPrefab = ResolveEffectPrefab(rocketLauncherShaftSparkPrefab, "RocketLauncherShaftSpark");
        machineGunPrefab = ResolveEffectPrefab(machineGunPrefab, "MachineGun");
        exploPrefab = ResolveEffectPrefab(exploPrefab, "Explo");
        exploDeactivationPrefab = ResolveEffectPrefab(exploDeactivationPrefab, "ExploDeactivation");
        healthHitPrefab = ResolveEffectPrefab(healthHitPrefab, "HealthHit");
    }

    GameObject ResolveEffectPrefab(GameObject a, string b)   // :1009-1021
    {
        if (a != null) return a;
        return Resources.Load("effects/" + b, typeof(GameObject)) as GameObject;
    }

    public void weaponBlockEffect(ShotController a, WeaponType b) { }   // :1023-1025 (empty in the original)

    /// <summary>reloadEffect (:1612-1713).</summary>
    public void reloadEffect(WeaponType a, AudioSource audio, string c, int d)
    {
        switch (a)
        {
            case WeaponType.FLAMER: case WeaponType.SNOW_GUN: case WeaponType.ACID_THROWER: case WeaponType.ELECTRO_SHOCKER: case WeaponType.BIO_SHOCKER: break;
            case WeaponType.GATLING_GUN: break;
            case WeaponType.HAND_GUN:
                SoundManager.Play(audio, c == "HG_Glock_S" ? "Glock_Reload" : "Makarov_Reload", AudioPlayMode.Play);
                break;
            case WeaponType.MACHINE_GUN:
                SoundManager.Play(audio, c == "MG_UMP45" || c.StartsWith("MG_AUG") ? "TMP_Reload" : "AK47_Reload", AudioPlayMode.Play);
                break;
            case WeaponType.SHOT_GUN:
            {
                if (c == "SG_DB") { SoundManager.Play(audio, "DB_Reload", AudioPlayMode.Play); break; }
                if (d == 0) d = 2;
                SoundManager.Play(audio, "Winchester1887_ReloadStart", AudioPlayMode.Play);
                float t = 0.3f;
                for (int k = 0; k < d; k++) { SoundManager.PlayAfterSeconds(audio, "Winchester1887_ReloadAmmo", AudioPlayMode.Play, t); t += 0.5f; }
                SoundManager.PlayAfterSeconds(audio, "Winchester1887_ReloadEnd", AudioPlayMode.Play, t);
                break;
            }
            case WeaponType.ROCKET_LAUNCHER:
                SoundManager.Play(audio, "RPG26_Reload", AudioPlayMode.Play);
                break;
            case WeaponType.SNIPER_RIFLE:
                if (!c.StartsWith("SR_Wildcat")) SoundManager.Play(audio, c == "SR_Steyr" || c == "SR_Arctic" ? "Steyr_Reload" : "SVD_Reload", AudioPlayMode.Play);
                break;
            case WeaponType.GRENADE_LAUNCHER: case WeaponType.BOMB_LAUNCHER:
                if (c.Contains("SnowLauncher"))
                {
                    if (d == 0) d = 2;
                    SoundManager.Play(audio, "SnowLauncher_ReloadStart", AudioPlayMode.Play);
                    float t = 0.7f;
                    for (int i = 0; i < d; i++) { SoundManager.PlayAfterSeconds(audio, "SnowLauncher_ReloadAmmo", AudioPlayMode.Play, t); t++; }
                    SoundManager.PlayAfterSeconds(audio, "SnowLauncher_ReloadEnd", AudioPlayMode.Play, t);
                }
                else if (c.Contains("Sticky"))
                {
                    if (d == 0) d = 2;
                    float t = 0.7f;
                    for (int j = 0; j < d; j++) { SoundManager.PlayAfterSeconds(audio, "Steyr_Reload", AudioPlayMode.Play, t); t++; }
                }
                break;
        }
    }

    /// <summary>gatlingGunEffect (:1740-1760): SPIN → "M134_Spin" loop, LAUNCH → "M134_Launch", BLOW → "M134_Stop" with PlayStop.</summary>
    public void gatlingGunEffect(ShotLaunchMode a, AudioSource audio, bool c)
    {
        if (audio == null) return;
        switch (a)
        {
            case ShotLaunchMode.SPIN: SoundManager.Play(audio, "M134_Spin", AudioPlayMode.PlayLoop); break;
            case ShotLaunchMode.LAUNCH: SoundManager.Play(audio, "M134_Launch", AudioPlayMode.Play); break;
            case ShotLaunchMode.BLOW: SoundManager.Play(audio, "M134_Stop", AudioPlayMode.PlayStop); break;
        }
    }

    // ---------------- weapon node lookup (:1303-1492) ----------------
    public Transform getWeaponTransformByName(Transform a, string b)
    {
        if (a == null || string.IsNullOrEmpty(b)) return null;
        int id = a.GetInstanceID();
        if (weaponTransformCache.TryGetValue(id, out var cache) && cache != null && cache.TryGetValue(b, out var hit))
        {
            if (hit != null) return hit;
            cache.Remove(b);
        }
        Transform found = null;
        foreach (var wl in a.GetComponentsInChildren<WeaponLook>(true)) if (wl.gameObject.name == b) { found = wl.transform; break; }
        if (found != null)
        {
            if (cache == null) { cache = new Dictionary<string, Transform>(); weaponTransformCache[id] = cache; }
            cache[b] = found;
        }
        return found;
    }

    Transform FindWeaponModelTransform(Transform a, string b)
    {
        if (a == null) return null;
        if (!string.IsNullOrEmpty(b))
        {
            var t = a.Find(b);
            if (t != null) return t;
            for (int i = 0; i < a.childCount; i++) { var c = a.GetChild(i); if (c != null && c.name == b) return c; }
        }
        for (int j = 0; j < a.childCount; j++) { var c = a.GetChild(j); if (c != null && c.gameObject.activeInHierarchy) return c; }
        if (a.childCount > 0) return a.GetChild(0);
        return a;
    }

    static bool IsRaiderMachineGun(string a) => !string.IsNullOrEmpty(a) && a.StartsWith("MG_AK47R", StringComparison.OrdinalIgnoreCase);

    static Transform FindWeaponMarkerRecursive(Transform a)
    {
        if (a == null) return null;
        foreach (var n in new[] { "Target", "Muzzle", "MuzzlePoint", "FirePoint", "ShotPoint", "BulletPoint", "Shaft" })
            if (string.Equals(a.name, n, StringComparison.OrdinalIgnoreCase)) return a;
        for (int j = 0; j < a.childCount; j++) { var t = FindWeaponMarkerRecursive(a.GetChild(j)); if (t != null) return t; }
        return null;
    }

    static Vector3 GetRaiderTracerOrigin(Transform a, Transform b, Vector3 c)
    {
        Vector3 result = b != null ? b.position : a != null ? a.position : Vector3.zero;
        if (a == null) return result;
        bool any = false; Bounds bounds = default;
        foreach (var r in a.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        if (!any)
        {
            var m = FindWeaponMarkerRecursive(a);
            return m != null && m != a ? m.position : result;
        }
        Vector3 v = c - bounds.center;
        if (v.sqrMagnitude < 0.0001f) return result;
        v.Normalize();
        Vector3 e = bounds.extents;
        float num = Mathf.Abs(v.x) * e.x + Mathf.Abs(v.y) * e.y + Mathf.Abs(v.z) * e.z;
        Vector3 p = bounds.center + v * num;
        return (p - bounds.center).sqrMagnitude > 9f ? result : p;
    }

    Transform FindWeaponTargetTransform(Transform a, string b)
    {
        var model = FindWeaponModelTransform(a, b);
        if (model == null) return null;
        var t = model.Find("Target");
        if (t != null) return t;
        if (IsRaiderMachineGun(b)) { var m = FindWeaponMarkerRecursive(model); if (m != null) return m; }
        return model;
    }

    /// <summary>machineGunEffect (:1762-1940): the shot sound on the fighter's audio, the wall spark with "BulletImpact" (no target),
    /// the muzzle flash on the model's Target and the tracer from it to the hit point; dual weapons (…L / …R) get both.</summary>
    public void machineGunEffect(Shot a, ShotController b, bool c)
    {
        if (b == null) return;
        Transform root = b.Container;
        AudioSource audio = SoundManager.LocalPlayerAudio;
        Transform target = null, target2 = null;
        string typeName = CombatWeapon.getName(a.WeaponType);
        var weapon = b.GetWeaponByType((int)a.WeaponType);
        string sname = weapon == null ? string.Empty : weapon.SystemName;
        Transform model = null;
        var node = getWeaponTransformByName(root, typeName);
        if (node != null)
        {
            model = FindWeaponModelTransform(node, sname);
            if (model != null) target = FindWeaponTargetTransform(node, sname);
        }
        else
        {
            var l = getWeaponTransformByName(root, typeName + "L");
            var r = getWeaponTransformByName(root, typeName + "R");
            if (l != null) { model = FindWeaponModelTransform(l, sname); target = FindWeaponTargetTransform(l, sname); }
            if (r != null) { if (model == null) model = FindWeaponModelTransform(r, sname); target2 = FindWeaponTargetTransform(r, sname); }
        }
        float f = 0.03f, e = 0.05f;   // trail time, start width
        switch (a.WeaponType)
        {
            case WeaponType.HAND_GUN:
                e = 0.03f; f = 0.02f;
                SoundManager.Play(audio, sname == "HG_Glock_S" ? "Glock_Shot" : "Makarov_Shot", AudioPlayMode.Play);
                break;
            case WeaponType.MACHINE_GUN:
            {
                e = 0.1f;
                string s = "AK47_Shot";
                if (weapon != null && weapon.SystemName.StartsWith("MG_UMP45")) s = weapon.SystemName.StartsWith("MG_UMP45D") ? "UMP45_Shot" : "UMP45D_Shot";
                else if (weapon != null && (weapon.SystemName.StartsWith("MG_AUG") || weapon.SystemName.StartsWith("MG_AssaultRifle03") || weapon.SystemName.StartsWith("MG_AK47R"))) s = "UMP45_Shot";
                SoundManager.Play(audio, s, AudioPlayMode.Play);
                if (weapon != null) SoundManager.PlayAfterSeconds(audio, s + "1", AudioPlayMode.Play, weapon.ShotTime / 20000000f);
                break;
            }
            case WeaponType.SHOT_GUN:
                e = 0.07f;
                SoundManager.Play(audio, sname == "SG_DB" ? "DB_Shot" : "Winchester1887_Shot", AudioPlayMode.Play);
                break;
            case WeaponType.SNIPER_RIFLE:
                e = 0.1f; f = 0.1f;
                if (weapon != null && weapon.SystemName.StartsWith("SR_Wildcat")) SoundManager.Play(audio, "Crossbow_Shot", AudioPlayMode.Play);
                else if (weapon != null && (weapon.SystemName.StartsWith("SR_Steyrf0rd", StringComparison.OrdinalIgnoreCase) || weapon.SystemName.StartsWith("SR_Steyrford", StringComparison.OrdinalIgnoreCase))) SoundManager.Play(audio, "SR_Steyrf0rd", AudioPlayMode.Play);
                else SoundManager.Play(audio, "SVD_Shot", AudioPlayMode.Play);
                break;
            case WeaponType.GATLING_GUN:
                if (sname == "GG_M249") { e = 0.2f; SoundManager.Play(audio, "M249_Shot", AudioPlayMode.PlayLoop); }
                else { e = 0.1f; SoundManager.Play(audio, "M134_Shot", AudioPlayMode.PlayLoop); }
                break;
        }
        if (target == null) target = b.transform;
        if (!string.IsNullOrEmpty(sname) && sname.StartsWith("SR_Wildcat"))   // :1882-1892 the crossbow: spark + bolt (destroyed on a fighter)
        {
            if (target != null)
            {
                var bolt = CharacterPrefabs.GetPrefab("CrossbowBoltPrefab");
                sparkEffect(a.Origin, a.Direction, laserSparkPrefab);
                bool g = !c || a.HasTargets;
                arrowTracerEffect(target.position, a.Origin, bolt, false, e, 0.015f, g);
            }
            return;
        }
        if (!a.HasTargets)
        {
            var spark = sparkEffect(a.Origin, a.Direction, machineGunSparkPrefab);
            if (spark != null) SoundManager.Play(spark.GetComponent<AudioSource>(), "BulletImpact", AudioPlayMode.Play);
        }
        if (target != null)
        {
            Vector3 origin = target.position;
            bool raider = IsRaiderMachineGun(sname);
            if (raider) origin = GetRaiderTracerOrigin(model, target, a.Origin);
            Vector3 to = a.Origin;   // GetBattleRemoteTracerTarget offline
            if (raider) muzzleSparkEffectAtPosition(model, origin, to, MachineGunShaftSparkPrefab, c);
            else muzzleSparkEffect(target, MachineGunShaftSparkPrefab, c);
            tracerEffect(origin, to, machineGunPrefab, false, e, f);
            if (target2 != null)
            {
                muzzleSparkEffect(target2, MachineGunShaftSparkPrefab, c);
                tracerEffect(target2.position, a.Origin, machineGunPrefab, false, e, f);
            }
        }
        else tracerEffect(b.transform.position, a.Origin, machineGunPrefab, false, e, f);
    }

    void muzzleSparkEffectAtPosition(Transform a, Vector3 b, Vector3 c, GameObject d, bool e)   // :1942-1961
    {
        if (a == null || !EnsureEffectPrefab(d, "muzzleSpark")) return;
        Vector3 v = c - b;
        Quaternion rot = a.rotation;
        if (v.sqrMagnitude > 0.0001f) rot = Quaternion.LookRotation(v.normalized);
        var go = Instantiate(d, b, rot);
        go.transform.parent = a;
        go.transform.position = b;
        go.transform.rotation = rot;
        if (e) go.layer = a.gameObject.layer;
    }

    /// <summary>muzzleSparkEffect (:1963-1975): the flash under the muzzle at local 0; for the local fighter on the weapon layer
    /// (only the root object, like the original).</summary>
    public void muzzleSparkEffect(Transform a, GameObject b, bool c)
    {
        if (a == null || !EnsureEffectPrefab(b, "muzzleSpark")) return;
        var go = Instantiate(b, Vector3.zero, Quaternion.identity);
        go.transform.parent = a;
        go.transform.localPosition = Vector3.zero;
        if (c) go.layer = a.gameObject.layer;
    }

    public GameObject sparkEffect(Vector3 a, Vector3 b, GameObject c)   // :1977-1990
    {
        if (!EnsureEffectPrefab(c, "spark")) return null;
        var go = Instantiate(c);
        go.transform.position = a;
        if (b != Vector3.zero) go.transform.rotation = Quaternion.LookRotation(b);
        return go;
    }

    /// <summary>tracerEffect (:2018-2041): flat speed 11 (6 for the random-shift variant), the trail time / widths per weapon.</summary>
    public void tracerEffect(Vector3 a, Vector3 b, GameObject c, bool d, float e, float f)
    {
        if (!EnsureEffectPrefab(c, "tracer")) return;
        var go = Instantiate(c);
        var tracer = go.GetComponent<Tracer>();
        float num = 0f;
        tracer.FlatSpeed = 11f;
        if (d) { tracer.FlatSpeed = 6f; num = 0f - System.Convert.ToSingle(rnd.NextDouble() * 0.004999999888241291); }
        go.transform.position = b * num + a * (1f - num);
        go.transform.LookAt(b);
        tracer.Target = b;
        if (tracer != null && tracer.Trail != null)
        {
            tracer.Trail.time = f;
            tracer.Trail.startWidth = e;
            tracer.Trail.endWidth = e * 2f;
        }
    }

    // ---------------- stage 4: stream weapons ----------------
    static Flamer FindFlamer(ShotController b)
    {
        Flamer f = b.Container ? b.Container.GetComponentInChildren<Flamer>() : null;   // the original: CombatPlayer.GetComponentInChildren
        return f != null ? f : b.GetComponentInChildren<Flamer>();
    }

    /// <summary>flamerEffect(BLOW) (:2758-2769): the stream goes out.</summary>
    public void flamerEffect(ShotLaunchMode a, ShotController b, bool c)
    {
        if (a != ShotLaunchMode.BLOW || b == null) return;
        var f = FindFlamer(b);
        if (f) f.fire(false);
    }

    /// <summary>flamerEffect(Shot) (:2771-2792): "Snowgun_Shot" for the snow gun, else "Flamer_Shot"; the Flamer stream on.</summary>
    public void flamerEffect(Shot a, ShotController b, bool c)
    {
        if (b == null) return;
        var w = b.GetWeaponByType((int)a.WeaponType);
        bool snow = w != null && w.SystemName != null && w.SystemName.Contains("Snowgun");
        SoundManager.Play(SoundManager.LocalPlayerAudio, snow ? "Snowgun_Shot" : "Flamer_Shot", AudioPlayMode.Play);
        var f = FindFlamer(b);
        if (f) f.fire(true);
    }

    /// <summary>acidThrowerEffect (:2740-2756): "FT1" / "FT2" at random, the stream on (no caller in New Era's own DoShot).</summary>
    public void acidThrowerEffect(ShotController a, bool b)
    {
        if (a == null) return;
        SoundManager.Play(SoundManager.LocalPlayerAudio, UnityEngine.Random.value >= 0.5f ? "FT1" : "FT2", AudioPlayMode.Play);
        var f = FindFlamer(a);
        if (f) f.fire(true);
    }

    // ---------------- stage 3: melee, shotgun, launchers, explosion, crossbow bolt ----------------
    /// <summary>batEffect (:1715-1738): the swing "Bat_Launch"; the hit: on a fighter "Bat_Shot_Enemy" (PlayStop), on a wall the
    /// laserSparkPrefab spark (BatSparkConcrete) and "Bat_Shot_Stone" (PlayStop).</summary>
    public void batEffect(Shot a, ShotController b, bool c)
    {
        if (b == null || a == null) return;
        var audio = SoundManager.LocalPlayerAudio;
        switch (a.LaunchMode)
        {
            case ShotLaunchMode.LAUNCH: SoundManager.Play(audio, "Bat_Launch", AudioPlayMode.Play); break;
            case ShotLaunchMode.SHOT:
                if (a.HasTargets) SoundManager.Play(audio, "Bat_Shot_Enemy", AudioPlayMode.PlayStop);
                else if (a.Direction.sqrMagnitude > 0.0001f) { sparkEffect(a.Origin, a.Direction, laserSparkPrefab); SoundManager.Play(audio, "Bat_Shot_Stone", AudioPlayMode.PlayStop); }
                break;
        }
    }

    /// <summary>shotGunEffect (:1539-1610): the muzzle flash, "DB_Shot" / "Winchester1887_Shot", then 6 visual pellets around the hit
    /// point on a disc of |shot| × 0.022: a spark each (laser spark on a fighter) and a random-shift tracer (width 0.15, time 0.03).</summary>
    public void shotGunEffect(Shot a, ShotController b, bool c)
    {
        if (b == null) return;
        Transform target = null;
        var random = rnd;
        string typeName = CombatWeapon.getName(a.WeaponType);
        var weapon = b.GetWeaponByType((int)a.WeaponType);
        string sname = weapon == null ? string.Empty : weapon.SystemName;
        var node = getWeaponTransformByName(b.Container, typeName);
        if (node != null)
        {
            var model = FindWeaponModelTransform(node, sname);
            if (model != null)
            {
                target = FindWeaponTargetTransform(getWeaponTransformByName(b.Container, typeName), sname);
                muzzleSparkEffect(target, MachineGunShaftSparkPrefab, c);
            }
            else target = b.transform;
        }
        if (target == null) target = b.transform;
        SoundManager.Play(SoundManager.LocalPlayerAudio, sname == "SG_DB" ? "DB_Shot" : "Winchester1887_Shot", AudioPlayMode.Play);
        Vector3 origin = target.position, to = a.Origin;   // GetBattleRemoteTracerOrigin / Target offline
        Vector3 v = to - origin;
        float len = v.magnitude;
        Vector3 fwd = !(len > 0.001f) ? a.Direction.normalized : v / len;
        Vector3 side = Vector3.Cross(fwd, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(fwd, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, fwd).normalized;
        float radius = len * 0.022f;
        for (int i = 0; i < 6; i++)
        {
            float f = (float)random.NextDouble() * (float)Math.PI * 2f;
            float r = Mathf.Sqrt((float)random.NextDouble()) * radius;
            Vector3 off = side * (Mathf.Cos(f) * r) + up * (Mathf.Sin(f) * r);
            sparkEffect(a.Origin + off, a.Direction, a.HasTargets ? laserSparkPrefab : machineGunSparkPrefab);
            tracerEffect(origin, to + off, machineGunPrefab, true, 0.15f, 0.03f);
        }
    }

    /// <summary>rocketLauncherEffect (:2092-2130): "RPG26_Shot", the rocket shaft spark on the RPG's muzzle marker, the rocket
    /// (whizbangPrefab, RocketTracer) from the muzzle, registered as the fighter's item.</summary>
    public void rocketLauncherEffect(Shot a, ShotController b, bool c)
    {
        if (b == null) return;
        SoundManager.Play(SoundManager.LocalPlayerAudio, "RPG26_Shot", AudioPlayMode.Play);
        Transform marker = null;
        var node = getWeaponTransformByName(b.Container, "RocketLauncherWeapon");
        if (node != null)
        {
            marker = FindWeaponMarkerRecursive(FindWeaponModelTransform(node, string.Empty));
            if (marker != null && rocketLauncherShaftSparkPrefab != null)
            {
                var sp = Instantiate(rocketLauncherShaftSparkPrefab, Vector3.zero, Quaternion.identity);
                sp.transform.parent = marker; sp.transform.localEulerAngles = Vector3.zero; sp.transform.localPosition = Vector3.zero;
            }
        }
        if (!EnsureEffectPrefab(whizbangPrefab, "rocket")) return;
        var go = Instantiate(whizbangPrefab);
        var tracer = go.GetComponent<RocketTracer>();
        if (marker != null) go.transform.position = marker.position;
        else if (a != null && a.StartOrigin.sqrMagnitude > 0.0001f) go.transform.position = a.StartOrigin;
        else go.transform.position = b.transform.position + Vector3.up * 1.2f;
        tracer.Launch(a, b, c);
        b.RegisterItem(tracer);
    }

    /// <summary>bombLauncherEffect (:2167-2219): "SnowLauncher_Shot", the grenade-launcher shaft spark on the model's Target, the sticky
    /// bomb (CharacterManager "BombPrefab" for Sticky models, else mortarWhizbangPrefab) tinted by team, BombTracer.</summary>
    public void bombLauncherEffect(Shot a, CombatWeapon b, ShotController c, bool d, bool e)
    {
        if (c == null) return;
        SoundManager.Play(SoundManager.LocalPlayerAudio, "SnowLauncher_Shot", AudioPlayMode.Play);
        Transform target = null;
        var node = getWeaponTransformByName(c.Container, "BombLauncherWeapon");
        if (node != null)
        {
            var bl = c.GetWeaponByType((int)WeaponType.BOMB_LAUNCHER);
            node = bl != null ? node.Find(bl.SystemName) : null;
            if (node != null && node.childCount > 0) target = node.Find("Target");
            if (target != null && grenadeLauncherShaftSparkPrefab != null)
            {
                var sp = Instantiate(grenadeLauncherShaftSparkPrefab, Vector3.zero, Quaternion.identity);
                sp.transform.parent = target; sp.transform.localEulerAngles = Vector3.zero; sp.transform.localPosition = Vector3.zero;
            }
        }
        var prefab = mortarWhizbangPrefab;
        if (node != null && node.name.Contains("Sticky")) prefab = CharacterPrefabs.GetPrefab("BombPrefab");
        if (!EnsureEffectPrefab(prefab, "bomb")) return;
        var go = Instantiate(prefab);
        var mr = go.GetComponentInChildren<MeshRenderer>();
        if (mr != null)
            switch (GameFlow.Instance != null ? GameFlow.Instance.LocalTeam : 0)
            {
                case 1: mr.material.color = new Color(1f, 82f / 255f, 82f / 255f); break;
                case 2: mr.material.color = new Color(70f / 255f, 132f / 255f, 1f); break;
            }
        var tracer = go.GetComponent<BombTracer>();
        go.transform.position = target != null ? target.position : c.transform.position;
        tracer.Launch(a, b, c, d, e);
        c.RegisterItem(tracer);
    }

    /// <summary>grenadeLauncherEffect (:2221-2262): "SnowLauncher_Shot", the shaft spark on the model's muzzle marker, the grenade
    /// (CharacterManager "GrenadePrefab", "SnowballPrefab" for Snow launchers) from the muzzle, GrenadeTracer.</summary>
    public void grenadeLauncherEffect(Shot a, ShotController b, bool c)
    {
        if (b == null) return;
        SoundManager.Play(SoundManager.LocalPlayerAudio, "SnowLauncher_Shot", AudioPlayMode.Play);
        Transform marker = null;
        var w = b.GetWeaponByType((int)WeaponType.GRENADE_LAUNCHER);
        string sname = w == null ? string.Empty : w.SystemName;
        var node = getWeaponTransformByName(b.Container, "GrenadeLauncherWeapon");
        var model = FindWeaponModelTransform(node, sname);
        if (model != null)
        {
            marker = FindWeaponMarkerRecursive(model);
            if (marker != null && grenadeLauncherShaftSparkPrefab != null)
            {
                var sp = Instantiate(grenadeLauncherShaftSparkPrefab, Vector3.zero, Quaternion.identity);
                sp.transform.parent = marker; sp.transform.localEulerAngles = Vector3.zero; sp.transform.localPosition = Vector3.zero;
            }
        }
        bool snow = (!string.IsNullOrEmpty(sname) && sname.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0) || (model != null && model.name.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0);
        var prefab = CharacterPrefabs.GetPrefab(snow ? "SnowballPrefab" : "GrenadePrefab");
        if (!EnsureEffectPrefab(prefab, "grenade")) return;
        var go = Instantiate(prefab);
        var tracer = go.GetComponent<GrenadeTracer>();
        if (marker != null) go.transform.position = marker.position;
        else if (a != null && a.StartOrigin.sqrMagnitude > 0.0001f) go.transform.position = a.StartOrigin;
        else go.transform.position = b.transform.position + Vector3.up * 1.2f;
        tracer.Launch(a, b, c);
        b.RegisterItem(tracer);
    }

    // explosion knockback (:715-820)
    static float GetExplosionImpulseRadiusFallback(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER ? 14f : a == WeaponType.GRENADE_LAUNCHER ? 9f : a == WeaponType.BOMB_LAUNCHER ? 8f : 12f;
    static float GetExplosionImpulseFactorFallback(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER ? 0.45f : a == WeaponType.GRENADE_LAUNCHER ? 0.3f : a == WeaponType.BOMB_LAUNCHER ? 0.22f : 0.4f;
    static float GetExplosionImpulseRadius(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER || a == WeaponType.GRENADE_LAUNCHER ? 10.5f : GetExplosionImpulseRadiusFallback(a);
    static float GetExplosionImpulseFactor(WeaponType a) => a == WeaponType.ROCKET_LAUNCHER ? 6.2f : a == WeaponType.GRENADE_LAUNCHER ? 3.1f : GetExplosionImpulseFactorFallback(a);
    static float GetExplosionImpulseVerticalOffset(CombatWeapon a) => a != null && (a.Type == WeaponType.ROCKET_LAUNCHER || a.Type == WeaponType.GRENADE_LAUNCHER) ? 6f : 5f;
    static bool IsVorchunGrenadeWeapon(CombatWeapon a) => a != null && a.Type == WeaponType.GRENADE_LAUNCHER && (a.WeaponID == 104 || a.SystemName == "GL_GrenadeLauncher03" || (a.SystemName ?? "").IndexOf("GrenadeLauncher03", StringComparison.OrdinalIgnoreCase) >= 0);
    static bool IsTrolleybusinaRocketWeapon(CombatWeapon a) => a != null && a.Type == WeaponType.ROCKET_LAUNCHER && (a.WeaponID == 59 || a.SystemName == "RL_RPG7B02" || (a.SystemName ?? "").IndexOf("RPG7B02", StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>launcherEffect (:2619-2717): the explosion (ExploSnowPrefab for the SnowLauncher, else exploPrefab) at the blast point;
    /// the scorch projector is off for sticky bombs and beyond the quality distance (Good 24000, Simple 48000, Beautiful / Fantastic
    /// 90000 squared units, none on Fast / Fastest); "RPG26_Explosion" on the explosion's source; the local fighter within the impulse
    /// radius (rockets / grenades 10.5) is pushed by (radius − distance) × factor (6.2 / 3.1; «Ворчун» × 1.2, «Троллебузина» / 3 for
    /// others' blasts; sticky bombs × 0.33, and only with targets) from 6 (5) below the blast.</summary>
    public void launcherEffect(Shot a, ShotController b, ShotController c, bool d)
    {
        if (a == null || b == null) return;
        var weapon = b.GetWeaponByType((int)a.WeaponType);
        string sname = weapon == null ? string.Empty : weapon.SystemName ?? string.Empty;
        GameObject go;
        if (sname.Contains("SnowLauncher"))
        {
            var snow = CharacterPrefabs.GetPrefab("ExploSnowPrefab");
            go = Instantiate(snow != null ? snow : exploPrefab);
        }
        else
        {
            go = Instantiate(exploPrefab);
            if (a.WeaponType == WeaponType.BOMB_LAUNCHER) { var pj = go.GetComponentInChildren<Projector>(); if (pj != null) pj.enabled = false; }
            float dist = float.MaxValue;
            if (GameFlow.Instance != null && GameFlow.Instance.Player) dist = (GameFlow.Instance.Player.transform.position - a.Origin).sqrMagnitude;
            float limit = 0f;
            switch (QualitySettings.GetQualityLevel())   // 喞.QualityLevel = the Unity quality level (Fastest … Fantastic)
            {
                case 0: case 1: limit = 0f; break;
                case 3: limit = 24000f; break;
                case 2: limit = 48000f; break;
                default: limit = 90000f; break;
            }
            if (dist >= limit) { var pj = go.GetComponentInChildren<Projector>(); if (pj != null) pj.enabled = false; }
        }
        go.transform.position = a.Origin;
        var src = go.GetComponent<AudioSource>();
        if (src == null) src = go.GetComponentInChildren<AudioSource>();
        if (src != null) SoundManager.Play(src, "RPG26_Explosion", AudioPlayMode.Play);
        if (a.Direction != Vector3.zero) go.transform.rotation = Quaternion.LookRotation(a.Direction);
        if (c == null) return;
        float distance = Vector3.Distance(c.transform.position, go.transform.position);
        float radius = GetExplosionImpulseRadius(a.WeaponType);
        float factor = GetExplosionImpulseFactor(a.WeaponType);
        if (!d && IsVorchunGrenadeWeapon(weapon)) factor *= 1.2f;
        if (!d && IsTrolleybusinaRocketWeapon(weapon)) factor /= 3f;
        if (a.WeaponType == WeaponType.BOMB_LAUNCHER && !a.HasTargets) { b.UnregisterItem(a.TimeStamp); return; }
        if (distance < radius)
        {
            var motor = c.GetComponentInChildren<CharacterMotor>();
            float power = Mathf.Max(0f, radius - distance) * factor;   // zombie types: / 3 boss, / 2 × 0.7 regular (no zombies yet)
            if (a.WeaponType == WeaponType.BOMB_LAUNCHER) power *= 0.33f;
            if (motor != null) motor.SetExplosionForce(go.transform.position - new Vector3(0f, GetExplosionImpulseVerticalOffset(weapon), 0f), power);
        }
        b.UnregisterItem(a.TimeStamp);
    }

    /// <summary>arrowTracerEffect (:1992-2016): the crossbow bolt, flat speed 9 (6 with the random shift), trail time / widths.</summary>
    public void arrowTracerEffect(Vector3 a, Vector3 b, GameObject c, bool d, float e, float f, bool g)
    {
        if (!EnsureEffectPrefab(c, "arrowTracer")) return;
        var go = Instantiate(c);
        var tracer = go.GetComponent<ArrowTracer>();
        tracer.destroy = g;
        float num = 0f;
        tracer.FlatSpeed = 9f;
        if (d) { tracer.FlatSpeed = 6f; num = 0f - Convert.ToSingle(rnd.NextDouble() * 0.004999999888241291); }
        go.transform.position = b * num + a * (1f - num);
        go.transform.LookAt(b);
        tracer.Target = b;
        if (tracer != null && tracer.Trail != null) { tracer.Trail.time = f; tracer.Trail.startWidth = e; tracer.Trail.endWidth = e * 2f; }
    }

    /// <summary>HitEffect (:3157-3201): the blood hit (healthHitPrefab, Particles/Multiply) at the hit point under the target, except for
    /// melee; "Bat_Shot_Enemy" on the target, except for explosives.</summary>
    public bool HitEffect(Shot a, Transform target, AudioSource targetAudio)
    {
        if (a.WeaponType != WeaponType.ONE_HANDED_COLD_ARMS && a.WeaponType != WeaponType.TWO_HANDED_COLD_ARMS && EnsureEffectPrefab(healthHitPrefab, "healthHit"))
        {
            var go = Instantiate(healthHitPrefab);
            var prs = go.GetComponentsInChildren<ParticleSystemRenderer>();   // ParticleRenderer (U6: converted)
            if (prs.Length > 0) { var s = RuntimeTextureLoader.FindShader("Particles/Multiply"); if (s != null) prs[0].material.shader = s; }
            go.transform.position = a.Origin;
            go.transform.parent = target;
        }
        if (a.WeaponType != WeaponType.ROCKET_LAUNCHER && a.WeaponType != WeaponType.GRENADE_LAUNCHER && a.WeaponType != WeaponType.BOMB_LAUNCHER)
            SoundManager.Play(targetAudio, "Bat_Shot_Enemy", AudioPlayMode.Play);
        return true;
    }

    bool EnsureEffectPrefab(GameObject a, string b)   // :2043-2054
    {
        if (a != null) return true;
        if (missingEffectPrefabs.Add(b)) Debug.LogWarning("[EffectManager] Missing effect prefab for key: " + b);
        return false;
    }
}
