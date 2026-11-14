// The local player's battle weapons = New Era PlayerManager.BuildLocalWeaponInfoForBattle / TryAddLocalWeaponForBattle
// (NE PlayerManager.cs:1666-1880) with the Resolve* helpers (:2057-2110, :2111-2286, :2288-2722): one weapon-info hashtable per slot
// (keys 80 id, 98 type, 92 clip, 91 total, 97 velocity, 90 life, 96 distance, 95 angle, 99 sname, 87 deviation, 94 rapidity,
// 128 exact attack speed, 93 reload, 129 exact reload, 131-133 periodic damage), read by the CombatWeapon(int, Hashtable) constructor.
// An empty slot takes the default weapon (嗭.WeaponDefault — Profile.WeaponInSlot). Weapon fields are the server item fields
// (Weapon.cs:594-835: ammo, ammo_tot, vel, rad, ang, dev, rap, attackspeed, rt, reload, accuracy, lt).
// Not here yet: workshop upgrades (keys 110-125, 127, 130; weapons stage 4) and the damage keys 88 / 86 (stage 2).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class BattleWeapons
{
    // TryAddLocalWeaponForBattle(dictionary, slot, weapon, default velocity, default distance) — NE :1669-1675
    static readonly int[] DefaultVelocity = { 3200, 2800, 2200, 1800, 1500, 1500, 1500 };
    static readonly int[] DefaultDistance = { 900, 850, 500, 400, 350, 350, 350 };

    const int WeaponRapidity = 5, WeaponAmmo = 7, WeaponAccuracy = 10;   // 嗲 (GameLogic.Ability)

    public static Dictionary<int, Hashtable> Build(Profile p)
    {
        var d = new Dictionary<int, Hashtable>();
        for (int b = 0; b < 7; b++) TryAdd(p, d, b, p.WeaponInSlot(b + 1), DefaultVelocity[b], DefaultDistance[b]);
        return d;
    }

    /// <summary>Weapon.NormalizeSystemName (NE Weapon.cs:845-852).</summary>
    public static string SystemName(GameItem c) => string.Equals(c.SName, "SR_Steyrford", StringComparison.OrdinalIgnoreCase) ? "SR_Steyrf0rd" : c.SName;

    static void TryAdd(Profile p, Dictionary<int, Hashtable> a, int b, GameItem c, int d, int e)
    {
        if (c == null || c.Id <= 0 || string.IsNullOrEmpty(c.SName)) return;
        var type = ResolveType(c);
        int clip = ResolveClipAmmo(c, type);
        int total = ResolveTotalAmmo(p, c, type, clip);
        int rapidity = ResolveRapidity(p, c, type);
        int reload = ResolveReloadTime(c, type);
        var h = new Hashtable();
        h[(byte)80] = c.Id;
        h[(byte)98] = (int)type;
        h[(byte)92] = clip;
        h[(byte)91] = Math.Max(total, clip);    // num15 < num14 → num14 (:1739-1742)
        h[(byte)97] = ResolveVelocity(c, type, d);
        h[(byte)90] = c.Int("lt");
        h[(byte)96] = ResolveDistance(c, type, e);
        h[(byte)95] = ResolveAngle(c, type, 0);
        h[(byte)99] = SystemName(c);
        h[(byte)87] = ResolveDeviation(p, c, type);
        h[(byte)94] = rapidity;
        if (c.Int("attackspeed") != 0) h[(byte)128] = rapidity;
        h[(byte)93] = reload;
        if (c.Int("reload") != 0) h[(byte)129] = reload;
        int impact = ResolvePeriodicImpactType(c.Str("dot_type"));
        if (impact > 0) h[(byte)131] = impact;
        if (c.Int("dot_damage") > 0) h[(byte)132] = c.Int("dot_damage");
        if (c.Int("dot_ticks") > 0) h[(byte)133] = c.Int("dot_ticks");
        a[b] = h;
    }

    /// <summary>ResolveConfiguredPeriodicImpactType (NE PlayerManager.cs:2031-2056): fire/burn 1, blood/bleed/bleeding 2, poison/acid 3,
    /// cold/frost/freeze 5.</summary>
    static int ResolvePeriodicImpactType(string dotType)
    {
        if (string.IsNullOrEmpty(dotType)) return 0;
        string t = dotType.Trim().ToLowerInvariant();
        if (t == "fire" || t == "burn") return 1;
        if (t == "blood" || t == "bleed" || t == "bleeding") return 2;
        if (t == "poison" || t == "acid") return 3;
        if (t == "cold" || t == "frost" || t == "freeze") return 5;
        return 0;
    }

    static WeaponType ResolveType(GameItem a)   // :2057-2110
    {
        string s = a.SName ?? string.Empty;
        if (s.StartsWith("OHCA_")) return WeaponType.ONE_HANDED_COLD_ARMS;
        if (s.StartsWith("THCA_")) return WeaponType.TWO_HANDED_COLD_ARMS;
        if (s.StartsWith("HG_")) return WeaponType.HAND_GUN;
        if (s.StartsWith("MG_")) return WeaponType.MACHINE_GUN;
        if (s.StartsWith("FL_")) return WeaponType.FLAMER;
        if (s.StartsWith("GG_")) return WeaponType.GATLING_GUN;
        if (s.StartsWith("SG_")) return WeaponType.SHOT_GUN;
        if (s.StartsWith("RL_")) return WeaponType.ROCKET_LAUNCHER;
        if (s.StartsWith("GL_")) return WeaponType.GRENADE_LAUNCHER;
        if (s.StartsWith("SR_")) return WeaponType.SNIPER_RIFLE;
        if (s.StartsWith("SNG_")) return WeaponType.SNOW_GUN;
        if (s.StartsWith("BL_")) return WeaponType.BOMB_LAUNCHER;
        return (WeaponType)a.Wt;
    }

    static bool Has(string s, string part) => (s ?? string.Empty).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    static bool IsMels(GameItem a) => a.Id == 43 || a.SName == "RL_M202A1" || Has(a.SName, "M202A1") || Has(a.SName, "MELS") || Has(a.SName, "MELC");   // :2111
    static bool IsTrollebusina(GameItem a) => a.Id == 59 || a.SName == "RL_RPG7B02" || Has(a.SName, "RPG7B02");                                      // :2125
    static bool IsVorchun(GameItem a) => a.Id == 104 || a.SName == "GL_GrenadeLauncher03" || Has(a.SName, "GrenadeLauncher03");                       // :2139
    static bool IsWinchester(GameItem a) => a.Id == 5 || a.SName == "SG_Winchester1887" || Has(a.SName, "Winchester1887");                           // :2153
    static bool IsKaban(GameItem a) => a.Id == 106 || a.SName == "SG_Spas" || Has(a.SName, "SG_Spas");                                              // :2167
    static bool IsAdvisor(GameItem a) => a.Id == 109 || a.SName == "SG_Remington" || Has(a.SName, "Remington");                                     // :2181
    static bool IsKrik(GameItem a) => a.Id == 70 || a.SName == "SR_ArcticB01" || Has(a.SName, "ArcticB01");                                         // :2195
    static bool IsPovstanec(GameItem a) => a.Id == 80 || a.SName == "MG_AUG5_O" || Has(a.SName, "AUG5");                                            // :2209
    static bool IsBurstUsp(GameItem a) => a.Id == 18 || a.SName == "HG_USP" || a.SName == "HG_USP_O";                                               // :2223
    static bool IsSpeculant(GameItem a) => a.Id == 68 || a.SName == "HG_GlockB01_S";                                                               // :2237

    static bool UsesExactBalancePatchAmmo(GameItem a)   // :2251-2286
    {
        switch (a.Id)
        {
            case 5: case 16: case 18: case 21: case 24: case 33: case 34: case 36: case 62: case 68: case 69: case 75: case 79: case 80:
            case 103: case 104: case 107: case 108: case 109: case 112: case 114: case 115: case 191114: return true;
            default: return false;
        }
    }

    static int ResolveClipAmmo(GameItem a, WeaponType b)   // :2288-2345
    {
        int num = Math.Max(a.Int("ammo"), 1);
        if (UsesExactBalancePatchAmmo(a)) return num;
        switch (b)
        {
            case WeaponType.ONE_HANDED_COLD_ARMS: case WeaponType.TWO_HANDED_COLD_ARMS: return 1;
            case WeaponType.SNIPER_RIFLE: return num;
            case WeaponType.SHOT_GUN:
                if (IsKaban(a)) return num;
                if (IsWinchester(a) || IsAdvisor(a)) return 3;
                return 5;
            case WeaponType.HAND_GUN:
                if (IsBurstUsp(a)) return Math.Max(num, 12);
                if (IsSpeculant(a)) return num;
                if (a.Id == 108 || (a.SName ?? string.Empty) == "HG_Taurus") return Math.Max(num, 6);
                return 5;
            case WeaponType.MACHINE_GUN: return num;
            case WeaponType.ROCKET_LAUNCHER: return IsMels(a) ? Math.Max(num, 4) : 1;
            case WeaponType.GRENADE_LAUNCHER: return IsVorchun(a) ? Math.Max(num, 5) : Math.Max(num, 1);
            case WeaponType.BOMB_LAUNCHER: return Math.Max(num, 1);
            default: return num;
        }
    }

    static int ResolveTotalAmmo(Profile p, GameItem a, WeaponType b, int c)   // :2347-2412
    {
        if (b == WeaponType.ONE_HANDED_COLD_ARMS || b == WeaponType.TWO_HANDED_COLD_ARMS) return c;
        int total = a.Int("ammo_tot");
        int num;
        if (UsesExactBalancePatchAmmo(a)) num = Math.Max(total, c);
        else if (b == WeaponType.SHOT_GUN) num = IsKaban(a) ? Math.Max(total, c) : IsWinchester(a) ? 11 : !IsAdvisor(a) ? 23 : 20;
        else if (b == WeaponType.HAND_GUN) num = !IsBurstUsp(a) ? 29 : Math.Max(total, Math.Max(c, 60));
        else if (b == WeaponType.MACHINE_GUN && IsPovstanec(a)) num = 193;
        else if (b == WeaponType.SNIPER_RIFLE) num = !IsKrik(a) ? 20 : 17;
        else if (b == WeaponType.ROCKET_LAUNCHER && IsMels(a)) num = Math.Max(c, 28);
        else if (b == WeaponType.ROCKET_LAUNCHER && IsTrollebusina(a)) num = Math.Max(c, 8);
        else if (b == WeaponType.GRENADE_LAUNCHER && IsVorchun(a)) num = Math.Max(c, 19);
        else
        {
            num = Math.Max(total, c);
            if (b == WeaponType.SNIPER_RIFLE && num < 20) num = 25;
            if (b == WeaponType.ROCKET_LAUNCHER && num < 5) num = 5;
            if (b == WeaponType.GRENADE_LAUNCHER && num <= c) num = c + 12;
        }
        int b2 = Math.Max(0, num - c);
        num = c + (int)BattleStats.GetNewValue(p, WeaponAmmo, (uint)b2);   // «Барахольщик»
        return Math.Max(num, c);
    }

    static int ResolveVelocity(GameItem a, WeaponType b, int c)   // :2414-2431
    {
        if (a.Int("vel") != 0) return a.Int("vel");
        switch (b)
        {
            case WeaponType.ROCKET_LAUNCHER: return 20;
            case WeaponType.GRENADE_LAUNCHER: return 12;
            case WeaponType.BOMB_LAUNCHER: return 18;
            default: return c;
        }
    }

    static int ResolveDistance(GameItem a, WeaponType b, int c)   // :2433-2466
    {
        int rad = a.Int("rad");
        if ((b == WeaponType.ONE_HANDED_COLD_ARMS || b == WeaponType.TWO_HANDED_COLD_ARMS) && rad != 0) return rad;
        switch (b)
        {
            case WeaponType.ONE_HANDED_COLD_ARMS: return 8;
            case WeaponType.TWO_HANDED_COLD_ARMS: return 9;
        }
        if (rad != 0) return rad;
        switch (b)
        {
            case WeaponType.SNIPER_RIFLE: return 1200;
            case WeaponType.HAND_GUN: case WeaponType.MACHINE_GUN: return Math.Max(c, 700);
            case WeaponType.SHOT_GUN: return 450;
            case WeaponType.GRENADE_LAUNCHER: case WeaponType.BOMB_LAUNCHER: return Math.Max(c, 350);
            default: return c;
        }
    }

    static float ResolveAngle(GameItem a, WeaponType b, int c)   // :2468-2496 (Weapon.Angel is the uint "ang")
    {
        float num = (uint)a.Int("ang");
        switch (b)
        {
            case WeaponType.SHOT_GUN:
                if (num > 3.14f && num <= 180f) num = num * (float)Math.PI / 180f;
                if (num <= 0f || num < 0.18f || num > 0.75f) num = 0.34f;
                if (c > 0) num *= 1f + (3 * c) / 100f;
                return Mathf.Clamp(num, 0.25f, 0.45f);
            case WeaponType.ONE_HANDED_COLD_ARMS: case WeaponType.TWO_HANDED_COLD_ARMS:
                if (num <= 0f || num > 3.14f) return 2.05f;
                break;
        }
        return num;
    }

    static int ResolveDeviation(Profile p, GameItem a, WeaponType b)   // :2498-2545
    {
        int deviation = a.Int("dev");
        int accuracy = a.Int("accuracy");
        int num2;
        if (accuracy > 0) num2 = accuracy;
        else if (b == WeaponType.MACHINE_GUN) num2 = 8;
        else if (deviation > 0) num2 = deviation;
        else
            switch (b)
            {
                case WeaponType.SNIPER_RIFLE: num2 = 0; break;
                case WeaponType.SHOT_GUN: num2 = 35; break;
                case WeaponType.HAND_GUN: num2 = 6; break;
                default: num2 = 8; break;
            }
        int bonus = (int)BattleStats.GetNewValue(p, WeaponAccuracy, 0u);   // «Точность по ГОСТу»
        if (bonus > 0) num2 = Math.Max(0, num2 - bonus);
        return num2;
    }

    static int ResolveRapidity(Profile p, GameItem a, WeaponType b)   // :2547-2617
    {
        int attackSpeed = a.Int("attackspeed"), rap = a.Int("rap");
        int num3;
        if (attackSpeed > 0) num3 = attackSpeed;
        else
            switch (b)
            {
                case WeaponType.SNIPER_RIFLE: num3 = Math.Max(rap, 1200); break;
                case WeaponType.SHOT_GUN:
                    num3 = Math.Max(rap, 850);
                    if (a.Id == 109 || string.Equals(a.SName, "SG_Remington", StringComparison.OrdinalIgnoreCase))
                    {
                        num3 = Math.Max(50, Mathf.RoundToInt(num3 / 1.14f));
                        num3 = Math.Max(50, Mathf.RoundToInt(num3 / 1.1f));
                    }
                    break;
                case WeaponType.ROCKET_LAUNCHER: case WeaponType.GRENADE_LAUNCHER: case WeaponType.BOMB_LAUNCHER: num3 = Math.Max(rap, 950); break;
                case WeaponType.HAND_GUN: num3 = !IsBurstUsp(a) ? Math.Max(rap, 260) : rap <= 0 ? 180 : Math.Max(rap, 180); break;
                case WeaponType.ONE_HANDED_COLD_ARMS: case WeaponType.TWO_HANDED_COLD_ARMS: num3 = Math.Max(rap, 450); break;
                case WeaponType.FLAMER: case WeaponType.GATLING_GUN: case WeaponType.SNOW_GUN: case WeaponType.ACID_THROWER: num3 = rap <= 0 ? 90 : rap; break;
                case WeaponType.MACHINE_GUN: num3 = rap <= 0 ? 110 : rap; break;
                default: num3 = rap <= 0 ? 300 : rap; break;
            }
        int gym = Mathf.Clamp(BattleStats.GetRawValue(p, WeaponRapidity), 0, 10);   // «Шустряк»
        if (gym > 0) num3 = Math.Max(50, Mathf.RoundToInt(num3 / (1f + gym * 0.01f)));
        return num3;
    }

    static bool TryResolveExactReloadTime(GameItem a, out int b)   // :2619-2670
    {
        b = 0;
        switch (a.Id)
        {
            case 109: b = 2400; return true;
            case 18: b = 2990; return true;
            case 108: b = 2990; return true;
            case 79: case 80: case 112: case 114: case 115: case 117: b = 2340; return true;
            case 70: case 74: case 75: case 103: case 107: b = 2440; return true;
        }
        string n = a.Name ?? string.Empty;
        bool Is(params string[] names) { foreach (var x in names) if (string.Equals(n, x, StringComparison.OrdinalIgnoreCase)) return true; return false; }
        if (Is("Советник")) { b = 2400; return true; }
        if (Is("Палач")) { b = 2990; return true; }
        if (Is("Кобра", "Повстанец", "Рейдер", "Звездочет", "Звёздочет", "Фантом", "Марсель")) { b = 2340; return true; }
        if (Is("Крик", "Росомаха", "Россомаха", "Шершень", "Вымпел", "Анаконда")) { b = 2440; return true; }
        return false;
    }

    static int ResolveReloadTime(GameItem a, WeaponType b)   // :2672-2722
    {
        int balance = a.Int("reload");
        if (balance > 0) return Math.Max(100, balance);
        if (IsTrollebusina(a))
        {
            int n2 = Math.Max(a.Int("rt"), 1600);
            int n3 = Math.Max(1, (n2 * 100 + 41) / 83);
            return Math.Max(1, (n3 * 125 + 50) / 100);
        }
        if (TryResolveExactReloadTime(a, out int exact)) return exact;
        int rt = a.Int("rt");
        switch (b)
        {
            case WeaponType.SNIPER_RIFLE: return Math.Max(rt, 1300);
            case WeaponType.SHOT_GUN: return Math.Max(rt, 1500);
            case WeaponType.ROCKET_LAUNCHER: return Math.Max(1, (Math.Max(rt, 1600) * 83 + 50) / 100);
            case WeaponType.GRENADE_LAUNCHER: case WeaponType.BOMB_LAUNCHER: return Math.Max(rt, 1600);
            case WeaponType.HAND_GUN: return IsBurstUsp(a) ? Math.Max(rt, 800) : Math.Max(rt, 600);
            case WeaponType.MACHINE_GUN: case WeaponType.GATLING_GUN: return Math.Max(rt, 1500);
            default: return Math.Max(rt, 700);
        }
    }
}
