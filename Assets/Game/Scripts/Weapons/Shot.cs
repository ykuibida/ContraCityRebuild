// Port of the shot record of the New Era client: class Shot (NE_Scripts_Decrypted/Shot.cs; Penta 2016 Shot.cs), the launch
// modes 嗠 (ShotLaunchMode) and the target entry 啨 (ShotTarget) with its type 啧 and hit zone 喘. Only what the offline client uses:
// no network hashtables (ToHashtable / FromHashtable are Photon).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ShotLaunchMode : byte { SHOT = 0, LAUNCH = 1, BLOW = 2, TURRET_SHOT = 3, TURRET_CONTROL = 4, SPIN = 5, DISACTIVATE = 6 }
public enum ShotTargetType : byte { NONE = 0, PLAYER = 1, PLAYER_ITEM = 2, BOT = 3, GATES = 4 }
public enum HitZone : byte { BASE = 0, ENGINE = 0x10, CABIN = 0x20 }

public class ShotTarget
{
    public int TargetID;
    public long ItemTimeStamp;
    public ShotTargetType TargetType;
    public Transform TargetTransform;
    public HitZone HitZone;
    public int HealthDamage, EnergyDamage;
    public bool Direct;
}

public class Shot
{
    Vector3 origin, direction, startOrigin, startDirection;
    long timeStamp, landingTimeStamp;
    WeaponType weaponType;
    ShotLaunchMode launchMode;
    List<ShotTarget> targets;
    ArrayList trajectory;

    /// <summary>TimeManager.NetworkTime offline (NE TimeManager.cs:65-77): DateTime.Now.ToBinary() / 10000 (milliseconds).</summary>
    public static long NetworkTime => System.DateTime.Now.ToBinary() / 10000;

    public bool Crit;
    public long TimeStamp { get => timeStamp; set => timeStamp = value; }
    public long LandingTimeStamp => landingTimeStamp;
    public Vector3 Origin { get => origin; set => origin = value; }
    public Vector3 Direction => direction;
    public Vector3 StartOrigin => startOrigin;
    public Vector3 StartDirection => startDirection;
    public WeaponType WeaponType => weaponType;
    public ShotLaunchMode LaunchMode { get => launchMode; set => launchMode = value; }
    public List<ShotTarget> Targets { get { if (targets == null) targets = new List<ShotTarget>(); return targets; } }
    public bool HasTargets => targets != null && targets.Count > 0;
    public ArrayList Trajectory { get => trajectory; set => trajectory = value; }   // NE Shot.cs:114-124
    public void addPoint(Vector3 a) { if (trajectory == null) trajectory = new ArrayList(); trajectory.Add(a); }   // NE Shot.cs:195-202

    /// <summary>NE Shot.cs:166-176 (the time stamp is the network time, TimeManager.NetworkTime in ms).</summary>
    public Shot(Vector3 a, Vector3 b, byte c)
    {
        origin = a; direction = b;
        landingTimeStamp = -1L;
        startOrigin = Vector3.zero; startDirection = Vector3.zero;
        weaponType = (WeaponType)c;
        launchMode = ShotLaunchMode.SHOT;
        targets = null;
    }

    public void ClearTargets() { if (targets != null) targets.Clear(); }

    /// <summary>NE Shot.cs:186-193.</summary>
    public void Launch(Vector3 a, Vector3 b, long c, long d)
    {
        startOrigin = a; startDirection = b; timeStamp = c; landingTimeStamp = d;
        launchMode = ShotLaunchMode.LAUNCH;
    }
}
