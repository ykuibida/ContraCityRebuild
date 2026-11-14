// Port of the original GrenadeTracer (New Era NE_Scripts_Decrypted/GrenadeTracer.cs; Penta 2016 GrenadeTracer.cs). Same class name,
// fields and script GUID (2e625077851b0a7de17f307f43875314): the grenade / snowball (CharacterManager "GrenadePrefab" / "SnowballPrefab")
// and the scene's mortarWhizbangPrefab. It follows the precomputed trajectory (Shot.Trajectory) between the launch and landing time
// stamps, scans for fighters within 6 (3.5 below it) every 100 ms when controlled, and blows up at the landing time.
// Line numbers: NE GrenadeTracer.cs.
using System;
using System.Collections;
using UnityEngine;

public class GrenadeTracer : ItemTracer
{
    const float ActivateRadius = 6f;
    Vector3 start;
    Shot shot;
    public float flatSpeed = 15f;
    Vector3 Speed;
    public int launchDelay = 200;
    public int destroyDelay = 300;
    long landingTime;
    long lastScanTime;
    bool active;
    bool control;
    ShotController player;
    public ShotController OwnerPlayer => player;

    public void Launch(Shot a, ShotController b, bool c) { InitializeLaunch(a); control = c; player = b; }
    public void Launch(Shot a) { InitializeLaunch(a); }

    void InitializeLaunch(Shot a)   // :46-73
    {
        shot = a;
        if (a == null) { active = false; return; }
        start = a.StartOrigin;
        if (start.sqrMagnitude < 0.0001f) start = transform.position;
        launchTime = a.TimeStamp;
        landingTime = a.LandingTimeStamp;
        var traj = a.Trajectory;
        if (traj == null || traj.Count < 2) { traj = new ArrayList { start, a.Origin }; a.Trajectory = traj; }
        if (landingTime <= launchTime) landingTime = launchTime + Math.Max(120, (traj.Count - 1) * 50);
        transform.LookAt(a.Origin);
        setRocketVisible(false);
    }

    /// <summary>setRocketVisible (:75-111): emitters and renderers on / off, renderer 2 Particles/Additive, the own renderer
    /// Particles/~Additive-Multiply, the snowball's first particle renderer Particles/Additive.</summary>
    public void setRocketVisible(bool a)
    {
        SetEmit(transform, a);
        var rs = transform.GetComponentsInChildren<Renderer>();
        foreach (var r in rs) if (r != null) r.enabled = a;
        if (rs.Length > 2 && rs[2] != null && rs[2].sharedMaterial != null) { var s = RuntimeTextureLoader.FindShader("Particles/Additive"); if (s != null) rs[2].sharedMaterial.shader = s; }
        var own = gameObject.GetComponent<Renderer>();
        if (own != null && own.sharedMaterial != null) { var s = RuntimeTextureLoader.FindShader("Particles/~Additive-Multiply"); if (s != null) own.sharedMaterial.shader = s; }
        if (!gameObject.name.StartsWith("SnowballPrefab")) return;
        var prs = transform.GetComponentsInChildren<ParticleSystemRenderer>();   // ParticleRenderer (U6: converted)
        if (prs.Length > 0 && prs[0] != null && prs[0].sharedMaterial != null) { var s = RuntimeTextureLoader.FindShader("Particles/Additive"); if (s != null) prs[0].sharedMaterial.shader = s; }
    }

    void FixedUpdate()   // :113-168
    {
        if (shot == null) { Destroy(); return; }
        long num = Shot.NetworkTime;
        if (num >= launchTime && num < landingTime) { active = true; setRocketVisible(true); }
        else if (num >= landingTime) { Blow(shot.Origin); return; }
        if (!active) return;
        long total = Math.Max(1L, landingTime - launchTime);
        long left = Math.Max(0L, landingTime - num);
        float k = 1f - Mathf.Clamp01((float)left / total);
        var traj = shot.Trajectory;
        bool placed = false;
        if (traj != null && traj.Count >= 2)
        {
            float f = (traj.Count - 1) * k;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, traj.Count - 2);
            float t = Mathf.Clamp01(f - i);
            if (traj[i] is Vector3 v1 && traj[i + 1] is Vector3 v2)
            {
                transform.position = Vector3.Lerp(v1, v2, t);
                if (v1 != v2) transform.LookAt(v2);
                placed = true;
            }
        }
        if (!placed)
        {
            transform.position = Vector3.Lerp(start, shot.Origin, k);
            var d = shot.Origin - transform.position;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }
        if ((lastScanTime < num + 100 || lastScanTime == 0) && control)
        {
            lastScanTime = num;
            if (ShotCalculator.Scan(transform.position + new Vector3(0f, -3.5f, 0f), ActivateRadius) > 0) Blow(transform.position);
        }
    }

    protected void Blow(Vector3 a)   // :170-190
    {
        if (control)
        {
            var s = new Shot(a, shot.Direction, (byte)shot.WeaponType);
            s.TimeStamp = shot.TimeStamp;
            s.LaunchMode = ShotLaunchMode.BLOW;
            if (player != null) { ShotCalculator.RadialShot(player, s, false); player.SendShot(s); player.UnregisterItem(TimeStamp); }
            Destroy();
        }
        else Destroy();
    }

    public override void Destroy()   // :192-213
    {
        active = false;
        enabled = false;
        var g = transform.Find("Grenade");
        if (g != null) { var r = g.gameObject.GetComponent<Renderer>(); if (r != null) r.enabled = false; }
        SetEmit(transform, false);
        UnityEngine.Object.Destroy(gameObject, destroyDelay / 1000f);
    }
}
