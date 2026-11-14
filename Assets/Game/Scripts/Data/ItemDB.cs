// Item database built from New Era server answers (Resources/data/ne_items.json): weapons (+upgrades, defaults),
// wear, sets, boosters (enhancers), taunts, gym skills, maps. Field names are the server's own.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public enum ItemKind { Weapon, Wear, Booster, Taunt }

public class GameItem
{
    public ItemKind Kind;
    public int Id;
    public string Name, SName;
    public int Wt, Ws;
    public int Level;
    public int Price;          // tPv (permanent price)
    public int Price1, Price7, Price30; // rent prices (boosters, taunts)
    public bool IsDefault;
    public Dictionary<string, object> Raw;

    public string Str(string k) => Raw != null && Raw.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
    public int Int(string k) => Raw != null && Raw.TryGetValue(k, out var v) && v != null ? (int)Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
    public float Float(string k) => Raw != null && Raw.TryGetValue(k, out var v) && v != null ? (float)Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0f;
    public bool Has(string k) => Raw != null && Raw.ContainsKey(k);

    public string Description => Pick("desc_ru", "desc", "localized_description", "description");
    public string Bonuses => Pick("desca_ru", "desca", "localized_description_extra", "description_extra");
    public string UpgradeText => Pick("descupgrade_ru", "descupgrade", "localized_upgrade_description", "upgrade_description");
    string Pick(params string[] keys) { foreach (var k in keys) { var s = Str(k); if (s != "") return s; } return ""; }

    public string SlotName => Kind == ItemKind.Wear ? ItemDB.WearSlotName(Wt) : Kind == ItemKind.Weapon ? ItemDB.WeaponClassName(Wt) :
        Kind == ItemKind.Booster ? "Усилитель" : "Насмешка";

    public string IconPath
    {
        get
        {
            switch (Kind)
            {
                case ItemKind.Weapon: return "gui/icons/items/weapon/" + SName;
                case ItemKind.Wear: return "gui/icons/items/" + ItemDB.WearIconFolder(Wt) + "/" + SName;
                case ItemKind.Booster: return "gui/icons/enhancer/" + SName;
                default: return "gui/icons/taunt/" + Id;   // New Era: icons/taunt/<t_id>.png
            }
        }
    }

    Texture2D icon; bool iconTried;
    public Texture2D Icon
    {
        get
        {
            if (!iconTried) { iconTried = true; icon = Resources.Load<Texture2D>(IconPath); }
            return icon;
        }
    }

    // Wear set info (server fields set_id, set_name, set_items, set_bonus)
    public string SetId => Str("set_id");
    public string SetName => Pick("set_name_ru", "set_name");
    public List<int> SetItems => Raw != null && Raw.TryGetValue("set_items", out var v) && v is List<object> l ? l.Select(x => (int)Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToList() : new List<int>();
    public string SetBonus => Str("set_bonus");
}

public class GymSkill
{
    public int Id; public string Name, Step; public int Price1; public int Steps = 5;
    public int IconId;   // New Era ability icon number = ability id (ability_1 = Шмотник, ability_2 = Здоровяк)
    public Texture2D Icon => Resources.Load<Texture2D>("gui/icons/ability/ability_" + (IconId > 0 ? IconId : Id));
}

public static class ItemDB
{
    public static readonly List<GameItem> Weapons = new List<GameItem>();
    public static readonly List<GameItem> WeaponUpgrades = new List<GameItem>();
    public static readonly List<GameItem> DefaultWeapons = new List<GameItem>();
    public static readonly List<GameItem> Wears = new List<GameItem>();
    public static readonly List<GameItem> Boosters = new List<GameItem>();
    public static readonly List<GameItem> Taunts = new List<GameItem>();
    public static readonly List<GymSkill> Gym = new List<GymSkill>();
    public static readonly List<Dictionary<string, object>> Sets = new List<Dictionary<string, object>>();
    public static readonly List<(string id, string name)> Maps = new List<(string, string)>();
    static readonly Dictionary<(ItemKind, int), GameItem> byId = new Dictionary<(ItemKind, int), GameItem>();
    static bool loaded;

    public static readonly string[] WeaponTabs = { "Ближний бой", "Пистолеты", "Автоматы", "Пулемёты", "Дробовики", "Тяжёлое", "Снайперские" };
    public static readonly string[] WearSlots = { "", "Голова", "Лицо", "Кисти рук", "Торс", "Ноги", "Ступни", "Спина", "Другое", "Головы" };
    static readonly string[] WearFolders = { "", "hats", "masks", "gloves", "shirts", "pants", "boots", "backpacks", "others", "heads" };
    static readonly string[] WeaponClasses = { "", "Оружие ближнего боя", "Оружие ближнего боя", "Пистолет", "Автомат", "Огнемет", "Пулемет", "Дробовик", "Ракетница", "Гранатомет", "Снайперская винтовка", "", "", "", "", "Липучкомёт" };   // 15: rebuild_items.json

    public static string WearSlotName(int wt) => wt > 0 && wt < WearSlots.Length ? WearSlots[wt] : "";
    public static string WearIconFolder(int wt) => wt > 0 && wt < WearFolders.Length ? WearFolders[wt] : "others";
    public static string WeaponClassName(int wt) => wt > 0 && wt < WeaponClasses.Length ? WeaponClasses[wt] : "";

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        var ta = Resources.Load<TextAsset>("data/ne_items");
        var root = (Dictionary<string, object>)MiniJson.Parse(ta.text);
        Fill(root, "weapons", ItemKind.Weapon, Weapons, true);
        Fill(root, "weaponUpgrades", ItemKind.Weapon, WeaponUpgrades, false);
        Fill(root, "defaultWeapons", ItemKind.Weapon, DefaultWeapons, true);
        foreach (var w in DefaultWeapons) w.IsDefault = true;
        Fill(root, "wears", ItemKind.Wear, Wears, true);
        Fill(root, "boosters", ItemKind.Booster, Boosters, true);
        Fill(root, "taunts", ItemKind.Taunt, Taunts, true);
        foreach (Dictionary<string, object> s in (List<object>)root["sets"]) Sets.Add(s);
        foreach (Dictionary<string, object> g in (List<object>)root["gym"])
        {
            var gs = new GymSkill { Id = I(g["id"]), Name = (string)g["name"], Step = (string)g["step"], Price1 = I(g["price1"]) };
            // New Era ability ids: 1 = Шмотник, 2 = Здоровяк (ability_name_N); the captured list is in display order, where
            // 嚱.InitShowedList swaps ids 2 and 1 so Здоровяк is shown first — keep that order, use the real ids
            if (gs.Name == "Здоровяк") gs.Id = 2; else if (gs.Name == "Шмотник") gs.Id = 1;
            gs.IconId = gs.Id;
            Gym.Add(gs);
        }
        foreach (Dictionary<string, object> m in (List<object>)root["maps"]) Maps.Add(((string)m["id"], (string)m["name"]));
        LoadRebuildWeapons();
    }

    /// <summary>Rebuild weapons that are not in the captured New Era data (Resources/data/rebuild_items.json, owner's decision
    /// 12.11.2026; numbers and sources in Docs/weapons/balance_proposal.md). Each record has its own "enabled" flag, and
    /// <see cref="RebuildWeaponsEnabled"/> switches the whole group off (the future switch for bots and the like).</summary>
    public static bool RebuildWeaponsEnabled = true;
    public static readonly List<GameItem> RebuildWeapons = new List<GameItem>();
    static void LoadRebuildWeapons()
    {
        var ta = Resources.Load<TextAsset>("data/rebuild_items");
        if (ta == null || !RebuildWeaponsEnabled) return;
        var root = (Dictionary<string, object>)MiniJson.Parse(ta.text);
        if (!root.TryGetValue("weapons", out var arr) || !(arr is List<object> l)) return;
        var on = l.Where(o => o is Dictionary<string, object> d && !(d.TryGetValue("enabled", out var e) && e is bool b && !b)).ToList();
        Fill(new Dictionary<string, object> { ["weapons"] = on }, "weapons", ItemKind.Weapon, RebuildWeapons, true);
        Weapons.AddRange(RebuildWeapons);
    }

    static int I(object o) => o == null ? 0 : (int)Convert.ToDouble(o, CultureInfo.InvariantCulture);

    static void Fill(Dictionary<string, object> root, string key, ItemKind kind, List<GameItem> list, bool index)
    {
        if (!root.TryGetValue(key, out var arr) || !(arr is List<object> l)) return;
        foreach (Dictionary<string, object> o in l)
        {
            var it = new GameItem { Kind = kind, Raw = o };
            it.Id = kind == ItemKind.Booster ? it.Int("e_id") : kind == ItemKind.Taunt ? it.Int("t_id") : it.Int("w_id");
            it.Name = it.Str("name_ru"); if (it.Name == "") it.Name = it.Str("name"); if (it.Name == "") it.Name = it.Str("localized_name");
            it.SName = it.Str("sname");
            it.Wt = it.Int("wt"); it.Ws = it.Int("ws"); it.Level = it.Int("nlvl");
            if (o.TryGetValue("sc", out var sc) && sc is Dictionary<string, object> s)
            {
                it.Price = s.TryGetValue("tPv", out var p) ? I(p) : 0;
                it.Price1 = s.TryGetValue("t1v", out var p1) ? I(p1) : 0;
                it.Price7 = s.TryGetValue("t7v", out var p7) ? I(p7) : 0;
                it.Price30 = s.TryGetValue("t30v", out var p30) ? I(p30) : 0;
            }
            if (it.Price == 0 && o.ContainsKey("price_tPv")) it.Price = it.Int("price_tPv");
            if (kind == ItemKind.Weapon && it.Name == "")
            {
                var named = Weapons.FirstOrDefault(x => x.SName == it.SName);
                if (named != null) it.Name = named.Name;
            }
            list.Add(it);
            if (index && !byId.ContainsKey((kind, it.Id))) byId[(kind, it.Id)] = it;
        }
    }

    public static GameItem Get(ItemKind k, int id) { Load(); return byId.TryGetValue((k, id), out var it) ? it : null; }

    /// <summary>Shop order like New Era: weapons by tab (ws), wear by slot (wt), in server order.</summary>
    public static IEnumerable<GameItem> ShopWeapons(int ws) { Load(); return Weapons.Where(w => w.Ws == ws); }
    public static IEnumerable<GameItem> ShopWears(int wt) { Load(); return Wears.Where(w => w.Wt == wt); }
}
