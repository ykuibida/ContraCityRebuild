// «Битвы» tab = port of New Era GUIRoomList (decrypted client): sub-tabs Доступные битвы / Создать битву / Карты / Сменить сервер,
// DrawSelectServer, DrawRoomList (mode filters, rooms, «В ИГРУ»), DrawCreateGame (map picture + description, Режим / Карта dropdowns
// = GUIDropDownList, room name + refresh, players / time / victory-point grids, START) and DrawMaps (3 cards per row, «Играть»).
// Local game: one local server; rooms exist only while a battle runs, so the list shows the original «no rooms» text.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattlesUI
{
    public enum Menu { ServerList, RoomList, CreateGame, Maps }
    public Menu Select = Menu.ServerList;
    public bool Connected;                         // 啲.SelectServer.IsConnected
    bool manualServerSelection;
    GameMode listMode = GameMode.NONE, mapsMode = GameMode.NONE;   // ListSelectMode / MAP_CREATE_SELECT_MODE (New Era starts the maps tab on DM)
    Vector2 serverScroll, listScroll, mapsScroll;
    RoomSettings room;
    DropDown drop;

    public const string LocalServerName = "Локальный сервер";
    static string T(string k) => LanguageManager.GetText(k);

    public void OpenServerSelection() { manualServerSelection = true; Select = Menu.ServerList; }

    // test hooks (-uitest)
    public void DebugMapsMode(GameMode m) { mapsMode = m; mapsScroll = Vector2.zero; }
    public void DebugOpenModeList()
    {
        var r = Room; var items = new List<(string, object)>();
        foreach (GameMode g in System.Enum.GetValues(typeof(GameMode))) if (g != GameMode.NONE && r.Map.Has(g) && MapList.IsAllowedCreateMode(g)) items.Add((g.GetFullName(), g));
        drop = new DropDown("modeSelect", items, o => r.Mode = (GameMode)o);
    }
    public void DebugCloseList() { if (drop != null) drop.Show = false; }

    RoomSettings Room
    {
        get
        {
            if (room == null) { room = new RoomSettings(); room.Map = MapList.All.FirstOrDefault(); }   // 喬(name, level, 0): first map, TDM
            return room;
        }
    }

    // ---------------- quick battle (MainNetworkController.QuickJoinRandomBattle → CreateQuickRandomBattle) ----------------
    public void QuickJoinRandomBattle()
    {
        Connected = true;   // PickQuickBattleServer: the only server
        var maps = MapList.All;
        if (maps.Count == 0) return;
        var map = maps[Random.Range(0, maps.Count)];
        var modes = new[] { GameMode.DEATHMATCH, GameMode.TEAM_DEATHMATCH, GameMode.CAPTURE_THE_FLAG, GameMode.CONTROL_POINTS, GameMode.ZOMBIE }.Where(map.Has).ToList();
        if (modes.Count == 0) return;
        var r = new RoomSettings();
        r.Map = map; r.Mode = modes[Random.Range(0, modes.Count)];
        r.GenerateName();
        GameFlow.Instance.StartBattle(r);
    }

    // ---------------- OnGUI ----------------
    public void Draw()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuRow03"), GUILayout.Height(41));
        if (Skins.AutoButton(T("Available battles"), Skins.Btn(Select == Menu.RoomList ? "menuRow03active" : "menuRow03"), GUILayout.Height(31))) Select = Menu.RoomList;
        if (Skins.AutoButton(T("Create battle"), Skins.Btn(Select == Menu.CreateGame ? "menuRow03active" : "menuRow03"), GUILayout.Height(31))) { Select = Menu.CreateGame; Room.GenerateName(); }
        if (Skins.AutoButton(T("Maps"), Skins.Btn(Select == Menu.Maps ? "menuRow03active" : "menuRow03"), GUILayout.Height(31))) Select = Menu.Maps;
        if (Skins.AutoButton(T("Change server"), Skins.Btn(Select == Menu.ServerList ? "menuRow03active" : "menuRow03"), GUILayout.Height(31))) OpenServerSelection();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        if (manualServerSelection || !Connected) Select = Menu.ServerList;
        switch (Select)
        {
            case Menu.ServerList: DrawSelectServer(); break;
            case Menu.CreateGame: DrawCreateGame(); break;
            case Menu.RoomList: DrawRoomList(); break;
            case Menu.Maps: DrawMaps(); break;
        }
    }

    /// <summary>Drawn after the lobby column (screen coordinates) so the list is on top of everything.</summary>
    public void DrawOverlay()
    {
        if (drop == null || !drop.Show) return;
        if (Event.current.type == EventType.MouseDown && drop.ValidRect && !drop.Position.Contains(Event.current.mousePosition)) { drop.Show = false; drop.HidedTime = Time.time; Event.current.Use(); return; }
        drop.Draw();
    }

    void ModeTabs(ref GameMode cur)
    {
        GUILayout.BeginHorizontal();
        foreach (var (m, key) in new[] { (GameMode.NONE, "All"), (GameMode.DEATHMATCH, "Deathmatch"), (GameMode.TEAM_DEATHMATCH, "Team Deathmatch"), (GameMode.CAPTURE_THE_FLAG, "Capture the flag"), (GameMode.CONTROL_POINTS, "Control point"), (GameMode.ZOMBIE, null) })
        {
            string text = key != null ? T(key) : GameMode.ZOMBIE.GetFullName().Replace("(ZM) ", "").Replace("(ZM)", "").Trim();
            if (cur == m)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
                Skins.AutoLabel(text, Skins.Txt("partActiveSmall"));
                GUILayout.EndHorizontal();
            }
            else if (Skins.AutoButton(text, Skins.Txt("partSmall"))) cur = m;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
    }

    static void PartTitle(string text)
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
        Skins.AutoLabel(text, Skins.Txt("partActive"));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), text, Skins.Txt("partActive"), Skins.Txt("partActiveShadow"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ---------------- DrawSelectServer ----------------
    void DrawSelectServer()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        PartTitle(T("SELECT SERVER"));
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuTitle"), GUILayout.Height(36));
        GUILayout.Space(11);
        GUILayout.Label(T("Server"), Skins.Txt("menuTitle"), GUILayout.Width(109));
        GUILayout.FlexibleSpace();
        GUILayout.Label(T("Capacity"), Skins.Txt("menuTitle"), GUILayout.Width(159));
        GUILayout.Label(T("Load"), Skins.Txt("menuTitle"), GUILayout.Width(63.5f));
        GUILayout.Label(T("Ping"), Skins.Txt("menuTitle"), GUILayout.Width(53.5f));
        GUILayout.Space(120);
        GUILayout.Button(GUIContent.none, Skins.Btn("refresh"), GUILayout.Width(32), GUILayout.Height(32));
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        serverScroll = GUILayout.BeginScrollView(serverScroll, false, true, GUILayout.MinHeight(340));
        GUILayout.BeginVertical();
        GUILayout.Space(4);
        GUILayout.BeginHorizontal(GUILayout.Height(36));
        GUILayout.Space(11);
        GUILayout.Label(LocalServerName, Skins.Txt("room"));
        GUILayout.FlexibleSpace();
        int online = GameFlow.Instance.Current == GameFlow.State.Battle ? 1 : 0;
        GUILayout.Label($"{online} / 1", Skins.Txt("room"), GUILayout.Width(149));
        GUILayout.Label("0 %", Skins.Txt("room"), GUILayout.Width(63.5f));
        GUILayout.Label("0 ms", Skins.Txt("room"), GUILayout.Width(53.5f));
        GUILayout.Space(18);
        if (GUILayout.Button(T("JOIN"), Skins.Btn("btn01"), GUILayout.Width(111), GUILayout.Height(32))) { manualServerSelection = false; Connected = true; Select = Menu.RoomList; }
        GUILayout.Space(4);
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- DrawRoomList ----------------
    void DrawRoomList()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        ModeTabs(ref listMode);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuTitle"), GUILayout.Height(36));
        GUILayout.Space(9);
        GUILayout.Label(T("Map"), Skins.Txt("menuTitle"), GUILayout.Width(156));
        GUILayout.Label(T("Room name"), Skins.Txt("menuTitle"), GUILayout.Width(283));
        GUILayout.Label(T("Mode"), Skins.Txt("menuTitle"), GUILayout.Width(81));
        GUILayout.Label(T("Players"), Skins.Txt("menuTitle"), GUILayout.Width(81));
        GUILayout.Space(105);
        GUILayout.Button(GUIContent.none, Skins.Btn("refresh"), GUILayout.Width(32), GUILayout.Height(32));
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        listScroll = GUILayout.BeginScrollView(listScroll, false, true, GUILayout.MinHeight(340));
        // local server: no other rooms while in the HQ
        GUILayout.Label(T("There are no active rooms. You can start new battle by pressing \"Create battle\" button"), Skins.Txt("noneBattle"), GUILayout.Height(320));
        GUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- DrawCreateGame ----------------
    void DrawCreateGame()
    {
        var r = Room;
        if (r.Map == null) return;
        bool listOpen = drop != null && drop.Show;
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        PartTitle(T("Creating the battle"));
        GUILayout.Space(13);
        GUILayout.BeginHorizontal();
        GUILayout.Space(15);
        GUILayout.BeginVertical(GUILayout.Width(320));
        GUILayout.Label(r.Map.Ico, GUIStyle.none, GUILayout.Width(316), GUILayout.Height(188));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), r.Map.Name.ToUpper(), Skins.Txt("mapNameBig"), Skins.Txt("mapNameBigShadow"));
        GUILayout.Space(12);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(316), GUILayout.Height(126));
        GUILayout.Space(5);
        GUILayout.BeginVertical();
        GUILayout.Label(r.Map.Name, Skins.Txt("mapName"));
        GUILayout.Space(1);
        GUILayout.Label(r.Map.Desc, Skins.Txt("mapDesc"));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        // Режим
        GUILayout.BeginHorizontal(GUILayout.Height(45));
        GUILayout.Label(T("Mode:"), Skins.Txt("mapPropertie"), GUILayout.Width(121), GUILayout.Height(29));
        if (listOpen) GUILayout.Label(r.Mode.GetFullName(), Skins.Bg("dropdownlist"), GUILayout.Width(256), GUILayout.Height(33));
        else if (GUILayout.Button(r.Mode.GetFullName(), Skins.Bg("dropdownlist"), GUILayout.Width(256), GUILayout.Height(33)))
        {
            var items = new List<(string, object)>();
            foreach (GameMode g in System.Enum.GetValues(typeof(GameMode))) if (g != GameMode.NONE && r.Map.Has(g) && MapList.IsAllowedCreateMode(g)) items.Add((g.GetFullName(), g));
            drop = new DropDown("modeSelect", items, o => { var g = (GameMode)o; if (r.Map.Has(g)) r.Mode = g; });
        }
        PlaceDrop("modeSelect", 300f);
        GUILayout.EndHorizontal();
        // Карта
        GUILayout.BeginHorizontal(GUILayout.Height(45));
        GUILayout.Label(T("Map:"), Skins.Txt("mapPropertie"), GUILayout.Width(121), GUILayout.Height(29));
        if (listOpen) GUILayout.Label(r.Map.Name, Skins.Bg("dropdownlist"), GUILayout.Width(256), GUILayout.Height(33));
        else if (GUILayout.Button(r.Map.Name, Skins.Bg("dropdownlist"), GUILayout.Width(256), GUILayout.Height(33)))
            drop = new DropDown("mapSelect", MapList.All.Select(m => (m.Name, (object)m)).ToList(), o => r.Map = (MapInfo)o);
        PlaceDrop("mapSelect", 320f);
        GUILayout.EndHorizontal();
        // Название комнаты
        GUILayout.BeginHorizontal(GUILayout.Height(43));
        GUILayout.Label(T("Room name:"), Skins.Txt("mapPropertie"), GUILayout.Width(121), GUILayout.Height(29));
        var tf = Skins.S("Main", "textfield");
        if (listOpen) GUILayout.Label(r.Name, tf, GUILayout.Height(32), GUILayout.Width(257));
        else
        {
            r.Name = GUILayout.TextField(r.Name, RoomSettings.MaxNameLength, tf, GUILayout.Height(32), GUILayout.MaxWidth(257), GUILayout.Width(257));
            GUILayout.Space(4);
            if (GUILayout.Button(GUIContent.none, Skins.Btn("refresh"), GUILayout.Width(32), GUILayout.Height(32))) r.GenerateName();
        }
        GUILayout.EndHorizontal();
        bool canChange = drop == null || (!drop.Show && drop.HidedTime + 0.3f < Time.time);
        // Макс. игроков
        GUILayout.BeginHorizontal(GUILayout.Height(43));
        GUILayout.Label(T("Max. players:"), Skins.Txt("mapPropertie"), GUILayout.Width(121), GUILayout.Height(29));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("selectGrid"), GUILayout.Height(32));
        var ap = r.AvailPlayers;
        float minW = Mathf.Min(293f, 222f * Mathf.Max(1, ap.Length) / 7f);
        int n = GUILayout.SelectionGrid(r.SelectPlayerIndex, ap, ap.Length, Skins.Btn("selectGrid"), GUILayout.Height(28), GUILayout.MinWidth(minW));
        if (n != r.SelectPlayerIndex && canChange) r.SelectPlayerIndex = n;
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();
        // Очки победы (CTF / CP) или Время игры
        GUILayout.BeginHorizontal(GUILayout.Height(43));
        bool obj = r.Mode == GameMode.CAPTURE_THE_FLAG || r.Mode == GameMode.CONTROL_POINTS;
        GUILayout.Label(T(obj ? "Victory points:" : "Time played:"), Skins.Txt("mapPropertie"), GUILayout.Width(121), GUILayout.Height(29));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("selectGrid"), GUILayout.Height(32));
        if (obj) { n = GUILayout.SelectionGrid(r.SelectedFragIndex, r.AvailFragLimit, r.AvailFragLimit.Length, Skins.Btn("selectGrid"), GUILayout.Height(28), GUILayout.MinWidth(181)); if (n != r.SelectedFragIndex && canChange) r.SelectedFragIndex = n; }
        else { n = GUILayout.SelectionGrid(r.SelectTimeIndex, RoomSettings.AvailTimes, RoomSettings.AvailTimes.Length, Skins.Btn("selectGrid"), GUILayout.Height(28), GUILayout.MinWidth(181)); if (n != r.SelectTimeIndex && canChange) r.SelectTimeIndex = n; }
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        var sr = GUILayoutUtility.GetLastRect();
        if (GUI.Button(new Rect(sr.x + 295f, sr.y + 225f, 115f, 115f), GUIContent.none, Skins.Btn("start")) && !listOpen)
        {
            r.Name = r.Name.Trim();
            if (r.Name == "") r.Name = Profile.Current.Name + Random.Range(1, 1000);
            if (!r.Map.Has(r.Mode)) r.Mode = GameMode.TEAM_DEATHMATCH;
            var go = r; room = null;   // a new setting next time, like the original after CreateRoom
            GameFlow.Instance.StartBattle(go);
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    void PlaceDrop(string owner, float h)
    {
        if (Event.current.type != EventType.Repaint || drop == null || drop.ValidRect || drop.Owner != owner) return;
        var lr = GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
        drop.SetRect(new Rect(lr.x, lr.y + lr.height - 11f, lr.width, h));
    }

    // ---------------- DrawMaps ----------------
    void DrawMaps()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        ModeTabs(ref mapsMode);
        GUILayout.Space(3);
        GUILayout.BeginHorizontal();
        GUILayout.Space(2);
        mapsScroll = GUILayout.BeginScrollView(mapsScroll, false, true, GUILayout.MinHeight(378));
        var list = MapList.ForMode(MapList.IsAllowedCreateMode(mapsMode) || mapsMode == GameMode.NONE ? mapsMode : GameMode.TEAM_DEATHMATCH);
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (i % 3 == 0) { GUILayout.Space(7); GUILayout.BeginHorizontal(); }
            GUILayout.Space(13);
            GUILayout.BeginHorizontal(GUILayout.Width(224), GUILayout.Height(160));
            GUILayout.Label(m.Ico, GUIStyle.none, GUILayout.Width(224), GUILayout.Height(135));
            var lr = GUILayoutUtility.GetLastRect();
            Skins.ShadowLabel(lr, m.Name.ToUpper(), Skins.Txt("mapNameBig02"), Skins.Txt("mapNameBig02Shadow"));
            lr.y += lr.height - 10f; lr.height = 35f;
            GUI.BeginGroup(lr);
            if (GUI.Button(new Rect(101f, 0f, 111f, 32f), T("Play"), Skins.Btn("btn01")))
            {
                // 啲.ConnectQuick(map, mode): join or create a room of this map in the selected mode
                var r = new RoomSettings(); r.Map = m;
                if (mapsMode != GameMode.NONE && m.Has(mapsMode)) r.Mode = mapsMode;
                r.GenerateName();
                GameFlow.Instance.StartBattle(r);
            }
            GUI.EndGroup();
            GUILayout.EndHorizontal();
            if (i % 3 == 2 || i == list.Count - 1) { GUILayout.EndHorizontal(); GUILayout.Space(7); }
        }
        if (list.Count == 0) GUILayout.Label(T("Currently there are no available maps for this game mode."), Skins.Txt("noneBattle"), GUILayout.Height(325));
        GUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    /// <summary>GUIDropDownList: DropDownList skin box + buttons, scrolls when longer than its rect.</summary>
    class DropDown
    {
        public readonly string Owner;
        readonly List<(string text, object tag)> items;
        readonly System.Action<object> onPick;
        public Rect Position; public bool ValidRect, Show = true; public float HidedTime;
        Vector2 scroll; bool scrollOn;
        public DropDown(string owner, List<(string, object)> list, System.Action<object> pick) { Owner = owner; items = list; onPick = pick; }
        public void SetRect(Rect r) { Position = r; ValidRect = true; }
        bool drawThisFrame;   // decided on Layout only: the rect arrives during Repaint, and both passes must see the same controls
        public void Draw()
        {
            if (Event.current.type == EventType.Layout) drawThisFrame = ValidRect;
            if (!drawThisFrame) return;
            var skin = Skins.Get("DropDownList");
            GUILayout.BeginArea(Position);
            if (scrollOn) scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.Height(Position.height));
            GUILayout.BeginVertical(GUIContent.none, skin.GetStyle("box"));
            float total = 0f;
            foreach (var (text, tag) in items)
            {
                if (GUILayout.Button(text, skin.GetStyle("button"))) { onPick(tag); Show = false; HidedTime = Time.time; }
                total += GUILayoutUtility.GetLastRect().height;
            }
            GUILayout.EndVertical();
            if (scrollOn) GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) scrollOn = total > Position.height;
            GUILayout.EndArea();
        }
    }
}
