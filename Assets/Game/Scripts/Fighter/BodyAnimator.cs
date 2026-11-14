// Weapons stage 5: the weapon part of New Era's ActorAnimator for a fighter's body (NE_Scripts_Decrypted/ActorAnimator.cs; Penta 2016
// the same clips): the upper-body aim stance per weapon set ("<set>BendClip", layer 1, mixed from Bip01 Spine1, its time = the aim pitch),
// the body reload clips, the melee swings, the weapon-model clips (Shot / Load / Spin / Stop), and the weapon-set table of
// PlayerManager.ChangeWeaponAnimation. Locomotion (layer 0) stays with the fighter's own driver (FighterBody for the local soldier).
// Line numbers: NE ActorAnimator.cs unless noted.
using System;
using UnityEngine;

public class BodyAnimator : MonoBehaviour
{
    static readonly string[] BendFallbackAnimationNames = { "ShotgunBendClip", "AK47BendClip", "MakarovBendClip", "BatBendClip", "KatanaBendClip" };   // :8-15

    public Animation Anim;
    public float BendAnimationAngle = 0.5f;   // :125
    /// <summary>SoldierController.targetXRotation: the aim pitch in degrees (−90 … 90), read by Update into the bend clip's time.</summary>
    public float TargetXRotation;
    public bool Taunting, IsZombie, Crouch;
    public string IdleAnimationName = "Idle";
    bool setFlag;
    string bendAnimationName = "BatBendClip";   // :121
    string cachedBendAnimationName = string.Empty, lastMissingBendAnimationName;
    AnimationState cachedBendAnimationState;
    int tauntId;

    public string BendAnimationName
    {
        get => bendAnimationName;
        set { bendAnimationName = value; cachedBendAnimationName = string.Empty; cachedBendAnimationState = null; }   // :187-201
    }

    void Start() { try { SetAnimationProperties(); } catch (Exception ex) { Debug.LogWarning("[ActorAnimator] SetAnimationProperties failed (some clips may be missing): " + ex.Message); } }   // :331-339

    AnimationState GetAnimationState(string a) => Anim != null && !string.IsNullOrEmpty(a) ? Anim[a] : null;

    string ResolveBendAnimationName(string a)   // :281-302
    {
        if (GetAnimationState(a) != null) return a;
        foreach (var t in BendFallbackAnimationNames)
            if (t != a && GetAnimationState(t) != null)
            {
                if (lastMissingBendAnimationName != a) { lastMissingBendAnimationName = a; Debug.LogWarning($"[ActorAnimator] Missing bend clip '{a}', using '{t}' instead."); }
                return t;
            }
        return a;
    }

    AnimationState GetResolvedBendAnimationState()   // :303-317
    {
        if (cachedBendAnimationState != null && cachedBendAnimationName == BendAnimationName) return cachedBendAnimationState;
        string t = ResolveBendAnimationName(BendAnimationName);
        if (bendAnimationName != t) bendAnimationName = t;
        cachedBendAnimationName = bendAnimationName;
        cachedBendAnimationState = GetAnimationState(bendAnimationName);
        return cachedBendAnimationState;
    }

    void SafeAddClip(string b, string c, bool d, AnimationBlendMode e, WrapMode f)   // :319-329
    {
        if (Anim[b] == null || Anim[b].clip == null) return;
        Anim.AddClip(Anim[b].clip, c);
        SetupAdditiveAiming(c, d);
        Anim[c].blendMode = e;
        Anim[c].wrapMode = f;
    }

    void AddAiming(string src, string dst, bool head = false)
    {
        if (Anim[src] == null) return;
        Anim.AddClip(Anim[src].clip, dst);
        if (head) SetupHeadAiming(dst, true); else SetupAdditiveAiming(dst, true);
    }

    /// <summary>SetAnimationProperties (:342-468), the weapon clips (jump and taunts belong to the locomotion driver).</summary>
    void SetAnimationProperties()
    {
        if (setFlag || Anim == null) return;
        setFlag = true;
        Anim.wrapMode = WrapMode.Loop;
        AddAiming("Bend", "ShotgunBendClip");
        AddAiming("Bat_Bend", "BatBendClip", true);
        AddAiming("Katana_Bend", "KatanaBendClip");
        AddAiming("Makarov_Bend", "MakarovBendClip");
        AddAiming("AK47_Bend", "AK47BendClip");
        foreach (var n in new[] { "UMP45", "SteyrAUG", "Wildcat", "TMP", "M134", "Winchester1887", "Novapump", "RPG26", "RPG7", "M202A1", "Milkor", "SnowLauncher",
                                  "SVD", "Spas", "FNMAG", "Vintorez", "M249", "AssaultRifle02", "AssaultRifle03", "SniperRifle03", "Shotgun02", "GrenadeLauncher03" })
            SafeAddClip(n + "_Bend", n + "BendClip", true, AnimationBlendMode.Blend, WrapMode.Once);
        if (Anim["ShotgunChange"] != null)
        {
            Anim.AddClip(Anim["ShotgunChange"].clip, "Change");
            SetupAdditiveAiming("Change", true);
            Anim["Change"].blendMode = AnimationBlendMode.Additive;
            Anim["Change"].wrapMode = WrapMode.Once;
        }
        foreach (var (src, dst) in new[] {
            ("ShotgunReload", "ShotgunReloadClip"), ("AK47_Reload", "MachineGunWeaponReloadClip"), ("UMP45_Reload", "UMP45ReloadClip"),
            ("SteyrAUG_Reload", "SteyrAUGReloadClip"), ("FNMAG_Reload", "FNMAGReloadClip"), ("Wildcat_Reload", "WildcatReloadClip"), ("TMP_Reload", "TMPReloadClip"),
            ("Makarov_Reload", "HandGunWeaponReloadClip"), ("RPG26_Reload", "RocketLauncherWeaponReloadClip"), ("M202A1_Reload", "M202A1ReloadClip"),
            ("Milkor_Reload", "GrenadeLauncherWeaponReloadClip"), ("SnowLauncher_Reload", "SnowLauncherWeaponReloadClip"), ("Winchester1887_Reload", "ShotGunWeaponReloadClip"),
            ("AssaultRifle02_Reload", "AssaultRifle02ReloadClip"), ("AssaultRifle03_Reload", "AssaultRifle03ReloadClip"), ("SniperRifle03_Reload", "SniperRifle03ReloadClip"),
            ("Shotgun02_Reload", "Shotgun02ReloadClip"), ("GrenadeLauncher03_Reload", "GrenadeLauncher03ReloadClip") })
            SafeAddClip(src, dst, true, AnimationBlendMode.Additive, WrapMode.Once);
        foreach (var (src, dst, mode, wrap) in new[] {
            ("Bat_Shot", "BatShotClip", AnimationBlendMode.Blend, WrapMode.Once), ("Zombie_Shot", "ZombieShotClip", AnimationBlendMode.Blend, WrapMode.Once),
            ("Bat_Idle", "BatIdleClip", AnimationBlendMode.Additive, WrapMode.Loop), ("Katana_Shot", "KatanaShotClip", AnimationBlendMode.Additive, WrapMode.Once),
            ("Katana_Idle", "KatanaIdleClip", AnimationBlendMode.Additive, WrapMode.Loop) })
        {
            if (Anim[src] == null) continue;
            Anim.AddClip(Anim[src].clip, dst);
            SetupAdditiveAiming(dst, true);
            Anim[dst].blendMode = mode;
            Anim[dst].wrapMode = wrap;
        }
    }

    /// <summary>IsBendAnimation (:469-630): false while the current set's reload clip (or melee swing) plays.</summary>
    bool IsBendAnimation()
    {
        if (Anim == null) return false;
        string blocking;
        switch (ResolveBendAnimationName(BendAnimationName))
        {
            case "BatBendClip": blocking = "BatShotClip"; break;
            case "KatanaBendClip": blocking = "KatanaShotClip"; break;
            case "MakarovBendClip": blocking = "HandGunWeaponReloadClip"; break;
            case "UMP45BendClip": blocking = "UMP45ReloadClip"; break;
            case "SteyrAUGBendClip": blocking = "SteyrAUGReloadClip"; break;
            case "WildcatBendClip": blocking = "WildcatReloadClip"; break;
            case "TMPBendClip": blocking = "TMPReloadClip"; break;
            case "AK47BendClip": case "VintorezBendClip": case "SVDBendClip": blocking = "MachineGunWeaponReloadClip"; break;
            case "SpasBendClip": case "Winchester1887BendClip": case "NovapumpBendClip": blocking = "ShotGunWeaponReloadClip"; break;
            case "RPG7BendClip": case "RPG26BendClip": blocking = "RocketLauncherWeaponReloadClip"; break;
            case "M202A1BendClip": blocking = "M202A1ReloadClip"; break;
            case "MilkorBendClip": blocking = "GrenadeLauncherWeaponReloadClip"; break;
            case "SnowLauncherBendClip": blocking = "SnowLauncherWeaponReloadClip"; break;
            case "SniperRifle03BendClip": blocking = "SniperRifle03ReloadClip"; break;
            case "AssaultRifle03BendClip": blocking = "AssaultRifle03ReloadClip"; break;
            case "AssaultRifle02BendClip": blocking = "AssaultRifle02ReloadClip"; break;
            case "Shotgun02BendClip": blocking = "Shotgun02ReloadClip"; break;
            case "GrenadeLauncher03BendClip": blocking = "GrenadeLauncher03ReloadClip"; break;
            default: return true;
        }
        return !Anim.IsPlaying(blocking);
    }

    void SetupHeadAiming(string a, bool b)   // :632-650
    {
        var s = GetAnimationState(a);
        if (s == null) return;
        if (b)
        {
            var neck = transform.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Spine3/Bip01 Neck");
            var head = transform.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Spine3/Bip01 Neck/Bip01 Head");
            if (neck) s.AddMixingTransform(neck, false);
            if (head) s.AddMixingTransform(head, false);
        }
        s.blendMode = AnimationBlendMode.Blend; s.enabled = true; s.weight = 0f; s.layer = 1; s.time = 0f; s.speed = 0f; s.wrapMode = WrapMode.Once;
    }

    void SetupAdditiveAiming(string a, bool b)   // :652-668
    {
        var s = GetAnimationState(a);
        if (s == null) return;
        if (b) { var spine1 = transform.Find("Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1"); if (spine1) s.AddMixingTransform(spine1); }
        s.blendMode = AnimationBlendMode.Blend; s.enabled = true; s.weight = 0f; s.layer = 1; s.time = 0f; s.speed = 0f; s.wrapMode = WrapMode.Once;
    }

    /// <summary>The bend part of Update (:938-970): the pitch → the bend clip's time (targetXRotation / 180 + 0.5, 0 … 0.96), then
    /// CrossFade to it unless taunting, a zombie, or the set's reload / swing plays.</summary>
    void Update()
    {
        BendAnimationAngle = Mathf.Clamp(TargetXRotation / 180f + 0.5f, 0f, 0.96f);
        if (Anim == null) return;
        SetAnimationProperties();
        var s = GetResolvedBendAnimationState();
        if (s != null) s.time = BendAnimationAngle;
        if (!Taunting && !IsZombie)
        {
            if (s != null && IsBendAnimation()) { s.weight = 1f; Anim.CrossFade(BendAnimationName, 0.2f); }
        }
        else if (s != null) s.weight = 0f;
    }

    /// <summary>ChangeWeaponAnimation(string) (:1115-1158): the old bend blends out over 0.3 s, the new one cross-fades in.</summary>
    public void ChangeWeaponAnimation(string a)
    {
        SetAnimationProperties();
        if (Anim == null) return;
        string t = ResolveBendAnimationName(a + "BendClip");
        if (BendAnimationName != t)
        {
            if (GetAnimationState(BendAnimationName) != null) Anim.Blend(BendAnimationName, 0f, 0.3f);
            BendAnimationName = t;
        }
        if (!Crouch) IdleAnimationName = "Idle";
        if (GetAnimationState(BendAnimationName) != null) Anim.CrossFade(BendAnimationName, 0.3f);
    }

    /// <summary>PlayerManager.ChangeWeaponAnimation (NE PlayerManager.cs:5025-5158): the weapon set by type and system name.</summary>
    public static string AnimationSet(WeaponType b, string c)
    {
        c = c ?? string.Empty;
        switch (b)
        {
            case WeaponType.ELECTRO_SHOCKER: case WeaponType.BIO_SHOCKER: return null;
            case WeaponType.ONE_HANDED_COLD_ARMS: return "Bat";
            case WeaponType.TWO_HANDED_COLD_ARMS: return "Katana";
            case WeaponType.HAND_GUN: return "Makarov";
            case WeaponType.MACHINE_GUN:
                return c.StartsWith("MG_AssaultRifle02") ? "AssaultRifle02" : c.StartsWith("MG_AssaultRifle03") ? "AssaultRifle03" : c.StartsWith("MG_AUG") ? "SteyrAUG"
                    : c.StartsWith("MG_UMP45D") || c.StartsWith("MG_UMP45V") ? "UMP45" : c.StartsWith("MG_UMP45") ? "TMP" : "AK47";
            case WeaponType.FLAMER: case WeaponType.GATLING_GUN: case WeaponType.SNOW_GUN: case WeaponType.ACID_THROWER:
                return c == "GG_M249" ? "M249" : c == "GG_FNMAG" ? "FNMAG" : "M134";
            case WeaponType.SHOT_GUN: return c == "SG_Novapump" || c == "SG_NovapumpOld" ? "Novapump" : c == "SG_Spas" || c == "SG_Remington" ? "Spas" : "Winchester1887";
            case WeaponType.ROCKET_LAUNCHER: return c == "RL_RPG7" ? "RPG7" : c == "RL_M202A1" ? "M202A1" : "RPG26";
            case WeaponType.GRENADE_LAUNCHER: case WeaponType.BOMB_LAUNCHER:
                return c.StartsWith("GL_EX41") ? "Shotgun02" : c.StartsWith("GL_GrenadeLauncher03") ? "GrenadeLauncher03" : c.Contains("GL_SnowLauncher") ? "SnowLauncher" : "Milkor";
            case WeaponType.SNIPER_RIFLE:
                return c.StartsWith("SR_SniperRifle03") ? "SniperRifle03" : c.StartsWith("SR_Wildcat") ? "Wildcat" : c.StartsWith("SR_Vintorez") ? "Vintorez" : "SVD";
            default: return null;
        }
    }

    public void ChangeWeaponAnimation(WeaponType b, string c) { var set = AnimationSet(b, c); if (set != null) ChangeWeaponAnimation(set); }

    /// <summary>ReloadWeaponAnimation (:1274-1369): the body's reload clip by type node and system name (Blend to 1 over 0.3 s).</summary>
    public void ReloadWeaponAnimation(string a, CombatWeapon b)
    {
        SetAnimationProperties();
        if (Anim == null) return;
        void Go(string clip, float speed = 1f) { var s = Anim[clip]; if (s == null) return; s.speed = speed; Anim.Blend(clip, 1f, 0.3f); }
        string sn = b != null && b.SystemName != null ? b.SystemName : string.Empty;
        if (a == "MachineGunWeapon" || a == "HandGunWeapon" || a == "RocketLauncherWeapon" || a == "GrenadeLauncherWeapon" || a == "BombLauncherWeapon" || a == "ShotGunWeapon")
        {
            if (b != null && b.Type == WeaponType.ROCKET_LAUNCHER && (b.WeaponID == 59 || sn.IndexOf("RPG7B02", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var s = Anim["RocketLauncherWeaponReloadClip"];
                if (s != null)
                {
                    float num = b.ReloadTime / 10000000f;
                    s.speed = num > 0.01f && s.length > 0.01f ? Mathf.Clamp(s.length / num, 0.1f, 5f) : 1f;
                    Anim.Blend("RocketLauncherWeaponReloadClip", 1f, 0.3f);
                }
            }
            else if (sn.StartsWith("GL_GrenadeLauncher03")) Go("GrenadeLauncher03ReloadClip");
            else if (sn.StartsWith("SG_Shotgun02")) Go("Shotgun02ReloadClip");
            else if (sn.StartsWith("MG_AssaultRifle03")) Go("AssaultRifle03ReloadClip");
            else if (sn.StartsWith("MG_AssaultRifle02")) Go("AssaultRifle02ReloadClip");
            else if (sn.StartsWith("MG_AUG")) Go("SteyrAUGReloadClip");
            else if (sn.StartsWith("MG_UMP45D") || sn.StartsWith("MG_UMP45V")) Go("UMP45ReloadClip");
            else if (sn.StartsWith("MG_UMP45")) Go("TMPReloadClip");
            else if (sn.StartsWith("BL_Sticky")) Go("GrenadeLauncherWeaponReloadClip");
            else Go(a + "ReloadClip");
        }
        else if (a == "SniperRifleWeapon")
        {
            if (sn.StartsWith("SR_SniperRifle03")) Go("SniperRifle03ReloadClip");
            else if (sn.StartsWith("SR_Wildcat")) Go("WildcatReloadClip");
            else Go("MachineGunWeaponReloadClip");
        }
        else Go("ShotgunReloadClip");
    }

    /// <summary>ShotWeaponAnimation(string) (:1567-1585): the melee swing, Bat ×2, Katana ×1.75, Zombie ×1.5.</summary>
    public void ShotWeaponAnimation(string a)
    {
        SetAnimationProperties();
        if (!setFlag || Anim == null || Anim[a + "ShotClip"] == null) return;
        Anim[a + "ShotClip"].speed = a == "Bat" ? 2f : a == "Katana" ? 1.75f : 1.5f;
        Anim.Blend(a + "ShotClip", 1f, 0.1f);
    }

    /// <summary>ShotWeaponAnimation(string, Transform) (:1502-1565): the clips of the weapon MODEL — Stop → "End", Load → "Start" then
    /// "Load", Spin → "Load", else the clip itself — at ×1.5, with "Idle" queued (the Taurus visual reset is not ported).</summary>
    public static void ShotWeaponAnimation(string a, Transform b)
    {
        if (b == null) return;
        var an = b.GetComponentInChildren<Animation>();
        if (an == null) return;
        if (a == "Stop") { if (an["End"] != null) { an["End"].speed = 1.5f; an.Play("End", PlayMode.StopSameLayer); } return; }
        if (a == "Load")
        {
            if (an["Start"] != null) { an["Start"].speed = 1.5f; an.Play("Start", PlayMode.StopSameLayer); }
            if (an["Load"] != null) { an["Load"].speed = 1.5f; an.PlayQueued("Load", QueueMode.CompleteOthers); }
            return;
        }
        if (a == "Spin") { if (an["Load"] != null) { an["Load"].speed = 1.5f; an.Play("Load", PlayMode.StopSameLayer); } return; }
        if (an[a] == null) return;
        an[a].speed = 1.5f; an[a].time = 0f;
        an.Play(a, PlayMode.StopSameLayer);
        if (an["Idle"] != null) { an["Idle"].wrapMode = WrapMode.Loop; an.PlayQueued("Idle", QueueMode.CompleteOthers); }
    }

    // ---------------- taunts on the body (:1217-1272): the bend steps aside ----------------
    public void TauntAnimation(int id) { Taunting = true; tauntId = id; var s = GetResolvedBendAnimationState(); if (s != null) s.weight = 0f; }
    public void CancelTauntAnimation() { Taunting = false; tauntId = 0; }
    public void FinishTauntAnimation() { Taunting = false; tauntId = 0; }
}
