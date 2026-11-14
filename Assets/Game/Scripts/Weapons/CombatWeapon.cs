// Port of the weapon runtime of the New Era client: class 噔 (NE_Scripts_Decrypted/噔.cs) = Penta 2016 CombatWeapon.cs, with the enums
// 啁 (WeaponType, NE 啁.cs = Penta WeaponType) and 啄 (WeaponMode, NE 啄.cs). New Era is the base, so its balance code is kept as it is
// (the Is* weapon checks, NormalizeLimitedBattleAmmo / NormalizeMeleeAmmo / NormalizeMeleeCombatValues / NormalizeWeaponTimings and the
// sequential shell reload); Penta's simpler CombatWeapon is only the reference. Times are DateTime.Now.Ticks (10 000 ticks = 1 ms).
// Every member keeps the original order and logic; line numbers are NE 噔.cs.
using System;
using System.Collections;
using UnityEngine;

public enum WeaponType : byte
{
    NONE = 0, ONE_HANDED_COLD_ARMS = 1, TWO_HANDED_COLD_ARMS = 2, HAND_GUN = 3, MACHINE_GUN = 4, FLAMER = 5, GATLING_GUN = 6, SHOT_GUN = 7,
    ROCKET_LAUNCHER = 8, GRENADE_LAUNCHER = 9, SNIPER_RIFLE = 10, SNOW_GUN = 11, ACID_THROWER = 12, ELECTRO_SHOCKER = 13, BIO_SHOCKER = 14,
    BOMB_LAUNCHER = 15, MINE_SPIDER = 101, TURRET_MACHINE_GUN = 102, MINE_ELECTRIC_REMOTE = 103, MINE_TIME = 104, MINE_TOUCH = 105,
    MINE_REMOTE = 106, MINE_ELECTRIC = 107, TURRET_TESLA = 108, CHARGER = 201, MEGA_CHARGER = 202, KAMIKADZE = 203
}

public enum WeaponMode : byte { Changing = 0, Ready = 1, Shooting = 2, Reloading = 3, ReloadingReady = 4, Launching = 5, Empty = 6 }

public class CombatWeapon
{
    WeaponMode weaponMode = WeaponMode.Ready;
    protected Transform target;
    protected Transform transform;
    int weaponID = -1;
    protected WeaponType type;
    bool isShaking;
    string attributes = "_";
    bool canLaunch;
    int enhancerModes;
    protected int launchTime = 15000000;
    protected int shotTime = 1000000;
    protected int changeTime = 4500000;
    protected int reloadSingleTime = 1500000;
    protected int reloadTime = 1500000;
    protected bool hasExactReloadOverride;
    protected long lastChangeTime, lastReloadTime, lastReloadSingleTime, lastShotTime, lastLaunchTime;
    protected int ammoReserve, maxAmmoReserve, maxLoadedAmmo, loadedAmmo, pendingReloadAmmo;
    long sequentialReloadFirstDelay, sequentialReloadNextDelay;
    bool sequentialReloadFirstRound = true, sequentialReloadInterruptPending, sequentialReloadInterruptReady;
    protected int index;
    int velocity;
    public int Life;
    float distance, angle, deviation;
    string systemName = string.Empty;
    bool complexReload;
    float speedValue = 1f;
    int workshopPeriodicTickDamage, workshopPeriodicTickCount, periodicImpactType, periodicTickDamage, periodicTickCount;
    /// <summary>Max / min damage of the battle weapon (hashtable keys 88 / 86; New Era offline damage, weapons stage 2).</summary>
    public int MaxDamage, MinDamage;

    public WeaponMode WeaponMode => weaponMode;
    public Transform Target { get => target; set => target = value; }
    public Transform Transform { get => transform; set => transform = value; }
    public int WeaponID => weaponID;
    public WeaponType Type => type;
    public int WorkshopPeriodicTickDamage => workshopPeriodicTickDamage;
    public int WorkshopPeriodicTickCount => workshopPeriodicTickCount;
    public int PeriodicImpactType => periodicImpactType;
    public int PeriodicTickDamage => periodicTickDamage;
    public int PeriodicTickCount => periodicTickCount;
    public bool IsShaking => isShaking;
    public string Attributes
    {
        get
        {
            if (attributes == "_") attributes = SystemName.Split('_').Length > 2 ? SystemName.Split('_')[2] : string.Empty;
            return attributes;
        }
    }
    public bool CanLaunch => canLaunch;
    public int EnhacerModes => enhancerModes;
    public int ShotTime { get => shotTime; set => shotTime = value; }
    public int ReloadSingleTime { get => reloadSingleTime; set => reloadSingleTime = value; }
    public int ReloadTime { get => reloadTime; set => reloadTime = value; }
    public bool HasExactReloadOverride => hasExactReloadOverride;
    public int MaxAmmoReserve { get => maxAmmoReserve; set { maxAmmoReserve = value; NormalizeLimitedBattleAmmo(); } }
    public int AmmoReserve => ammoReserve;
    public int MaxLoadedAmmo => maxLoadedAmmo;
    public int LoadedAmmo => loadedAmmo;
    public int Index { get => index; set => index = value; }
    public int Velocity => velocity;
    public float Distance { get => distance; set => distance = value; }
    public float Angle { get => angle; set => angle = value; }
    public float Deviation => deviation;
    public string SystemName { get => systemName; set => systemName = value; }
    public bool ComplexReload => complexReload;
    public bool SequentialReload => IsSequentialReloadWeapon();
    public bool HasQueuedSequentialReloadFire => IsCloseBeforeShotGrenadeLauncher() && sequentialReloadInterruptPending;
    public float SpeedValue => speedValue;

    /// <summary>NE :403-409 (GetWeaponByType(203) kamikaze stub).</summary>
    public CombatWeapon(int a, int b, WeaponType c, string d) { weaponID = b; systemName = d; Index = a; type = c; }

    /// <summary>NE :411-442 (zombie claws: WeaponController.InitZombieWeapon).</summary>
    public CombatWeapon(int a, WeaponType b, int c, int d, int e, int f, int g)
    {
        Index = a; type = b; maxLoadedAmmo = e; Life = g; velocity = f;
        shotTime = c + 10; if (shotTime < 100) shotTime = 100; shotTime *= 10000;
        reloadTime = d + 10; if (reloadTime < 100) reloadTime = 100;
        if ((type == WeaponType.SHOT_GUN || type == WeaponType.ROCKET_LAUNCHER || type == WeaponType.GRENADE_LAUNCHER || type == WeaponType.BOMB_LAUNCHER) && maxLoadedAmmo >= 3)
        { reloadSingleTime = reloadTime / maxLoadedAmmo + 10; complexReload = true; }
        else { reloadSingleTime = reloadTime; complexReload = false; }
        reloadTime *= 10000; reloadSingleTime *= 10000;
        NormalizeWeaponTimings();
    }

    /// <summary>NE :444-563 — the battle weapon from the weapon-info hashtable (keys 80, 98, 92, 97, 90, 99, 91, 127-133, 96, 95, 87, 94, 93, 79).</summary>
    public CombatWeapon(int a, Hashtable b)
    {
        Index = a;
        weaponID = (int)b[(byte)80];
        type = (WeaponType)(int)b[(byte)98];
        maxLoadedAmmo = (int)b[(byte)92];
        velocity = (int)b[(byte)97];
        Life = (int)b[(byte)90];
        systemName = (string)b[(byte)99];
        maxAmmoReserve = (int)b[(byte)91];
        workshopPeriodicTickDamage = Key(b, 127); workshopPeriodicTickCount = Key(b, 130); periodicImpactType = Key(b, 131);
        periodicTickDamage = Key(b, 132); periodicTickCount = Key(b, 133);
        if (b.ContainsKey((byte)88)) MaxDamage = (int)b[(byte)88];
        if (b.ContainsKey((byte)86)) MinDamage = (int)b[(byte)86];
        NormalizeLimitedBattleAmmo();
        NormalizeMeleeAmmo();
        loadedAmmo = maxLoadedAmmo;
        ammoReserve = Math.Max(0, maxAmmoReserve - loadedAmmo);
        distance = (int)b[(byte)96];
        angle = (float)b[(byte)95];
        NormalizeMeleeCombatValues();
        deviation = (int)b[(byte)87];
        if (systemName == "GG_M249" || systemName == "GG_FNMAG") launchTime = 4000000;
        shotTime = (int)b[(byte)94] + 10; if (shotTime < 100) shotTime = 110; shotTime *= 10000;
        reloadTime = (int)b[(byte)93] + 10; if (reloadTime < 100) reloadTime = 110;
        if ((type == WeaponType.SHOT_GUN || type == WeaponType.ROCKET_LAUNCHER || type == WeaponType.GRENADE_LAUNCHER || type == WeaponType.BOMB_LAUNCHER) && maxLoadedAmmo >= 3)
        { reloadSingleTime = reloadTime / maxLoadedAmmo + 10; complexReload = true; }
        else { reloadSingleTime = reloadTime; complexReload = false; }
        reloadTime *= 10000; reloadSingleTime *= 10000;
        if (b.ContainsKey((byte)79))
        {
            var m = (Hashtable)b[(byte)79];
            if (m.ContainsKey((byte)78)) speedValue = 1f + (int)m[(byte)78] / 100f;
            if (m.ContainsKey((byte)75)) canLaunch = true;
            if (m.ContainsKey((byte)76)) isShaking = true;
        }
        NormalizeWeaponTimings();
        ApplyExactReloadTime(b);
        ApplyExactAttackSpeed(b);
    }

    static int Key(Hashtable b, int k)
    {
        if (!b.ContainsKey((byte)k) || b[(byte)k] == null) return 0;
        try { return Math.Max(0, Convert.ToInt32(b[(byte)k])); } catch { return 0; }
    }

    void ApplyExactReloadTime(Hashtable a)   // NE :565-595
    {
        if (a == null || !a.ContainsKey((byte)129) || a[(byte)129] == null) return;
        try
        {
            int num = Convert.ToInt32(a[(byte)129]);
            if (num <= 0) return;
            num = Math.Max(100, num);
            long t = (long)num * 10000L;
            reloadTime = (int)(t <= int.MaxValue ? t : int.MaxValue);
            if (complexReload && maxLoadedAmmo > 0)
            {
                long s = (long)Math.Max(100, num / maxLoadedAmmo + 10) * 10000L;
                reloadSingleTime = (int)(s <= int.MaxValue ? s : int.MaxValue);
            }
            else reloadSingleTime = reloadTime;
            hasExactReloadOverride = true;
        }
        catch { }
    }

    void ApplyExactAttackSpeed(Hashtable a)   // NE :597-615
    {
        if (a == null || !a.ContainsKey((byte)128) || a[(byte)128] == null) return;
        try
        {
            int num = Convert.ToInt32(a[(byte)128]);
            if (num > 0) { long t = (long)num * 10000L; shotTime = (int)(t <= int.MaxValue ? t : int.MaxValue); }
        }
        catch { }
    }

    bool IsMeleeWeapon() => type == WeaponType.ONE_HANDED_COLD_ARMS || type == WeaponType.TWO_HANDED_COLD_ARMS;

    static bool Has(string s, string part) => (s ?? string.Empty).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

    bool IsMelsRocketLauncher() => type == WeaponType.ROCKET_LAUNCHER && (weaponID == 43 || systemName == "RL_M202A1" || Has(systemName, "M202A1") || Has(systemName, "MELS") || Has(systemName, "MELC"));
    bool IsTrollebusinaRocketLauncher() => type == WeaponType.ROCKET_LAUNCHER && (weaponID == 59 || systemName == "RL_RPG7B02" || Has(systemName, "RPG7B02"));
    bool IsVorchunGrenadeLauncher() => type == WeaponType.GRENADE_LAUNCHER && (weaponID == 104 || systemName == "GL_GrenadeLauncher03" || Has(systemName, "GrenadeLauncher03"));
    public bool IsCloseBeforeShotGrenadeLauncher()
    {
        if (type != WeaponType.GRENADE_LAUNCHER) return false;
        if (weaponID == 44 || weaponID == 45 || weaponID == 46 || weaponID == 104) return true;
        string a = systemName ?? string.Empty;
        return string.Equals(a, "GL_Milkor", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "GL_Milkor_A", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "GL_SnowLauncher", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "GL_GrenadeLauncher03", StringComparison.OrdinalIgnoreCase);
    }
    bool IsWinchesterShotgun() => type == WeaponType.SHOT_GUN && (weaponID == 5 || systemName == "SG_Winchester1887" || Has(systemName, "Winchester1887"));
    bool IsKabanShotgun() => type == WeaponType.SHOT_GUN && (weaponID == 106 || systemName == "SG_Spas" || Has(systemName, "SG_Spas"));
    bool IsAdvisorShotgun() => type == WeaponType.SHOT_GUN && (weaponID == 109 || systemName == "SG_Remington" || Has(systemName, "Remington"));
    bool IsKrikSniper() => type == WeaponType.SNIPER_RIFLE && (weaponID == 70 || systemName == "SR_ArcticB01" || Has(systemName, "ArcticB01"));
    bool IsPovstanecMachineGun() => type == WeaponType.MACHINE_GUN && (weaponID == 80 || systemName == "MG_AUG5_O" || Has(systemName, "AUG5"));

    bool UsesExactBalancePatchAmmo()   // NE :748-777 (no 24 / 69, unlike PlayerManager.UsesExactBalancePatchAmmo)
    {
        switch (weaponID)
        {
            case 5: case 16: case 18: case 21: case 33: case 34: case 36: case 62: case 68: case 75: case 79: case 80: case 103: case 104:
            case 107: case 108: case 109: case 112: case 114: case 115: case 191114: return true;
            default: return false;
        }
    }

    void NormalizeMeleeAmmo()   // NE :779-786
    {
        if (IsMeleeWeapon()) { maxLoadedAmmo = Math.Max(1, Math.Min(maxLoadedAmmo, 1)); maxAmmoReserve = maxLoadedAmmo; }
    }

    void NormalizeLimitedBattleAmmo()   // NE :788-868
    {
        if (UsesExactBalancePatchAmmo()) { maxLoadedAmmo = Math.Max(1, maxLoadedAmmo); maxAmmoReserve = Math.Max(maxLoadedAmmo, maxAmmoReserve); return; }
        switch (type)
        {
            case WeaponType.SHOT_GUN:
                if (IsKabanShotgun()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 6); maxAmmoReserve = Math.Max(maxAmmoReserve, 19); }
                else if (IsWinchesterShotgun()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 3); maxAmmoReserve = Math.Max(maxAmmoReserve, 11); }
                else if (IsAdvisorShotgun()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 3); maxAmmoReserve = Math.Max(maxAmmoReserve, 20); }
                else { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 5); maxAmmoReserve = Math.Max(maxAmmoReserve, 23); }
                break;
            case WeaponType.HAND_GUN:
                maxLoadedAmmo = Math.Max(maxLoadedAmmo, 5); maxAmmoReserve = Math.Max(maxAmmoReserve, 29);
                break;
            case WeaponType.MACHINE_GUN:
                if (IsPovstanecMachineGun()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 30); maxAmmoReserve = Math.Max(maxAmmoReserve, 193); }
                break;
            case WeaponType.ROCKET_LAUNCHER:
                if (IsMelsRocketLauncher()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 4); maxAmmoReserve = Math.Max(maxAmmoReserve, 28); }
                else if (IsTrollebusinaRocketLauncher()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 1); maxAmmoReserve = Math.Max(maxAmmoReserve, 8); }
                else { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 1); maxAmmoReserve = Math.Max(maxAmmoReserve, 25); }
                break;
            case WeaponType.GRENADE_LAUNCHER:
                if (IsVorchunGrenadeLauncher()) { maxLoadedAmmo = Math.Max(maxLoadedAmmo, 3); maxAmmoReserve = Math.Max(maxAmmoReserve, 19); }
                else { maxLoadedAmmo = Math.Max(1, maxLoadedAmmo); maxAmmoReserve = Math.Max(maxAmmoReserve, maxLoadedAmmo + 12); }
                break;
            case WeaponType.SNIPER_RIFLE:
                maxLoadedAmmo = Math.Max(maxLoadedAmmo, 1); maxAmmoReserve = Math.Max(maxAmmoReserve, !IsKrikSniper() ? 20 : 17);
                break;
        }
    }

    bool IsSequentialReloadWeapon() => IsCloseBeforeShotGrenadeLauncher() || type == WeaponType.SHOT_GUN || IsMelsRocketLauncher();   // NE :870-873

    void NormalizeMeleeCombatValues()   // NE :875-937
    {
        if (type == WeaponType.SHOT_GUN)
        {
            if (distance <= 0f || distance > 900f) distance = 450f;
            if (angle > 3.14f && angle <= 180f) angle = angle * (float)Math.PI / 180f;
            if (angle <= 0f || angle < 0.18f || angle > 0.75f) angle = 0.34f;
            angle = Mathf.Clamp(angle, 0.25f, 0.45f);
        }
        else if (type == WeaponType.ROCKET_LAUNCHER)
        {
            if (distance <= 0f) distance = 350f;
            if (velocity <= 0) velocity = 20;
        }
        else if (type == WeaponType.GRENADE_LAUNCHER || type == WeaponType.BOMB_LAUNCHER)
        {
            if (distance <= 0f) distance = 350f;
            if (velocity <= 0) velocity = 12;
        }
        else
        {
            if (!IsMeleeWeapon()) return;
            if (type == WeaponType.TWO_HANDED_COLD_ARMS) { if (distance <= 0f || distance > 16f) distance = 9f; }
            else if (distance <= 0f || distance > 16f) distance = 8f;
            if (angle <= 0f || angle > 3.14f) angle = 2.05f;
        }
    }

    static int ScaleTimingTicks(int a, int b, int c)   // NE :939-955
    {
        if (a <= 0 || b <= 0 || c <= 0) return a;
        long num = ((long)a * b + c / 2) / c;
        if (num < 1) num = 1L;
        if (num > int.MaxValue) num = int.MaxValue;
        return (int)num;
    }

    void NormalizeWeaponTimings()   // NE :957-1017
    {
        if (type == WeaponType.SNIPER_RIFLE && shotTime < 12100000) shotTime = 12100000;
        if (type == WeaponType.SHOT_GUN)
        {
            if (IsKabanShotgun()) { if (shotTime < 10000000) shotTime = 10000000; }
            else
            {
                int num = !IsAdvisorShotgun() ? 8600000 : ScaleTimingTicks(ScaleTimingTicks(8600000, 100, 114), 100, 110);
                if (shotTime < num) shotTime = num;
                if (shotTime > 10000000) shotTime = 10000000;
            }
            if (reloadSingleTime > 4800000) reloadSingleTime = 4800000;
            changeTime = 4500000;
        }
        if (IsVorchunGrenadeLauncher())
        {
            shotTime = ScaleTimingTicks(shotTime, 100, 117);
            reloadTime = ScaleTimingTicks(reloadTime, 100, 110);
            reloadSingleTime = ScaleTimingTicks(reloadSingleTime, 100, 110);
        }
        switch (weaponID)
        {
            case 104: shotTime = ScaleTimingTicks(shotTime, 100, 110); break;
            case 75: shotTime = 15000000; break;
            case 74: shotTime = ScaleTimingTicks(shotTime, 100, 64); break;
            case 107: shotTime = ScaleTimingTicks(shotTime, 100, 88); break;
            case 103: shotTime = ScaleTimingTicks(shotTime, 100, 64); break;
            case 70: shotTime = ScaleTimingTicks(shotTime, 100, 50); break;
        }
    }

    public void SetEnhancerMode(int a) { enhancerModes |= a; }
    public void UnsetEnhancerMode(int a) { enhancerModes ^= a; }

    public void ConfigureSequentialReloadTiming(float a, float b)   // NE :1029-1036
    {
        if (!IsSequentialReloadWeapon()) return;
        sequentialReloadFirstDelay = Math.Max(1L, (long)(Math.Max(a, 0.05f) * 10000000f));
        sequentialReloadNextDelay = Math.Max(1L, (long)(Math.Max(b, 0.05f) * 10000000f));
    }

    public void SwitchWeaponTexture(int a)   // NE :1038-1048
    {
        if (Type != WeaponType.TWO_HANDED_COLD_ARMS || transform == null) return;
        var sw = transform.GetComponentInChildren<RuntimeTextureSwitch>();
        if (sw != null) sw.Switch(a);
    }

    public void UpdateAmmoCount(int a, int b)   // NE :1050-1064
    {
        pendingReloadAmmo = 0;
        if (IsMeleeWeapon()) { NormalizeMeleeAmmo(); loadedAmmo = maxLoadedAmmo; ammoReserve = 0; }
        else { loadedAmmo = Math.Max(0, Math.Min(a, maxLoadedAmmo)); ammoReserve = Math.Max(0, Math.Min(b, GetMaxReserveAmmo())); }
    }

    int GetMaxReserveAmmo() => Math.Max(0, maxAmmoReserve - loadedAmmo);

    public void AddAmmoToReserve(int a)   // NE :1071-1085
    {
        if (IsMeleeWeapon()) { NormalizeMeleeAmmo(); loadedAmmo = maxLoadedAmmo; ammoReserve = 0; return; }
        ammoReserve = Math.Max(0, ammoReserve + a);
        if (ammoReserve > GetMaxReserveAmmo()) ammoReserve = GetMaxReserveAmmo();
    }

    public void AddAmmoToReserve(int a, bool b) { AddAmmoToReserve(a); }

    public void ShootAmmo()   // NE :1092-1103
    {
        if (loadedAmmo <= 0) { loadedAmmo = 0; Debug.Log("NOOO AMMOOO SHOOOT!!!"); }
        else loadedAmmo--;
    }

    public int GetIndex() => Index + 1;
    public new int GetType() => (int)type;
    public string GetTypeString() => Type.ToString();
    public string GetName() => getName(type);

    public static string getName(WeaponType a)   // NE :1125-1156
    {
        switch (a)
        {
            case WeaponType.ONE_HANDED_COLD_ARMS: return "OneHandedColdArmsWeapon";
            case WeaponType.TWO_HANDED_COLD_ARMS: return "TwoHandedColdArmsWeapon";
            case WeaponType.HAND_GUN: return "HandGunWeapon";
            case WeaponType.MACHINE_GUN: return "MachineGunWeapon";
            case WeaponType.FLAMER: case WeaponType.SNOW_GUN: case WeaponType.ACID_THROWER: return "FlamerWeapon";
            case WeaponType.GATLING_GUN: return "GatlingGunWeapon";
            case WeaponType.SHOT_GUN: return "ShotGunWeapon";
            case WeaponType.ROCKET_LAUNCHER: return "RocketLauncherWeapon";
            case WeaponType.GRENADE_LAUNCHER: return "GrenadeLauncherWeapon";
            case WeaponType.SNIPER_RIFLE: return "SniperRifleWeapon";
            case WeaponType.BOMB_LAUNCHER: return "BombLauncherWeapon";
            default: return "None";
        }
    }

    public WeaponMode CalcMode()   // NE :1158-1211
    {
        if (IsSequentialReloadWeapon() && (weaponMode == WeaponMode.Reloading || weaponMode == WeaponMode.ReloadingReady))
        {
            UpdateSequentialReloadQueue();
            return weaponMode;
        }
        long now = DateTime.Now.Ticks;
        switch (weaponMode)
        {
            case WeaponMode.Changing:
                if (now - lastChangeTime >= changeTime) weaponMode = WeaponMode.Ready;
                break;
            case WeaponMode.Shooting:
                if (now - lastShotTime >= shotTime)
                    weaponMode = type == WeaponType.GATLING_GUN || type == WeaponType.FLAMER || type == WeaponType.ACID_THROWER || type == WeaponType.SNOW_GUN ? WeaponMode.Launching : WeaponMode.Ready;
                break;
            case WeaponMode.Reloading:
                if (now - lastReloadTime >= reloadTime) { ApplyPendingReload(); weaponMode = WeaponMode.Ready; }
                else if (now - lastReloadTime >= reloadSingleTime) weaponMode = WeaponMode.ReloadingReady;
                break;
            case WeaponMode.ReloadingReady:
                if (now - lastReloadTime >= reloadTime) { ApplyPendingReload(); weaponMode = WeaponMode.Ready; }
                break;
        }
        return weaponMode;
    }

    public void CancelQueuedSequentialReloadFire()   // NE :1213-1219
    {
        if (IsVorchunGrenadeLauncher()) sequentialReloadInterruptPending = false;
    }

    public bool InterruptSequentialReloadForFire()   // NE :1221-1270
    {
        if (!IsSequentialReloadWeapon()) return false;
        if (weaponMode != WeaponMode.Reloading && weaponMode != WeaponMode.ReloadingReady) return false;
        if (IsCloseBeforeShotGrenadeLauncher())
        {
            if (IsVorchunGrenadeLauncher())
            {
                sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
                if (loadedAmmo <= 0) return false;
            }
            else
            {
                sequentialReloadInterruptPending = true;
                if (loadedAmmo <= 0 || !sequentialReloadInterruptReady) return false;
            }
        }
        else
        {
            if (loadedAmmo <= 0) return false;
            if (DateTime.Now.Ticks - lastShotTime < shotTime) return false;
        }
        pendingReloadAmmo = 0;
        sequentialReloadFirstRound = true; sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
        lastReloadTime = 0L; lastReloadSingleTime = 0L;
        weaponMode = WeaponMode.Ready;
        return true;
    }

    public bool CanFire()   // NE :1272-1303
    {
        long now = DateTime.Now.Ticks;
        if (type == WeaponType.GATLING_GUN && now - lastLaunchTime < launchTime) return false;
        if (type == WeaponType.GATLING_GUN || type == WeaponType.FLAMER || type == WeaponType.ACID_THROWER || type == WeaponType.SNOW_GUN)
            return weaponMode == WeaponMode.Launching && now - lastShotTime >= shotTime;
        if (IsSequentialReloadWeapon()) return weaponMode == WeaponMode.Ready && now - lastShotTime >= shotTime;
        if (type == WeaponType.ROCKET_LAUNCHER || type == WeaponType.GRENADE_LAUNCHER || type == WeaponType.BOMB_LAUNCHER)
        {
            if (now - lastShotTime < shotTime) return false;
            return weaponMode == WeaponMode.ReloadingReady || weaponMode == WeaponMode.Ready;
        }
        return weaponMode == WeaponMode.Ready && now - lastShotTime >= shotTime;
    }

    public bool Fire()   // NE :1305-1314
    {
        if (!CanFire()) return false;
        lastShotTime = DateTime.Now.Ticks;
        weaponMode = WeaponMode.Shooting;
        return true;
    }

    public bool Launch()   // NE :1316-1325
    {
        if (weaponMode != WeaponMode.Ready) return false;
        lastLaunchTime = DateTime.Now.Ticks;
        weaponMode = WeaponMode.Launching;
        return true;
    }

    public bool Stop()   // NE :1327-1335
    {
        if (weaponMode == WeaponMode.Launching || weaponMode == WeaponMode.Shooting) { weaponMode = WeaponMode.Ready; return true; }
        return false;
    }

    public bool IsWeaponBlockedChange() => weaponMode == WeaponMode.Changing;

    public bool IsWeaponBlocked()   // NE :1342-1349
    {
        if (IsSequentialReloadWeapon() && (weaponMode == WeaponMode.Reloading || weaponMode == WeaponMode.ReloadingReady)) return true;
        return type != WeaponType.ONE_HANDED_COLD_ARMS && type != WeaponType.TWO_HANDED_COLD_ARMS && (type != WeaponType.SHOT_GUN || weaponMode != WeaponMode.ReloadingReady)
            && (weaponMode == WeaponMode.Reloading || loadedAmmo + ammoReserve == 0);
    }

    public bool CanReload()   // NE :1351-1358
    {
        if ((type == WeaponType.ROCKET_LAUNCHER || type == WeaponType.GRENADE_LAUNCHER || type == WeaponType.BOMB_LAUNCHER) && weaponMode == WeaponMode.Shooting && !IsCloseBeforeShotGrenadeLauncher() && loadedAmmo > 0)
            return false;
        return ammoReserve != 0 && loadedAmmo != maxLoadedAmmo && weaponMode != WeaponMode.Reloading && pendingReloadAmmo <= 0;
    }

    public int Reload()   // NE :1360-1371
    {
        weaponMode = WeaponMode.Reloading;
        lastReloadSingleTime = DateTime.Now.Ticks;
        lastReloadTime = lastReloadSingleTime;
        sequentialReloadFirstRound = true; sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
        pendingReloadAmmo = Math.Min(maxLoadedAmmo - loadedAmmo, ammoReserve);
        return pendingReloadAmmo;
    }

    void UpdateSequentialReloadQueue()   // NE :1373-1412
    {
        if (IsCloseBeforeShotGrenadeLauncher() && sequentialReloadInterruptReady && !sequentialReloadInterruptPending) sequentialReloadInterruptReady = false;
        if (pendingReloadAmmo <= 0 || loadedAmmo >= maxLoadedAmmo || ammoReserve <= 0)
        {
            pendingReloadAmmo = 0; sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
            weaponMode = WeaponMode.Ready;
            return;
        }
        long ticks = DateTime.Now.Ticks;
        long num = !sequentialReloadFirstRound ? sequentialReloadNextDelay : sequentialReloadFirstDelay;
        if (num <= 0) num = reloadSingleTime;
        if (ticks - lastReloadSingleTime >= num)
        {
            ApplyPendingReload(1);
            lastReloadSingleTime = ticks;
            sequentialReloadFirstRound = false;
            if (IsCloseBeforeShotGrenadeLauncher()) sequentialReloadInterruptReady = true;
        }
        if (pendingReloadAmmo <= 0 || loadedAmmo >= maxLoadedAmmo || ammoReserve <= 0)
        {
            pendingReloadAmmo = 0; sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
            weaponMode = WeaponMode.Ready;
        }
        else weaponMode = WeaponMode.Reloading;
    }

    void ApplyPendingReload() { ApplyPendingReload(pendingReloadAmmo); pendingReloadAmmo = 0; }

    void ApplyPendingReload(int a)   // NE :1422-1444
    {
        if (pendingReloadAmmo <= 0 || a <= 0) return;
        int num = Math.Min(a, Math.Min(pendingReloadAmmo, Math.Min(maxLoadedAmmo - loadedAmmo, ammoReserve)));
        if (num <= 0) return;
        loadedAmmo += num; ammoReserve -= num; pendingReloadAmmo -= num;
        if (ammoReserve < 0) ammoReserve = 0;
        if (pendingReloadAmmo < 0) pendingReloadAmmo = 0;
    }

    public int getReload() => weaponMode == WeaponMode.Reloading || weaponMode == WeaponMode.ReloadingReady ? 0 : 9;

    public void Change()   // NE :1454-1462
    {
        pendingReloadAmmo = 0;
        sequentialReloadFirstRound = true; sequentialReloadInterruptPending = false; sequentialReloadInterruptReady = false;
        lastChangeTime = DateTime.Now.Ticks;
        weaponMode = WeaponMode.Changing;
    }
}
