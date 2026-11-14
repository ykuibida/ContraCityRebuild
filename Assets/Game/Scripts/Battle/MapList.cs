// Map list = New Era 嗉 (OnLoadMaps + NormalizeReleaseMapModes) and Map (names / descriptions / icon path).
// The server map list (MAP_URL: n, p, m, dp) was not captured for New Era, so the base data is reconstructed:
//  - order = the New Era «Создание битвы» map dropdown (screenshot ne_ui2/067);
//  - players "4,6,8,10,12,14", default index 3 (= 10) as shown for «Ангар, задний двор»; zombie maps use GetZombiePlayersList;
//  - base modes: DM+TDM+CTF+CP for the maps seen in the CTF / CP tabs (ne_ui2/073, 074), DM+TDM for the rest
//    (Angar, Faccility, TF3_Arena_map, Zombi, Zombi_2 — the CTF/CP pages past the first row were not captured: verify).
// Then New Era's own rules: no DM on 7000 / ArenaRing / LegoTurnament / Sniper(_night); no CTF/CP on Lego / ShipConteyner;
// Sniper maps always CTF+CP; zombie maps get ZM+DM; InfernoZombie = 67 (DM+TDM+ZM).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapInfo
{
    public string SystemName, Name, Desc;
    public int Modes;
    public string[] AvailPlayers;
    public int DefaultPlayerIndex;
    Texture2D ico;
    public Texture2D Ico => ico != null ? ico : ico = Resources.Load<Texture2D>("gui/icons/maps/" + (SystemName == "InfernoZombie" ? "Inferno" : SystemName));
    public bool Has(GameMode m) => ((uint)Modes & (uint)m) == (uint)m && m != GameMode.NONE;
}

public static class MapList
{
    static List<MapInfo> maps;

    static readonly (string sys, string name, string desc)[] Friendly =
    {
        ("Arena_3lvl", "Ангар, задний двор", "Открытая многоуровневая версия ангара для быстрых перестрелок и командной игры."),
        ("Angar", "Ангар", "Командный бой на складе с контейнерами."),
        ("7000", "Хоздвор", "Хозяйственный двор для быстрых командных боёв."),
        ("Arena_well", "Перестройполис", "Городская карта для ближнего и среднего боя."),
        ("ArenaRing", "Форпост", "Укреплённый форпост для командных боёв."),
        ("Bit_map", "Комиссариат", "Тактическая карта с коридорами и внутренними помещениями."),
        ("Cache", "Промзона", "Промышленная зона для динамичных перестрелок."),
        ("Inferno", "Урбан", "Городская карта для ближнего и среднего боя."),
        ("LegoTurnament", "Рубеж", "Арена с открытыми направлениями для быстрых боёв."),
        ("ShipConteyner", "Монетный двор", "Контейнерная карта для ближнего и среднего боя."),
        ("Sniper", "Враг у ворот", "Карта для дальних дистанций и снайперов."),
        ("Sniper_night", "Ночь у ворот", "Ночная версия снайперской карты."),
        ("Zombi", "Зона-Z", "Засекреченная зона для режима заражения."),
        ("Zombi_2", "Индастриал", "Индустриальная карта для режима заражения."),
        ("Faccility", "Предприятие", "Бой на промышленном объекте."),
        ("TF3_Arena_map", "Складчина", "Карта для ближнего и среднего боя."),
        ("InfernoZombie", "Урбан Карантин", "Городская карантинная зона только для режима заражения."),
    };

    static readonly HashSet<string> BaseCtfCp = new HashSet<string> { "Arena_3lvl", "7000", "Arena_well", "ArenaRing", "Bit_map", "Cache", "Inferno", "Sniper", "Sniper_night" };

    static bool IsZombieOnly(string a) => a == "InfernoZombie";
    static bool IsZombieMap(string a) => a == "Zombi" || a == "Zombi_2" || IsZombieOnly(a);
    static bool IsDeathmatchBlocked(string a) => new[] { "7000", "ArenaRing", "LegoTurnament", "Sniper", "Sniper_night" }.Contains(a);

    /// <summary>嗉.NormalizeReleaseMapModes.</summary>
    static int Normalize(string a, int b)
    {
        if (IsDeathmatchBlocked(a)) b &= ~1;
        if (IsZombieOnly(a)) return 67;
        switch (a.ToLowerInvariant())
        {
            case "legoturnament": case "shipconteyner": b &= ~4; b &= ~8; break;
            case "sniper": case "sniper_night": b |= 4; b |= 8; break;
        }
        if (IsZombieMap(a)) { b |= 0x40; b |= 1; }
        return b;
    }

    public static List<MapInfo> All
    {
        get
        {
            if (maps != null) return maps;
            maps = new List<MapInfo>();
            foreach (var (sys, name, desc) in Friendly)
            {
                if (!GameFlow.MapAvailable(sys)) continue;
                int m = Normalize(sys, BaseCtfCp.Contains(sys) ? 15 : 3);
                bool z = IsZombieMap(sys);
                maps.Add(new MapInfo
                {
                    SystemName = sys, Name = name, Desc = desc, Modes = m,
                    AvailPlayers = z ? new[] { "2", "4", "6", "8", "10", "12", "14", "16" } : new[] { "4", "6", "8", "10", "12", "14" },
                    DefaultPlayerIndex = z ? 0 : 3,
                });
            }
            return maps;
        }
    }

    public static MapInfo Get(string sys) => All.FirstOrDefault(m => m.SystemName == sys);

    /// <summary>GUIRoomList.IsAllowedCreateMode: modes that can be created / filtered.</summary>
    public static bool IsAllowedCreateMode(GameMode a) => a == GameMode.DEATHMATCH || a == GameMode.TEAM_DEATHMATCH || a == GameMode.CAPTURE_THE_FLAG || a == GameMode.CONTROL_POINTS || a == GameMode.ZOMBIE;

    /// <summary>嗉.GetMaps(mode): NONE = all maps.</summary>
    public static List<MapInfo> ForMode(GameMode a) => a == GameMode.NONE ? All.ToList() : All.Where(m => m.Has(a)).ToList();
}
