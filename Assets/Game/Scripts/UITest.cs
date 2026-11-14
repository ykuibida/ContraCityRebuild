// ContraCity.exe -uitest : walks every lobby screen (tabs, sub-tabs, all shop categories with a tooltip),
// opens map selection, plays a short battle (HUD, Esc menu, options) and saves screenshots to uitest/.
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public class UITest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        if (!args.Contains("-uitest") && !args.Contains("-tooltips") && !args.Contains("-uicompare")) return;
        var go = new GameObject("UITest"); DontDestroyOnLoad(go); go.AddComponent<UITest>();
    }

    string dir; int n;

    IEnumerator Start()
    {
        dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "uitest");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        yield return new WaitForSeconds(1.5f);
        var ui = UIRoot.Instance; var L = ui.Lobby;
        if (System.Environment.GetCommandLineArgs().Contains("-tooltips")) { yield return Tooltips(L); Application.Quit(); yield break; }
        if (System.Environment.GetCommandLineArgs().Contains("-uicompare")) { yield return Compare(ui, L); Application.Quit(); yield break; }
        Profile.ResetProfile();
        L.mainTab = 0; yield return Shot("lobby_home");
        foreach (var (clip, t) in new[] { ("Idle", 0f), ("Idle", 0.5f), ("IdleLong", 0f), ("IdleLong", 0.3f), ("IdleLong", 0.6f) })
        { L.Preview.DebugPose(clip, t); yield return Shot($"pose_{clip}_{(int)(t * 100)}"); }
        L.Preview.DebugPose("IdleLong", 0f);
        // try-on in the shop: two boots one after another (the second must replace the first on the soldier)
        L.mainTab = 2; L.shopTab = 0; L.gearIdx = 5;
        var boots = ItemDB.ShopWears(6).Take(2).ToList();
        L.DebugTryOn(boots[0]); yield return Shot("tryon_boots_a");
        L.DebugTryOn(boots[1]); yield return Shot("tryon_boots_b");
        L.DebugTryOn(null);
        L.mainTab = 2; L.shopTab = 0;
        for (int g = 0; g < 9; g++) { L.gearIdx = g; L.DebugHover = ItemDB.ShopWears(GearWt(g)).FirstOrDefault(); yield return Shot($"shop_wear_{g + 1}"); }
        L.shopTab = 1;
        for (int w = 0; w < 7; w++) { L.weaponWs = w + 1; L.DebugHover = ItemDB.ShopWeapons(w + 1).FirstOrDefault(); yield return Shot($"shop_weapon_{w + 1}"); }
        L.shopTab = 2; L.DebugHover = ItemDB.Taunts.FirstOrDefault(); yield return Shot("shop_taunts");
        L.shopTab = 3; L.DebugHover = ItemDB.Boosters.FirstOrDefault(); yield return Shot("shop_boosters");
        L.DebugHover = null;
        // buy a few things and check the HQ
        var p = Profile.Current; p.Money = 999999; p.Level = 99;
        foreach (var it in ItemDB.Wears.Take(3)) p.Buy(it);
        p.Buy(ItemDB.Weapons[3]); p.Buy(ItemDB.Taunts[0], 1);
        L.mainTab = 1;
        for (int h = 0; h < 4; h++) { L.hqTab = h; yield return Shot($"hq_{h}"); }
        L.mainTab = 3; yield return Shot("dossier");
        ui.Options.Open = true; yield return Shot("options"); ui.Options.Open = false;
        // «Битвы» (GUIRoomList): server list → JOIN → battles, create (with the mode list open), maps per mode
        L.mainTab = 7; var BT = L.Battles;
        BT.Connected = false; yield return Shot("battles_server");
        BT.Connected = true; BT.Select = BattlesUI.Menu.RoomList; yield return Shot("battles_list");
        BT.Select = BattlesUI.Menu.CreateGame; yield return Shot("battles_create");
        BT.DebugOpenModeList(); yield return Shot("battles_create_mode_list"); BT.DebugCloseList();
        BT.Select = BattlesUI.Menu.Maps;
        foreach (var gm in new[] { GameMode.NONE, GameMode.DEATHMATCH, GameMode.CAPTURE_THE_FLAG, GameMode.ZOMBIE }) { BT.DebugMapsMode(gm); yield return Shot("battles_maps_" + gm); }
        // START from «Создание битвы»: TDM on the first map, loading popup over the HQ
        BT.Select = BattlesUI.Menu.CreateGame;
        var room = new RoomSettings(); room.Map = MapList.All.First(); room.Mode = GameMode.TEAM_DEATHMATCH;
        GameFlow.Instance.StartBattle(room);
        yield return null; yield return Shot("battle_loading");
        while (GameFlow.Instance.Current != GameFlow.State.Battle) yield return null;
        yield return new WaitForSeconds(2f);
        var B = ui.Battle;
        yield return Shot("battle_select_team");
        B.JoinSelectedBattleTeam(1); yield return new WaitForSeconds(1.2f);
        yield return Shot("battle_respawn_countdown");
        yield return new WaitForSeconds(7f);
        yield return Shot("battle_press_fire");
        B.RequestRespawn(); yield return new WaitForSeconds(1f);
        yield return Shot("battle_hud");
        if (ShotController.Instance) ShotController.Instance.SetWeapon(3); B.ShowWeapon(); yield return new WaitForSeconds(0.6f); yield return Shot("battle_weapon_bar");
        GameFlow.Instance.Player.GetComponent<PlayerVitals>().Kill(); yield return new WaitForSeconds(0.5f);
        yield return Shot("battle_dead_suicide");
        B.DebugShowScore = true; yield return Shot("battle_tab"); B.DebugShowScore = false;
        B.MenuOpen = true; yield return Shot("battle_esc_menu");
        ui.Options.Open = true; yield return Shot("battle_options"); ui.Options.Open = false; B.MenuOpen = false;
        GameFlow.Instance.ExitToLobby();
        yield return new WaitForSeconds(1.5f);
        yield return Shot("back_in_lobby");
        Profile.ResetProfile();
        File.WriteAllText(Path.Combine(dir, "done.txt"), $"screenshots: {n}");
        Application.Quit();
    }

    // Renders the tooltip of every item (level 1 profile, like a fresh New Era account) and saves crops to uitest/tooltips/.
    IEnumerator Tooltips(LobbyUI L)
    {
        var tdir = Path.Combine(dir, "tooltips"); Directory.CreateDirectory(tdir);
        Profile.ResetProfile();
        L.mainTab = 2;
        var all = ItemDB.Wears.Concat(ItemDB.Weapons).Concat(ItemDB.Boosters).Concat(ItemDB.Taunts).ToList();
        var index = new System.Text.StringBuilder("file;kind;id;name;sname\n");
        foreach (var it in all)
        {
            L.shopTab = it.Kind == ItemKind.Wear ? 0 : it.Kind == ItemKind.Weapon ? 1 : it.Kind == ItemKind.Taunt ? 2 : 3;
            L.DebugHover = it;
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var r = HoverUI.LastRect; float s = 1f;
            int x = Mathf.Clamp((int)(r.x * s) - 2, 0, tex.width - 1), w = Mathf.Min((int)(r.width * s) + 4, tex.width - x);
            int yTop = (int)(r.y * s) - 2, h = Mathf.Min((int)(r.height * s) + 4, tex.height - Mathf.Max(0, yTop));
            int y = Mathf.Clamp(tex.height - yTop - h, 0, tex.height - 1);
            var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
            crop.SetPixels(tex.GetPixels(x, y, w, h)); crop.Apply();
            string file = $"{it.Kind}_{it.Id}.png";
            File.WriteAllBytes(Path.Combine(tdir, file), crop.EncodeToPNG());
            index.Append($"{file};{it.Kind};{it.Id};{it.Name};{it.SName}\n");
            Destroy(tex); Destroy(crop);
        }
        L.DebugHover = null;
        File.WriteAllText(Path.Combine(tdir, "index.csv"), index.ToString(), new System.Text.UTF8Encoding(true));
    }

    // -uicompare: the exact states of the New Era reference screenshots (fresh account: level 1, 15010, "Fallen666";
    // the player's New Era options: sensitivity 3.00, FOV 55, quality "Хорошее"). Files: uitest/compare_cases/<case>.png,
    // compared by Tools/uidiff/uidiff.py. Options are changed in memory only (never saved to PlayerPrefs).
    IEnumerator Compare(UIRoot ui, LobbyUI L)
    {
        var cdir = Path.Combine(dir, "compare_cases"); Directory.CreateDirectory(cdir);
        foreach (var f in Directory.GetFiles(cdir, "*.png")) File.Delete(f);
        Profile.ResetProfile();
        var p = Profile.Current; p.Name = "Fallen666"; p.Money = 15010; p.Level = 1;
        GameOptions.Sensivity = 3f; GameOptions.AimSensitivity = 3f; GameOptions.InvertMouseY = false; GameOptions.MouseMode = CameraOrbitMode.Neutral;
        GameOptions.FpsFieldOfView = 55f; OptionsWindow.Quality = 3; OptionsWindow.VSync = true; OptionsWindow.BattleChat = true;
        IEnumerator C(string name) { yield return null; yield return null; yield return new WaitForEndOfFrame(); var t = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(cdir, name + ".png"), t.EncodeToPNG()); Destroy(t); }
        L.mainTab = 0; yield return new WaitForSeconds(1f); yield return C("home");
        L.mainTab = 1; L.hqTab = 0; yield return C("hq_look");
        L.hqTab = 1; yield return C("hq_weapons");
        L.hqTab = 2; yield return C("hq_taunts");
        L.hqTab = 3; yield return C("hq_gym");
        L.hqTab = 4; yield return C("hq_workshop");
        L.DebugWorkshopAll(true); yield return C("hq_workshop_all"); L.DebugWorkshopAll(false);
        L.DebugRename(true); yield return C("hq_rename"); L.DebugRename(false);
        L.mainTab = 2; L.shopTab = 0; L.gearIdx = 0; yield return C("shop_hats");
        L.mainTab = 3;   // Досье (GUIStatistic)
        L.dossierTab = 0; yield return C("dossier_achievements");
        L.dossierTab = 1; L.statMenu = 1; yield return C("dossier_stats");
        L.dossierTab = 2; L.statWeaponWs = 1; yield return C("dossier_weapons");
        L.dossierTab = 3; yield return C("dossier_rating");
        L.mainTab = 1; L.hqTab = 4;
        // audio values of the New Era capture (ne_ui/093_settings_audio): master 5, music 0, effects 50, mic off, players 0 — in memory only
        GameOptions.SoundVolumeMaster = 0.05f; GameOptions.SoundVolumeMusic = 0f; GameOptions.SoundVolumeEffect = 0.5f; OptionsWindow.Microphone = false; OptionsWindow.PlayersVolume = 0f;
        string[] st = { "settings_controls", "settings_video", "settings_audio", "settings_aim" };
        for (int i = 0; i < 4; i++) { ui.Options.Open = true; ui.Options.Tab = i; yield return C(st[i]); }
        ui.Options.Open = false;
        L.mainTab = 7; var BT = L.Battles;
        BT.Connected = false; yield return C("battles_server");
        BT.Connected = true; BT.Select = BattlesUI.Menu.RoomList; yield return C("battles_list");
        BT.Select = BattlesUI.Menu.CreateGame; yield return C("battles_create");
        BT.DebugOpenModeList(); yield return C("battles_create_mode_dd"); BT.DebugCloseList();
        BT.Select = BattlesUI.Menu.Maps;
        foreach (var (gm, n) in new[] { (GameMode.DEATHMATCH, "dm"), (GameMode.TEAM_DEATHMATCH, "tdm"), (GameMode.CAPTURE_THE_FLAG, "ctf"), (GameMode.CONTROL_POINTS, "cp"), (GameMode.ZOMBIE, "zm") })
        { BT.DebugMapsMode(gm); yield return C("battles_maps_" + n); }
        // battle on «Комиссариат» TDM, as in the New Era captures
        var room = new RoomSettings(); room.Map = MapList.Get("Bit_map") ?? MapList.All.First(); room.Mode = GameMode.TEAM_DEATHMATCH;
        GameFlow.Instance.StartBattle(room);
        yield return null; yield return C("loading");
        while (GameFlow.Instance.Current != GameFlow.State.Battle) yield return null;
        yield return new WaitForSeconds(1.5f);
        var B = ui.Battle;
        yield return C("battle_select_team");
        B.JoinSelectedBattleTeam(1); yield return new WaitForSeconds(7.5f);
        yield return C("battle_dead"); File.Copy(Path.Combine(cdir, "battle_dead.png"), Path.Combine(cdir, "battle_respawn_strip.png"), true);
        // default outfit (as in the New Era capture 113_rifle_hud) with the AK, and the scene lighting as the build sees it
        { var sp = RenderSettings.ambientProbe; var sun = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional).ToArray();
          Debug.Log($"[uicompare] lighting ambientMode {RenderSettings.ambientMode} ambientLight {RenderSettings.ambientLight} intensity {RenderSettings.ambientIntensity} probeDC ({sp[0, 0]:F3},{sp[1, 0]:F3},{sp[2, 0]:F3}) fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance} lightmaps {LightmapSettings.lightmaps.Length} suns {sun.Length} {string.Join(",", sun.Select(l => l.name + " " + l.intensity + " " + l.color + " " + l.transform.eulerAngles))} sky {(RenderSettings.skybox ? RenderSettings.skybox.shader.name : "-")}");
          B.RequestRespawn(); yield return new WaitForSeconds(1f);
          if (ShotController.Instance) ShotController.Instance.SetWeapon(3);
          yield return new WaitForSeconds(1.5f); yield return C("weapon_default_ak");
          GameFlow.Instance.Player.GetComponent<PlayerVitals>().Kill(); yield return new WaitForSeconds(7.5f); }
        // the New Era "1test weapon" models: Вымпел (SR_Vintorez), Скиф (HG_USP), Бастион (GG_FNMAG) must be visible
        { var pp = Profile.Current;
          foreach (var id in new[] { 107, 18, 20 }) { var it = ItemDB.Get(ItemKind.Weapon, id); if (it != null) { pp.OwnedWeapons.Add(id); pp.Equip(it); } }
          B.RequestRespawn(); yield return new WaitForSeconds(0.3f);
          var shm = ShotController.Instance;
          foreach (var ws in new[] { 7, 2, 4 })
          {
              shm.SetWeapon(ws); yield return new WaitForSeconds(0.7f);
              var w = shm.CurrentWeapon; int vis = 0; string names = "";
              if (w.Transform) foreach (var r in w.Transform.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) { vis++; if (names.Length < 160) names += r.name + "[" + (r.sharedMaterial ? r.sharedMaterial.shader.name : "no material") + "] "; }
              Debug.Log($"[uicompare] logic model {w.SystemName}: {vis} visible renderers: {names}");
              yield return C("model_" + w.SystemName);
          }
          foreach (var id in new[] { 107, 18, 20 }) { var it = ItemDB.Get(ItemKind.Weapon, id); if (it != null) pp.Unequip(it); }
          GameFlow.Instance.Player.GetComponent<PlayerVitals>().Kill(); yield return new WaitForSeconds(7.5f); }
        // first-person arms (ArmClothModel.Sync + SetHandTextureID): shirt / glove pairs, the AK in hand
        { var pp = Profile.Current;
          var sets = new (string name, int shirt, int glove)[] {
              ("fp_shirt_trooper", 1416, 0),          // Тельник: own sleeve mesh, default hands
              ("fp_glove_tactical01", 0, 401),        // Кожчатки: no glove mesh → glove texture on the base hands
              ("fp_raven_tactical01", 1414, 401),     // Броня «Ворон» + Кожчатки (the test outfit)
              ("fp_stalker", 240032, 230006),         // Разрушитель + Нитриловые перчи: own glove mesh
              ("fp_mummy2", 240148, 230148),          // Пелены Фараона + Кисти Анубиса (model_sname mummy2_3 / mummy2_4)
              ("fp_business", 240002, 230001),        // Сюменовский + Перчатки Гудини
              ("fp_glove_tactical01_2", 0, 401),
              ("fp_no_glove_after_glove", 1416, 0) }; // bare hands again after a gloved life
          foreach (var s in sets)
          {
              foreach (var wt in new[] { 3, 4 }) { var cur = pp.WearInSlot(wt); if (cur != null) pp.Unequip(cur); }
              foreach (var id in new[] { s.shirt, s.glove }) { if (id == 0) continue; var it = ItemDB.Get(ItemKind.Wear, id); if (it != null) { pp.OwnedWears.Add(id); pp.Equip(it); } }
              B.RequestRespawn(); yield return new WaitForSeconds(0.3f);
              if (ShotController.Instance) ShotController.Instance.SetWeapon(3);
              yield return new WaitForSeconds(1.2f);
              var shc = ShotController.Instance; string shown = "";
              if (shc && shc.Container) foreach (var r in shc.Container.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy && (r.name.StartsWith("Shirts_") || r.name.StartsWith("Gloves_"))) shown += r.name + "[" + (r.sharedMaterial && r.sharedMaterial.mainTexture ? r.sharedMaterial.mainTexture.name : "-") + "] ";
              Debug.Log($"[uicompare] logic fp arms {s.name}: {shown}");
              yield return C(s.name);
              GameFlow.Instance.Player.GetComponent<PlayerVitals>().Kill(); yield return new WaitForSeconds(7.5f);
          }
          foreach (var wt in new[] { 3, 4 }) { var cur = pp.WearInSlot(wt); if (cur != null) pp.Unequip(cur); } }
        // test outfit (only from here on: the lobby comparisons above need the default fighter)
        // full «Ворон» set (set bonus +15% shotgun defence, +5% health) + gym Здоровяк 2 / Шмотник 1 / Бегун 3 + «Легкое Приземление»
        // expected BattleStats: HP 120 + 5% = 126, armor 10 + 15 = 25, speed ×1.06, fall ×0.5
        { var pp = Profile.Current;
          foreach (var id in new[] { 620, 364, 401, 1414, 500, 1609 }) { var it = ItemDB.Get(ItemKind.Wear, id); if (it != null) { pp.OwnedWears.Add(id); pp.Equip(it); } }
          foreach (var (n, l) in new[] { ("Здоровяк", 2), ("Шмотник", 1), ("Бегун", 3) }) { var g = ItemDB.Gym.FirstOrDefault(x => x.Name == n); if (g != null) pp.GymLevels[g.Id] = l; }
          pp.BoosterUntil[3] = Profile.Now + 3600;
          Debug.Log("[uicompare] stats " + BattleStats.Compute(pp)); }
        B.RequestRespawn(); if (ShotController.Instance) ShotController.Instance.SetWeapon(3); yield return new WaitForSeconds(2.5f);
        yield return C("battle_hud");
        // ---- weapons stage 1: battle weapons built from the profile, switching 1..7 / Q / wheel, visible model, container animation ----
        { var shc = ShotController.Instance;
          if (shc == null || shc.CurrentWeapon == null) Debug.LogWarning("[uicompare] logic weapons: no ShotController / weapon");
          else
          {
              foreach (var w in shc.Weapons)
                  if (w != null) Debug.Log($"[uicompare] logic weapon slot {w.GetIndex()} {w.SystemName} type {w.Type} clip {w.LoadedAmmo}/{w.MaxLoadedAmmo} reserve {w.AmmoReserve} shot {w.ShotTime / 10000} ms reload {w.ReloadTime / 10000} ms single {w.ReloadSingleTime / 10000} ms dist {w.Distance} ang {w.Angle} dev {w.Deviation} node {(w.Transform ? w.Transform.name + "/" + string.Join(",", w.Transform.Cast<Transform>().Select(c => c.name)) : "-")}");
              for (int n = 1; n <= 7; n++)
              {
                  shc.SetWeapon(n);
                  yield return null;
                  var cw = shc.CurrentWeapon;
                  var cont = shc.WeaponCamera ? shc.WeaponCamera.transform.parent.Find("Weapon") : null;
                  var an = cont ? cont.GetComponent<Animation>() : null;
                  int shown = 0; string shownNames = "";
                  if (cont) foreach (var r in cont.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) { shown++; if (shownNames.Length < 200) shownNames += r.name + " "; }
                  Debug.Log($"[uicompare] logic switch {n}: current {cw?.GetIndex()} {cw?.SystemName} mode {cw?.WeaponMode} change anim {(an && an.IsPlaying("WeaponChangeAnimation"))} renderers {shown}: {shownNames}");
                  yield return new WaitForSeconds(0.55f);
                  cw.CalcMode();
                  Debug.Log($"[uicompare] logic switch {n} after 0.55 s: mode {cw.WeaponMode}");
                  yield return C("weapon_slot" + n);
              }
              shc.SetWeapon(3); yield return new WaitForSeconds(0.6f);
              shc.SetWeapon(5); yield return new WaitForSeconds(0.6f);
              var q = typeof(ShotController).GetMethod("QuickChange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
              q.Invoke(shc, null); yield return null;
              Debug.Log($"[uicompare] logic quick change 3 → 5 → Q: current {shc.CurrentWeapon.GetIndex()}");
              var nx = typeof(ShotController).GetMethod("SetNextWeapon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
              shc.SetWeapon(7); yield return null; nx.Invoke(shc, null); yield return null;
              Debug.Log($"[uicompare] logic wheel next after 7: current {shc.CurrentWeapon.GetIndex()}");
              shc.SetWeapon(3); yield return new WaitForSeconds(0.6f);
              // ---- weapons stage 2: firing at the ground, effects, ammo, auto-reload, manual reload, sniper zoom ----
              {
                  var look = GameFlow.Instance.Player.GetComponent<SoldierCamera>();
                  if (look) look.SetPitch(25f);
                  shc.SetWeapon(3); yield return new WaitForSeconds(0.6f);
                  var ak = shc.CurrentWeapon;
                  string Fx()
                  {
                      int sparks = 0, flashes = 0, tracers = 0, ps = 0, psAlive = 0;
                      foreach (var g in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                      {
                          if (g.name.StartsWith("MachineGunSpark(")) sparks++;
                          if (g.name.StartsWith("MachineGunShaftSpark(")) flashes++;
                          if (g.name.StartsWith("MachineGun(")) tracers++;
                      }
                      foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) { ps++; if (p.particleCount > 0) psAlive++; }
                      return $"sparks {sparks} flashes {flashes} tracers {tracers} particleSystems {ps} withParticles {psAlive}";
                  }
                  var akAnim = ak.Transform ? ak.Transform.GetComponentInChildren<Animation>() : null;
                  int before = ak.LoadedAmmo;
                  ShotController.TestFire = true;
                  yield return new WaitForSeconds(0.35f);
                  string playing = akAnim ? (akAnim.IsPlaying("Shot") ? "Shot" : akAnim.isPlaying ? "other" : "none") : "-";
                  var ch = BattleUI.CrossHairs ? BattleUI.CrossHairs.Find("CrossHair") : null;
                  var chr = ch ? ch.GetComponent<Renderer>() : null;
                  Debug.Log($"[uicompare] logic fire AK: ammo {before} → {ak.LoadedAmmo} in 0.35 s (shot time {ak.ShotTime / 10000} ms), clip {playing}, {Fx()}, audio playing {SoundManager.LocalPlayerAudio && SoundManager.LocalPlayerAudio.isPlaying}, crosshair {(chr ? chr.material.mainTexture?.name + " " + chr.material.color + " active " + ch.gameObject.activeInHierarchy : "-")}");
                  yield return C("fire_ak");
                  for (float until = Time.time + 8f; ak.LoadedAmmo > 0 && Time.time < until;) yield return null;
                  ShotController.TestFire = false;
                  yield return null; yield return null;
                  Debug.Log($"[uicompare] logic AK empty: mode {ak.WeaponMode}, reserve {ak.AmmoReserve}, clip {(akAnim && akAnim.IsPlaying("Reload") ? "Reload" : "-")}, crosshair {(chr ? chr.material.color.ToString() : "-")}");
                  yield return C("reload_ak");
                  yield return new WaitForSeconds(ak.ReloadTime / 10000000f + 0.2f);
                  ak.CalcMode();
                  Debug.Log($"[uicompare] logic AK after reload: {ak.LoadedAmmo}/{ak.AmmoReserve} mode {ak.WeaponMode}");
                  ShotController.TestFire = true; yield return new WaitForSeconds(0.25f); ShotController.TestFire = false;
                  yield return new WaitForSeconds(0.2f);
                  var rl = typeof(ShotController).GetMethod("Reload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  int partial = ak.LoadedAmmo;
                  rl.Invoke(shc, new object[] { ak });
                  Debug.Log($"[uicompare] logic AK manual reload at {partial}: mode {ak.WeaponMode}, pending fill {ak.MaxLoadedAmmo - partial}");
                  yield return new WaitForSeconds(ak.ReloadTime / 10000000f + 0.2f); ak.CalcMode();
                  Debug.Log($"[uicompare] logic AK after manual reload: {ak.LoadedAmmo}/{ak.AmmoReserve}");
                  // pistol: one round per shot time
                  shc.SetWeapon(2); yield return new WaitForSeconds(0.6f);
                  var pm = shc.CurrentWeapon; int pb = pm.LoadedAmmo;
                  ShotController.TestFire = true; yield return new WaitForSeconds(0.6f); ShotController.TestFire = false;
                  Debug.Log($"[uicompare] logic fire Makarov: {pb} → {pm.LoadedAmmo} in 0.6 s (shot time {pm.ShotTime / 10000} ms)");
                  yield return new WaitForSeconds(0.3f);
                  // sniper zoom (FPSCamera.Zoom via the Zoom key)
                  shc.SetWeapon(7); yield return new WaitForSeconds(0.6f);
                  var fc = GameFlow.Instance.Player.GetComponentInChildren<FPSCamera>();
                  var zm = typeof(FPSCamera).GetMethod("Zoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  zm.Invoke(fc, new object[] { true }); fc.isZooming = true;
                  yield return new WaitForSeconds(0.3f);
                  Debug.Log($"[uicompare] logic SVD zoom: factor {fc.zoomFactor} eye FOV {fc.GetComponent<Camera>().fieldOfView:F2} weapon camera active {shc.WeaponCamera.gameObject.activeSelf} crosshair active {(BattleUI.CrossHairs ? BattleUI.CrossHairs.gameObject.activeSelf : false)}");
                  yield return C("zoom_svd");
                  ShotController.TestFire = true; yield return new WaitForSeconds(0.1f); ShotController.TestFire = false;
                  yield return new WaitForSeconds(0.3f);
                  Debug.Log($"[uicompare] logic SVD after the shot: ammo {shc.CurrentWeapon.LoadedAmmo} mode {shc.CurrentWeapon.WeaponMode} zoom {shc.Zoom}");
                  zm.Invoke(fc, new object[] { false }); fc.isZooming = false;
                  // minigun: spin-up (Launch), fire, release (Stop ends the loop), then the AK must fire
                  shc.SetWeapon(4); yield return new WaitForSeconds(0.6f);
                  var gg = shc.CurrentWeapon; int gb = gg.LoadedAmmo;
                  var src = SoundManager.LocalPlayerAudio;
                  ShotController.TestFire = true; yield return new WaitForSeconds(0.3f);
                  var ggAnim = gg.Transform ? gg.Transform.GetComponentInChildren<Animation>() : null;
                  Debug.Log($"[uicompare] logic GG spin-up: mode {gg.WeaponMode} ammo {gg.LoadedAmmo}/{gb} clip {(ggAnim ? (ggAnim.IsPlaying("Start") ? "Start" : ggAnim.IsPlaying("Load") ? "Load" : ggAnim.isPlaying ? "other" : "none") : "-")}");
                  yield return new WaitForSeconds(2.0f);
                  Debug.Log($"[uicompare] logic GG firing: mode {gg.WeaponMode} ammo {gb} → {gg.LoadedAmmo}, source clip {(src && src.clip ? src.clip.name : "-")} loop {src && src.loop}");
                  ShotController.TestFire = false; yield return new WaitForSeconds(0.4f);
                  Debug.Log($"[uicompare] logic GG released: mode {gg.WeaponMode}, source clip {(src && src.clip ? src.clip.name : "-")} playing {src && src.isPlaying}, clip {(ggAnim && ggAnim.IsPlaying("End") ? "End" : "-")}");
                  shc.SetWeapon(3); yield return new WaitForSeconds(0.7f);
                  var ak2 = shc.CurrentWeapon; int ab = ak2.LoadedAmmo;
                  ShotController.TestFire = true; yield return new WaitForSeconds(0.4f); ShotController.TestFire = false;
                  Debug.Log($"[uicompare] logic AK after the minigun: {ab} → {ak2.LoadedAmmo}, source clip {(src && src.clip ? src.clip.name : "-")}");
                  // ---- weapons stage 3: bat, shotgun, RPG (explosion, self damage, knockback), «Ворчун», crossbow ----
                  {
                      int Count(string prefix) { int n = 0; foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (tr.name.StartsWith(prefix)) n++; return n; }
                      var vit3 = GameFlow.Instance.Player.GetComponent<PlayerVitals>();
                      var mot3 = GameFlow.Instance.Player.GetComponent<CharacterMotor>();
                      // bat: the ground within 8 → spark + "Bat_Shot_Stone" after 0.2 s
                      if (look) look.SetPitch(60f);
                      shc.SetWeapon(1); yield return new WaitForSeconds(0.6f);
                      ShotController.TestFire = true; yield return new WaitForSeconds(0.1f); ShotController.TestFire = false;
                      var batAnim = shc.CurrentWeapon.Transform ? shc.CurrentWeapon.Transform.GetComponentInChildren<Animation>() : null;
                      Debug.Log($"[uicompare] logic bat swing: clip {(batAnim && batAnim.IsPlaying("Shot") ? "Shot" : "-")} mode {shc.CurrentWeapon.WeaponMode}");
                      yield return new WaitForSeconds(0.25f);
                      Debug.Log($"[uicompare] logic bat hit: sparks {Count("BatSparkConcrete")} ");
                      yield return C("melee_bat");
                      yield return new WaitForSeconds(0.6f);
                      // shotgun: 6 visual pellets
                      if (look) look.SetPitch(25f);
                      shc.SetWeapon(5); yield return new WaitForSeconds(0.6f);
                      var sg = shc.CurrentWeapon; int sgb = sg.LoadedAmmo;
                      ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                      Debug.Log($"[uicompare] logic shotgun: ammo {sgb} → {sg.LoadedAmmo}, sparks {Count("MachineGunSpark")}, tracers {Count("MachineGun(") + Count("MachineGun_11(")}");
                      yield return C("fire_shotgun");
                      yield return new WaitForSeconds(1.2f);
                      // RPG: the rocket, the explosion at the landing time, self damage within 13.5, the knockback
                      shc.SetWeapon(6); if (look) look.SetPitch(80f); yield return new WaitForSeconds(0.6f);
                      var rpg = shc.CurrentWeapon; int rlb = rpg.LoadedAmmo; int hp0 = vit3.Health, ar0 = vit3.Armor;
                      ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                      Debug.Log($"[uicompare] logic RPG fired: ammo {rlb} → {rpg.LoadedAmmo}, rockets {Count("rocket")}, items {shc.RegisteredItems.Count}");
                      yield return new WaitForSeconds(0.25f);
                      yield return C("fire_rpg_flight");
                      float until = Time.time + 3f; while (shc.RegisteredItems.Count > 0 && Time.time < until) yield return null;
                      float exd = -1f; foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (tr.name.StartsWith("Explo")) exd = Vector3.Distance(tr.position, shc.transform.position);
                      Vector3 force = mot3.explosionForce; float peak = 0f; Vector3 pos0 = mot3.transform.position, peakV = Vector3.zero;
                      for (float t0 = Time.time; Time.time < t0 + 0.5f; ) { yield return null; if (mot3.movement.velocity.magnitude > peak) { peak = mot3.movement.velocity.magnitude; peakV = mot3.movement.velocity; } }
                      Debug.Log($"[uicompare] logic RPG blow: explosions {Count("Explo")}, distance {exd:F2}, health {hp0} → {vit3.Health}, armor {ar0} → {vit3.Armor}, force {force}, peak velocity {peakV}, moved {Vector3.Distance(pos0, mot3.transform.position):F2}");
                      yield return C("fire_rpg_blow");
                      yield return new WaitForSeconds(2f);
                      // «Ворчун» and «Шершень» (crossbow bolt): equip them for the test
                      var pp3 = Profile.Current;
                      foreach (var id in new[] { 104, 75 }) { var it = ItemDB.Get(ItemKind.Weapon, id); if (it != null) { pp3.OwnedWeapons.Add(id); pp3.Equip(it); } }
                      B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                      var shg = ShotController.Instance; look = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (look) look.SetPitch(10f);
                      shg.SetWeapon(6); yield return new WaitForSeconds(0.6f);
                      var gl = shg.CurrentWeapon; int glb = gl.LoadedAmmo;
                      ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                      Debug.Log($"[uicompare] logic {gl.SystemName}: ammo {glb} → {gl.LoadedAmmo}, grenades {Count("GrenadePrefab")}, items {shg.RegisteredItems.Count}");
                      yield return new WaitForSeconds(0.4f);
                      yield return C("fire_vorchun");
                      until = Time.time + 4f; while (shg.RegisteredItems.Count > 0 && Time.time < until) yield return null;
                      Debug.Log($"[uicompare] logic {gl.SystemName} blow: explosions {Count("Explo")}");
                      shg.SetWeapon(7); yield return new WaitForSeconds(0.6f);
                      var cb = shg.CurrentWeapon; int cbb = cb.LoadedAmmo;
                      ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                      yield return new WaitForSeconds(0.3f);
                      Debug.Log($"[uicompare] logic {cb.SystemName}: ammo {cbb} → {cb.LoadedAmmo}, bolts {Count("CrossbowBoltPrefab")}");
                      yield return C("fire_crossbow");
                      // the owner's report: every grenade-launcher blast leaves the map darker — 4 blasts, then everything should have expired
                      {
                          float Lum() { var tx = ScreenCapture.CaptureScreenshotAsTexture(); var px = tx.GetPixels32(); double s = 0; for (int i = 0; i < px.Length; i += 97) s += px[i].r + px[i].g + px[i].b; Destroy(tx); return (float)(s / (px.Length / 97) / 3); }
                          string Alive() => $"projectors {FindObjectsByType<Projector>(FindObjectsSortMode.None).Length}, particle systems {FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length}, lights {FindObjectsByType<Light>(FindObjectsSortMode.None).Length}, renderers {FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length}, ambient {RenderSettings.ambientLight} fog {RenderSettings.fog} {RenderSettings.fogColor} {RenderSettings.fogDensity}";
                          if (look) look.SetPitch(0f);
                          shg.SetWeapon(6); yield return new WaitForSeconds(0.8f);
                          yield return new WaitForEndOfFrame(); float l0 = Lum(); string a0 = Alive();
                          yield return C("gl_dark_before");
                          for (int k = 0; k < 4; k++)
                          {

                              ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                              yield return new WaitForSeconds(2.5f);
                              yield return new WaitForEndOfFrame(); Debug.Log($"[uicompare] logic GL dark {k}: lum {Lum():F1}, {Alive()}");
                              if (k == 1) yield return C("gl_dark_during");
                          }
                          yield return new WaitForSeconds(6f);
                          yield return new WaitForEndOfFrame();
                          Debug.Log($"[uicompare] logic GL dark: before lum {l0:F1} ({a0}); after lum {Lum():F1} ({Alive()})");
                          yield return C("gl_dark_after");
                          // controlled: the explosion prefab 12 ahead on the floor, particles off; facing up (rocket: the hit normal), then
                          // facing the player (grenade: the flight direction) — only a local scorch may darken
                          var pl = GameFlow.Instance.Player.transform; var fw = Vector3.ProjectOnPlane(pl.forward, Vector3.up).normalized;
                          if (look) look.SetPitch(20f);
                          yield return new WaitForSeconds(0.3f); yield return new WaitForEndOfFrame(); float lb = Lum(); yield return C("scorch_none");
                          foreach (var dir in new[] { Vector3.up, fw })
                          {
                              Vector3 at = pl.position + fw * 12f + Vector3.up * 3f;
                              if (Physics.Raycast(at, Vector3.down, out var hh, 20f)) at = hh.point;
                              var ex = Instantiate(EffectManager.Instance.exploPrefab, at, Quaternion.LookRotation(dir));
                              foreach (var ps in ex.GetComponentsInChildren<ParticleSystem>()) ps.gameObject.SetActive(false);
                              foreach (var au in ex.GetComponentsInChildren<AudioSource>()) au.enabled = false;
                              yield return new WaitForSeconds(1.5f); yield return new WaitForEndOfFrame();
                              var pj = ex.GetComponentInChildren<Projector>();
                              if (pj && pj.material) { var ck = pj.material.GetTexture("_ShadowTex"); var fo = pj.material.GetTexture("_FalloffTex");
                                  Debug.Log($"[uicompare] logic scorch textures: cookie {(ck ? ck.name + " " + ck.wrapModeU + "/" + ck.wrapModeV + " mips " + ck.mipmapCount : "-")}, falloff {(fo ? fo.name + " " + fo.wrapModeU + "/" + fo.wrapModeV + " mips " + fo.mipmapCount : "-")}"); }
                              Debug.Log($"[uicompare] logic scorch {(dir == Vector3.up ? "up" : "flight")}: lum {lb:F1} → {Lum():F1}, projector at {(pj ? pj.transform.position.ToString() : "-")} far {(pj ? pj.farClipPlane : 0):F1} shader {(pj && pj.material ? pj.material.shader.name : "-")}, player {pl.position}");
                              yield return C(dir == Vector3.up ? "scorch_up" : "scorch_flight");
                              Destroy(ex); yield return new WaitForSeconds(0.2f);
                          }
                      }
                      // rebuild weapons (Resources/data/rebuild_items.json): in the shop, equip, fire
                      Debug.Log($"[uicompare] logic rebuild weapons: {string.Join(", ", ItemDB.RebuildWeapons.Select(x => x.Id + " " + x.SName + " ws" + x.Ws))}; shop ws3 has Вождь {ItemDB.ShopWeapons(3).Any(x => x.Id == 73)}, ws6 {string.Join("/", ItemDB.ShopWeapons(6).Select(x => x.SName))}");
                      foreach (var (id, slot, shotName) in new[] { (73, 3, "rb_vozhd"), (45, 6, "rb_gadyuka"), (46, 6, "rb_pavlik"), (44, 6, "rb_granatin") })
                      {
                          var it = ItemDB.Get(ItemKind.Weapon, id); if (it == null) { Debug.Log($"[uicompare] logic rebuild {id}: missing"); continue; }
                          pp3.OwnedWeapons.Add(id); pp3.Equip(it);
                          B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                          var shr = ShotController.Instance; var lk = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lk) lk.SetPitch(8f);
                          shr.SetWeapon(slot); yield return new WaitForSeconds(0.7f);
                          var rw = shr.CurrentWeapon; int a0 = rw.LoadedAmmo; int ex0 = Count("Explo");
                          ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                          yield return new WaitForSeconds(0.35f);
                          var model = rw.Transform; var anim = model ? model.GetComponentInChildren<Animation>() : null;
                          Debug.Log($"[uicompare] logic rebuild {rw.SystemName} ({it.Name}): type {rw.Type}, ammo {a0} → {rw.LoadedAmmo} / {rw.AmmoReserve}, model {(model ? model.name : "-")} visible {(model && model.gameObject.activeInHierarchy)}, clip {(anim && anim.isPlaying ? "playing" : "-")}, items {shr.RegisteredItems.Count}");
                          Debug.Log($"[uicompare] logic rebuild {rw.SystemName} projectile: grenade {Count("GrenadePrefab")}, snowball {Count("SnowballPrefab")}, shot sound {(shr.GetComponent<AudioSource>() && shr.GetComponent<AudioSource>().clip ? shr.GetComponent<AudioSource>().clip.name : "-")}");
                          yield return C(shotName);
                          float u2 = Time.time + 4f; while (shr.RegisteredItems.Count > 0 && Time.time < u2) yield return null;
                          if (id == 46) { yield return new WaitForSeconds(0.15f); var ex = FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name.StartsWith("ExploSnow")); var exa = ex ? ex.GetComponentInChildren<AudioSource>() : null;
                              Debug.Log($"[uicompare] logic rebuild Павлик blow: {(ex ? ex.name : "-")}, sound {(exa && exa.clip ? exa.clip.name : "-")}, particles {(ex ? ex.GetComponentsInChildren<ParticleSystem>().Length : 0)}"); yield return C("rb_pavlik_blow"); }
                          Debug.Log($"[uicompare] logic rebuild {rw.SystemName} after: explosions +{Count("Explo") - ex0}, snow {Count("ExploSnowPrefab")}, blast radius {ShotCalculator.GetExplosionRadius(rw, rw.Type):F1}");
                          yield return new WaitForSeconds(0.5f);
                      }
                      // sticky launchers: 4 throws (the 4th blows the oldest, CheckBlow), then the right button (DoBlow) blows the rest
                      var doBlow = typeof(ShotController).GetMethod("DoBlow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                      foreach (var (id, shotName) in new[] { (9001, "rb_sticky"), (9002, "rb_sticky_b02") })
                      {
                          var it = ItemDB.Get(ItemKind.Weapon, id); if (it == null) { Debug.Log($"[uicompare] logic sticky {id}: missing"); continue; }
                          pp3.OwnedWeapons.Add(id); pp3.Equip(it);
                          B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                          var shs = ShotController.Instance; var lk = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lk) lk.SetPitch(20f);
                          shs.SetWeapon(6); yield return new WaitForSeconds(0.7f);
                          var sw = shs.CurrentWeapon; int ex0 = Count("Explo"); var counts = new System.Collections.Generic.List<string>();
                          for (int k = 0; k < 4; k++)
                          {
                              float wait = Time.time + 4f; while ((sw.LoadedAmmo <= 0 || !sw.CanFire()) && Time.time < wait) yield return null;
                              ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                              yield return new WaitForSeconds(1.0f);
                              counts.Add($"{shs.RegisteredItems.Count}/{Count("Explo") - ex0}");
                          }
                          yield return C(shotName);
                          int bombsBefore = shs.RegisteredItems.Count;
                          doBlow.Invoke(shs, null); yield return new WaitForSeconds(0.6f);
                          Debug.Log($"[uicompare] logic sticky {sw.SystemName} ({it.Name}): type {sw.Type}, model {(sw.Transform ? sw.Transform.name : "-")}, bombs/explosions after each throw {string.Join(" ", counts)}; right button: bombs {bombsBefore} → {shs.RegisteredItems.Count}, explosions +{Count("Explo") - ex0}, blast radius {ShotCalculator.GetExplosionRadius(sw, sw.Type):F1}");
                          yield return C(shotName + "_blow");
                      }
                      // weapons stage 4: «Примус» (FL_N1) — hold fire 1.5 s: shots, ammo, the Flamer stream, "Flamer_Shot"; release: the stream goes out
                      {
                          var it = ItemDB.Get(ItemKind.Weapon, 25);
                          if (it == null) Debug.Log("[uicompare] logic flamer: FL_N1 missing");
                          else
                          {
                              pp3.OwnedWeapons.Add(25); pp3.Equip(it);
                              B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                              var shf = ShotController.Instance; var lkf = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lkf) lkf.SetPitch(5f);
                              shf.SetWeapon(it.Ws); yield return new WaitForSeconds(0.8f);
                              var fw = shf.CurrentWeapon; int fa0 = fw.LoadedAmmo;
                              var fl = shf.Container ? shf.Container.GetComponentInChildren<Flamer>() : null;
                              int Alive() { int n = 0; if (fl) foreach (var ps in fl.Flamers) n += ps.particleCount; return n; }
                              bool Emitting() { if (!fl) return false; foreach (var ps in fl.Flamers) if (ps.emission.enabled) return true; return false; }
                              Debug.Log($"[uicompare] logic flamer equipped: {fw.SystemName} type {fw.Type}, flamer {(fl ? fl.name : "-")} systems {(fl ? fl.Flamers.Length : 0)}, emitting {Emitting()}, alive {Alive()}");
                              ShotController.TestFire = true;
                              yield return new WaitForSeconds(1.5f);
                              var au = SoundManager.LocalPlayerAudio;
                              Debug.Log($"[uicompare] logic flamer firing: mode {fw.WeaponMode}, ammo {fa0} → {fw.LoadedAmmo}, emitting {Emitting()}, alive {Alive()}, sound {(au && au.clip ? au.clip.name : "-")}");
                              yield return C("fire_flamer");
                              ShotController.TestFire = false;
                              yield return new WaitForSeconds(0.6f);
                              Debug.Log($"[uicompare] logic flamer released: mode {fw.WeaponMode}, emitting {Emitting()}, alive {Alive()}");
                              yield return C("fire_flamer_off");
                              pp3.Unequip(it);
                          }
                      }
                      // playtest 12.11.2026: own shots with an effect — blood overlay, «Ворчун» fire ticks, «Гранатин» slow, icons
                      {
                          var bloodField = typeof(BattleUI).GetField("bloodOverlayAmount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                          bool IconOn(string n) { var g = GameObject.Find("BattleGUI"); if (!g) return false; var tr = g.transform.Find("Impact/" + n); var r = tr ? tr.GetComponent<MeshRenderer>() : null; return r && r.enabled; }
                          foreach (var (id, nm) in new[] { (104, "vorchun"), (44, "granatin") })
                          {
                              var it = ItemDB.Get(ItemKind.Weapon, id); if (it == null) continue;
                              pp3.OwnedWeapons.Add(id); pp3.Equip(it);
                              B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                              var shi = ShotController.Instance; var lki = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lki) lki.SetPitch(85f);
                              var vi = GameFlow.Instance.Player.GetComponent<PlayerVitals>(); var sci = GameFlow.Instance.Player.GetComponent<SoldierController>();
                              shi.SetWeapon(6); yield return new WaitForSeconds(0.8f);
                              int h0 = vi.Health, a0 = vi.Armor;
                              ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                              float untilSelf = Time.time + 3f; while (shi.RegisteredItems.Count > 0 && Time.time < untilSelf) yield return null;
                              yield return null;
                              int h1 = vi.Health, a1 = vi.Armor;
                              Debug.Log($"[uicompare] logic self {nm} hit: health {h0}/{a0} → {h1}/{a1}, blood {(float)bloodField.GetValue(UIRoot.Instance.Battle):F2}, icons fire {IconOn("Fire")} poison {IconOn("Poison")} slow {IconOn("Slow")}, speed x{sci.impactSpeedMultiplier:F2}");
                              yield return C("self_" + nm);
                              yield return new WaitForSeconds(1.7f);
                              Debug.Log($"[uicompare] logic self {nm} +1.7 s: health {vi.Health}/{vi.Armor}, icons fire {IconOn("Fire")} slow {IconOn("Slow")}, speed x{sci.impactSpeedMultiplier:F2}, blood {(float)bloodField.GetValue(UIRoot.Instance.Battle):F2}");
                              pp3.Unequip(it);
                          }
                      }
                      // playtest 12.11.2026: grenade arcs — flight time, peak above the muzzle, range, for level and 20° up aims
                      {
                          var shotField = typeof(GrenadeTracer).GetField("shot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                          var bombField = typeof(BombTracer).GetField("shot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                          foreach (var (id, slot) in new[] { (104, 6), (45, 6), (44, 6), (46, 6), (9001, 6) })
                          {
                              var it = ItemDB.Get(ItemKind.Weapon, id); if (it == null) continue;
                              pp3.OwnedWeapons.Add(id); pp3.Equip(it);
                              foreach (var pitch in new[] { 0f, -20f })
                              {
                                  B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                                  var sha = ShotController.Instance; var lka = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lka) lka.SetPitch(pitch);
                                  sha.SetWeapon(slot); yield return new WaitForSeconds(0.8f);
                                  var beforeIds = new System.Collections.Generic.HashSet<long>(sha.RegisteredItems.Keys);
                                  ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                                  yield return null;
                                  Shot gs = null;
                                  foreach (var kv in sha.RegisteredItems) if (!beforeIds.Contains(kv.Key)) { var tr = kv.Value; gs = tr is GrenadeTracer g ? (Shot)shotField.GetValue(g) : tr is BombTracer bt ? (Shot)bombField.GetValue(bt) : null; }
                                  if (gs == null || gs.Trajectory == null || gs.Trajectory.Count == 0) { Debug.Log($"[uicompare] logic arc {sha.CurrentWeapon.SystemName} pitch {pitch}: no grenade"); }
                                  else
                                  {
                                      var p0 = (Vector3)gs.Trajectory[0]; float top = p0.y; Vector3 last = p0;
                                      foreach (var o in gs.Trajectory) { var v = (Vector3)o; if (v.y > top) top = v.y; last = v; }
                                      float range = Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(last.x, last.z));
                                      Debug.Log($"[uicompare] logic arc {sha.CurrentWeapon.SystemName} pitch {pitch}: points {gs.Trajectory.Count}, flight {(gs.LandingTimeStamp - gs.TimeStamp)} ms, peak +{top - p0.y:F1}, range {range:F1}, drop {p0.y - last.y:F1}, velocity {sha.CurrentWeapon.Velocity} life {sha.CurrentWeapon.Life}");
                                  }
                                  yield return new WaitForSeconds(0.2f);
                              }
                              pp3.Unequip(it);
                          }
                      }
                      // playtest 12.11.2026: «Гранатин» seemed stuck in the reload pose; the same for «Гадюка» and «Павлик М.»
                      {
                          var rlm = typeof(ShotController).GetMethod("Reload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                          foreach (var (id, nm) in new[] { (44, "granatin"), (45, "gadyuka"), (46, "pavlik") })
                          {
                              var it = ItemDB.Get(ItemKind.Weapon, id); if (it == null) continue;
                              pp3.OwnedWeapons.Add(id); pp3.Equip(it);
                              B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                              var shr = ShotController.Instance; var lkr = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lkr) lkr.SetPitch(10f);
                              shr.SetWeapon(6); yield return new WaitForSeconds(0.8f);
                              var gw = shr.CurrentWeapon;
                              string State() { var an = gw.Transform ? gw.Transform.GetComponentInChildren<Animation>() : null; if (!an) return "no anim"; var pl = new System.Collections.Generic.List<string>(); foreach (AnimationState s in an) if (an.IsPlaying(s.name)) pl.Add($"{s.name}@{s.normalizedTime:F2}"); return $"playing [{string.Join(", ", pl)}] mode {gw.WeaponMode} ammo {gw.LoadedAmmo}/{gw.AmmoReserve}"; }
                              // (1) one shot, R, wait for the whole reload
                              ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                              yield return new WaitForSeconds(1.5f);
                              rlm.Invoke(shr, new object[] { gw });
                              Debug.Log($"[uicompare] logic reload {gw.SystemName} started: {State()}");
                              yield return new WaitForSeconds(4f);
                              Debug.Log($"[uicompare] logic reload {gw.SystemName} done: {State()}");
                              yield return C("reload_" + nm + "_done");
                              // (2) empty the clip (auto-reload), fire during the reload
                              for (int k = 0; k < 6 && gw.LoadedAmmo > 0; k++) { ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false; yield return new WaitForSeconds(1.2f); }
                              yield return new WaitForSeconds(0.6f);
                              Debug.Log($"[uicompare] logic reload {gw.SystemName} auto: {State()}");
                              yield return new WaitForSeconds(0.9f);
                              ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
                              Debug.Log($"[uicompare] logic reload {gw.SystemName} fire during reload: {State()}");
                              for (float tt = 0f; tt < 6f; tt += 0.2f) { yield return new WaitForSeconds(0.2f); var anr = gw.Transform ? gw.Transform.GetComponentInChildren<Animation>() : null; string sp = ""; if (anr) foreach (AnimationState s in anr) if (anr.IsPlaying(s.name)) sp += $" {s.name}@{s.time:F2}/{s.length:F2}x{s.speed:F2}"; Debug.Log($"[uicompare] logic reload {gw.SystemName} t+{tt + 0.2f:F1}: mode {gw.WeaponMode} ammo {gw.LoadedAmmo}/{gw.AmmoReserve} anim{sp}"); }
                              Debug.Log($"[uicompare] logic reload {gw.SystemName} after interrupt: {State()}");
                              yield return C("reload_" + nm + "_interrupt");
                              pp3.Unequip(it);
                          }
                      }
                      // weapons stage 5: a dummy fighter (another soldier's body) 7 ahead with the profile's loadout: model in hand per slot,
                      // aim stance (bend) per set, pitch, reload, bat swing, remote rifle shot on the model
                      {
                          B.RequestRespawn(); yield return new WaitForSeconds(0.4f);
                          var pl5 = GameFlow.Instance.Player.transform; var lk5 = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lk5) lk5.SetPitch(8f);
                          var fw5 = Vector3.ProjectOnPlane(pl5.forward, Vector3.up).normalized;
                          var holder5 = new GameObject("TestDummy");
                          Vector3 at5 = pl5.position + fw5 * 9f + Vector3.up * 3f;
                          if (Physics.Raycast(at5, Vector3.down, out var h5, 30f)) at5 = h5.point;
                          holder5.transform.position = at5; holder5.transform.rotation = Quaternion.LookRotation(-fw5);
                          var dummy = Dresser.CreateFighter(holder5.transform);
                          if (dummy == null) Debug.Log("[uicompare] logic body: no fighter template");
                          else
                          {
                              var outfit = new System.Collections.Generic.Dictionary<int, GameItem>();
                              foreach (var kv in Profile.Current.EquippedWear) { var wi = ItemDB.Get(ItemKind.Wear, kv.Value); if (wi != null) outfit[kv.Key] = wi; }
                              Dresser.Dress(dummy, outfit);
                              var ba = dummy.AddComponent<BodyAnimator>(); ba.Anim = dummy.GetComponentInChildren<Animation>();
                              var bw = dummy.AddComponent<BodyWeapons>(); bw.Animator = ba;
                              if (ba.Anim != null && ba.Anim["Idle"] != null) ba.Anim.CrossFade("Idle");
                              foreach (var kv in BattleWeapons.Build(Profile.Current)) bw.InitWeapon(kv.Key, kv.Value);
                              int VisibleIn(Transform tr) { int n = 0; if (tr) foreach (var r in tr.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) n++; return n; }
                              string Shown(Transform tr) { if (!tr) return "-"; foreach (Transform c in tr) foreach (var r in c.GetComponentsInChildren<Renderer>(true)) if (r.enabled) return c.name; return "-"; }
                              Debug.Log($"[uicompare] logic body dummy: {dummy.name}, weapon looks {dummy.GetComponentsInChildren<WeaponLook>(true).Length}, slots {string.Join(", ", bw.Weapons.Where(x => x != null).Select(x => x.Index + ":" + x.SystemName + (x.Transform ? "" : "(no node)")))}, layer of first look {(dummy.GetComponentInChildren<WeaponLook>(true) ? LayerMask.LayerToName(dummy.GetComponentInChildren<WeaponLook>(true).gameObject.layer) : "-")}");
                              for (int sl = 1; sl <= 7; sl++)
                              {
                                  if (bw.GetWeapon(sl) == null) continue;
                                  var ty = bw.OnChangeWeapon(sl);
                                  ba.TargetXRotation = 0f;
                                  yield return new WaitForSeconds(0.5f);
                                  var cw = bw.CurrentWeapon; var bs = ba.Anim ? ba.Anim[ba.BendAnimationName] : null;
                                  Debug.Log($"[uicompare] logic body slot {sl}: {cw.SystemName} {ty}, shown {Shown(cw.Transform)}, visible renderers {VisibleIn(cw.Transform)}, set {BodyAnimator.AnimationSet(cw.Type, cw.SystemName)}, bend {ba.BendAnimationName} weight {(bs ? bs.weight : -1):F2} time {(bs ? bs.time : -1):F2}");
                                  yield return C("body_slot" + sl);
                              }
                              // rifle (slot 3): pitch up / down, reload, a remote shot on the model
                              if (bw.GetWeapon(3) != null)
                              {
                                  bw.OnChangeWeapon(3);
                                  foreach (var (pitch, nm) in new[] { (-60f, "body_aim_up"), (60f, "body_aim_down") })
                                  {
                                      ba.TargetXRotation = pitch; yield return new WaitForSeconds(0.4f);
                                      var bs = ba.Anim[ba.BendAnimationName];
                                      Debug.Log($"[uicompare] logic body pitch {pitch}: bend {ba.BendAnimationName} time {(bs ? bs.time : -1):F2} angle {ba.BendAnimationAngle:F2}");
                                      yield return C(nm);
                                  }
                                  ba.TargetXRotation = 0f; yield return new WaitForSeconds(0.3f);
                                  bw.ReloadAnimation(); yield return new WaitForSeconds(0.4f);
                                  var rc = ba.Anim["MachineGunWeaponReloadClip"];
                                  var playingReload = new System.Collections.Generic.List<string>(); foreach (AnimationState ras in ba.Anim) if (ras.name.Contains("Reload") && ba.Anim.IsPlaying(ras.name)) playingReload.Add(ras.name);
                                  Debug.Log($"[uicompare] logic body reload ({bw.CurrentWeapon.SystemName}): playing {string.Join("/", playingReload)}, bend blocked {ba.Anim[ba.BendAnimationName].weight:F2}");
                                  yield return C("body_reload");
                                  yield return new WaitForSeconds(2f);
                                  bw.ShotAnimation(WeaponType.MACHINE_GUN, ShotLaunchMode.SHOT); yield return null;
                                  var ma = bw.CurrentWeapon.Transform ? bw.CurrentWeapon.Transform.GetComponentInChildren<Animation>() : null;
                                  Debug.Log($"[uicompare] logic body remote shot: model animation {(ma ? ma.name : "-")} playing Shot {(ma && ma.IsPlaying("Shot"))}");
                              }
                              if (bw.GetWeapon(1) != null)
                              {
                                  bw.OnChangeWeapon(1); yield return new WaitForSeconds(0.4f);
                                  bw.ShotAnimation(WeaponType.ONE_HANDED_COLD_ARMS, ShotLaunchMode.LAUNCH); yield return new WaitForSeconds(0.15f);
                                  Debug.Log($"[uicompare] logic body bat swing: BatShotClip playing {ba.Anim.IsPlaying("BatShotClip")}, bend {ba.BendAnimationName}");
                                  yield return C("body_bat_swing");
                              }
                              bw.Kill(); yield return null;
                              Debug.Log($"[uicompare] logic body killed: visible renderers {VisibleIn(bw.CurrentWeapon.Transform)}");
                          }
                          Destroy(holder5);
                      }
                      foreach (var id in new[] { 73, 45, 46, 44, 9001, 9002 }) { var it = ItemDB.Get(ItemKind.Weapon, id); if (it != null) pp3.Unequip(it); }
                      foreach (var id in new[] { 104, 75 }) { var it = ItemDB.Get(ItemKind.Weapon, id); if (it != null) pp3.Unequip(it); }
                      shc = ShotController.Instance; look = GameFlow.Instance.Player.GetComponent<SoldierCamera>();   // the loops above respawn the player
                  }
                  if (look) look.SetPitch(0f);
                  shc.SetWeapon(3); yield return new WaitForSeconds(0.6f);
              }
          }
        }
        B.DebugShowScore = true; yield return C("battle_tab"); B.DebugShowScore = false;
        // taunts (E = free taunt 1; 3 = full-body taunt with its prop) — third-person camera, dressed soldier
        { var fb = GameFlow.Instance.Player ? GameFlow.Instance.Player.GetComponent<FighterBody>() : null;
          if (fb)
          {
              var lk = GameFlow.Instance.Player.GetComponent<SoldierCamera>(); if (lk) lk.SetYaw(GameFlow.Instance.Player.transform.eulerAngles.y + 180f);   // spawn faces a wall behind
              yield return new WaitForSeconds(0.3f);
              { var scc = GameFlow.Instance.Player.GetComponent<SoldierController>(); scc.testMove = Vector3.forward; yield return new WaitForSeconds(0.6f);
                var legsA = GameFlow.Instance.Player.GetComponentsInChildren<AudioSource>(true).FirstOrDefault(s => s.name == "Legs");
                Debug.Log($"[uicompare] sound legs: playing {(legsA ? legsA.isPlaying : false)} clip {(legsA && legsA.clip ? legsA.clip.name : "-")} loop {(legsA ? legsA.loop : false)}");
                scc.testMove = Vector3.zero; yield return new WaitForSeconds(0.3f);
                Debug.Log($"[uicompare] sound legs after stop: playing {(legsA ? legsA.isPlaying : false)}"); }
              fb.LaunchTaunt(1);
              { var spk = GameFlow.Instance.Player.GetComponentsInChildren<AudioSource>(true).FirstOrDefault(s => s.name == "Speaker"); yield return null;
                Debug.Log($"[uicompare] sound taunt speaker playing {(spk ? spk.isPlaying : false)} vol {(spk ? spk.volume : -1)} listener {AudioListener.volume} paused {AudioListener.pause}"); }
              yield return new WaitForSeconds(0.3f); yield return C("taunt_1a"); yield return new WaitForSeconds(0.4f); yield return C("taunt_1");
              yield return new WaitForSeconds(2.5f); fb.LaunchTaunt(3); yield return new WaitForSeconds(0.6f); yield return C("taunt_3a"); yield return new WaitForSeconds(0.8f); yield return C("taunt_3"); fb.CancelTaunt();
          }
          else Debug.LogWarning("[uicompare] no FighterBody"); }
        // ---- logic checks (New Era base): speed, death → ragdoll + TPS camera, countdown, respawn, time over → new round ----
        {
            var F = GameFlow.Instance;
            var scc = F.Player.GetComponent<SoldierController>();
            {
                var f4 = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "finish4");
                string Lay(string n) { var g = f4 ? f4.Find(n) : null; return g ? n + "=" + g.gameObject.layer + (g.gameObject.activeSelf ? "" : "(off)") : n + "=-"; }
                Debug.Log("[uicompare] logic gates team " + F.LocalTeam + ": " + Lay("GatesRed1") + " " + Lay("GatesBlue1") + " ignore(2,12)=" + Physics.GetIgnoreLayerCollision(2, 12) + " ignore(2,0)=" + Physics.GetIgnoreLayerCollision(2, 0) + " playerLayer=" + F.Player.layer);
            }
            var mot = F.Player.GetComponent<CharacterMotor>();
            scc.testMove = Vector3.forward; yield return new WaitForSeconds(1.0f);
            var hv = mot.movement.velocity; hv.y = 0f;
            Debug.Log("[uicompare] logic run speed " + hv.magnitude.ToString("F2") + " (runSpeed " + scc.runSpeed.ToString("F2") + ", walk " + scc.walk + ", key " + scc.keyState + ")");
            scc.testMove = Vector3.zero; yield return new WaitForSeconds(0.5f);
            if (ShotController.Instance)   // ShotController.GetSpeedMultiplier: one-handed melee ×1.25
            {
                Debug.Log("[uicompare] logic run speed above with " + ShotController.Instance.CurrentWeapon?.SystemName + " ×" + ShotController.Instance.GetSpeedMultiplier());
                ShotController.Instance.SetWeapon(1); yield return new WaitForSeconds(0.6f);
                scc.testMove = Vector3.forward; yield return new WaitForSeconds(1.0f);
                hv = mot.movement.velocity; hv.y = 0f;
                Debug.Log("[uicompare] logic run speed " + hv.magnitude.ToString("F2") + " with " + ShotController.Instance.CurrentWeapon?.SystemName + " ×" + ShotController.Instance.GetSpeedMultiplier());
                scc.testMove = Vector3.zero; yield return new WaitForSeconds(0.5f);
                ShotController.Instance.SetWeapon(3); yield return new WaitForSeconds(0.6f);
            }
            var vit = F.Player.GetComponent<PlayerVitals>();
            vit.ApplyFallDamage(500);
            yield return new WaitForSeconds(0.5f);
            var fb2 = F.Player.GetComponent<FighterBody>();
            var anim2 = F.Player.GetComponentInChildren<Animation>();
            Debug.Log("[uicompare] logic death: dead " + vit.Dead + " state " + B.PlayerState + " tps " + (fb2 && fb2.TpsCamera && fb2.TpsCamera.enabled) + " anim " + (anim2 && anim2.enabled) + " defeated " + (fb2 && fb2.Defeated));
            yield return C("logic_death");
            yield return new WaitForSeconds(7.2f);
            Debug.Log("[uicompare] logic after 7.7 s: state " + B.PlayerState);
            B.RequestRespawn(); yield return new WaitForSeconds(0.5f);
            Debug.Log("[uicompare] logic respawned: state " + B.PlayerState + " team " + F.LocalTeam + " pos " + F.Player.transform.position);
            float before = (float)Profile.Current.Stat("time");
            F.BattleStartTime -= 100000f;   // force the time limit
            yield return null; yield return null;
            Debug.Log("[uicompare] logic time over: state " + B.PlayerState + " playerDead " + F.Player.GetComponent<PlayerVitals>().Dead + " stats time +" + (Profile.Current.Stat("time") - before));
            yield return C("logic_timeover");
            yield return new WaitForSeconds(8.5f);
            Debug.Log("[uicompare] logic new round: state " + B.PlayerState + " team " + F.LocalTeam + " alive " + (F.Player && !F.Player.GetComponent<PlayerVitals>().Dead));
        }
        GameFlow.Instance.ExitToLobby();
        yield return new WaitForSeconds(1f);
        // regression: back from a battle the lobby must look exactly like before (the soldier used to vanish — HUD layer 31)
        while (GameFlow.Instance.Current != GameFlow.State.Lobby) yield return null;
        L.mainTab = 0; yield return new WaitForSeconds(1.5f); yield return C("home_after_battle");
        // ---- base gates on Angar (TDM): the red player opens the red gates (AutomaticDoor Team 1), not the blue ones ----
        {
            var room2 = new RoomSettings(); room2.Map = MapList.Get("Angar"); room2.Mode = GameMode.TEAM_DEATHMATCH;
            if (room2.Map != null && GameFlow.MapAvailable("Angar"))
            {
                GameFlow.Instance.StartBattle(room2);
                while (GameFlow.Instance.Current != GameFlow.State.Battle) yield return null;
                yield return new WaitForSeconds(1f);
                ui.Battle.JoinSelectedBattleTeam(1); yield return new WaitForSeconds(7.5f);
                ui.Battle.RequestRespawn(); yield return new WaitForSeconds(1f);
                var doors = FindObjectsByType<AutomaticDoor>(FindObjectsSortMode.None);
                Debug.Log("[uicompare] doors found " + doors.Length);
                foreach (byte team in new byte[] { 1, 2 })
                {
                    var d = doors.FirstOrDefault(x => x.Team == team);
                    if (d == null) continue;
                    var sc = d.transform.parent.Find("Scanner");
                    var pl = GameFlow.Instance.Player;
                    var cc = pl.GetComponent<CharacterController>(); cc.enabled = false;
                    pl.transform.position = (sc ? sc.position : d.transform.parent.position) + Vector3.up * 1f; cc.enabled = true;
                    yield return new WaitForSeconds(1.5f);
                    var an = d.transform.parent.GetComponentInChildren<Animation>();
                    var clips = new System.Collections.Generic.List<string>(); if (an) foreach (AnimationState s in an) clips.Add(s.name);
                    Debug.Log("[uicompare] door team " + team + " near red player: IsOpen " + d.IsOpen + " anim " + (an ? an.name : "-") + " clips [" + string.Join(",", clips) + "] openState " + (an && an[d.OpenAnimationName] != null) + " doorPos " + d.transform.position);
                    yield return C("gate_team" + team);
                }
                GameFlow.Instance.ExitToLobby();
                yield return new WaitForSeconds(1f);
            }
            else Debug.LogWarning("[uicompare] Angar not available");
        }
        // ---- Zone-Z DM: the neutral T0 markers are under the floor → New Era rejects them and uses its Zone-Z DM table ----
        {
            var rz = new RoomSettings(); rz.Map = MapList.Get("Zombi"); rz.Mode = GameMode.DEATHMATCH;
            if (rz.Map != null && GameFlow.MapAvailable("Zombi"))
            {
                GameFlow.Instance.StartBattle(rz);
                while (GameFlow.Instance.Current != GameFlow.State.Battle) yield return null;
                yield return new WaitForSeconds(8.5f);
                ui.Battle.RequestRespawn(); yield return new WaitForSeconds(1.5f);
                var pl = GameFlow.Instance.Player;
                var mot = pl.GetComponent<CharacterMotor>();
                Debug.Log($"[uicompare] logic Zone-Z DM spawn at {pl.transform.position} grounded {(mot ? mot.grounded : false)}");
                yield return C("zonez_dm_spawn");
                GameFlow.Instance.ExitToLobby();
                yield return new WaitForSeconds(1f);
            }
            else Debug.LogWarning("[uicompare] Zombi not available");
        }
        Profile.ResetProfile();
    }

    static int GearWt(int tab) => new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }[tab];

    IEnumerator Shot(string name)
    {
        yield return null; yield return null; yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(dir, $"{++n:00}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
    }
}
