// Status effects on the LOCAL fighter from his own shots (fire, bleeding, poison, frost; the rebuild's slow).
// New Era runs these periodic impacts offline only on OPPONENTS (PlayerManager.StartOfflinePeriodicImpact :9217-9277 returns when
// attacker == target; online the server decided). Owner's request 12.11.2026: an own grenade / rocket / bullet with an effect hits the
// shooter too. The rules are New Era's offline ones (GetPeriodicImpactTypeForWeapon :9128-9157, GetPeriodicImpactTickDamage :9172-9215,
// ProcessOfflinePeriodicImpacts :9279-9330):
// - type from the weapon data (dot_type), else the hard-coded weapons; tick damage: data, else 4 (Candy 6);
// - ticks: data, else 3 (Candy 2); one tick every 0.5 s, armor first; a new hit of the same type refreshes the count, the next tick
//   at most 0.5 s later;
// - EffectManager.ImpactEffect: the HUD icon (BattleGUIImpact) 2 s on the hit, 0.65 s per tick; each tick also flashes the blood overlay
//   (GameHUD.ShotMePeriodic, through PlayerVitals.Damaged).
// SLOW is not in New Era at all (no slow code, no slow icon in the BattleGUI prefab): the rebuild weapons with "slow_ms"
// (Resources/data/rebuild_items.json, «Гранатин») slow the fighter to SlowFactor for that time, icon battlestate_slow in the unused
// Electro slot. The factor is a PROPOSAL (Docs/weapons/balance_proposal.md), not from any client.
using System.Collections.Generic;
using UnityEngine;

public class LocalImpacts : MonoBehaviour
{
    public const float SlowFactor = 0.6f;   // proposal: −40 % speed while slowed
    class Impact { public BattleGUIImpact.ImpactType Type; public int DamagePerTick, RemainingTicks; public float NextTickTime; public string Weapon; }
    readonly List<Impact> impacts = new List<Impact>();
    float slowUntil;
    PlayerVitals vitals;
    SoldierController soldier;

    void Awake() { vitals = GetComponent<PlayerVitals>(); soldier = GetComponent<SoldierController>(); }

    static bool IsName(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>GetPeriodicImpactTypeForWeapon (NE PlayerManager.cs:9128-9157).</summary>
    public static int ImpactTypeFor(CombatWeapon w, WeaponType t)
    {
        int id = w != null ? w.WeaponID : 0;
        string n = w != null && w.SystemName != null ? w.SystemName : string.Empty;
        if (w != null && w.PeriodicImpactType >= 1 && w.PeriodicImpactType <= 7) return w.PeriodicImpactType;
        if (id == 71 || IsName(n, "OHCA_Candy2")) return 5;
        if (t == WeaponType.ACID_THROWER || id == 45 || id == 75 || IsName(n, "GL_Milkor_A") || IsName(n, "SR_Wildcat2")) return 3;
        if (t == WeaponType.FLAMER || id == 25 || id == 72 || id == 80 || id == 104 || id == 112 || id == 114 || IsName(n, "FL_N1") || IsName(n, "OHCA_Candy")
            || IsName(n, "MG_AUG5_O") || IsName(n, "GL_GrenadeLauncher03") || IsName(n, "MG_UMP45Dold_O") || IsName(n, "MG_M4Elite_O")) return 1;
        if (id == 27 || id == 35 || id == 36 || id == 41 || id == 42 || id == 59 || id == 68 || id == 79 || id == 109 || IsName(n, "HG_SIGSauerP226_B") || IsName(n, "SR_HK417_D")
            || IsName(n, "SR_M110_B") || IsName(n, "THCA_Katana_B") || IsName(n, "THCA_Scythe_B") || IsName(n, "RL_RPG7B02") || IsName(n, "HG_GlockB01_S") || IsName(n, "MG_AUG4_O") || IsName(n, "SG_Remington")) return 2;
        return 0;
    }

    static bool IsCandy(CombatWeapon w) => w != null && (w.WeaponID == 71 || w.WeaponID == 72 || IsName(w.SystemName, "OHCA_Candy") || IsName(w.SystemName, "OHCA_Candy2"));

    static int TickDamage(CombatWeapon w, int type)   // :9172-9215
    {
        if (w != null)
        {
            if (w.WorkshopPeriodicTickDamage > 0) return w.WorkshopPeriodicTickDamage;
            if (w.PeriodicTickDamage > 0) return w.PeriodicTickDamage;
            if (w.WeaponID == 79) return 3;
            if (w.WeaponID == 80) return 0;
            if (w.WeaponID == 112 || w.WeaponID == 114) return 4;
        }
        if (IsCandy(w)) return 6;
        return type == 1 || type == 2 || type == 3 || type == 5 ? 4 : 0;
    }

    static void ShowIcon(BattleGUIImpact.ImpactType t, float sec) { var b = UIRoot.Instance ? UIRoot.Instance.Battle : null; if (b != null) b.ShowImpact(t, sec); }

    /// <summary>The local fighter hit by his own shot of weapon `w`: StartOfflinePeriodicImpact (:9217-9277) with the shooter as target,
    /// the first-hit icon (ImpactEffect, 2 s), and the rebuild slow.</summary>
    public void Apply(CombatWeapon w, WeaponType t)
    {
        if (vitals == null || vitals.Dead) return;
        var item = w != null ? ItemDB.Get(ItemKind.Weapon, w.WeaponID) : null;
        int slowMs = item != null ? item.Int("slow_ms") : 0;
        if (slowMs > 0) { slowUntil = Mathf.Max(slowUntil, Time.time + slowMs / 1000f); ShowIcon(BattleGUIImpact.ImpactType.Slow, slowMs / 1000f); }
        int type = ImpactTypeFor(w, t);
        if (type == 0) return;
        int dmg = TickDamage(w, type);
        if (dmg <= 0) return;
        var it = (BattleGUIImpact.ImpactType)type;
        ShowIcon(it, 2f);
        Impact imp = impacts.Find(x => x.Type == it);
        bool fresh = imp == null;
        if (fresh) { imp = new Impact { Type = it }; impacts.Add(imp); }
        imp.DamagePerTick = dmg;
        imp.Weapon = w != null ? w.SystemName : null;
        imp.RemainingTicks = w != null && w.WorkshopPeriodicTickCount > 0 ? w.WorkshopPeriodicTickCount : w != null && w.PeriodicTickCount > 0 ? w.PeriodicTickCount : IsCandy(w) ? 2 : 3;
        if (fresh || imp.NextTickTime <= 0f) { imp.NextTickTime = Time.time; return; }
        float soon = Time.time + 0.5f;
        if (imp.NextTickTime > soon) imp.NextTickTime = soon;
    }

    public void Clear() { impacts.Clear(); slowUntil = 0f; if (soldier) soldier.impactSpeedMultiplier = 1f; }

    void Update()
    {
        if (soldier) soldier.impactSpeedMultiplier = Time.time < slowUntil ? SlowFactor : 1f;
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            var imp = impacts[i];
            if (imp.RemainingTicks <= 0 || Time.time < imp.NextTickTime) continue;
            if (vitals == null || vitals.Dead) { impacts.RemoveAt(i); continue; }
            vitals.ApplyDamageArmorFirst(0, Mathf.Max(1, imp.DamagePerTick), imp.Weapon);   // ApplyDamageArmorFirstAndReport(target, 0, tick)
            if (i >= impacts.Count || impacts[i] != imp) break;   // the tick killed the fighter: OnLocalDied cleared the list
            imp.RemainingTicks--;
            imp.NextTickTime += 0.5f;
            ShowIcon(imp.Type, 0.65f);   // PlayerImpact periodic → ImpactEffect(type, periodic)
            if (vitals.Dead || imp.RemainingTicks <= 0) impacts.RemoveAt(i);
        }
    }
}
