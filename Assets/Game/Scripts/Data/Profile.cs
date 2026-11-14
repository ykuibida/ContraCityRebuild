// Local player profile (replaces the server account): money, level/exp, owned items, equipped wear and weapons,
// rented boosters/taunts, training hall levels. Saved as JSON in Application.persistentDataPath/profile.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public class Profile
{
    public string Name = "Боец";
    public int Money = 15010;          // a fresh New Era account starts with 15010
    public int Level = 1;
    public int Exp = 0;
    /// <summary>Exp needed for the next level (server minExp/maxExp). Known: level 1 = 100 (New Era TAB «0/100»); other levels are not captured yet.</summary>
    public int MaxExp => 100 * Level;
    public HashSet<int> OwnedWeapons = new HashSet<int>();
    public HashSet<int> OwnedWears = new HashSet<int>();
    public Dictionary<int, int> EquippedWear = new Dictionary<int, int>();     // wt (slot 1..9) -> w_id
    public Dictionary<int, int> EquippedWeapon = new Dictionary<int, int>();   // ws (1..7) -> w_id (0 = default weapon)
    public Dictionary<int, long> BoosterUntil = new Dictionary<int, long>();   // e_id -> unix seconds
    public Dictionary<int, long> TauntUntil = new Dictionary<int, long>();     // t_id -> unix seconds
    public int[] TauntSlots = new int[3];
    public Dictionary<int, int> GymLevels = new Dictionary<int, int>();        // skill id -> 0..5
    /// <summary>Dossier statistics (New Era user stats 啎, kept locally): flat counters — "kill", "death", "suicide", "head", "nuts",
    /// "death_head", "death_nuts", "win", "lose", "shot", "hit", "dom", "dom_max", "rev", "rev_max", "time" (seconds),
    /// "map.&lt;sys&gt;.win|lose|time", "mode.&lt;n&gt;.win|lose|time", "weapon.&lt;id&gt;.kill|head|nuts|shot|hit", "ach.&lt;sub id&gt;" (achievement progress).</summary>
    public Dictionary<string, double> Stats = new Dictionary<string, double>();
    public double Stat(string k) => Stats.TryGetValue(k, out var v) ? v : 0;
    public void AddStat(string k, double v) { Stats[k] = Stat(k) + v; }
    public void MaxStat(string k, double v) { if (v > Stat(k)) Stats[k] = v; }

    static Profile current;
    public static Profile Current { get { if (current == null) current = Load(); return current; } }
    /// <summary>The test runs (-uitest / -uicompare / -tooltips) use their own profile so they never touch the player's progress.</summary>
    static bool TestRun => Environment.GetCommandLineArgs().Any(x => x == "-uitest" || x == "-uicompare" || x == "-tooltips");
    static string FilePath => Path.Combine(Application.persistentDataPath, TestRun ? "profile_test.json" : "profile.json");

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public bool Owns(GameItem it)
    {
        switch (it.Kind)
        {
            case ItemKind.Weapon: return it.IsDefault || OwnedWeapons.Contains(it.Id);
            case ItemKind.Wear: return OwnedWears.Contains(it.Id);
            case ItemKind.Booster: return BoosterUntil.TryGetValue(it.Id, out var t) && t > Now;
            default: return TauntUntil.TryGetValue(it.Id, out var u) && u > Now;
        }
    }

    public bool IsEquipped(GameItem it)
    {
        if (it.Kind == ItemKind.Wear) return EquippedWear.TryGetValue(it.Wt, out var id) && id == it.Id;
        if (it.Kind == ItemKind.Weapon) return EquippedWeapon.TryGetValue(it.Ws, out var w) ? w == it.Id : it.IsDefault;
        if (it.Kind == ItemKind.Taunt) return Array.IndexOf(TauntSlots, it.Id) >= 0;
        return Owns(it);
    }

    public enum BuyResult { Ok, AlreadyOwned, LowLevel, NoMoney }

    /// <summary>Buy permanently (weapons, wear) or rent for days (boosters, taunts: 1, 7 or 30).</summary>
    public BuyResult Buy(GameItem it, int days = 0)
    {
        if (it.Level > Level) return BuyResult.LowLevel;
        int price = days == 1 ? it.Price1 : days == 7 ? it.Price7 : days == 30 ? it.Price30 : it.Price;
        if ((it.Kind == ItemKind.Weapon || it.Kind == ItemKind.Wear) && Owns(it)) return BuyResult.AlreadyOwned;
        if (price > Money) return BuyResult.NoMoney;
        Money -= price;
        switch (it.Kind)
        {
            // ShopManager.OnBuyWeapon → 嗎.WeaponSlot.Set(weapon); OnBuyWear → 嗎.View.DressUp(wear) (saved like an HQ equip) — Penta 2016 and New Era
            case ItemKind.Weapon: OwnedWeapons.Add(it.Id); Equip(it); break;
            case ItemKind.Wear: OwnedWears.Add(it.Id); Equip(it); break;
            // ShopManager.OnBuyEnhancer / OnBuyTaunt: 86460 s per day (×7 week, ×30 month) added to the time left, permanent = no limit;
            // a bought taunt is NOT put into a slot (that is done in the HQ)
            case ItemKind.Booster: BoosterUntil[it.Id] = Extend(BoosterUntil, it.Id, days); break;
            case ItemKind.Taunt: TauntUntil[it.Id] = Extend(TauntUntil, it.Id, days); break;
        }
        Save();
        return BuyResult.Ok;
    }

    /// <summary>"Permanent" rent (year 3000): a plain number so the JSON (double) round-trip stays exact.</summary>
    public const long Permanent = 32503680000L;

    static long Extend(Dictionary<int, long> until, int id, int days)
    {
        if (days <= 0) return Permanent;
        long cur = until.TryGetValue(id, out var u) ? u : 0;
        if (cur >= Permanent) return cur;
        return Math.Max(Now, cur) + days * 86460L;
    }

        public void Equip(GameItem it)
    {
        if (!Owns(it)) return;
        if (it.Kind == ItemKind.Wear) EquippedWear[it.Wt] = it.Id;
        else if (it.Kind == ItemKind.Weapon) EquippedWeapon[it.Ws] = it.IsDefault ? 0 : it.Id;
        Save();
    }

    public void Unequip(GameItem it)
    {
        if (it.Kind == ItemKind.Wear && IsEquipped(it)) EquippedWear.Remove(it.Wt);
        else if (it.Kind == ItemKind.Weapon) EquippedWeapon.Remove(it.Ws);
        Save();
    }

    public GameItem WeaponInSlot(int ws)
    {
        if (EquippedWeapon.TryGetValue(ws, out var id) && id != 0) { var w = ItemDB.Get(ItemKind.Weapon, id); if (w != null) return w; }
        return ItemDB.DefaultWeapons.FirstOrDefault(d => d.Ws == ws);
    }

    public GameItem WearInSlot(int wt) => EquippedWear.TryGetValue(wt, out var id) ? ItemDB.Get(ItemKind.Wear, id) : null;

    // ---------- persistence ----------
    public void Save()
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = Name, ["money"] = Money, ["level"] = Level, ["exp"] = Exp,
            ["ownedWeapons"] = OwnedWeapons.ToList(), ["ownedWears"] = OwnedWears.ToList(),
            ["equippedWear"] = EquippedWear.ToDictionary(k => k.Key.ToString(), k => (object)k.Value),
            ["equippedWeapon"] = EquippedWeapon.ToDictionary(k => k.Key.ToString(), k => (object)k.Value),
            ["boosterUntil"] = BoosterUntil.ToDictionary(k => k.Key.ToString(), k => (object)k.Value),
            ["tauntUntil"] = TauntUntil.ToDictionary(k => k.Key.ToString(), k => (object)k.Value),
            ["tauntSlots"] = TauntSlots.ToList(),
            ["gym"] = GymLevels.ToDictionary(k => k.Key.ToString(), k => (object)k.Value),
            ["gymIds"] = "ne",
            ["stats"] = Stats.ToDictionary(k => k.Key, k => (object)k.Value),
        };
        try { File.WriteAllText(FilePath, MiniJson.Serialize(d)); } catch (Exception e) { Debug.LogWarning("[Profile] save failed: " + e.Message); }
    }

    static Profile Load()
    {
        ItemDB.Load();
        var p = new Profile();
        try
        {
            if (!File.Exists(FilePath)) { p.Save(); return p; }
            var d = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(FilePath));
            int I(object o) => (int)Convert.ToDouble(o, CultureInfo.InvariantCulture);
            p.Name = (string)d["name"]; p.Money = I(d["money"]); p.Level = I(d["level"]); p.Exp = I(d["exp"]);
            foreach (var x in (List<object>)d["ownedWeapons"]) p.OwnedWeapons.Add(I(x));
            foreach (var x in (List<object>)d["ownedWears"]) p.OwnedWears.Add(I(x));
            foreach (var kv in (Dictionary<string, object>)d["equippedWear"]) p.EquippedWear[int.Parse(kv.Key)] = I(kv.Value);
            foreach (var kv in (Dictionary<string, object>)d["equippedWeapon"]) p.EquippedWeapon[int.Parse(kv.Key)] = I(kv.Value);
            foreach (var kv in (Dictionary<string, object>)d["boosterUntil"]) p.BoosterUntil[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
            foreach (var kv in (Dictionary<string, object>)d["tauntUntil"]) p.TauntUntil[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
            var ts = (List<object>)d["tauntSlots"]; for (int i = 0; i < Math.Min(3, ts.Count); i++) p.TauntSlots[i] = I(ts[i]);
            foreach (var kv in (Dictionary<string, object>)d["gym"]) p.GymLevels[int.Parse(kv.Key)] = I(kv.Value);
            if (!d.ContainsKey("gymIds"))   // older saves numbered the gym in display order (1 Здоровяк, 2 Шмотник): swap to the New Era ids
            {
                p.GymLevels.TryGetValue(1, out var a1); p.GymLevels.TryGetValue(2, out var a2);
                p.GymLevels.Remove(1); p.GymLevels.Remove(2);
                if (a1 > 0) p.GymLevels[2] = a1; if (a2 > 0) p.GymLevels[1] = a2;
            }
            if (d.TryGetValue("stats", out var so) && so is Dictionary<string, object> sd) foreach (var kv in sd) p.Stats[kv.Key] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) { Debug.LogWarning("[Profile] load failed, new profile: " + e.Message); p = new Profile(); }
        return p;
    }

    public static void ResetProfile() { current = new Profile(); current.Save(); }
}
