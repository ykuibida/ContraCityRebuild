// Port of the original RocketTracer (New Era NE_Scripts_Decrypted/RocketTracer.cs; Penta 2016 RocketTracer.cs). Same class name, fields
// and script GUID (e9e70253e08d08a671f1bb9cc28066df): the rocket of EffectManager.whizbangPrefab (rocket_11). Hidden until the launch
// time stamp, then it flies linearly from the start to the landing point between Shot.TimeStamp and Shot.LandingTimeStamp, scanning for
// fighters within 4 every 100 ms (controlled = own rocket); at the landing time (or on a fighter) it blows up: RadialShot and a BLOW shot.
// Line numbers: NE RocketTracer.cs.
using System;
using UnityEngine;

public class RocketTracer : ItemTracer
{
    protected Vector3 start;
    protected Shot shot;
    public float flatSpeed = 15f;
    protected Vector3 Speed;
    public int launchDelay = 200;
    public int destroyDelay = 600;
    protected long landingTime;
    protected long lastScanTime;
    protected bool active;
    protected bool control;
    protected ShotController player;
    protected float rocketIndex;

    void Start() { weaponType = WeaponType.ROCKET_LAUNCHER; }

    public void Launch(Shot a, ShotController b, bool c) { InitializeLaunch(a); control = c; player = b; }
    public void Launch(Shot a) { InitializeLaunch(a); }

    void InitializeLaunch(Shot a)   // :37-55
    {
        shot = a;
        if (a == null) { active = false; return; }
        start = a.StartOrigin;
        if (start.sqrMagnitude < 0.0001f) start = transform.position;
        launchTime = a.TimeStamp;
        landingTime = a.LandingTimeStamp;
        if (landingTime <= launchTime) landingTime = launchTime + 120;
        transform.LookAt(a.Origin);
        setRocketVisible(false);
    }

    /// <summary>setRocketVisible (:57-90): emitters and renderers on / off; renderer 0 Particles/Alpha Blended, 1 Mobile/Diffuse.</summary>
    public void setRocketVisible(bool a)
    {
        SetEmit(transform, a);
        var rs = transform.GetComponentsInChildren<Renderer>();
        foreach (var r in rs) if (r != null) r.enabled = a;
        if (rs.Length > 0 && rs[0] != null && rs[0].sharedMaterial != null) { var s = RuntimeTextureLoader.FindShader("Particles/Alpha Blended"); if (s != null) rs[0].sharedMaterial.shader = s; }
        if (rs.Length > 1 && rs[1] != null && rs[1].sharedMaterial != null) { var s = Shader.Find("Mobile/Diffuse"); if (s != null) rs[1].sharedMaterial.shader = s; }
    }

    void FixedUpdate()   // :92-123
    {
        if (shot == null) { Destroy(); return; }
        long num = Shot.NetworkTime;
        if (num >= launchTime && num < landingTime) { active = true; setRocketVisible(true); }
        else if (num >= landingTime) { Blow(shot.Origin); return; }
        if (!active) return;
        long total = Math.Max(1L, landingTime - launchTime);
        long left = Math.Max(0L, landingTime - num);
        float t = 1f - Mathf.Clamp01((float)left / total);
        transform.position = Vector3.Lerp(start, shot.Origin, t);
        if ((lastScanTime < num + 100 || lastScanTime == 0) && control)
        {
            lastScanTime = num;
            if (ShotCalculator.Scan(transform.position, 4f) > 0) Blow(transform.position);
        }
    }

    protected void Blow(Vector3 a)   // :125-145
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

    public override void Destroy()   // :147-168
    {
        active = false;
        enabled = false;
        var rocket = transform.Find("Rocket");
        if (rocket != null) { var r = rocket.gameObject.GetComponent<Renderer>(); if (r != null) r.enabled = false; }
        SetEmit(transform, false);
        UnityEngine.Object.Destroy(gameObject, destroyDelay / 1000f);
    }
}
