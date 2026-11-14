// New Era NotificationWindow (decrypted client): a queue of popups drawn as GUI.Window(2) with the Backgound window style,
// default 360x230 (喦.windowSize). Title «popupTitle» 34 px, body by type, a button row (Цена / money / Купить, Сменить,
// assemblage Снять / Надеть, the button text, «Закрыть»), close cross 30x30 at (width − 32, 2).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Notify
{
    public enum Kind { NOTIFICATION, ERROR, BUY_ITEM, SET_NAME, CONNECTION_LOST, DAILY_BONUS }
    public Kind Type;
    public string Title = "", Message = "", ButtonText = "";
    public Action OnClick;
    public GameItem Item;
    public Vector2 WindowSize = new Vector2(360f, 230f);
    public bool CanClose;
    public int SelectedDays = -1;      // BUY_ITEM: 1 / 7 / 30, 0 = permanent

    /// <summary>嘽.嘼.MISSING_MONEY.AddNotification: «Невозможно купить» / «Недостаточно денег» / «Пополнить».</summary>
    public static Notify MissingMoney(Action addMore) => new Notify { Type = Kind.ERROR, Title = LanguageManager.GetText("Cannot purchase"), Message = LanguageManager.GetText("Not enough money"), ButtonText = LanguageManager.GetText("Add more"), OnClick = addMore };
}

public static class NotificationWindow
{
    static readonly List<Notify> list = new List<Notify>();
    public static bool IsShow => list.Count > 0;
    public static Notify Current => list.Count > 0 ? list[0] : null;
    public static void Add(Notify n) => list.Add(n);
    public static void Complete(Notify n) { list.Remove(n); HoverUI.Enabled = true; }
    public static void Clear() => list.Clear();

    // SET_NAME state (噮)
    public static string UserName = "", NameError = "";
    public static Func<string, string> ChangeName;   // returns an error text or null on success
    public static int ChangeNameCost = 1000;           // server conf.cst.cn (New Era capture: 1000)

    static Notify cur;

    public static void Draw()
    {
        if (list.Count == 0) return;
        var skin = GUI.skin;
        GUI.skin = Skins.Main;
        cur = list[0];
        HoverUI.Enabled = false;
        var r = (new Rect((Screen.width - cur.WindowSize.x) / 2f, (Screen.height - cur.WindowSize.y) / 2f, cur.WindowSize.x, cur.WindowSize.y));
        var bg = Skins.Get("Backgound");
        Skins.Window(2, r, DrawPopup, bg != null ? bg.window : GUI.skin.window);
        GUI.FocusWindow(2);
        if (cur.CanClose) GUI.Button(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        GUI.skin = skin;
    }

    static bool IsAssemblagePopup(Notify n) => n.Type == Notify.Kind.NOTIFICATION && n.Item != null && n.Item.Kind == ItemKind.Wear && n.Item.SetItems.Count > 0;

    static void DrawPopup(int id)
    {
        bool done = false; int price = 0;
        GUILayout.BeginHorizontal();
        GUILayout.Label(cur.Title, Skins.Txt("popupTitle"), GUILayout.Height(34));
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        GUILayout.BeginVertical();
        bool assemblage = IsAssemblagePopup(cur);
        if (assemblage)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(cur.Item.Icon, Skins.Bg("itemRight"), GUILayout.Width(97), GUILayout.Height(85));
            GUILayout.Space(10);
            GUILayout.BeginVertical(); GUILayout.Label(cur.Message, Skins.Lbl("notificationDesc")); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }
        else if (cur.Type == Notify.Kind.BUY_ITEM && cur.Item != null) price = DrawCCItem(cur);
        else if (cur.Type == Notify.Kind.SET_NAME) DrawSetName();
        else GUILayout.Label(cur.Message, Skins.Lbl("notificationDesc"));
        GUILayout.EndVertical();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (cur.Type == Notify.Kind.BUY_ITEM)
        {
            GUILayout.Label(LanguageManager.GetText("Price:"), Skins.Txt("normal16"), GUILayout.Height(36));
            GUILayout.Space(3);
            GUILayout.Label(GUIContent.none, Skins.Ico("money"), GUILayout.Width(36), GUILayout.Height(36));
            GUILayout.Space(5);
            GUILayout.Label(price.ToString(), Skins.Lbl("money"));
            GUILayout.Space(10);
            if (GUILayout.Button(cur.ButtonText, Skins.Btn("green"), GUILayout.MinWidth(106), GUILayout.Height(37))) { cur.OnClick?.Invoke(); done = true; }
            GUILayout.Space(5);
        }
        else if (assemblage)
        {
            var p = Profile.Current;
            var owned = cur.Item.SetItems.Select(x => ItemDB.Get(ItemKind.Wear, x)).Where(x => x != null && p.Owns(x)).ToList();
            if (GUILayout.Button("Снять", BattleUI.TeamColorButtonPublic(1), GUILayout.Width(106), GUILayout.Height(37))) { foreach (var w in owned) if (p.IsEquipped(w)) p.Unequip(w); done = true; }
            GUILayout.Space(5);
            if (GUILayout.Button("Надеть", BattleUI.TeamColorButtonPublic(2), GUILayout.Width(106), GUILayout.Height(37))) { foreach (var w in owned) p.Equip(w); done = true; }
            GUILayout.Space(5);
        }
        else if (!string.IsNullOrEmpty(cur.ButtonText))
        {
            if (GUILayout.Button(cur.ButtonText, Skins.Btn("green"), GUILayout.MinWidth(106), GUILayout.Height(37))) { cur.OnClick?.Invoke(); done = true; }
            GUILayout.Space(5);
        }
        if (cur.Type == Notify.Kind.SET_NAME)
        {
            GUILayout.Label(LanguageManager.GetText("Price:"), Skins.Txt("normal16"), GUILayout.Height(36));
            GUILayout.Space(3);
            GUILayout.Label(GUIContent.none, Skins.Ico("money"), GUILayout.Width(36), GUILayout.Height(36));
            GUILayout.Space(3);
            GUILayout.Label(ChangeNameCost.ToString(), Skins.Lbl("money"));
            GUILayout.Space(10);
            if (GUILayout.Button(LanguageManager.GetText("Change"), Skins.Btn("green"), GUILayout.MinWidth(106), GUILayout.Height(37)))
            {
                NameError = ChangeName != null ? ChangeName(UserName) ?? "" : "";
                if (NameError == "") done = true;
            }
            GUILayout.Space(5);
        }
        if (cur.Type != Notify.Kind.BUY_ITEM && cur.Type != Notify.Kind.CONNECTION_LOST && cur.Type != Notify.Kind.DAILY_BONUS && !cur.CanClose
            && GUILayout.Button(LanguageManager.GetText("Close"), Skins.Btn("cancel"), GUILayout.Width(106), GUILayout.Height(37))) done = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        if (!cur.CanClose && GUI.Button(new Rect(cur.WindowSize.x - 32f, 2f, 30f, 30f), GUIContent.none, Skins.Btn("popupClose"))) done = true;
        if (done) Complete(cur);
    }

    /// <summary>draw_CCItem: icon + rent grid (1 / 7 / 30 days / permanent) with prices, or the item description.</summary>
    static int DrawCCItem(Notify n)
    {
        var a = n.Item;
        GUILayout.BeginHorizontal();
        GUILayout.Label(a.Icon, Skins.Bg(a.Kind == ItemKind.Wear ? "itemRight" : "itemRight02"), GUILayout.Width(97), GUILayout.Height(85));
        GUILayout.Space(10);
        GUILayout.BeginVertical();
        bool rent = a.Price1 > 0;
        int price = a.Price;
        if (rent)
        {
            var prices = new List<string>(); var names = new List<string>(); var days = new List<int>();
            if (a.Price1 > 0) { prices.Add(a.Price1.ToString()); names.Add(LanguageManager.GetText("Cost 1 day")); days.Add(1); }
            if (a.Price7 > 0) { prices.Add(a.Price7.ToString()); names.Add(LanguageManager.GetText("Cost 7 day")); days.Add(7); }
            if (a.Price30 > 0) { prices.Add(a.Price30.ToString()); names.Add(LanguageManager.GetText("Cost 30 day")); days.Add(30); }
            if (a.Price > 0) { prices.Add(a.Price.ToString()); names.Add(LanguageManager.GetText("Cost permanent")); days.Add(0); }
            if (!days.Contains(n.SelectedDays)) n.SelectedDays = days[0];
            GUILayout.BeginHorizontal();
            int idx = GUILayout.SelectionGrid(days.IndexOf(n.SelectedDays), names.ToArray(), 1, Skins.Btn("selectGridRadio"), GUILayout.Width(140));
            n.SelectedDays = days[Mathf.Clamp(idx, 0, days.Count - 1)];
            GUILayout.FlexibleSpace();
            GUILayout.SelectionGrid(0, prices.ToArray(), 1, Skins.Btn("selectGridMoney"), GUILayout.Width(80));
            GUILayout.EndHorizontal();
            price = n.SelectedDays == 1 ? a.Price1 : n.SelectedDays == 7 ? a.Price7 : n.SelectedDays == 30 ? a.Price30 : a.Price;
        }
        else
        {
            n.SelectedDays = 0;
            GUILayout.Label(a.Description, Skins.Lbl("notificationDesc"));
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        return price;
    }

    /// <summary>draw_SetNamePopup.</summary>
    static void DrawSetName()
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.Label(LanguageManager.GetText("You current name:"), Skins.Txt("mapPropertie"), GUILayout.Height(29));
        GUILayout.Label(Profile.Current.Name, Skins.Txt("mapPropertie"), GUILayout.Height(29));
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.Label(LanguageManager.GetText("Please, enter the name which will be shown to other players."), Skins.Lbl("notificationDesc"));
        GUILayout.Space(15);
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.Label(LanguageManager.GetText("Name:"), Skins.Txt("mapPropertie"), GUILayout.Height(29));
        GUI.SetNextControlName("userNameInput");
        UserName = GUILayout.TextField(UserName, Skins.Main.textField, GUILayout.Height(32), GUILayout.MinWidth(226));
        GUI.FocusControl("userNameInput");
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        if (NameError != "")
        {
            GUILayout.Space(5);
            GUILayout.BeginHorizontal(GUILayout.Height(20)); GUILayout.FlexibleSpace();
            GUILayout.Label($"'{UserName}' - {NameError}", Skins.Txt("error01"));
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Label(LanguageManager.GetText("The name of the character should be created according to the rules:"), Skins.Txt("normal07"));
            GUILayout.Label(LanguageManager.GetText("- character’s name can contain from 3 to 16 characters"), Skins.Txt("normal07"));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        }
        GUILayout.FlexibleSpace();
    }
}
