// Weapons stage 5: the weapon in the hands of a fighter's BODY (third person). Port of New Era's WeaponController
// (NE_Scripts_Decrypted/WeaponController.cs), the part a remote fighter runs. ShotController derives from it in New Era; the local
// first-person side is our ShotController. The body prefab (Enemy_Fighter_locomotion) holds Bip01 R Hand/WeaponRight with the eleven
// WeaponLook type nodes (MachineGunWeapon …), each with every model of its class as a child named by the system name.
// In New Era the LOCAL fighter's body never shows a weapon (CombatPlayer.ShowWeapons is only called from setPlayerShipVisible, and
// KillMe hides the weapons), and the lobby preview turns every WeaponLook off (PlayerCustomisator.Init :125-132). So this is for other
// fighters (bots / remote players, later), tested on a dummy (UITest). Line numbers: NE WeaponController.cs unless noted.
using System;
using System.Collections;
using UnityEngine;

public class BodyWeapons : MonoBehaviour
{
    public const int WeaponNum = 7;   // WeaponController.WeaponNum
    CombatWeapon weapon;
    readonly CombatWeapon[] weapons = new CombatWeapon[WeaponNum];   // Awake :287
    bool taunt;
    int activeTauntID;
    Coroutine tauntCoroutine;

    public BodyAnimator Animator;     // the fighter's ActorAnimator (weapon part)
    public bool IsZombie, IsDead;
    public CombatWeapon CurrentWeapon => weapon;
    public CombatWeapon[] Weapons => weapons;

    void Awake() { if (Animator == null) Animator = GetComponent<BodyAnimator>(); }

    public void ResetWeapon() { weapon = weapons[0]; }   // :39-45

    public CombatWeapon GetWeapon(int a) => a < 1 || a > weapons.Length ? null : weapons[a - 1];   // :47-54

    /// <summary>OnChangeWeapon (:56-84) + PlayerManager.ChangeWeapon for a remote fighter (NE PlayerManager.cs:4995-5016): the slot's
    /// weapon becomes current, Change() (its timers), the models swap (Change coroutine: instantly), then ChangeWeaponAnimation.</summary>
    public WeaponType OnChangeWeapon(int a)
    {
        if (a < 1 || a > weapons.Length) return WeaponType.NONE;
        var w = weapons[a - 1];
        if (w == null) return WeaponType.NONE;
        int num = weapon != null ? weapon.Index : -1;
        weapon = w;
        weapon.Change();
        if (num >= 0 && num < weapons.Length && weapons[num] != null) StartCoroutine(Change(num, a - 1));
        else { HideWeapons(); ShowWeapon(); }
        if (Animator != null) Animator.ChangeWeaponAnimation(weapon.Type, weapon.SystemName);   // PlayerManager.cs:5013
        return weapon.Type;
    }

    /// <summary>InitZombieWeapon (:86-103): the claw (OHCA_Zombie), weapons hidden after 0.2 s.</summary>
    public void InitZombieWeapon(bool a) { StartCoroutine(ChangeZombieWeapon(new CombatWeapon(1, 996, WeaponType.ONE_HANDED_COLD_ARMS, "OHCA_Zombie"))); }
    IEnumerator ChangeZombieWeapon(CombatWeapon a) { yield return new WaitForSeconds(0.2f); weapon = a; HideWeapons(); }

    public int InitWeapon(int a, Hashtable b) => b == null ? 0 : InitWeapon(a, new CombatWeapon(a, b));   // :105-111

    /// <summary>InitWeapon (:112-170): the weapon's Transform = the WeaponLook named by its type (or with "L" / "R"); under it the
    /// system-name model is activated (visible for slot 0, hidden for the others), every other model's renderers off
    /// (喩.Destroy_Geometry, the low-memory option that destroys them, is not ported).</summary>
    public int InitWeapon(int a, CombatWeapon b)
    {
        if (a < 0 || a >= weapons.Length || b == null) return 0;
        weapons[a] = b;
        string n = b.GetName();
        foreach (var look in transform.GetComponentsInChildren<WeaponLook>(true))
            if (look.transform.name == n || look.transform.name == n + "L" || look.transform.name == n + "R") b.Transform = look.transform;
        if (b.Transform != null)
        {
            bool first = a == 0;
            if (first) weapon = b;
            for (int j = 0; j < b.Transform.childCount; j++)
            {
                var child = b.Transform.GetChild(j);
                if (child.gameObject.name == b.SystemName)
                {
                    NormalizeVeteranRemoteRenderLayer(b, child);
                    child.gameObject.SetActive(true);
                    setWeaponVisible(child, first);
                    continue;
                }
                setWeaponVisible(child, false);
            }
        }
        else if (a == 0) weapon = b;
        return b.GetType();
    }

    /// <summary>HideWeapons (:172-202): every renderer under every WeaponLook and every weapon's Transform off.</summary>
    public void HideWeapons()
    {
        foreach (var look in transform.GetComponentsInChildren<WeaponLook>(true))
            if (look != null) foreach (var r in look.transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var w in weapons)
            if (w != null && w.Transform != null) foreach (var r in w.Transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    public void HideWeapon()   // :204-215
    {
        if (weapon == null || weapon.Transform == null) return;
        foreach (var r in weapon.Transform.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    public void ShowWeapon()   // :217-224
    {
        if (weapon == null || weapon.Transform == null) return;
        HideWeapons();
        ShowOnlyWeaponModel(weapon);
    }

    static bool IsVeteranShotgun(CombatWeapon a) => a != null && !string.IsNullOrEmpty(a.SystemName) && string.Equals(a.SystemName, "SG_NovapumpOld", StringComparison.OrdinalIgnoreCase);

    static void NormalizeVeteranRemoteRenderLayer(CombatWeapon a, Transform b)   // :231-247
    {
        if (!IsVeteranShotgun(a) || a.Transform == null || b == null) return;
        int layer = a.Transform.gameObject.layer;
        foreach (var r in b.GetComponentsInChildren<Renderer>(true)) if (r != null) r.gameObject.layer = layer;
    }

    /// <summary>ShowOnlyWeaponModel (:249-285): the child named exactly by the system name, else the first that starts with it.</summary>
    void ShowOnlyWeaponModel(CombatWeapon a)
    {
        if (a == null || a.Transform == null) return;
        var t = a.Transform;
        if (t.childCount <= 0) { setWeaponVisible(t, true); return; }
        bool exact = false;
        for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.name == a.SystemName) { exact = true; break; }
        for (int j = 0; j < t.childCount; j++)
        {
            var c = t.GetChild(j);
            bool on = c.gameObject.name == a.SystemName;
            if (!exact && !string.IsNullOrEmpty(a.SystemName)) on = c.gameObject.name.StartsWith(a.SystemName);
            if (on) NormalizeVeteranRemoteRenderLayer(a, c);
            setWeaponVisible(c, on);
        }
    }

    IEnumerator Change(int a, int b)   // :297-305
    {
        if (b >= 0 && b < weapons.Length && weapons[b] != null) { HideWeapons(); ShowOnlyWeaponModel(weapons[b]); }
        yield break;
    }

    public void setWeaponVisible(Transform a, bool b) { foreach (var r in a.GetComponentsInChildren<Renderer>(true)) r.enabled = b; }   // :307-314

    public CombatWeapon GetWeaponByType(int a)   // :316-331 (203 = Kamikadze: no item in the rebuild)
    {
        foreach (var w in weapons) if (w != null && w.GetType() == a) return w;
        return null;
    }

    // ---------------- taunts (:338-403) ----------------
    public void LaunchTaunt(int b)
    {
        if (Animator != null) Animator.TauntAnimation(b);
        taunt = true; activeTauntID = b;
        if (!IsZombie) HideWeapon();
        tauntCoroutine = StartCoroutine(FinishTaunt(b));
    }

    public void CancelTaunt()
    {
        if (!taunt && activeTauntID <= 0) return;
        if (tauntCoroutine != null) { StopCoroutine(tauntCoroutine); tauntCoroutine = null; }
        activeTauntID = 0; taunt = false;
        if (Animator != null) Animator.CancelTauntAnimation();
        if (!IsDead && !IsZombie) ShowWeapon();
    }

    IEnumerator FinishTaunt(int b)
    {
        float s = b == 10 ? 15.3f : b == 9 ? 3.5f : b == 8 ? 8.5f : b == 7 ? 3.5f : b > 5 ? 5.6f : b > 1 ? 2.8f : 1.5f;
        yield return new WaitForSeconds(s);
        tauntCoroutine = null;
        if (!IsDead) { if (Animator != null) Animator.FinishTauntAnimation(); if (!IsZombie) ShowWeapon(); }
        activeTauntID = 0; taunt = false;
    }

    public void ResumeWeapon() { ShowWeapon(); }   // :428-431

    // ---------------- remote actions (NE PlayerManager / BattleServerHttpClient) ----------------
    /// <summary>CombatPlayer.Kill (:1378): the weapons hide.</summary>
    public void Kill() { IsDead = true; HideWeapons(); }

    /// <summary>PlayerManager.ReloadWeapon for a remote fighter (NE PlayerManager.cs:4937-4943): the body's reload clip.</summary>
    public void ReloadAnimation() { if (weapon != null && Animator != null) Animator.ReloadWeaponAnimation(CombatWeapon.getName(weapon.Type), weapon); }

    /// <summary>A remote shot (NE PlayerManager.ShotEffect :9730-9750, BattleServerHttpClient.PlayRemoteWeaponShotAnimation :7876-7940):
    /// melee LAUNCH → the body's Bat / Katana / Zombie swing; hand gun, rifle, shotgun, sniper → "Shot" on the type node's model
    /// Animation, gatling → on its Transform; flamer and launchers: nothing.</summary>
    public void ShotAnimation(WeaponType b, ShotLaunchMode mode)
    {
        if (b == WeaponType.ONE_HANDED_COLD_ARMS) { if (mode == ShotLaunchMode.LAUNCH && Animator != null) Animator.ShotWeaponAnimation(IsZombie ? "Zombie" : "Bat"); return; }
        if (b == WeaponType.TWO_HANDED_COLD_ARMS) { if (mode == ShotLaunchMode.LAUNCH && Animator != null) Animator.ShotWeaponAnimation("Katana"); return; }
        if (mode == ShotLaunchMode.BLOW || IsDead) return;
        string text;
        switch (b)
        {
            case WeaponType.HAND_GUN: text = "HandGunWeapon"; break;
            case WeaponType.MACHINE_GUN: text = "MachineGunWeapon"; break;
            case WeaponType.SHOT_GUN: text = "ShotGunWeapon"; break;
            case WeaponType.SNIPER_RIFLE: text = "SniperRifleWeapon"; break;
            case WeaponType.GATLING_GUN: text = ""; break;
            default: return;
        }
        Transform t = null;
        if (text.Length > 0)
        {
            if (EffectManager.Instance != null) t = EffectManager.Instance.getWeaponTransformByName(transform, text);
            else foreach (var look in transform.GetComponentsInChildren<WeaponLook>(true)) if (look != null && look.gameObject.name == text) { t = look.transform; break; }
        }
        if (t == null && weapon != null) t = weapon.Transform;
        if (t != null) BodyAnimator.ShotWeaponAnimation("Shot", t);
    }
}
