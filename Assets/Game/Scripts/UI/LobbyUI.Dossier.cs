// «Досье» = New Era GUIStatistic (sub-tabs) → GUIAchievement / GUIStatMain (Общая / Карты / Режимы) / GUIStatWeapon / GUILeague.
// The user statistics (啎) and the achievement list (嚮, ACHIEVEMENT_URL) come from the New Era server; here they are local:
// Profile.Stats counters and Resources/data/achievements_ne.json (step-1 threshold / reward / level read from ne_ui2/02x screenshots),
// names / descriptions / icons of every step from the original language file (ach<id>name|desc|img).
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>嚯: one achievement step (id = sub id * 100 + level).</summary>
public class Achievement
{
    public long Id; public int UserLvl, Value, MaxValue, Reward, Level, MaxLevel; public bool Complete;
    public long SubId => Id / 100;
    public string Name => LanguageManager.GetText("ach" + Id + "name");
    public string Description => LanguageManager.GetText("ach" + Id + "desc");
    Texture2D ico;
    public Texture2D Ico => ico != null ? ico : ico = Resources.Load<Texture2D>("gui/icons/achievement/" + LanguageManager.GetText("ach" + Id + "img"));

    static List<Achievement> all;
    /// <summary>嚮.BaseList in server order (= language file order).</summary>
    public static List<Achievement> All { get { if (all == null) Load(); return all; } }

    static void Load()
    {
        all = new List<Achievement>();
        var data = new Dictionary<long, (int v, int r, int ul)>();
        var ta = Resources.Load<TextAsset>("data/achievements_ne");
        if (ta != null && MiniJson.Parse(ta.text) is Dictionary<string, object> d && d.TryGetValue("b", out var bo) && bo is List<object> b)
            foreach (Dictionary<string, object> c in b.OfType<Dictionary<string, object>>())
                data[System.Convert.ToInt64(c["i"])] = (System.Convert.ToInt32(c["v"]), System.Convert.ToInt32(c["r"]), System.Convert.ToInt32(c["ul"]));
        var rx = new Regex(@"^ach(\d+)name$");
        var firstReward = new Dictionary<long, int>();
        foreach (var k in LanguageManager.KeysInOrder)
        {
            var m = rx.Match(k);
            if (!m.Success) continue;
            long id = long.Parse(m.Groups[1].Value);
            var a = new Achievement { Id = id, Level = (int)(id % 100) };
            if (data.TryGetValue(id, out var e)) { a.MaxValue = e.v; a.Reward = e.r; a.UserLvl = e.ul; firstReward[a.SubId] = e.r; }
            else
            {
                // later steps: threshold = the last number of the description («Убить 5 000 врагов»), reward unknown → step-1 reward
                var nums = Regex.Matches(a.Description, @"\d+(?: \d{3})*");
                if (nums.Count > 0) a.MaxValue = int.Parse(nums[nums.Count - 1].Value.Replace(" ", ""));
                a.Reward = firstReward.TryGetValue(a.SubId, out var r) ? r : 50;
            }
            all.Add(a);
        }
        foreach (var a in all) a.MaxLevel = all.Where(x => x.SubId == a.SubId).Max(x => x.Level);
    }

    /// <summary>嚮.InitShowedList: per achievement group the first step that is not complete (or the last one).</summary>
    public static List<Achievement> Showed()
    {
        var p = Profile.Current;
        var list = new List<Achievement>();
        foreach (var g in All.GroupBy(a => a.SubId))
        {
            int val = (int)p.Stat("ach." + g.Key);
            if (g.Key == 100000000) val = (int)p.Stat("kill");   // «Отчаянный…»: kills
            Achievement shown = null;
            foreach (var a in g.OrderBy(a => a.Level))
            {
                a.Value = Mathf.Min(val, a.MaxValue); a.Complete = val >= a.MaxValue;
                shown = a;
                if (!a.Complete) break;
            }
            if (shown != null) list.Add(shown);
        }
        return list;
    }
}

public partial class LobbyUI
{
    public int dossierTab = 0;        // 喹.StatisticsMenuSelect: 0 Achievement, 1 Main, 2 Weapon, 3 Rating
    public int statMenu = 1;          // GUIStatMain.menu_slot_selected: 1 Общая, 2 Карты, 3 Режимы
    public int statWeaponWs = 1;      // GUIStatWeapon.menu_weapon_slot_selected
    public int leagueIndex = 1;       // 嗝.LeagueIndex
    Vector2 achScroll, statScroll, statWeaponScroll, leagueScroll;
    static string T(string k) => LanguageManager.GetText(k);

    void Dossier()
    {
        int t = SubTabs(new[] { T("Achievements"), T("Statistics"), T("Weapons stat."), T("Players rating") }, dossierTab);
        if (t != dossierTab) dossierTab = t;
        switch (dossierTab)
        {
            case 0: AchievementsTab(); break;
            case 1: StatMain(); break;
            case 2: StatWeapon(); break;
            default: League(); break;
        }
    }

    // ---------------- GUIAchievement ----------------
    void AchievementsTab()
    {
        var shown = Achievement.Showed();
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        GUILayout.BeginHorizontal();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
        Skins.AutoLabel(T("Achievements"), Skins.Txt("partActive"));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), T("Achievements"), Skins.Txt("partActive"), Skins.Txt("partActiveShadow"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(3);
        GUILayout.BeginHorizontal();
        GUILayout.Space(2);
        achScroll = GUILayout.BeginScrollView(achScroll, false, true, GUILayout.MinHeight(378));
        int num = 0;
        foreach (var a in shown)
        {
            if (num % 2 == 0) { GUILayout.Space(7); GUILayout.BeginHorizontal(); }
            GUILayout.Space(13);
            GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(344), GUILayout.Height(101));
            GUILayout.Label(a.Ico, GUIStyle.none, GUILayout.Width(97), GUILayout.Height(85));
            GUILayout.Space(6);
            GUILayout.BeginVertical();
            GUILayout.BeginVertical(GUILayout.MinHeight(70));
            GUILayout.BeginHorizontal();
            GUILayout.Label(a.Name, Skins.Txt("normal01"));
            GUILayout.FlexibleSpace();
            bool done = a.Complete && a.Level == a.MaxLevel;
            for (int i = 1; i <= a.MaxLevel; i++) GUILayout.Label(GUIContent.none, Skins.Lbl(done || i < a.Level ? "starActive" : "starNone"));
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            GUILayout.Label(a.Description, Skins.Txt("normal02"), GUILayout.MaxWidth(225));
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            if (done) Skins.AutoLabel(T("Complete"), Skins.Txt("normal03"));
            else
            {
                Skins.AutoLabel(LanguageManager.GetTextFormat("{0} step reward:", a.Level), Skins.Txt("normal03"));
                GUILayout.Space(4);
                Skins.AutoLabel(a.Reward.ToString(), Skins.Lbl("costSmall"));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.BeginHorizontal();
            if (a.UserLvl > Profile.Current.Level) GUILayout.Label(LanguageManager.GetTextFormat("Available from level {0}", a.UserLvl), Skins.Txt("error02"));
            else
            {
                ProgressBar(143, a.MaxValue, a.Value, "pb4");
                GUILayout.Space(4);
                GUILayout.Label($"{a.Value}/{a.MaxValue}", Skins.Txt("normal04"));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            num++;
            if (num % 2 == 0 || num >= shown.Count) { GUILayout.EndHorizontal(); GUILayout.Space(7); }
        }
        GUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- GUIStatMain ----------------
    void StatMain()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal(GUILayout.MinWidth(760));
        GUILayout.Space(5);
        StatWearSlots();
        GUILayout.Space(8);
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(334), GUILayout.Height(454));
        GUILayout.BeginHorizontal(GUILayout.MinHeight(55));
        GUILayout.Space(8);
        string[] menu = { null, T("Common"), T("Maps"), T("Modes") };
        for (int k = 1; k <= 3; k++)
        {
            if (k == statMenu)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
                Skins.AutoLabel(menu[k], Skins.Txt("partActive"));
                GUILayout.EndHorizontal();
            }
            else if (Skins.AutoButton(menu[k], Skins.Txt("part"))) { statMenu = k; statScroll = Vector2.zero; }
        }
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
        GUILayout.BeginHorizontal();
        GUILayout.Space(4);
        statScroll = GUILayout.BeginScrollView(statScroll, false, true, GUILayout.Height(380));
        if (statMenu == 1) StatCommon(); else if (statMenu == 2) StatMaps(); else StatModes();
        GUILayout.EndScrollView();
        GUILayout.Space(6);
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    static void StatRow(string key, string value)
    {
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label(T(key), Skins.Txt("normal03"));
        GUILayout.FlexibleSpace();
        GUILayout.Label(value, Skins.Txt("normal04"));
        GUILayout.EndHorizontal();
    }

    static void StatBlock(string title, System.Action rows, float minH = 70)
    {
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.MinHeight(minH));
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal(); GUILayout.Label(T(title), Skins.Txt("normal01")); GUILayout.EndHorizontal();
        rows();
        GUILayout.Space(8);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    /// <summary>啎.playedTimeStr: minutes, «{0} h {1} min» above an hour.</summary>
    static string PlayedTime(double seconds)
    {
        long min = (long)(seconds / 60);
        if (min > 60) { long m = min % 60; return LanguageManager.GetTextFormat("{0} h {1} min", (min - m) / 60, m); }
        return LanguageManager.GetTextFormat("{0} min.", min);
    }
    static string Pct(double v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2} %", v);

    void StatCommon()
    {
        var p = Profile.Current;
        double kill = p.Stat("kill"), death = p.Stat("death"), shot = p.Stat("shot");
        double kd = death < 1 ? kill : kill / death;
        double acc = shot > 0 ? p.Stat("hit") / shot * 100.0 : 0;
        StatBlock("Battle statistics", () =>
        {
            StatRow("Level", p.Level.ToString()); StatRow("Time in the game", PlayedTime(p.Stat("time")));
            StatRow("Kills", kill.ToString("0")); StatRow("Deaths", death.ToString("0")); StatRow("K/D", Pct(kd));
            StatRow("Victories", p.Stat("win").ToString("0")); StatRow("Defeats", p.Stat("lose").ToString("0")); StatRow("Accuracy", Pct(acc));
        });
        StatBlock("Kills", () => { StatRow("Overall", kill.ToString("0")); StatRow("Kills to head", p.Stat("head").ToString("0")); StatRow("Kills to nuts", p.Stat("nuts").ToString("0")); });
        StatBlock("Deaths", () =>
        {
            StatRow("Overall", death.ToString("0")); StatRow("Suicide", p.Stat("suicide").ToString("0"));
            StatRow("Dead of head shot", p.Stat("death_head").ToString("0")); StatRow("Dead of nuts shot", p.Stat("death_nuts").ToString("0"));
        });
        StatBlock("Various", () =>
        {
            StatRow("Overall dominations", p.Stat("dom").ToString("0")); StatRow("Max. dominations", p.Stat("dom_max").ToString("0"));
            StatRow("Overall revanges", p.Stat("rev").ToString("0")); StatRow("Max. revanges", p.Stat("rev_max").ToString("0"));
            StatRow("Overall shots", shot.ToString("0"));
        });
        StatReset(2);
    }

    void StatMaps()
    {
        var p = Profile.Current;
        foreach (var m in MapList.All.Where(m => p.Stat($"map.{m.SystemName}.time") > 0 || p.Stat($"map.{m.SystemName}.win") > 0).OrderByDescending(m => p.Stat($"map.{m.SystemName}.time")))
        {
            GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.MinHeight(75));
            GUILayout.Label(m.Ico, GUIStyle.none, GUILayout.Width(100), GUILayout.Height(60));
            GUILayout.Space(6);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(); GUILayout.Label(m.Name, Skins.Txt("normal01")); GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(); GUILayout.Label(T("Victories"), Skins.Txt("normal03")); GUILayout.FlexibleSpace(); GUILayout.Label(p.Stat($"map.{m.SystemName}.win").ToString("0"), Skins.Txt("normal04")); GUILayout.EndHorizontal();
            GUILayout.Space(2);
            GUILayout.BeginHorizontal(); GUILayout.Label(T("Time in the game"), Skins.Txt("normal03")); GUILayout.FlexibleSpace(); GUILayout.Label(PlayedTime(p.Stat($"map.{m.SystemName}.time")), Skins.Txt("normal04")); GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        StatReset(4);
    }

    void StatModes()
    {
        var p = Profile.Current;
        var modes = System.Enum.GetValues(typeof(GameMode)).Cast<GameMode>().Where(g => g != GameMode.NONE && p.Stat($"mode.{(int)g}.time") > 0).OrderByDescending(g => p.Stat($"mode.{(int)g}.time"));
        foreach (var g in modes)
        {
            GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.MinHeight(70));
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(); GUILayout.Label(g.GetFullName(), Skins.Txt("normal01")); GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(); GUILayout.Label(T("Victories"), Skins.Txt("normal03")); GUILayout.FlexibleSpace(); GUILayout.Label(p.Stat($"mode.{(int)g}.win").ToString("0"), Skins.Txt("normal04")); GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal(); GUILayout.Label(T("Time in the game"), Skins.Txt("normal03")); GUILayout.FlexibleSpace(); GUILayout.Label(PlayedTime(p.Stat($"mode.{(int)g}.time")), Skins.Txt("normal04")); GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        StatReset(3);
    }

    /// <summary>«Сброс статистики» block: 30 money, 啞.ClearStatistic(1 weapons / 2 common / 3 modes / 4 maps).</summary>
    void StatReset(int what)
    {
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.MinHeight(60));
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); GUILayout.Label(T("Statistics reset"), Skins.Txt("normal01")); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.Label(T("Cost:"), Skins.Txt("normal03"));
        GUILayout.Space(4);
        GUILayout.Label("30", Skins.Lbl("costSmall"));
        GUILayout.Space(5);
        if (GUILayout.Button(T("Reset"), Skins.Btn("btnStatClear"), GUILayout.Width(77), GUILayout.Height(22))) ClearStatistic(what);
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    void ClearStatistic(int what)
    {
        var p = Profile.Current;
        if (p.Money < 30) { NotificationWindow.Add(Notify.MissingMoney(AddMoney)); return; }
        p.Money -= 30;
        bool Flat(string k) => !k.Contains(".");
        var keys = p.Stats.Keys.Where(k => what == 1 ? k.StartsWith("weapon.") : what == 2 ? Flat(k) : what == 3 ? k.StartsWith("mode.") : k.StartsWith("map.")).ToList();
        foreach (var k in keys) p.Stats.Remove(k);
        p.Save();
    }

    /// <summary>GUIStatMain.DrawName: the fighter's name above the soldier (statisticTopName, 210x32).</summary>
    static void StatName()
    {
        GUILayout.BeginHorizontal(GUILayout.Width(210), GUILayout.Height(32));
        GUILayout.FlexibleSpace();
        GUILayout.Label(Profile.Current.Name, Skins.Txt("statisticTopName"));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    /// <summary>GUIStatMain.DrawWearSlots: 94.5x83.7 cells (95 here: old Unity drew the half pixel to the right, so the right column sits 1 px further) — left Head / Hat / Shirt / Gloves / Pants, the name, right Mask / Backpack / Other / Boots.</summary>
    void StatWearSlots()
    {
        var p = Profile.Current;
        void Col(int[] wts)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(11);
            for (int i = 0; i < wts.Length; i++)
            {
                if (i > 0) GUILayout.Space(6);
                var it = p.WearInSlot(wts[i]);
                GUILayout.Label(it != null ? it.Icon : EmptyTex(wts[i]), Skins.Bg("itemLeft"), GUILayout.Width(95f), GUILayout.Height(83.7f));
                if (it != null) HoverUI.Hover(it, Scr(GUILayoutUtility.GetLastRect()));
            }
            GUILayout.EndVertical();
        }
        Col(new[] { 9, 1, 4, 3, 5 });
        StatName();
        Col(new[] { 2, 7, 8, 6 });
    }

    // ---------------- GUIStatWeapon ----------------
    void StatWeapon()
    {
        var p = Profile.Current;
        var list = ItemDB.Weapons.Concat(ItemDB.DefaultWeapons).Where(w => w.Ws == statWeaponWs && p.Stat($"weapon.{w.Id}.kill") + p.Stat($"weapon.{w.Id}.shot") > 0)
            .GroupBy(w => w.Id).Select(g => g.First()).OrderByDescending(w => p.Stat($"weapon.{w.Id}.kill")).ToList();
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal(GUILayout.MinWidth(760));
        GUILayout.Space(101);
        StatName();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain2"), GUILayout.Width(334), GUILayout.Height(361));
        GUILayout.BeginHorizontal(GUILayout.MinHeight(55));
        GUILayout.Space(8);
        for (int ws = 1; ws <= 7; ws++)
        {
            if (ws == statWeaponWs)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive3"));
                GUILayout.Label(GUIContent.none, Skins.S("PartsWeapon", $"ws{ws}Active"));
                HoverUI.HoverText(T("ws_name_" + ws), Scr(GUILayoutUtility.GetLastRect()));
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button(GUIContent.none, Skins.S("PartsWeapon", $"ws{ws}"))) { statWeaponWs = ws; statWeaponScroll = Vector2.zero; }
                HoverUI.HoverText(T("ws_name_" + ws), Scr(GUILayoutUtility.GetLastRect()));
            }
        }
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
        GUILayout.BeginHorizontal();
        GUILayout.Space(4);
        statWeaponScroll = GUILayout.BeginScrollView(statWeaponScroll, false, true, GUILayout.Height(286));
        foreach (var w in list)
        {
            double shot = p.Stat($"weapon.{w.Id}.shot");
            GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.MinHeight(101));
            GUILayout.Label(w.Icon, Skins.Bg("itemRight02"), GUILayout.Width(97), GUILayout.Height(85));
            GUILayout.Space(6);
            GUILayout.BeginVertical();
            GUILayout.Label(w.Name, Skins.Txt("normal01"));
            StatRow("Kills", p.Stat($"weapon.{w.Id}.kill").ToString("0"));
            StatRow("To head", p.Stat($"weapon.{w.Id}.head").ToString("0"));
            StatRow("To nuts", p.Stat($"weapon.{w.Id}.nuts").ToString("0"));
            StatRow("Accuracy", Pct(shot > 0 ? p.Stat($"weapon.{w.Id}.hit") / shot * 100.0 : 0));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        StatReset(1);
        GUILayout.EndScrollView();
        GUILayout.Space(6);
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.Space(3);
        // DrawWeeaponSlots: the 7 slots with the equipped (or default) weapons
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(707));
        for (int ws = 1; ws <= 7; ws++)
        {
            GUILayout.BeginVertical(GUILayout.Width(101));
            var w = p.WeaponInSlot(ws);
            // the stat user's slots come from the server with isDefault set → Weapon.LoadIco uses Icons/Items/Weapon/Default/<sname> (silhouettes)
            var ico = w == null ? null : w.IsDefault ? (Resources.Load<Texture2D>("gui/icons/items/weapon/default/" + w.SName) ?? w.Icon) : w.Icon;
            GUILayout.Label(ico, Skins.Bg("itemLeft2"), GUILayout.Width(101), GUILayout.Height(90));
            if (w != null) HoverUI.Hover(w, Scr(GUILayoutUtility.GetLastRect()));
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- GUILeague ----------------
    /// <summary>Players rating: there is no league server — like New Era today (ne_ui2/024) the list shows the maintenance text.</summary>
    void League()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        GUILayout.BeginHorizontal();
        for (int n = 1; n < 16; n++)
        {
            if (leagueIndex == n)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
                GUILayout.Label(GUIContent.none, Skins.S("StatsLeague", "league" + n + "Active"));
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button(GUIContent.none, Skins.S("StatsLeague", "league" + n))) leagueIndex = n;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuTitle"), GUILayout.Height(36));
        GUILayout.Space(11);
        GUILayout.Label(T("Rank"), Skins.Txt("menuTitle"), GUILayout.Width(66));
        GUILayout.Label(T("Name"), Skins.Txt("menuTitle"), GUILayout.Width(310));
        GUILayout.Label(T("Level"), Skins.Txt("menuTitle"), GUILayout.Width(90));
        GUILayout.Label(T("Kills"), Skins.Txt("menuTitle"), GUILayout.Width(90));
        GUILayout.Label(T("Deaths"), Skins.Txt("menuTitle"), GUILayout.Width(90));
        GUILayout.Label(T("K/D"), Skins.Txt("menuTitle"), GUILayout.Width(60));
        GUILayout.Space(34);
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Space(2);
        GUILayout.BeginVertical();
        leagueScroll = GUILayout.BeginScrollView(leagueScroll, false, true, GUILayout.Height(310));
        GUILayout.Label(T("Current section is in maintenance mode. We are sorry for temporary nconveniences."), Skins.Txt("noneBattle"), GUILayout.Height(310));
        GUILayout.FlexibleSpace();
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("menuTitle"), GUILayout.Height(50));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); GUILayout.Label(GUIContent.none, GUIStyle.none); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }
}
