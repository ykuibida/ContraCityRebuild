// Port of the original BombTracer (New Era NE_Scripts_Decrypted/BombTracer.cs; Penta 2016 BombTracer.cs). Same class name, fields and
// script GUID (e522e61469d84c555f3cc0917e8e9dbb): the sticky bomb (CharacterManager "BombPrefab"). It follows the trajectory until the
// landing time stamp, then lies there until the right button (DoBlow → InitBlow), the fourth bomb (CheckBlow, oldest first) or its life
// (weapon LifeTime, ms) after landing makes it blow up. Line numbers: NE BombTracer.cs.
using UnityEngine;

public class BombTracer : ItemTracer
{
    const float ActivateRadius = 6f;
    Shot shot;
    public float flatSpeed = 15f;
    Vector3 Speed;
    public int launchDelay = 200;
    public int destroyDelay = 300;
    long landingTime;
    long lastScanTime;
    int life = 30000;
    bool active;
    bool control;
    ShotController player;
    public ShotController Owner => player;

    public void Launch(Shot a, CombatWeapon b, ShotController c, bool d, bool e)   // :34-49
    {
        shot = a;
        launchTime = a.TimeStamp;
        landingTime = a.LandingTimeStamp;
        life = b.Life;
        transform.LookAt(a.Origin);
        setRocketVisible(false);
        control = d;
        weaponType = a.WeaponType;
        player = c;
        if (e) { transform.position = (Vector3)a.Trajectory[a.Trajectory.Count - 1]; setRocketVisible(true); }
    }

    public void Launch(Shot a)
    {
        shot = a; launchTime = a.TimeStamp; landingTime = a.LandingTimeStamp;
        transform.LookAt(a.Origin);
        setRocketVisible(false);
    }

    /// <summary>setRocketVisible (:59-73): emitters and renderers on / off, the trail Particles/~Additive-Multiply.</summary>
    public void setRocketVisible(bool a)
    {
        SetEmit(transform, a);
        var rs = transform.GetComponentsInChildren<Renderer>();
        var trail = gameObject.GetComponent<TrailRenderer>();
        if (trail != null && trail.sharedMaterial != null) { var s = RuntimeTextureLoader.FindShader("Particles/~Additive-Multiply"); if (s != null) trail.sharedMaterial.shader = s; }
        foreach (var r in rs) r.enabled = a;
    }

    void FixedUpdate()   // :75-111
    {
        long now = Shot.NetworkTime;
        if (now >= launchTime && now < landingTime) { active = true; setRocketVisible(true); }
        else
        {
            if (now > landingTime + life) { Blow(shot.Origin); return; }
            if (now > landingTime) return;
        }
        if (!active) return;
        long num = landingTime - now;
        if (num > 0)
        {
            float f = (float)num / (landingTime - launchTime);
            int i = Mathf.FloorToInt((shot.Trajectory.Count - 1) * (1f - f));
            float w = (shot.Trajectory.Count - 1) * (1f - f) - i;
            var v1 = (Vector3)shot.Trajectory[i];
            var v2 = (Vector3)shot.Trajectory[i + 1];
            transform.position = v2 * w + v1 * (1f - w);
            if (v1 != v2) transform.LookAt(v2);
        }
    }

    public void InitBlow() { Blow(transform.position); }

    protected void Blow(Vector3 a)   // :118-136
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

    public override void Destroy()   // :138-150
    {
        active = false;
        enabled = false;
        var g = transform.Find("Grenade");
        if (g != null) g.gameObject.GetComponent<Renderer>().enabled = false;
        SetEmit(transform, false);
        UnityEngine.Object.Destroy(gameObject, destroyDelay / 1000f);
    }
}
