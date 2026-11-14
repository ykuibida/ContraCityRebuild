// The local fighter's battle characteristics, summed like the New Era client (PlayerManager.GetLocalBattleMaxHealth / MaxEnergy,
// ApplyLocalWearBattleBonus, GetLocalWearStatTotal, SoldierController speed, CharacterMotor fall damage):
//   • clothes: every equipped item's fields are summed; a COMPLETE set (all ids of the item's set_items equipped) adds its
//     set_bonus once per set (set key = set_id, else the sorted set_items);
//   • percents apply to the base value: v = base + flat + Convert.ToInt32(base * pct / 100) (round half to even);
//   • training hall: the bought level's value from the skill text (ability_desc_<id>_<level>: «+20 к максимуму здоровья»),
//     skills matched by name to the original ability ids (our gym list is in display order);
//   • boosters: active while bought time is left (e_id 3 «Легкое Приземление» −50% fall damage).
// Damage dealt / defence against other players' weapons is summed too (Defence, DamageBonus, …) for later — no weapons yet.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public class BattleStats
{
    public int BaseHealth = 100, MaxHealth = 100, MaxArmor;          // HP and armor at respawn
    public float SpeedMultiplier = 1f;                                // Бегун × speed_percent (clothes + sets)
    public int DamageTakenReduction;                                  // % — Железный Феликс + all_def (BattleDamageDefensePercent, clamp 0..95)
    public float FallDamageMultiplier = 1f;                           // «Легкое Приземление»: ×0.5
    public float SelfProjectileMultiplier = 1f;                       // «Стальные Подошвы»: ×0.5 (own rockets / grenades)
    public float ExpMultiplier = 1f;                                  // «Личный Ускоритель Опыта»
    // weapons (for later): training hall + clothes + boosters
    public int FireRatePercent, CritChance, MinDamage, MaxDamage, Accuracy, HeadDamagePercent, AmmoPercent, CritDamagePercent;
    public readonly Dictionary<string, int> Defence = new Dictionary<string, int>();      // auto_def, sniper_def, … (% vs that weapon)
    public readonly Dictionary<string, int> DamageBonus = new Dictionary<string, int>();  // *_damage_bonus, booster weapon damage
    public readonly Dictionary<string, int> ElementResist = new Dictionary<string, int>();// fire / bleed / poison / cold (%)
    public readonly List<string> CompleteSets = new List<string>();
    public readonly List<string> Sources = new List<string>();

    // ---------------- training hall ----------------
    static Dictionary<string, int> abilityIdByName;
    /// <summary>Original ability id (ability_name_&lt;id&gt; in the language file) of a gym skill name.</summary>
    static int AbilityId(string name)
    {
        if (abilityIdByName == null)
        {
            abilityIdByName = new Dictionary<string, int>();
            for (int i = 1; i <= 30; i++) { var n = LanguageManager.GetText("ability_name_" + i); if (n != "ability_name_" + i) abilityIdByName[n] = i; }
        }
        return abilityIdByName.TryGetValue(name ?? "", out var id) ? id : 0;
    }

    /// <summary>Value of a bought gym level, read from its text (cumulative per level, like the server rows).</summary>
    public static int GymValue(string skillName, int level)
    {
        if (level <= 0) return 0;
        int id = AbilityId(skillName);
        string text = id > 0 ? LanguageManager.GetText($"ability_desc_{id}_{level}") : "";
        var m = Regex.Match(text, @"\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    /// <summary>The bought value of the gym skill with the original ability id (嗲: 5 WeaponRapidity, 7 WeaponAmmo, 10 WeaponAccuracy …);
    /// <paramref name="percent"/> = the server row's EstimateType Percent (the skill text has "%").</summary>
    public static int GymAbility(Profile p, int abilityId, out bool percent)
    {
        percent = false;
        string name = LanguageManager.GetText("ability_name_" + abilityId);
        var g = ItemDB.Gym.FirstOrDefault(x => x.Name == name);
        int level = g != null && p.GymLevels.TryGetValue(g.Id, out var l) ? l : 0;
        if (level <= 0) return 0;
        percent = LanguageManager.GetText($"ability_desc_{abilityId}_{level}").Contains("%");
        return GymValue(name, level);
    }

    /// <summary>嚱.GetRawValue (NE 嚱.cs:251-263): the sum of the skill's values.</summary>
    public static int GetRawValue(Profile p, int abilityId) => GymAbility(p, abilityId, out _);

    /// <summary>嚱.GetNewValue (NE 嚱.cs:265-284): b + points, + Convert.ToUInt32(b × value %) for a percent row.</summary>
    public static uint GetNewValue(Profile p, int abilityId, uint b)
    {
        int v = GymAbility(p, abilityId, out bool percent);
        if (v == 0) return b;
        uint num = percent ? Convert.ToUInt32((float)(double)b * (Convert.ToSingle(v) * 0.01f)) : (uint)v;
        return b + num;
    }

    // ---------------- computation ----------------
    public static BattleStats Compute(Profile p)
    {
        var s = new BattleStats();
        // equipped clothes in slot order (Hats … Heads), duplicates dropped
        var wears = new List<GameItem>();
        for (int wt = 1; wt <= 9; wt++) { var it = p.WearInSlot(wt); if (it != null && !wears.Contains(it)) wears.Add(it); }
        var ids = new HashSet<int>(wears.Select(w => w.Id));

        var sum = new Dictionary<string, double>();
        void Add(string k, double v) { if (v != 0) sum[k] = (sum.TryGetValue(k, out var o) ? o : 0) + v; }
        foreach (var w in wears)
        {
            foreach (var kv in w.Raw) if (IsStatKey(kv.Key) && TryNum(kv.Value, out var v)) Add(kv.Key, v);
        }
        // complete sets: the set_bonus once per set key, from the first complete item in slot order
        var seen = new HashSet<string>();
        foreach (var w in wears)
        {
            var items = w.SetItems;
            if (items.Count == 0) continue;
            string key = w.Str("set_id"); if (key == "") key = string.Join(",", items.OrderBy(x => x));
            if (seen.Contains(key) || !items.All(ids.Contains)) continue;
            seen.Add(key);
            s.CompleteSets.Add(w.Str("set_name_ru") != "" ? w.Str("set_name_ru") : key);
            if (w.Raw.TryGetValue("set_bonus", out var sb) && sb is Dictionary<string, object> bonus)
                foreach (var kv in bonus) if (TryNum(kv.Value, out var v)) Add(kv.Key, v);
        }
        int Sum(string k) => sum.TryGetValue(k, out var v) ? (int)Math.Round(v) : 0;

        // training hall (by skill name)
        int Gym(string name) { var g = ItemDB.Gym.FirstOrDefault(x => x.Name == name); return g == null ? 0 : GymValue(name, p.GymLevels.TryGetValue(g.Id, out var l) ? l : 0); }
        int gymHp = Gym("Здоровяк"), gymArmor = Gym("Шмотник"), gymSpeed = Mathf.Clamp(Gym("Бегун"), 0, 10);

        // max health: trainHP = clamp(100 + Здоровяк, 1, 150); + flat wear health + base × (health_percent | cheal_percent)%
        s.BaseHealth = Mathf.Clamp(100 + gymHp, 1, 150);
        int hpPct = Sum("health_percent") != 0 ? Sum("health_percent") : Sum("cheal_percent");
        s.MaxHealth = Mathf.Max(1, s.BaseHealth + Sum("cheal") + Pct(s.BaseHealth, hpPct));
        // armor at respawn: Шмотник + flat wear armor (cdef / armor / energy), clamp 0..100
        int armorBase = gymArmor;
        s.MaxArmor = Mathf.Clamp(armorBase + Sum("cdef") + Sum("armor") + Sum("energy"), 0, 100);
        // speed: (1 + Бегун%) × (1 + speed_percent%)
        s.SpeedMultiplier = (1f + gymSpeed * 0.01f) * Mathf.Max(0.01f, 1f + Sum("speed_percent") * 0.01f);
        // incoming damage: Железный Феликс + general defence of clothes
        s.DamageTakenReduction = Mathf.Clamp(Gym("Железный Феликс") + Sum("all_def") + Sum("cdecdam"), 0, 95);
        // weapon side
        s.FireRatePercent = Mathf.Clamp(Gym("Шустряк"), 0, 10);
        s.CritChance = Mathf.Clamp(Gym("Доктор Гаус"), 0, 25);
        s.AmmoPercent = Mathf.Clamp(Gym("Барахольщик"), 0, 60) + Sum("ammo");
        s.MinDamage = Gym("Немаленький"); s.MaxDamage = Gym("Максималист"); s.Accuracy = Gym("Точность по ГОСТу");
        s.HeadDamagePercent = Gym("Охотник за головами");
        foreach (var kv in sum)
        {
            if (kv.Key.EndsWith("_def") && kv.Key != "all_def") s.Defence[kv.Key] = (int)Math.Round(kv.Value);
            else if (kv.Key.EndsWith("_damage_bonus")) s.DamageBonus[kv.Key] = (int)Math.Round(kv.Value);
        }

        // boosters (enhancers) that are active
        foreach (var b in ItemDB.Boosters.Where(p.Owns))
        {
            switch (b.Id)
            {
                case 2: s.ExpMultiplier *= 1.5f; break;
                case 3: s.FallDamageMultiplier *= 0.5f; break;
                case 5: s.SelfProjectileMultiplier *= 0.5f; break;
                case 30: s.ElementResist["fire"] = 50; break;
                case 31: s.ElementResist["bleed"] = 50; break;
                case 32: s.ElementResist["poison"] = 50; break;
                case 34: s.ElementResist["cold"] = 50; break;
                case 157: s.CritDamagePercent += 10; break;
                case 158: s.HeadDamagePercent += 5; break;
                case 200: AddD(s, "auto_long_damage_bonus", 3); break;
                case 201: AddD(s, "sniper_long_damage_bonus", 4); break;
                case 202: AddD(s, "sniper_middle_damage_bonus", 4); break;
                case 203: AddD(s, "auto_middle_damage_bonus", 3); break;
                case 204: AddD(s, "grenade_damage_bonus", 7); break;
                case 206: AddD(s, "machinegun_damage_bonus", 7); break;
                case 207: AddD(s, "rocket_damage_bonus", 10); break;
                case 210: AddD(s, "sniper_damage_bonus", 4); break;
                // 4 Беспартийность (sign over the head), 10/13/150/151/156/205/208/209 — clan boosters: not in the game
            }
            s.Sources.Add("booster " + b.Name);
        }
        return s;
    }

    static void AddD(BattleStats s, string k, int v) => s.DamageBonus[k] = (s.DamageBonus.TryGetValue(k, out var o) ? o : 0) + v;

    /// <summary>Convert.ToInt32((float)b * (pct * 0.01f)) — banker's rounding like the original.</summary>
    static int Pct(int b, int pct) => pct == 0 ? 0 : Convert.ToInt32((float)b * (pct * 0.01f));

    static readonly HashSet<string> NotStats = new HashSet<string> { "w_id", "wt", "ws", "nlvl", "sc_id", "it", "iS", "assemblage_id", "shop_public", "price_tPv", "set_id" };
    static bool IsStatKey(string k) => !NotStats.Contains(k) && (k.EndsWith("_def") || k.EndsWith("_damage_bonus") || k.EndsWith("_percent") || k.EndsWith("_jump_bonus") || k == "cdef" || k == "cheal" || k == "armor" || k == "energy" || k == "ammo" || k == "cdecdam" || k == "fire_def_penalty");
    static bool TryNum(object o, out double v)
    {
        v = 0;
        if (o == null || o is Dictionary<string, object> || o is List<object> || o is bool) return false;
        return double.TryParse(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture).Replace("%", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"HP {MaxHealth} (base {BaseHealth}), armor {MaxArmor}, speed ×{SpeedMultiplier:F3}, damage taken −{DamageTakenReduction}%, fall ×{FallDamageMultiplier:F2}");
        if (CompleteSets.Count > 0) sb.Append(", sets: " + string.Join(", ", CompleteSets));
        if (Defence.Count > 0) sb.Append(", defence: " + string.Join(" ", Defence.Select(kv => kv.Key + "=" + kv.Value)));
        if (DamageBonus.Count > 0) sb.Append(", damage: " + string.Join(" ", DamageBonus.Select(kv => kv.Key + "=" + kv.Value)));
        if (Sources.Count > 0) sb.Append(", " + string.Join(", ", Sources));
        return sb.ToString();
    }
}
