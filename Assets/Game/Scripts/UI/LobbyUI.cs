// Lobby = port of the original MainMenu / GUIInventory (Штаб) / GUIShop (Магазин) / GUIAbility layouts
// (Penta 2016 + New Era, spec Catalog/ui_spec.md §1–§3). Real screen pixels, 760-px column centred, original GUISkins.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class LobbyUI
{
    readonly OptionsWindow options;
    public LobbyUI(OptionsWindow o) { options = o; }

    public const float ColW = 760f;
    public static float ColX => Mathf.Floor((Screen.width - ColW) / 2f);
    static readonly string[] MainTabs = { "Главная", "Штаб", "Магазин", "Досье", "Задания", "Клан", "Товарищи", "Битвы" };
    static readonly string[] ShopTabs = { "Внешний вид", "Оружие", "Насмешки", "Усилители" };
    static readonly string[] HqTabs = { "Внешний вид", "Оружие", "Насмешки", "Тренировочный зал", "Мастерская", "Изменить имя" };
    // gear categories in original order: style suffix, wear slot (wt), hover name, empty-slot icon
    static readonly (string type, int wt, string name, string empty)[] Gear =
    {
        ("Hats", 1, "Голова", "hat"), ("Masks", 2, "Лицо", "mask"), ("Gloves", 3, "Кисти рук", "gloves"), ("Shirts", 4, "Торс", "shirt"),
        ("Pants", 5, "Ноги", "pants"), ("Boots", 6, "Ступни", "boots"), ("Backpacks", 7, "Спина", "backpack"), ("Others", 8, "Другое", "other"),
        ("Heads", 9, "Головы", "head"),
    };
    static readonly string[] WsNames = { "", "Ближний бой", "Пистолеты", "Автоматы", "Тяжелое орудие", "Дробовики", "Гранатометы", "Снайперки" };
    // slot boxes around the character: left column Hat, Shirt, Gloves, Pants; right column Mask, Backpack, Other, Boots
    static readonly int[] LeftSlots = { 1, 4, 3, 5 }, RightSlots = { 2, 7, 8, 6 };

    public int mainTab = 0, shopTab = 0, hqTab = 0, gearIdx = 0, weaponWs = 1;   // MainMenu opens on «Главная»
    /// <summary>GUIInventory.menu_wear_type_seleted: the HQ keeps its own clothing tab (None = −1 until a type is owned), apart from the shop's.</summary>
    public int hqGearIdx = -1;
    float contentHeight;   // height of the tab content: the bottom row follows it (rating 505 → 5 px lower, ne_ui2/024)
    int GearSel { get => mainTab == 1 ? hqGearIdx : gearIdx; set { if (mainTab == 1) hqGearIdx = value; else gearIdx = value; } }
    bool HeadsSel => GearSel >= 0 && Gear[GearSel].wt == 9;
    Vector2 scroll;
    public CharacterPreview Preview;
    public GameItem DebugHover;
    public bool mapSelect;
    public readonly BattlesUI Battles = new BattlesUI();
    public void OpenMapSelect() => mapSelect = true;
    public void CloseMapSelect() => mapSelect = false;
    public void DebugTryOn(GameItem it) { if (it == null) shopPreview.Clear(); else shopPreview[it.Wt] = it; }   // test hook: same as a drop on the slot

    public void Draw()
    {
        GUI.skin = Skins.Main;
        float W = Screen.width, H = Screen.height, cx = ColX;
        HoverUI.Enabled = !mapSelect && !NotificationWindow.IsShow && !options.Open && dragged == null;
        if (Preview != null) { Preview.SetRect(new Rect(cx, H - 581 - 113, ColW, 581)); Preview.SetOutfit(OutfitWithTryOn()); }
        if (Event.current.type == EventType.Repaint) gridHits.Clear();

        // fills outside the column + footer (MainMenuCameraFix)
        GUI.Label(new Rect(0, 113, cx, 581), GUIContent.none, Skins.Fix("left"));
        GUI.Label(new Rect(cx + ColW, 113, W - cx - ColW, 581), GUIContent.none, Skins.Fix("left"));
        GUI.Label(new Rect(0, 694, W, Mathf.Max(0, H - 694)), GUIContent.none, Skins.Fix("bottom"));

        Row1(W); Row2(W);

        GUILayout.BeginArea(new Rect(cx, 113, ColW, 640));
        GUILayout.BeginVertical();
        switch (mainTab)
        {
            case 1: Hq(); break;
            case 2: Shop(); break;
            case 3: Dossier(); break;
            case 7: Battles.Draw(); break;
            default: Home(); break;
        }
        GUILayout.EndVertical();
        if (Event.current.type == EventType.Repaint) contentHeight = GUILayoutUtility.GetLastRect().height;
        GUILayout.EndArea();
        BottomRow(cx);
        ContraChat.OnGUI();   // 嘑.OnGUI at the end of MainMenu.OnGUI
        HandleDrag();
        if (mainTab == 7) Battles.DrawOverlay();

        if (mapSelect) MapSelect();
        options.Draw();
        if (!options.Open && !LoadingMapPopup.Show) NotificationWindow.Draw();
        if (DebugHover != null) { HoverUI.ForceItem = DebugHover; HoverUI.ForceCell = new Rect(cx + 416 + 7, 220, 97, 58); }
        else HoverUI.ForceItem = null;
        HoverUI.Draw();
    }

    // ---------------- row 1: logo, В БОЙ!, sound/settings/fullscreen ----------------
    void Row1(float W)
    {
        GUILayout.BeginArea(new Rect(0, 0, W, 73));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuRow01"), GUILayout.Width(W), GUILayout.Height(73));
        GUILayout.Label(GUIContent.none, Skins.Ico("logo"), GUILayout.Width(159), GUILayout.Height(65));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(LanguageManager.GetText("To BATTLE!"), Skins.Btn("menuQuickBattle"), GUILayout.MinWidth(258), GUILayout.Height(64))) Battles.QuickJoinRandomBattle();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(172));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(GUIContent.none, Skins.Btn(GameOptions.SoundIsMute ? "menuSoundOff" : "menuSoundOn"), GUILayout.Width(32), GUILayout.Height(32)))
            GameOptions.SoundIsMute = !GameOptions.SoundIsMute;   // MainMenu: 喞.SoundIsMute = !喞.SoundIsMute
        GUILayout.Space(3);
        if (GUILayout.Button(GUIContent.none, Skins.Btn("menuSetting"), GUILayout.Width(32), GUILayout.Height(32))) options.Open = !options.Open;
        GUILayout.Space(3);
        if (GUILayout.Button(GUIContent.none, Skins.Btn("menuFullscreen"), GUILayout.Width(42), GUILayout.Height(42))) Screen.fullScreen = !Screen.fullScreen;
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---------------- row 2: main tabs (MainMenu.OnGUI) ----------------
    static readonly string[] MainTabKeys = { "Home", "My HQ", "Shop", "Dossier", null, "Clan", "Comrades", "Battles" };
    void Row2(float W)
    {
        GUILayout.BeginArea(new Rect(0, 73, W, 40));
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuRow02"), GUILayout.Width(W), GUILayout.Height(40));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuRow02_inner"), GUILayout.Height(34));
        for (int i = 0; i < MainTabKeys.Length; i++)
        {
            if (i > 0) GUILayout.Label(GUIContent.none, Skins.Sep("menuRow02"), GUILayout.Width(1), GUILayout.Height(30));
            bool usable = i <= 3 || i == 7;
            string text = MainTabKeys[i] != null ? LanguageManager.GetText(MainTabKeys[i]) : "Задания";
            // New Era dims «Задания» only while 嘎.IsAvailableForLocalUser is false — on a fresh account it is available (ne_ui2/010_hq)
            var old = GUI.color;
            if (Skins.AutoButton(text, Skins.Btn(mainTab == i ? "menuRow02active" : "menuRow02"), GUILayout.Height(30)) && usable)
            { if (mainTab != i && i == 2) shopPreview.Clear(); if (i == 7) Battles.OpenServerSelection(); mainTab = i; scroll = Vector2.zero; dragged = null; }
            GUI.color = old;
        }
        GUILayout.EndHorizontal();
        if (Event.current.type == EventType.Repaint) { var tr = GUILayoutUtility.GetLastRect(); tr.y += 73; ContraChat.MenuTabsRect = tr; }   // MainMenu.MenuTabsRect (screen space)
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---------------- bottom row: level/exp and money (MainMenu.OnGUI tail) ----------------
    void BottomRow(float cx)
    {
        var p = Profile.Current;
        GUILayout.BeginArea(new Rect(0, 113 + (contentHeight > 0 ? contentHeight : 500) + 13, Screen.width, 68));   // MainMenu: Space(13) after the tab content
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(ColW));
        GUILayout.BeginVertical(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("bottomLeft"), GUILayout.Width(327), GUILayout.Height(54));
        GUILayout.Label("99", Skins.Lbl("progressLvlCur"), GUILayout.Width(38), GUILayout.Height(38));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), p.Level.ToString(), Skins.Txt("progressLvlCur"), Skins.Txt("progressLvlCurshadow"));
        GUILayout.Space(5);
        ProgressBar(212, p.MaxExp, p.Exp, "pb");
        GUILayout.Space(5);
        GUILayout.Label("99", Skins.Lbl("progressLvlNext"), GUILayout.Width(38), GUILayout.Height(38));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), (p.Level + 1).ToString(), Skins.Txt("progressLvlNext"), Skins.Txt("progressLvlNextshadow"));
        GUILayout.EndHorizontal();
        HoverUI.HoverText(LanguageManager.GetTextFormat("Exp: {0} / {1}", p.Exp, p.MaxExp), GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
        GUILayout.FlexibleSpace(); GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("bottomRight"), GUILayout.Width(310), GUILayout.Height(68));
        GUILayout.BeginVertical(GUILayout.Width(47)); GUILayout.FlexibleSpace();
        GUILayout.Label(GUIContent.none, Skins.Ico("money"), GUILayout.Width(42), GUILayout.Height(42));
        GUILayout.FlexibleSpace(); GUILayout.EndVertical();
        Skins.AutoLabel(p.Money.ToString(), Skins.Lbl("money"));
        GUILayout.Space(7);
        GUILayout.Label(GUIContent.none, Skins.Sep("money"), GUILayout.Height(60), GUILayout.Width(1));
        GUILayout.Space(7);
        GUILayout.BeginVertical(GUILayout.MaxWidth(172)); GUILayout.FlexibleSpace();
        if (GUILayout.Button(LanguageManager.GetText("Recharge"), Skins.Btn("green"), GUILayout.MinWidth(157), GUILayout.Height(46))) { p.Money += 10000; p.Save(); }
        var b = GUILayoutUtility.GetLastRect();
        GUI.Label(new Rect(b.x - 1, b.y - 1, 66, 29), GUIContent.none, Skins.Lbl("action"));
        GUILayout.FlexibleSpace(); GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    /// <summary>GUIProgressBar.ProgressBar: background horizontal of width w with the line label inside (min = line borders).</summary>
    public static void ProgressBar(float w, float max, float val, string e)
    {
        var line = Skins.S("Progressbar", e + "Line");
        float n = Mathf.FloorToInt(w * (max > 0 ? val / max : 0)), bw = line.border.left + line.border.right;
        if (n < bw) n = bw;
        if (n > w - bw) n = w - bw;
        GUILayout.BeginHorizontal(GUIContent.none, Skins.S("Progressbar", e + "Background"), GUILayout.Width(w));
        GUILayout.Label(GUIContent.none, line, GUILayout.Width(n));
        GUILayout.EndHorizontal();
    }

    // ---------------- sub-tab bar (menuRow03) ----------------
    static int SubTabs(string[] tabs, int cur, int neverActive = -1)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("menuRow03"), GUILayout.Height(41));
        for (int i = 0; i < tabs.Length; i++)
            if (Skins.AutoButton(tabs[i], Skins.Btn(cur == i && i != neverActive ? "menuRow03active" : "menuRow03"), GUILayout.Height(31))) cur = i;
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        return cur;
    }

    // ---------------- Магазин (GUIShop) ----------------
    void Shop()
    {
        int t = SubTabs(ShopTabs, shopTab);
        if (t != shopTab) { shopTab = t; scroll = Vector2.zero; }
        GUILayout.BeginHorizontal(GUILayout.Width(ColW));
        if (shopTab == 0)
        {
            GUILayout.Space(5);
            if (Gear[gearIdx].wt == 9) HeadSlot(shopPreview); else WearSlots(shopPreview);
            GUILayout.Space(8);
        }
        else GUILayout.FlexibleSpace();
        Panel(shopTab != 0, 454, () =>
        {
            if (shopTab == 0) { GearTabs(false); GridHeight(379, ItemDB.ShopWears(Gear[gearIdx].wt).ToList(), true); }
            else if (shopTab == 1) { WeaponTabs(); GridHeight(379, ItemDB.ShopWeapons(weaponWs).ToList(), true); }
            else if (shopTab == 2) { SingleTab("partTauntActive"); GridHeight(379, ItemDB.Taunts.Where(x => x.Id != 8).ToList(), true); }
            else { SingleTab("partEnhancerActive"); GridHeight(379, ItemDB.Boosters, true); }
        });
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
    }

    static void Panel(bool green, float h, System.Action body)
    {
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg(green ? "winMain2" : "winMain"), GUILayout.Width(334), GUILayout.Height(h));
        body();
        GUILayout.EndVertical();
    }

    void GearTabs(bool hqOnlyOwned)
    {
        GUILayout.BeginHorizontal(GUILayout.MinHeight(55));
        GUILayout.Space(12);
        var p = Profile.Current;
        for (int i = 0; i < Gear.Length; i++)
        {
            var g = Gear[i];
            if (hqOnlyOwned ? !ItemDB.ShopWears(g.wt).Any(p.Owns) : g.wt == 9 && !ItemDB.ShopWears(9).Any()) continue;
            if (i == GearSel)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
                GUILayout.Label(GUIContent.none, Skins.S("PartsGear", "part" + g.type + "Active"));
                HoverUI.HoverText(g.name, Scr(GUILayoutUtility.GetLastRect()));
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button(GUIContent.none, Skins.S("PartsGear", "part" + g.type))) { GearSel = i; scroll = Vector2.zero; }
                HoverUI.HoverText(g.name, Scr(GUILayoutUtility.GetLastRect()));
            }
            GUILayout.Space(5);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
    }

    void WeaponTabs(bool hqOnlyOwned = false)
    {
        GUILayout.BeginHorizontal(GUILayout.MinHeight(55));
        GUILayout.Space(8);
        var pr = Profile.Current;
        if (hqOnlyOwned && !ItemDB.ShopWeapons(weaponWs).Any(x => !x.IsDefault && pr.Owns(x)))
        { var firstWs = Enumerable.Range(1, 7).FirstOrDefault(n => ItemDB.ShopWeapons(n).Any(x => !x.IsDefault && pr.Owns(x))); if (firstWs > 0) weaponWs = firstWs; }
        for (int n = 1; n <= 7; n++)
        {
            if (hqOnlyOwned && !ItemDB.ShopWeapons(n).Any(x => !x.IsDefault && pr.Owns(x))) continue;
            if (n == weaponWs)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive3"));
                GUILayout.Label(GUIContent.none, Skins.S("PartsWeapon", $"ws{n}Active"));
                HoverUI.HoverText(WsNames[n], Scr(GUILayoutUtility.GetLastRect()));
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button(GUIContent.none, Skins.S("PartsWeapon", $"ws{n}"))) { weaponWs = n; scroll = Vector2.zero; }
                HoverUI.HoverText(WsNames[n], Scr(GUILayoutUtility.GetLastRect()));
            }
        }
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
    }

    static void SingleTab(string style)
    {
        GUILayout.BeginHorizontal(GUILayout.MinHeight(55));
        GUILayout.Space(8);
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive3"));
        GUILayout.Label(GUIContent.none, Skins.S("PartsGear", style));
        HoverUI.HoverText(style == "partTauntActive" ? "Насмешки" : "Усилители", Scr(GUILayoutUtility.GetLastRect()));
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
    }

    /// <summary>Shop (itemShop01/02) or HQ (itemRight/02) grid: 3 columns of 97x85, 3 px gaps; icon as the cell content (original).</summary>
    void GridHeight(float h, List<GameItem> items, bool shop)
    {
        var p = Profile.Current;
        GUILayout.BeginHorizontal(); GUILayout.Space(4);
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.Height(h));
        if (!shop && items.Count == 0)
            GUILayout.Label(hqTab == 2 ? "На данный момент у Вас нет активных насмешек. Перейдите в «МАГАЗИН» для их покупки."
                : "На данный момент у Вас есть набор базового оружия. Чтобы купить новое, посетите «МАГАЗИН»", Skins.Txt("noneBattle"), GUILayout.Height(hqTab == 0 ? 370 : 260));
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (i % 3 == 0) { GUILayout.BeginHorizontal(); GUILayout.Space(3); }
            bool wear = it.Kind == ItemKind.Wear;
            string cellStyle = shop ? (wear ? "itemShop01" : "itemShop02") : (wear ? "itemRight" : "itemRight02");
            var content = new GUIContent(it.Kind == ItemKind.Weapon && it.IsDefault ? (UIKit.Tex("icons/items/weapon/default/" + it.SName) ?? it.Icon) : it.Icon);
            bool box = shop
                ? dragged == null && (wear ? !shopPreview.Values.Contains(it) : it.Kind == ItemKind.Weapon ? !p.IsEquipped(it) : false)
                : dragged == null;
            if (shop) GUILayout.BeginVertical(GUILayout.Width(97));
            if (box) GUILayout.Box(content, Skins.Bg(cellStyle), GUILayout.Width(97), GUILayout.Height(85));
            else GUILayout.Label(content, Skins.Bg(cellStyle), GUILayout.Width(97), GUILayout.Height(85));
            if (shop) GUILayout.EndVertical();
            var r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint) gridHits.Add((it, Scr(r)));
            if (shop && it.Int("iS") == 1) GUI.Label(new Rect(r.x, r.y, 33, 29), GUIContent.none, Skins.Ico("sale"));
            if (it.Kind == ItemKind.Taunt) GUI.Button(new Rect(r.x + 5, r.y + 5, 24, 17), GUIContent.none, Skins.Btn("play"));
            if (shop)
            {
                HoverUI.Hover(it, Scr(new Rect(r.x, r.y, r.width, r.height - 27)));
                bool owned = p.Owns(it) && !it.IsDefault;
                if (owned) GUI.Label(new Rect(r.x + 68, r.y + 56, 24, 23), GUIContent.none, Skins.Lbl("isBuyed"));
                else if (it.Level > p.Level) GUI.Label(new Rect(r.x + 68, r.y + 56, 24, 23), GUIContent.none, Skins.Lbl("isDisabled"));
                else if (GUI.Button(new Rect(r.x + 68, r.y + 62, 24, 17), GUIContent.none, Skins.Btn("buy"))) OnBuy(it);
                int price = it.Kind == ItemKind.Booster || it.Kind == ItemKind.Taunt ? new[] { it.Price1, it.Price7, it.Price30, it.Price }.FirstOrDefault(v => v > 0) : it.Price;
                GUI.Label(new Rect(r.x + 4, r.y + 61, 60, 17), price.ToString(), Skins.Lbl("costSmall"));
            }
            else HoverUI.Hover(it, Scr(r));
            GUILayout.Space(3);
            if (i % 3 == 2 || i == items.Count - 1) { GUILayout.EndHorizontal(); GUILayout.Space(3); }
        }
        GUILayout.EndScrollView();
        if (Event.current.type == EventType.Repaint) gridRect = Scr(GUILayoutUtility.GetLastRect());
        GUILayout.Space(6); GUILayout.EndHorizontal(); GUILayout.Space(4);
    }

    static Texture2D EmptyTex(int wt) => UIKit.Tex("icons/items/empty/empty_slot_" + Gear.First(x => x.wt == wt).empty);

    /// <summary>GUIShop/GUIInventory.DrawWearSlots: two columns around the soldier; the dragged item's slot lights up (itemLeftActive).</summary>
    void WearSlots(Dictionary<int, GameItem> view)
    {
        int dragWt = dragged != null && dragged.Kind == ItemKind.Wear ? dragged.Wt : 0;
        bool rep = Event.current.type == EventType.Repaint;
        void Column(int[] slots)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(11);
            for (int k = 0; k < slots.Length; k++)
            {
                int wt = slots[k];
                if (k > 0) GUILayout.Space(8);
                view.TryGetValue(wt, out var it);
                GUILayout.Label(it != null ? it.Icon : EmptyTex(wt), Skins.Bg(dragWt == wt ? "itemLeftActive" : "itemLeft"), GUILayout.Width(105), GUILayout.Height(93));
                var r = GUILayoutUtility.GetLastRect();
                if (it != null) HoverUI.Hover(it, Scr(r));
                if (dragWt == wt && rep) draggedItemRect = Scr(r);
                if (dragged == null && rep) viewRects[wt] = Scr(r);
            }
            GUILayout.EndVertical();
        }
        Column(LeftSlots);
        GUILayout.FlexibleSpace();
        Column(RightSlots);
        if (rep) { var g = Scr(GUILayoutUtility.GetLastRect()); dropGlobalRect = new Rect(g.x - 180, g.y, 165, g.height); }
    }

    /// <summary>DrawBarbershopSlots / DrawHeadSlots: a single "Головы" slot.</summary>
    void HeadSlot(Dictionary<int, GameItem> view)
    {
        int dragWt = dragged != null && dragged.Kind == ItemKind.Wear ? dragged.Wt : 0;
        bool rep = Event.current.type == EventType.Repaint;
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.MinHeight(423));
        GUILayout.Space(11);
        view.TryGetValue(9, out var it);
        GUILayout.Label(it != null ? it.Icon : EmptyTex(9), Skins.Bg(dragWt == 9 ? "itemLeftActive" : "itemLeft"), GUILayout.Width(105), GUILayout.Height(93));
        var r = GUILayoutUtility.GetLastRect();
        if (it != null) HoverUI.Hover(it, Scr(r));
        if (dragWt == 9 && rep) draggedItemRect = Scr(r);
        if (dragged == null && rep) viewRects[9] = Scr(r);
        GUILayout.EndVertical();
        if (rep) { var g = Scr(GUILayoutUtility.GetLastRect()); dropGlobalRect = new Rect(g.x - 180, g.y, 165, g.height); }
    }

    Dictionary<int, GameItem> EquippedView()
    {
        var d = new Dictionary<int, GameItem>();
        foreach (var kv in Profile.Current.EquippedWear) { var it = ItemDB.Get(ItemKind.Wear, kv.Value); if (it != null) d[kv.Key] = it; }
        return d;
    }

    // ---------------- Штаб (GUIInventory) ----------------
    void Hq()
    {
        int t = SubTabs(HqTabs, hqTab, 5);
        if (t == 5) OpenRename();
        else if (t != hqTab) { hqTab = t; scroll = Vector2.zero; dragged = null; }
        if (hqTab == 3) { Gym(); return; }
        if (hqTab == 4) { Workshop(); return; }
        var p = Profile.Current;
        GUILayout.BeginHorizontal(GUILayout.MinWidth(ColW));
        if (hqTab == 0)
        {
            GUILayout.Space(5);
            if (hqGearIdx < 0 || !ItemDB.ShopWears(Gear[hqGearIdx].wt).Any(p.Owns)) hqGearIdx = System.Array.FindIndex(Gear, g => ItemDB.ShopWears(g.wt).Any(p.Owns));   // GUIInventory.Init order
            if (HeadsSel) HeadSlot(EquippedView()); else WearSlots(EquippedView());
            GUILayout.Space(8);
        }
        else GUILayout.FlexibleSpace();
        Panel(hqTab != 0, hqTab == 0 ? 454 : 335, () =>
        {
            if (hqTab == 0) { GearTabs(true); GridHeight(379, hqGearIdx >= 0 ? ItemDB.ShopWears(Gear[hqGearIdx].wt).Where(p.Owns).ToList() : new List<GameItem>(), false); }
            else if (hqTab == 1) { WeaponTabs(true); GridHeight(260, ItemDB.ShopWeapons(weaponWs).Where(x => !x.IsDefault && p.Owns(x)).ToList(), false); }
            else { SingleTab("partTauntActive"); GridHeight(260, ItemDB.Taunts.Where(p.Owns).ToList(), false); }
        });
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
        if (hqTab == 1) { GUILayout.Space(3); WeaponSlots(); }
        else if (hqTab == 2) { GUILayout.Space(3); TauntSlots(); }
    }

    static string KeyName(KeyCode k) => k == KeyCode.Mouse2 ? "СКМ" : k == KeyCode.Minus ? "Minus" : k.ToString().Replace("Alpha", "");

    void WeaponSlots()
    {
        var p = Profile.Current;
        int dragWs = dragged != null && dragged.Kind == ItemKind.Weapon ? dragged.Ws : 0;
        bool rep = Event.current.type == EventType.Repaint;
        KeyCode[] keys = { TRInput.Weapon1, TRInput.Weapon2, TRInput.Weapon3, TRInput.Weapon4, TRInput.Weapon5, TRInput.Weapon6, TRInput.Weapon7 };
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(707));
        for (int ws = 1; ws <= 7; ws++)
        {
            GUILayout.BeginVertical(GUILayout.Width(101));
            GUILayout.Label(KeyName(keys[ws - 1]), Skins.Bg("partWeaponNum"), GUILayout.Height(26));
            var w = p.WeaponInSlot(ws);
            var icon = w == null ? null : w.IsDefault ? (UIKit.Tex("icons/items/weapon/default/" + w.SName) ?? w.Icon) : w.Icon;
            GUILayout.Label(icon, Skins.Bg(dragWs == ws ? "itemLeft2Active" : "itemLeft2"), GUILayout.Width(101), GUILayout.Height(90));
            var r = Scr(GUILayoutUtility.GetLastRect());
            if (w != null) HoverUI.Hover(w, r);
            if (dragged == null && rep) weaponSlotRects[ws] = r;
            if (dragWs == ws && rep) draggedItemRect = weaponSlotRects[ws];
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    void TauntSlots()
    {
        var p = Profile.Current;
        bool rep = Event.current.type == EventType.Repaint;
        int target = dragged != null && dragged.Kind == ItemKind.Taunt ? System.Array.IndexOf(p.TauntSlots, 0) : -1;
        KeyCode[] keys = { TRInput.Taunt1, TRInput.Taunt2, TRInput.Taunt3 };
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(707));
        GUILayout.FlexibleSpace();
        for (int i = 0; i < 3; i++)
        {
            GUILayout.BeginVertical(GUILayout.Width(101));
            GUILayout.Label(KeyName(keys[i]), Skins.Bg("partWeaponNum"), GUILayout.Height(26));
            var t = p.TauntSlots[i] != 0 ? ItemDB.Get(ItemKind.Taunt, p.TauntSlots[i]) : null;
            GUILayout.Label(t?.Icon, Skins.Bg(target == i ? "itemLeft2Active" : "itemLeft2"), GUILayout.Width(101), GUILayout.Height(90));
            var r = Scr(GUILayoutUtility.GetLastRect());
            if (t != null) HoverUI.Hover(t, r);
            if (dragged == null && rep) tauntSlotRects[i] = r;
            if (target == i && rep) draggedItemRect = tauntSlotRects[i];
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- drag & drop (GUIShop / GUIInventory OnGUI tail), top-level screen coordinates ----------------
    GameItem dragged;                                                                 // Draged_item
    readonly Dictionary<int, GameItem> shopPreview = new Dictionary<int, GameItem>(); // GUIShop.preview (try-on set, starts empty)
    readonly Dictionary<int, Rect> viewRects = new Dictionary<int, Rect>();           // draged_view_* by wear slot
    readonly Rect[] weaponSlotRects = new Rect[8], tauntSlotRects = new Rect[3];
    Rect draggedItemRect, dropGlobalRect, gridRect;    // draged_item_rect, debug_drag_droped_rect_global, draged_items_scroll_rect
    readonly List<(GameItem it, Rect r)> gridHits = new List<(GameItem, Rect)>();     // inventoryHitEntries

    static Rect Scr(Rect r) => GUIUtility.GUIToScreenRect(r);

    GameItem GridItemAt(Vector2 m)
    {
        if (!gridRect.Contains(m)) return null;
        for (int i = gridHits.Count - 1; i >= 0; i--) if (gridHits[i].r.Contains(m)) return gridHits[i].it;
        return null;
    }

    GameItem SlotItemAt(Vector2 m, Dictionary<int, GameItem> view)
    {
        bool heads = HeadsSel;
        foreach (var wt in new[] { 9, 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            if (heads != (wt == 9)) continue;
            if (view.TryGetValue(wt, out var it) && it != null && viewRects.TryGetValue(wt, out var r) && r.Contains(m)) return it;
        }
        return null;
    }

    void HandleDrag()
    {
        var e = Event.current; var p = Profile.Current;
        bool shop = mainTab == 2 && shopTab == 0, hq = mainTab == 1 && hqTab <= 2;
        if ((!shop && !hq) || options.Open || NotificationWindow.IsShow || mapSelect) { dragged = null; return; }
        var m = e.mousePosition;
        if (hq && hqTab == 0 && e.type == EventType.MouseDown && e.button == 1)
        {
            var w = GridItemAt(m) ?? SlotItemAt(m, EquippedView());
            if (w != null && w.Kind == ItemKind.Wear && w.SetItems.Count > 0) { OpenAssemblage(w); e.Use(); return; }
        }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            var hit = GridItemAt(m);
            if (shop)
            {
                if (hit != null && hit.Kind == ItemKind.Wear) dragged = shopPreview.Values.Contains(hit) ? null : hit;
                var s = SlotItemAt(m, shopPreview); if (s != null) dragged = s;
            }
            else
            {
                if (hit != null && (hqTab == 0 && hit.Kind == ItemKind.Wear || hqTab == 1 && hit.Kind == ItemKind.Weapon || hqTab == 2 && hit.Kind == ItemKind.Taunt && !p.IsEquipped(hit))) dragged = hit;
                if (hqTab == 0) { var s = SlotItemAt(m, EquippedView()); if (s != null) dragged = s; }
                else if (hqTab == 1) { for (int ws = 1; ws <= 7; ws++) { var w = p.WeaponInSlot(ws); if (w != null && !w.IsDefault && weaponSlotRects[ws].Contains(m)) dragged = w; } }
                else for (int i = 0; i < 3; i++) if (p.TauntSlots[i] != 0 && tauntSlotRects[i].Contains(m)) dragged = ItemDB.Get(ItemKind.Taunt, p.TauntSlots[i]);
            }
        }
        if (dragged != null)
            GUI.Label(new Rect(m.x - 97 * 0.5f, m.y - 85 * 0.5f, 97, 85), dragged.Icon, GUIStyle.none);
        if (e.type != EventType.MouseUp || dragged == null) return;
        bool onTarget = draggedItemRect.Contains(m) || dropGlobalRect.Contains(m);
        if (shop)
        {
            if (onTarget && dragged.Kind == ItemKind.Wear) shopPreview[dragged.Wt] = dragged;                       // preview.DressUp
            else if (gridRect.Contains(m) && shopPreview.TryGetValue(dragged.Wt, out var pv) && pv == dragged) shopPreview.Remove(dragged.Wt);   // UnDress
        }
        else
        {
            if (onTarget && (dragged.Kind == ItemKind.Wear || dragged.Kind == ItemKind.Weapon)) p.Equip(dragged);    // View.DressUp / WeaponSlot.Set
            else if (gridRect.Contains(m))
            {
                if (dragged.Kind == ItemKind.Wear && p.IsEquipped(dragged)) p.Unequip(dragged);
                else if (dragged.Kind == ItemKind.Weapon) p.Unequip(dragged);                                         // back to the default weapon
            }
            if (dragged.Kind == ItemKind.Taunt)
            {
                int slot = System.Array.FindIndex(tauntSlotRects, r => r.Contains(m));
                if (slot >= 0) { int old = System.Array.IndexOf(p.TauntSlots, dragged.Id); if (old >= 0) p.TauntSlots[old] = 0; p.TauntSlots[slot] = dragged.Id; p.Save(); }
                else if (gridRect.Contains(m)) { int old = System.Array.IndexOf(p.TauntSlots, dragged.Id); if (old >= 0) { p.TauntSlots[old] = 0; p.Save(); } }
            }
        }
        dragged = null;
    }


    // ---------------- Тренировочный зал (GUIAbility) ----------------
    // GUIAbility: «Тренировочный зал», two 344x102 cards per row
    void Gym()
    {
        var p = Profile.Current;
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        GUILayout.BeginHorizontal();
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
        string title = LanguageManager.GetText("Practice hall");
        Skins.AutoLabel(title, Skins.Txt("partActive"));
        Skins.ShadowLabel(GUILayoutUtility.GetLastRect(), title, Skins.Txt("partActive"), Skins.Txt("partActiveShadow"));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(3);
        GUILayout.BeginHorizontal(); GUILayout.Space(2);
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.MinHeight(378));
        int num = 0, count = ItemDB.Gym.Count;
        foreach (var g in ItemDB.Gym)
        {
            if (num % 2 == 0) { GUILayout.Space(7); GUILayout.BeginHorizontal(); }
            GUILayout.Space(13);
            GymCard(g, p);
            num++;
            if (num % 2 == 0 || num >= count) { GUILayout.EndHorizontal(); GUILayout.Space(7); }
        }
        GUILayout.EndScrollView();
        GUILayout.Space(5); GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    void GymCard(GymSkill g, Profile p)
    {
        int lv = p.GymLevels.TryGetValue(g.Id, out var l) ? l : 0;
        bool bought = lv > 0, hasNext = lv < 5;
        GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("itemBlock"), GUILayout.Width(344), GUILayout.Height(102));
        GUILayout.Label(g.Icon, GUIStyle.none, GUILayout.Width(97), GUILayout.Height(85));
        GUILayout.Space(6);
        GUILayout.BeginVertical();
        GUILayout.BeginVertical(GUILayout.MinHeight(51));
        GUILayout.BeginHorizontal(); GUILayout.Label(g.Name, Skins.Txt("normal01")); GUILayout.EndHorizontal();
        GUILayout.Space(5);
        GUILayout.Label(LanguageManager.GetText("cur. lev: ") + (bought ? StepText(g, lv) : LanguageManager.GetText("no")), Skins.Txt("normal02"));
        GUILayout.Space(5);
        if (hasNext) GUILayout.Label(LanguageManager.GetText("Nxt. lev: ") + StepText(g, bought ? lv + 1 : 1), Skins.Txt("normal05"));
        GUILayout.EndVertical();
        GUILayout.BeginHorizontal(GUILayout.Height(33));
        GUILayout.Label(GUIContent.none, Skins.Ico("bg_ability_lvl" + (bought ? lv : 0)), GUILayout.Width(143), GUILayout.Height(19));
        GUILayout.Space(4);
        if (hasNext)
        {
            int price = g.Price1 * (lv + 1);   // only step 1 prices are known from New Era
            if (GUILayout.Button(price.ToString(), Skins.Btn("buyAbility"), GUILayout.Width(82), GUILayout.Height(33)))
            {
                if (p.Money < price) NotificationWindow.Add(Notify.MissingMoney(AddMoney));
                else { p.Money -= price; p.GymLevels[g.Id] = lv + 1; p.Save(); }
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    static string StepText(GymSkill g, int lv)
    {
        var m = System.Text.RegularExpressions.Regex.Match(g.Step, @"^([+-]?)(\d+)(.*)$");
        return m.Success ? m.Groups[1].Value + (int.Parse(m.Groups[2].Value) * lv) + m.Groups[3].Value : g.Step;
    }

    // GUIWorkshop: «Экипированное оружие» / «Все оружие»; only the base set here → the original empty text (no upgrades in this game)
    int workshopMenu = 1; Vector2 workshopScroll;
    public void DebugWorkshopAll(bool all) => workshopMenu = all ? 2 : 1;
    public void DebugRename(bool open) { if (open) OpenRename(); else NotificationWindow.Clear(); }
    void Workshop()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUIContent.none, Skins.Bg("winMain"), GUILayout.Width(755), GUILayout.Height(454));
        GUILayout.BeginHorizontal();
        foreach (var (n, key) in new[] { (1, "Equipped weapon"), (2, "All weapons") })
        {
            if (workshopMenu == n)
            {
                GUILayout.BeginHorizontal(GUIContent.none, Skins.Bg("partActive"));
                Skins.AutoLabel(LanguageManager.GetText(key), Skins.Txt("partActive"));
                GUILayout.EndHorizontal();
            }
            else if (Skins.AutoButton(LanguageManager.GetText(key), Skins.Txt("part"))) workshopMenu = n;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(GUIContent.none, Skins.Sep("black1Ver"), GUILayout.Height(1));
        GUILayout.Space(3);
        GUILayout.BeginHorizontal();
        GUILayout.Space(15);
        workshopScroll = GUILayout.BeginScrollView(workshopScroll, false, true, GUILayout.MinHeight(378));
        GUILayout.Label(LanguageManager.GetText("Currently you have only basic set of weapons which can't be improved.\\nTo buy new weapon you need to go to the Shop and then press Weapons tab"), Skins.Txt("noneBattle"), GUILayout.Height(378));
        GUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
    }

    // ---------------- Главная (GUIAction) / Досье ----------------
    // Packages column (langle + three 111x111 "action" frames spread by FlexibleSpace) and the 333x413 poster frame;
    // images are drawn inset (5 % / 6 of 320x400) with ScaleAndCrop, the poster rotates every 5 s (嚩.RotateOffset).
    int poster = 1; float posterAt;
    void Home()
    {
        if (Time.realtimeSinceStartup - posterAt > 5f) { posterAt = Time.realtimeSinceStartup; poster = poster % 3 + 1; }
        GUILayout.Space(46);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(ColW));
        GUILayout.BeginVertical(GUILayout.Height(413));
        GUILayout.Label("ВСЕ И СРАЗУ!", Skins.Bg("langle"), GUILayout.Height(33));
        for (int i = 1; i <= 3; i++)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(GUIContent.none, Skins.Bg("action"), GUILayout.Width(111), GUILayout.Height(111));
            var r = GUILayoutUtility.GetLastRect();
            var t = UIKit.Tex("promo/tile_" + i);
            if (t) { float nx = r.width * 5f / 100f, ny = r.height * 5f / 100f; GUI.DrawTexture(new Rect(r.x + nx, r.y + ny, r.width - nx * 2, r.height - ny * 2), t, ScaleMode.ScaleAndCrop, true); }
            if (GUI.Button(new Rect(r.x + 80, r.y + 7, 25, 25), GUIContent.none, Skins.Btn("btnZoom"))) { poster = i; posterAt = Time.realtimeSinceStartup + 15f; }
        }
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.Width(333), GUILayout.Height(454));
        GUILayout.Label(GUIContent.none, Skins.Bg("action"), GUILayout.Width(333), GUILayout.Height(413));
        var pr = GUILayoutUtility.GetLastRect();
        var pt = UIKit.Tex("promo/poster_" + poster);
        if (pt) { float nx = pr.width * 6f / 320f, ny = pr.height * 6f / 400f; GUI.DrawTexture(new Rect(pr.x + nx, pr.y + ny, pr.width - nx * 2, pr.height - ny * 2), pt, ScaleMode.ScaleAndCrop, true); }
        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ---------------- popups (NotificationWindow) ----------------
    void AddMoney() { Profile.Current.Money += 10000; Profile.Current.Save(); }   // «Пополнить»: local top-up

    /// <summary>GUIShop.OnBuy: not enough money → MISSING_MONEY, else the BUY_ITEM notification.</summary>
    void OnBuy(GameItem it)
    {
        int cheapest = it.Kind == ItemKind.Booster || it.Kind == ItemKind.Taunt ? new[] { it.Price1, it.Price7, it.Price30, it.Price }.FirstOrDefault(v => v > 0) : it.Price;
        if (cheapest > Profile.Current.Money) { NotificationWindow.Add(Notify.MissingMoney(AddMoney)); return; }
        var n = new Notify { Type = Notify.Kind.BUY_ITEM, Title = it.Name, Message = "Message", ButtonText = LanguageManager.GetText("Buy"), Item = it };
        n.OnClick = () =>
        {
            var res = Profile.Current.Buy(it, n.SelectedDays);
            if (res == Profile.BuyResult.NoMoney) NotificationWindow.Add(Notify.MissingMoney(AddMoney));
        };
        NotificationWindow.Add(n);
    }

    /// <summary>GUIInventory «Изменить имя»: SET_NAME, 500x343.</summary>
    void OpenRename()
    {
        NotificationWindow.UserName = ""; NotificationWindow.NameError = "";
        NotificationWindow.ChangeName = name =>
        {
            var p = Profile.Current; name = (name ?? "").Trim();
            if (name.Length < 3 || name.Length > 16) return LanguageManager.GetText("Name must contain 3-16 characters.");   // 嘽.嘼.USER_NAME_LEN
            if (p.Money < NotificationWindow.ChangeNameCost) return LanguageManager.GetText("Not enough money");
            p.Money -= NotificationWindow.ChangeNameCost; p.Name = name; p.Save(); return null;
        };
        NotificationWindow.Add(new Notify { Type = Notify.Kind.SET_NAME, Title = LanguageManager.GetText("CHANGE NAME"), WindowSize = new Vector2(500f, 343f) });
    }

    /// <summary>GUIInventory.OpenAssemblageContextMenu (right click on a set item).</summary>
    void OpenAssemblage(GameItem w)
    {
        var p = Profile.Current;
        bool any = w.SetItems.Select(x => ItemDB.Get(ItemKind.Wear, x)).Any(x => x != null && p.Owns(x) && p.IsEquipped(x));
        NotificationWindow.Add(new Notify { Type = Notify.Kind.NOTIFICATION, Title = $"Набор \"{w.SetName}\":", Message = any ? "Снять полный комплект?" : "Надеть полный комплект?", ButtonText = any ? "Снять" : "Надеть", Item = w });
    }

    // ---------------- map choice for "В БОЙ!" (local game: no servers) ----------------
    void MapSelect()
    {
        var r = new Rect(Screen.width / 2 - 380, 113, 760, 500);
        GUI.Window(14, r, id =>
        {
            GUILayout.Label("ВЫБРАТЬ КАРТУ", Skins.Txt("popupTitle"), GUILayout.Height(34));
            int i = 0;
            GUILayout.BeginHorizontal();
            foreach (var m in ItemDB.Maps.Where(x => GameFlow.MapAvailable(x.id)))
            {
                if (i > 0 && i % 5 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                GUILayout.BeginVertical(GUILayout.Width(140));
                var t = UIKit.Tex("icons/maps/" + m.id);
                if (GUILayout.Button(t != null ? new GUIContent(t) : new GUIContent(m.name), Skins.Bg("itemRight"), GUILayout.Width(136), GUILayout.Height(90))) { mapSelect = false; GameFlow.Instance.StartBattle(m.id); }
                GUILayout.Label(m.name, Skins.Txt("normal07"), GUILayout.Width(136));
                GUILayout.EndVertical();
                i++;
            }
            GUILayout.EndHorizontal();
            if (GUI.Button(new Rect(760 - 32, 2, 30, 30), GUIContent.none, Skins.Btn("popupClose"))) mapSelect = false;
        }, GUIContent.none, Skins.Bg("window"));
    }

    /// <summary>Soldier view: equipped wear; in the shop the try-on set is laid over it (SetPlayerViewAdditional).</summary>
    Dictionary<int, GameItem> OutfitWithTryOn()
    {
        var d = EquippedView();
        if (mainTab == 2) foreach (var kv in shopPreview) d[kv.Key] = kv.Value;
        return d;
    }
}
