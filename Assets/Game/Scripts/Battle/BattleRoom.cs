// Battle room and score = New Era 嗇 (RoomSettings), 嗱 (GameScore), 啺 (team), 啼 (score user), 嘙 (frag).
// Local game: the room lives in this process, the local player has id 1 (bots will be added as more users).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum GameMode : byte { NONE = 0, DEATHMATCH = 1, TEAM_DEATHMATCH = 2, CAPTURE_THE_FLAG = 4, CONTROL_POINTS = 8, TOWER_DEFENSE = 0x10, ESCORT = 0x20, ZOMBIE = 0x40 }

public static class GameModeExt
{
    public static string GetFullName(this GameMode m)
    {
        switch (m)
        {
            case GameMode.DEATHMATCH: return LanguageManager.GetText("DEATHMATCH");
            case GameMode.TEAM_DEATHMATCH: return LanguageManager.GetText("TEAM DEATHMATCH");
            case GameMode.CAPTURE_THE_FLAG: return LanguageManager.GetText("CAPTURE THE FLAG");
            case GameMode.CONTROL_POINTS: return LanguageManager.GetText("CONTROL POINT");
            case GameMode.TOWER_DEFENSE: return LanguageManager.GetText("TOWER DEFENSE");
            case GameMode.ESCORT: return LanguageManager.GetText("ESCORT");
            case GameMode.ZOMBIE: return LanguageManager.GetText("ZOMBIE");
            default: return "";
        }
    }
    public static bool IsTeam(this GameMode m) => (int)m > 1 && m != GameMode.TOWER_DEFENSE;
}

/// <summary>喬 (MyRoomSetting): name, map, mode, players / time / victory-point choices.</summary>
public class RoomSettings
{
    public const int MaxNameLength = 37;
    public static readonly string[] AvailTimes = { "5", "10", "15", "20" };
    static readonly string[] AvailFragFlag = { "5", "10", "15", "20", "30" };
    static readonly string[] AvailFragControl = { "50", "100", "200", "300", "500" };

    string name = GenerateRandomName();
    public string Name { get => name; set { if (value.Length <= MaxNameLength) name = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(value, "\\s", " "), "(.)\\1{2,}", ""); } }
    public string MapId = "", MapName = "";
    GameMode mode = GameMode.TEAM_DEATHMATCH;
    MapInfo map;
    int selectPlayerIndex = 2, selectTimeIndex = 1, selectedFragIndex = 1;
    public int MaxPlayers = 16;
    public int TimeLimit = 10;          // minutes
    public int Frags = 30;              // 喬.Frags: CTF list 5..30, every other mode the control list 50..500 (index 1 → 100 = DM/TDM frag limit)
    public int MaxPlayersTeam => Mathf.Max(1, (int)(MaxPlayers * 0.5f));

    /// <summary>嗅.GenerateName: map_words1 + map_words2 + map_words3 from the language file.</summary>
    public static string GenerateRandomName()
    {
        string Pick(string k) { var l = LanguageManager.GetText(k).Split(';'); return l[Random.Range(0, l.Length)]; }
        return $"{Pick("map_words1")} {Pick("map_words2")} {Pick("map_words3")}";
    }
    public void GenerateName() => name = GenerateRandomName();

    public MapInfo Map
    {
        get => map;
        set
        {
            if (value == null) return;
            if (((uint)value.Modes & (uint)mode) != (uint)mode)
                foreach (GameMode g in System.Enum.GetValues(typeof(GameMode))) if (g != GameMode.NONE && value.Has(g)) { mode = g; break; }
            map = value; MapId = value.SystemName; MapName = value.Name;
            selectPlayerIndex = mode == GameMode.ZOMBIE ? 0 : value.DefaultPlayerIndex;
            ApplySelectedPlayers();
        }
    }

    public GameMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            if (mode == GameMode.ZOMBIE && map != null) selectPlayerIndex = 0;
            if (map != null) ApplySelectedPlayers();
            if (mode == GameMode.TOWER_DEFENSE && (MaxPlayers > 8 || MaxPlayers < 4)) MaxPlayers = 6;
            SelectedFragIndex = selectedFragIndex;
        }
    }

    /// <summary>DM gets an extra "18".</summary>
    public string[] AvailPlayers
    {
        get
        {
            var a = map?.AvailPlayers ?? new string[0];
            return mode == GameMode.DEATHMATCH && System.Array.IndexOf(a, "18") < 0 ? a.Concat(new[] { "18" }).ToArray() : a;
        }
    }
    public int SelectPlayerIndex { get => selectPlayerIndex; set { selectPlayerIndex = value; ApplySelectedPlayers(); } }
    public int SelectTimeIndex { get => selectTimeIndex; set { selectTimeIndex = value; TimeLimit = int.Parse(AvailTimes[value]); } }
    public string[] AvailFragLimit => mode == GameMode.CAPTURE_THE_FLAG ? AvailFragFlag : AvailFragControl;
    public int SelectedFragIndex { get => selectedFragIndex; set { selectedFragIndex = value; Frags = int.Parse(AvailFragLimit[value]); } }

    void ApplySelectedPlayers()
    {
        var a = AvailPlayers;
        if (a.Length == 0) return;
        selectPlayerIndex = Mathf.Clamp(selectPlayerIndex, 0, a.Length - 1);
        MaxPlayers = Mathf.Max(4, int.Parse(a[selectPlayerIndex]));
    }

    /// <summary>CTF / CP rooms run on a fixed 60-minute limit (GetNetworkTimeLimit) and count up.</summary>
    public int EffectiveTimeLimit => mode == GameMode.CAPTURE_THE_FLAG || mode == GameMode.CONTROL_POINTS ? 60 : TimeLimit;
}

/// <summary>嘙: one kill feed line. Team 1 = red, 2 = blue, 0 = DM.</summary>
public class Frag
{
    public int KillerID, KilledID, KillerTeam, KilledTeam, KillerHealth, KillerEnergy;
    public string KillerShowedName, KilledShowedName, WeaponSystemName;
    public bool Head;
    public float TimeKill;
}

/// <summary>啼: a player's line in the score.</summary>
public class ScoreUser
{
    public int UserID, Team, Kill, Death, Damage, Ping, Exp, Point, Position;
    public string UserName;
    public bool IsDead = true;
    public Frag LastFrag;
    public readonly int[] KillsByCategory = new int[7], DamageTaken = new int[7];
    public int DamageTakenTotal => DamageTaken.Sum();

    public static readonly string[] DamageCategoryNames = { "Ближний бой:", "Пистолеты:", "Автоматы:", "Пулемёты:", "Дробовики:", "Взрывы:", "Снайперки:" };
    public static string FormatDamage(int a) => a < 10000 ? a.ToString() : (System.Math.Floor(a / 100.0) / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k";
}

public class BattleScore
{
    public readonly Dictionary<int, ScoreUser> Users = new Dictionary<int, ScoreUser>();
    public readonly int[] TeamPoints = new int[3];      // [1] red, [2] blue (DM uses personal points)
    readonly Queue<Frag> frags = new Queue<Frag>();
    float lastFragCheck;

    public bool ContainsUser(int id) => Users.ContainsKey(id);
    public ScoreUser this[int id] => Users[id];
    public int GetTeamCount(int team) => Users.Values.Count(u => u.Team == team);
    public int GetTeamPoints(int team) => TeamPoints[Mathf.Clamp(team, 0, 2)];
    /// <summary>啺.Kill: the team's kills (frag limit in TDM, OfflineMatchController.CheckFragLimit).</summary>
    public int GetTeamKills(int team) => Users.Values.Where(u => u.Team == team).Sum(u => u.Kill);
    /// <summary>啺.Wins: rounds won, kept between rounds (PlayerManager.TimeOver [50]).</summary>
    public readonly int[] TeamWins = new int[3];
    public void AddTeamWin(int team) => TeamWins[Mathf.Clamp(team, 0, 2)]++;

    /// <summary>嗱.Frags: lines live 4 s, at most 10.</summary>
    public IEnumerable<Frag> Frags
    {
        get
        {
            if (lastFragCheck + 1f < Time.time)
            {
                while (frags.Count > 10 || (frags.Count > 0 && frags.Peek().TimeKill + 4f < Time.time)) frags.Dequeue();
                lastFragCheck = Time.time;
            }
            return frags;
        }
    }

    /// <summary>啺.ResortList: damage, then points, then kills.</summary>
    public List<ScoreUser> Sorted(int team)
    {
        var l = Users.Values.Where(u => u.Team == team).OrderByDescending(u => u.Damage).ThenByDescending(u => u.Point).ThenByDescending(u => u.Kill).ToList();
        for (int i = 0; i < l.Count; i++) l[i].Position = i + 1;
        return l;
    }

    /// <summary>嗱.AddFrag: deaths, kills/points; a suicide costs a point and leaves no killer card.</summary>
    public void AddFrag(Frag a)
    {
        a.TimeKill = Time.time;
        frags.Enqueue(a);
        while (frags.Count > 10) frags.Dequeue();
        if (ContainsUser(a.KilledID)) { var k = this[a.KilledID]; k.LastFrag = a; k.IsDead = true; k.Death++; }
        if (ContainsUser(a.KillerID) && a.KillerID != a.KilledID)
        {
            var u = this[a.KillerID]; u.Kill++; u.Point++;
            if (u.Team > 0) TeamPoints[u.Team]++;
        }
        if (a.KillerID == a.KilledID && ContainsUser(a.KilledID)) { var s = this[a.KilledID]; s.LastFrag = null; s.Point--; }
    }

    public void ResetRound()
    {
        foreach (var u in Users.Values) { u.Kill = u.Death = u.Damage = u.Point = u.Exp = 0; u.LastFrag = null; u.IsDead = true; System.Array.Clear(u.KillsByCategory, 0, 7); System.Array.Clear(u.DamageTaken, 0, 7); }
        TeamPoints[0] = TeamPoints[1] = TeamPoints[2] = 0;
        frags.Clear();
    }
}
