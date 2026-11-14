# Contra City weapon system: architecture map for the Unity 6 port

Path abbreviations:
- **NE** = `W:/Contra City Extract/NE_Scripts_Decrypted/`
- **P** = `W:/Contra City Extract/Penta_Original/Export_WebPlayer_2016/ExportedProject/Assets/Scripts/Assembly-CSharp/`
- **FL** = `W:/ContraCityRebuild/Assets/Contra/asset_bundles/gamebundle/prefabs/soldier/Fighter_locomotion.prefab`
- **EFL** = `.../prefabs/soldier/Enemy_Fighter_locomotion.prefab`
- **OURS** = `W:/ContraCityRebuild/Assets/`

Obfuscated names in NE, with their Penta equivalents:

| NE | Penta | What it is |
|---|---|---|
| `噔` | `CombatWeapon` | weapon runtime |
| `啁` | `WeaponType` | weapon type enum |
| `啄` | `WeaponMode` | weapon state |
| `啫` | `ShotCalculator` | raycasts and target building |
| `啨` | `ShotTarget` | one target in a shot |
| `啧` | `ShotTargetType` | |
| `喘` | `PlayerHitZone` | BASE=0, ENGINE=0x10 (groin, "nut shot"), CABIN=0x20 (head) |
| `嗠` | `LaunchModes` | SHOT=0, LAUNCH=1, BLOW=2, TURRET_SHOT, TURRET_CONTROL, SPIN=5, DISACTIVATE |
| `啪` | `ShotEffectType` | ME_BEFORE/AFTER_SERVER, ENEMY_AFTER_SERVER, ENEMY_SET_GAME_STATE |
| `嚔` | `AudioPlayMode` | Play, PlayLoop, PlayStop, Stop |
| `噌` | `CrossHairMode` | |
| `啛` | `TRInput` | key bindings |
| `噩` | `CharacterManager` | `GetPrefab(name)` |
| `嗎` | profile/session static | `嗎.WeaponSlot.Weapon1..7` |
| `喩` | `NetworkDev` | `Destroy_Geometry = true` |
| `喞` | `OptionsManager` | quality, sound volume, FOV |
| `噑` | `Configuration` | debug flags |
| `嗔` | (none) | NE local/offline room manager; `嗔.Enabled` means offline |
| `噕` | (security helper) | combat-player anti-cheat (`CombatWeaponSecurity` in Penta) |
| `噋` | `ShotStatistics` | telemetry |
| `嘄` | `GUISkinManager` | |

The weapon-type enum is in NE `啁.cs:1-30`, identical to P `WeaponType.cs`:

| Value | Type | Value | Type |
|---|---|---|---|
| 1 | ONE_HANDED_COLD_ARMS | 9 | GRENADE_LAUNCHER |
| 2 | TWO_HANDED_COLD_ARMS | 10 | SNIPER_RIFLE |
| 3 | HAND_GUN | 11 | SNOW_GUN |
| 4 | MACHINE_GUN | 12 | ACID_THROWER |
| 5 | FLAMER | 13–14 | shockers (no-op) |
| 6 | GATLING_GUN | 15 | BOMB_LAUNCHER (sticky) |
| 7 | SHOT_GUN | 101+ | mines and turrets |
| 8 | ROCKET_LAUNCHER | 201+ | chargers and kamikaze |

---

## 0. Overall structure (who owns what)

**Local player** is `Fighter_locomotion/Fighter`. Its components (FL) are:
- `SoldierController`, `CharacterMotor`, `CharacterPusher`, `ActorAnimator`, `PlayerRemote`, `PlayerLocal`, `SoldierCamera`, `AnimationSynchronizer`, `NetworkTransformSender`, `ShotController`, `WalkController`, `PlayerCustomisator`, `Flame`
- a legacy `Animation` holding 86 body clips

`Fighter/MainCamera` holds `Camera`, `MouseOverLook`, `FPSCamera`, `AudioSource`, `AudioSourceVC`. Its children are `Weapon` (with an `Animation`) and `WeaponCamera` (the weapon layer camera).

**Remote players and bots** use EFL. The root carries `ActorAnimator`, `PlayerRemote`, `WeaponController` (the base class, third person), `NetworkTransformReceiver` and `Interpolation`, `PlayerCustomisator`, `Flame`, and a body `Animation` with 87 clips.

**Firing pipeline, as it runs offline in NE:**

1. `ShotController.Update` reads input.
2. The weapon runtime `噔.CalcMode` / `Fire()` / `Launch()` decides whether the weapon can act.
3. `ShotController.DoShot` builds a ray with deviation and calls `啫.StraightShot`, `RocketShot` or `GrenadeShot` to raycast and build targets.
4. It calls `啫.GunShot` or `SegmentShot` (target filtering, cone) and plays the local effect, `EffectManager.*Effect(shot, LocalPlayer, isMe=true)`.
5. `NetworkManager.SendShot(shot)` (NE `NetworkManager.cs:233/274`). When offline, `TryDispatchOfflineShot` (`:221-231`) loops the shot straight back into `PlayerManager.PlayerShot(localId, shot)` (NE `PlayerManager.cs:9377-9536`).
6. `PlayerShot` applies offline damage per target (`ApplyOfflineDamage`, `ResolveBattleServerDamage`), then calls `ShotEffect(shot, player, ME_AFTER_SERVER)` (`:9724-9940`).
7. When that returns true, `ShotController.OnShot(type)` consumes ammo (`:9533-9535`, NE `ShotController.cs:2920-2969`).

**Projectiles.** `EffectManager.rocketLauncherEffect` and `grenadeLauncherEffect` spawn a tracer prefab: `RocketTracer`, `GrenadeTracer` or `BombTracer`. The tracer moves between `Shot.TimeStamp` and `Shot.LandingTimeStamp`, then calls `Blow()`. `Blow()` runs `啫.RadialShot`, then `SendShot` with `LaunchMode=BLOW`. That goes into `PlayerShot` again, which calls `ShotEffect`, then `EffectManager.launcherEffect` (explosion and knockback).

In Penta (online), step 5 went to Photon. The server returned targets with damage, and `PlayerShot` (P `PlayerManager.cs:2865`) handled the echo. **Penta has no client-side damage formula.**

---

## 1. Weapon slots, switching, first-person models, HUD bar and ammo

### 1.1 From profile item to runtime weapon

**Item data.** NE `Weapon.cs:594-835` parses the JSON item fields:

| JSON field | Runtime field | Notes |
|---|---|---|
| `w_id` | `WeaponID` | |
| `wt` | `WeaponType` | |
| `ws` | slot | 1..7 |
| `vel` | `Velocity` | |
| `rad` | `Radius` | distance |
| `ang` | `Angel` | sic |
| `dev` | `Deviation` | |
| `rap` | `Rapidity` | ms |
| `attackspeed` | `AttackSpeed` | NE addition |
| `rt` | `ReloadTime` | |
| `reload` | `BalanceReloadTime` | NE addition |
| `accuracy` | `BalanceAccuracy` | NE addition |
| `lt` | `LifeTime` | |
| `smindam`/`smaxdam`, `mmindam`/`mmaxdam`, `lmindam`/`lmaxdam` | Short/Medium/Long Min/Max damage | |
| `krit` | crit | |
| `ammo`, `ammo_tot` | | |
| `stRa`/`stDi`/`stDa` | star rapidity/distance/damage | |
| `dStRa`/`dStDi`/`dStDa` | display stars | |
| `shiftb`/`shifts`/`shiftd` | | |
| `mast*` | Workshop* fields | see §4.4 |
| `dot_type`, `dot_damage`, `dot_ticks`, `dot_interval_ms` | DoT fields | |
| `u_id` | `UpgradeID` | |
| `sname` | `SystemName` | via `NormalizeSystemName`; `SR_Steyrford` becomes `SR_Steyrf0rd` (`:847`) |

`ne_items.json` (`OURS/Game/Resources/data/ne_items.json`) holds `weapons` (39), `defaultWeapons`, `weaponUpgrades` (40), `gym`, `wears` and others.
- `ws` is the slot: 1 melee, 2 pistol, 3 MG, 4 heavy (GG/FL), 5 shotgun, 6 RL/GL/BL, 7 sniper.
- The default loadout, from `defaultWeapons`, is OHCA_BasebalBat (1), HG_Makarov (2), MG_AK47 (3), GG_M134 (4), SG_Winchester1887 (5), …

**Server weapon-info hashtable.** In Penta the server sent it as `[94]` in actor data (`Dictionary<int slotIdx, Hashtable>`). NE builds it client-side:
- `PlayerManager.BuildLocalWeaponInfoForBattle` (`PlayerManager.cs:1666-1678`) calls `TryAddLocalWeaponForBattle` (`:1680-1880`).
- A duplicate is `MainNetworkController.CreateOfflineWeaponInfo` (`MainNetworkController.cs:3343-3358`) with `TryAddOfflineWeapon` (`:3393-3490`).
- The slot index is `WeaponSlot.Weapon1..7` mapped to 0..6. An empty slot falls back to `嗭.Instance.WeaponDefault[b+1]`.

Hashtable keys (NE `PlayerManager.cs:1756-1880`) and how they reach the runtime (NE `噔(int, Hashtable)` at `噔.cs:444-563`, Penta `CombatWeapon.cs:354-412`):

| key | source (NE builder) | runtime field |
|---|---|---|
| 80 | WeaponID | `weaponID` |
| 98 | type, resolved by sname prefix (OHCA_/THCA_/HG_/MG_/FL_/GG_/SG_/RL_/GL_/SR_/SNG_/BL_, `MainNetworkController.cs:3518-3570`) | `type` |
| 92 | clip ammo (`ResolveBattleClipAmmo`) | `maxLoadedAmmo`; `loadedAmmo = max` |
| 91 | total ammo (`ResolveBattleTotalAmmo`) | `maxAmmoReserve`; `ammoReserve = 91 - 92` |
| 97 | velocity | `velocity` (rockets/grenades) |
| 90 | LifeTime | `Life` (grenade steps, sticky lifetime) |
| 96 | distance (`rad`, else defaults) | `distance` (melee reach, cone, explosion) |
| 95 | `ang` (float) | `angle` (cone half-angle, radians) |
| 99 | SystemName | `systemName` (model child name, `Attributes` = 3rd `_` token) |
| 87 | deviation (`accuracy`, else `dev`, else defaults) | `deviation` |
| 94 | rapidity ms (`attackspeed`, else `rap`) | `shotTime = (v+10)*10000` ticks (min 110 ms in NE, 100 in P) |
| 93 | reload ms | `reloadTime = (v+10)*10000`; complex reload (SG/RL/GL/BL with clip ≥ 3): `reloadSingleTime = reload/clip + 10` |
| 79 | modifiers hashtable: {78: speed%, 75: canLaunch (gatling alt spin), 76: isShaking} | `speedValue = 1 + v/100`, `canLaunch`, `isShaking` |
| 88 / 86 | max / min damage (NE only; `GetBattleWeaponDamage` `PlayerManager.cs:3483-3549`) | used by offline damage only |
| 110–124 | workshop stars/percents (NE) | informational; applied in the builder |
| 127/130/131/132/133 | workshop/dot tick damage, tick count, impact type, dot damage, ticks | `workshopPeriodicTick*`, `periodic*` (NE `噔.cs:452-501`) |
| 128 | exact attack speed (NE) | `ApplyExactAttackSpeed`: `shotTime = v*10000` (`噔.cs:597-615`) |
| 129 | exact reload (NE) | `ApplyExactReloadTime` (`噔.cs:565-595`) |

Ticks are `DateTime.Now.Ticks`, so 10,000 ticks = 1 ms.

**Rebuilder rebalancing in NE (not original).** All of these are absent in P, so drop them or decide per weapon; mark them NE-only in the port:
- `噔.cs:617-1017`: the `Is*` weapon checks, `UsesExactBalancePatchAmmo`, `NormalizeLimitedBattleAmmo`, `NormalizeMeleeAmmo`, `NormalizeMeleeCombatValues`, `NormalizeWeaponTimings`, `ScaleTimingTicks`.
- `MainNetworkController.cs:3571-3940` and the PlayerManager twins: the `ResolveBattle*` fallbacks.
  - Examples: shotgun clip forced to 3/18, pistol to 6/30, MG deviation forced to 8, sniper reload ≥ 1300.
  - Hard-coded reload times keyed by Russian weapon names (`TryResolveExactWeaponReloadTime` `:3836-3900`).

**Other weapon constructors:**
- `噔(int, int id, 啁, string)` at `:403-409` is the `GetWeaponByType(203)` kamikaze stub.
- `噔(int, 啁, shot, reload, clip, vel, life)` at `:411-442` is used for the zombie claws: `WeaponController.InitZombieWeapon` (`WeaponController.cs:86-94`) builds it with `Distance 8.36`, `Angle 2.05` and `"OHCA_Zombie"`.
- Penta's 7-argument constructor has a bug, `reloadSingleTime = reloadTime*10000` twice (P `CombatWeapon.cs:351`).

**Who calls it.** `CombatPlayer.InitWeapon()` (NE `CombatPlayer.cs:1615-1635`, P `:1208`) loops over slots 0..6 and calls `ShotController.InitWeapon(i, weaponInfo[i])`. A zombie in slot 0 calls `InitZombieWeapon`. Then `combatPlayerSecurity.InitWeapon` runs (anti-cheat). Remote players use `InitEnemyWeapon()` (`:1637-1650`), which calls `WeaponController.InitWeapon` (third person).

### 1.2 Weapon runtime state machine (`噔` / `CombatWeapon`)

States (`啄`): Changing, Ready, Shooting, Reloading, ReloadingReady, Launching. Line refs are `噔.cs:` (NE) and `CombatWeapon.cs:` (P):

- `CalcMode()` (NE 1158-1211, P 522-568). Changing becomes Ready after `changeTime = 0.45 s`. Shooting becomes Ready (or Launching for GG/FL/SNG/ACID) after `shotTime`. Reloading becomes ReloadingReady after `reloadSingleTime`, then Ready plus the ammo commit after `reloadTime`.
- `CanFire()` (NE 1272-1303, P 570-597):
  - GG: `now - lastLaunch ≥ launchTime` (1.5 s; 0.4 s for GG_M249/GG_FNMAG, NE `:518-521`).
  - Stream weapons: only in Launching, and only once `shotTime` has elapsed.
  - RL/GL/BL: may fire in ReloadingReady.
  - Everything else: Ready and `shotTime` elapsed.
- `Fire()` 1305, `Launch()` 1316, `Stop()` 1327, `Change()` 1454.
- `IsWeaponBlocked()` 1342: reloading, or out of ammo (not for melee). `CanReload()` 1351. `Reload()` 1360 returns the rounds to load.
- `ShootAmmo()` 1092, `AddAmmoToReserve` 1071, `UpdateAmmoCount` 1050.
- **NE-only:** sequential per-shell reload with interrupt (`InterruptSequentialReloadForFire` 1221, `UpdateSequentialReloadQueue` 1373, `ConfigureSequentialReloadTiming` 1029). In Penta, ammo is committed once at the end of `reloadTime`; a complex-reload shotgun may only fire during ReloadingReady (P `IsWeaponBlocked` 640).

### 1.3 Switching

NE `ShotController.cs`:
- `Update` (1194-1496): keys 1..7 call `IsWeaponKeyDown` (1160) then `SetWeapon(n)`. Mouse wheel calls `SetPrev`/`SetNextWeapon` (1718-1758). `Q` calls `QuickChange` (2609).
- `SetWeapon(int)` (289-325): stops the gatling, calls `OnChangeWeapon`, `NetworkManager.SendChange(slot, type)`, then `GameHUD.ShowWeapon()`.
- `OnChangeWeapon` (327-378): calls `weapon.Change()`, then the `Change` coroutine (2623-2671):
  - `Zoom = false`
  - `ActorAnimator.ChangeWeaponAnimation(MainCamera/Weapon.Animation)` (`ActorAnimator.cs:1587-1601`): plays `WeaponChangeAnimation` on the `Weapon` container, then queues `WeaponAnimation_ShotgunRun` or `_ShotgunIdle`
  - `ResetReloadWeaponAnimation(new)` plays `Reset` on the model
  - `HideWeapons()`, then sets renderers visible only on the child named `SystemName`
  - auto-reload when the clip is empty
- **Penta difference** (P `ShotController.cs:1552-1604`): waits `0.2 s` before hiding the old weapon's renderers. NE removed the wait. For a 1:1 port, restore the 0.2 s wait. Penta `QuickChange` is at P 1542.
- Offline, `SendChange` (NE `NetworkManager.cs:373-400`) calls `PlayerManager.ChangeWeapon(localId, ht)` (`PlayerManager.cs:4946-5023`). That calls `OnChangeWeapon` again (no-op) and `ChangeWeaponAnimation(actorAnimator, type, sname)` (`:5025-5160`), which picks the body "Bend" clip for third person (see §5).

### 1.4 First-person model lookup (by name; no per-weapon script)

Hierarchy (FL): `Fighter/MainCamera/Weapon/<TypeWeapon>/<SystemName>/…`
- `<TypeWeapon>` is `噔.getName(type)` (NE `噔.cs:1125-1156`): `OneHandedColdArmsWeapon`, `TwoHandedColdArmsWeapon`, `HandGunWeapon`, `MachineGunWeapon`, `FlamerWeapon` (FL, SNG, ACID), `GatlingGunWeapon`, `ShotGunWeapon`, `RocketLauncherWeapon`, `GrenadeLauncherWeapon`, `SniperRifleWeapon`, `BombLauncherWeapon`.
- Each type node carries **`WeaponLook`** (guid `b47c18c4ff83b5838525f6131ad28c68`). Its fields are `sensitivityX/Y 200`, `minX -360`, `maxX 360`, `minY 0`, `maxY 0`, `Active 0`. `Update()` is empty (NE `WeaponLook.cs`), so it is only a marker.
- `InitWeapon` (NE `ShotController.cs:1024-1103`, P `490-566`):
  - finds the `WeaponLook` whose name is `getName()`, or `getName()+"L"`/`"R"` (dual wield), and stores it as `weapon.Transform`
  - for each child: the one named `SystemName` is `SetActive(true)` and shown (slot 0) or hidden; every other child gets renderers off and **`Destroy(child)`** because `喩.Destroy_Geometry = true` (`喩.cs:13`)
  - exception: cold arms in ZOMBIE mode are only `SetActive(false)`
  - **This destroy step matters:** later code uses `GetComponentInChildren<Animation>()` on the type node, and it must find the equipped model.
- Muzzle point: model child `Target`. NE `GetWeaponTargetTransform` (2190) / `GetWeaponModelTransform` (2205); P `weapon.Transform.GetChild(0).FindChild("Target")` (P `ShotController.cs:1052-1060`).
- Visibility helpers (NE `WeaponController.cs`): `HideWeapons` 172, `ShowWeapon` 217 / `ShowOnlyWeaponModel` 249 (falls back to a `StartsWith(SystemName)` match), `setWeaponVisible` 307. `NormalizeVeteranRemoteRenderLayer` (231) is an NE hack for SG_NovapumpOld.

**Models present in FL `Weapon` (all `ne_items` snames have both FP and TP models):**

| Type node | Models |
|---|---|
| GatlingGunWeapon | GG_M134, M134B01–03, GG_M249, GG_N2, GG_FNMAG2, GG_FNMAG |
| BombLauncherWeapon | BL_Sticky, BL_StickyB02 |
| HandGunWeapon | HG_Desert, Desert2, GlockB01_S, Makarov, Glock_S, SIGSauerP226_B, USP2, TT, Walther_R, WaltherP99, DesertB012, Taurus, USP, DesertB01 |
| MachineGunWeapon | 26 models: AK47, AK47B06–08, AK103(_O, D_O), M4(_O, D_O), M16, UMP45old, UMP45Dold_O, UMP45D_O, UMP45, UMP45D2_O, AUG1–5_O, AssaultRifle02/03, UMP45VKKS_O, M4Elite_O, AK47R_O |
| OneHandedColdArmsWeapon | BasebalBat, Icicle_W, Crowbar, Torch_F, Zombie, Candy, Candy2, "Candy (1)" |
| FlamerWeapon | FL_N1, SNG_Snowgun |
| GrenadeLauncherWeapon | GL_Milkor, Milkor_A, GrenadeLauncher03, SnowLauncher, EX41 |
| TwoHandedColdArmsWeapon | Katana_Old, THCA_Katana_B, THCA_Scythe_B |
| RocketLauncherWeapon | RL_M202A1, RPG7, RPG7B02, RPG26 |
| ShotGunWeapon | SG_DB, Winchester1887, Spas, NovapumpOld, Novapump, Remington, Remington_old, plus a stray `Target` |
| SniperRifleWeapon | SR_Arctic, SniperRifle03, M110_B, HK417_D, Steyr, SVD, ArcticB01, SteyrB01, Wildcat1/2, Vintorez2, Laser, Vintorez, Steyrf0rd |

**MonoBehaviours on weapon model objects.** The FL `Weapon` subtree holds:

| Script | Count | guid | Class status |
|---|---|---|---|
| `ArmClothModel` | 9196 | `4380021096a0a7478c7a2377f720d725` | **NE-only**, no Penta class; none in OURS |
| `RuntimeTextureLoader` | 6372 | `318430b40d0cc3047f8246d41cd8baa5` | class exists in OURS |
| `HandTextureLoader` | 274 | `a8dc0c17eb7a9f31d64635e67e4fe511` | none in OURS |
| `WeaponLook` | 11 | | none in OURS |
| `Flamer` | 2 | `382677f5174d5f584ffa2f7db23e4659` | none in OURS |
| `RuntimeTextureSwitch` | 2 | `c6d30dc9b55b8d493c434372211558c3` | none in OURS |

Each model also has a legacy `Animation` on its root. Each model is a full arms rig: `Bip01 Neck/… /Bip01 R Hand/WeaponRight/<mesh>`, plus about 100 `Shirts_*`/`Gloves_*` arm-cloth SkinnedMeshRenderers (`ArmClothModel` WearType 0 = shirt, 1 = glove; the base meshes are `Shirts_torso01` and `Gloves_hands01`).

Examples:
- **MG_AK47** (`…/MachineGunWeapon/MG_AK47`)
  - Root `Animation`: clips `Shot`, `Reload`, `Reset` (from `gamebundle/meshes/weapons/machine_gun/ak47/`), playAutomatically 1, wrapMode 0, culling 1.
  - Mesh `…/WeaponRight/AK47` (localPos 0.079, -0.859, -0.075) with children `Magazine` and `Shutter`. Each has MeshFilter, MeshRenderer and `RuntimeTextureLoader{textureID: ak_47_color, Folder: "Cloth/", materialID: 0}`.
  - `Gloves_hands01`: `HandTextureLoader{textureID: "", Folder: "Cloth/", materialID: 0}`.
  - `Gloves_thanos`: `ArmClothModel{WearType 1, BaseShirtName Shirts_torso01, BaseGloveName Gloves_hands01, BaseOverride []}` plus `RuntimeTextureLoader{CLOTH_Gloves_Thanos}`.
  - `Target` localPos (0.666, 5.706, 4.468).
- **HG_Makarov**
  - `Animation` [`Shot`, `Reload`, `Reset`] (`meshes/weapons/hand_gun/makarov`).
  - Meshes `WeaponRight/Makarov` and `WeaponRight/MakarovShutter`, each with `RuntimeTextureLoader{makarov_color}`.
  - `Target` (0.024, 5.981, 3.465). About 101 ArmClothModel instances.
- **SG_Winchester1887**
  - `Animation` [`Shot`, `Reload`, `Reset`, `ReloadStart`, `ReloadAmmo`, `ReloadEnd`] (`meshes/weapons/shot_gun/winchester1887`), **playAutomatically 0**.
  - Meshes `Winchester1887` (child `Winchester1887_BulletShaft`), `Winchester1887_ShutterLever` and `Cylinder01`, with `RuntimeTextureLoader{winch_1887_color}`.
  - `Target` (0.706, 5.604, 4.396).
- **FL_N1 / SNG_Snowgun**
  - `Flamer{scale 1, speed 1}` on `WeaponLeft/flamethrower_n1` (and `WeaponLeft/SNG_Snowgun`).
  - Clips [`Shot`, `Reset`, `Start`, `Load`, `End`].
  - SNG also has `RuntimeTextureSwitch{textureID snowgun_Color, Folder Cloth/, materialID 0, textureIDList []}`.
- THCA_Katana_B / Scythe have a `RuntimeTextureSwitch` on `WeaponRight/Katana` (`SwitchWeaponTexture`, `噔.cs:1038`).

Other animation clip sets:
- GG_*: [`Shot`, `Load`, `Start`, `End`, `Reset`].
- RL_M202A1 / GL_* / BL_Sticky / SG_Novapump / Spas / Remington: also `ReloadStart`, `ReloadAmmo`, `ReloadEnd`.
- OHCA_*: [`Shot`, `Reset`]. OHCA_Zombie: [`Shot`, `Idle`, `Reset`].
- `Weapon` container: [`WeaponChangeAnimation`, `WeaponAnimation_ShotgunIdle`, `WeaponAnimation_ShotgunRun`, `Shot`, `WeaponLoadAnimation`] from `game/animations/weaponanimations/*`.

### 1.5 HUD weapon bar and ammo

NE `GameHUD.cs`:
- `ShowWeapon()` (866) sets `hideWeaponTime = now + offsetShowWeapon`.
- `DrawUserStats()` (897-981):
  - calls `battleGUI.SetAmmo(loaded, maxLoaded, reserve)`; melee and zombie pass (0,0,0)
  - while `hideWeaponTime > now`, draws 7 slot icons through GUILayout using the `BattleIcoWeapon` GUISkin
  - slot styles are `ws1`..`ws7` / `ws{n}active` with key labels `wsTxt` / `wsTxtActive`
  - the rect is screen-centre minus 325, bottom minus 155, size 650x56; at width ≥ 1015 it is x minus 250, bottom minus 60
- The skin is `Resources.Load("GUI/Skins/BattleIcoWeapon")` (`嘄.cs:71-80`). It exists at `OURS/Game/Resources/gui/skins/BattleIcoWeapon.guiskin`.
- The older 5-slot ammo/reload-bar block (3360-3440) is **dead**: `weaponStylesInit` is always false (`InitWeaponStyles` 391-394).
- Penta equivalents: P `GameHUD.cs` `ShowWeapon` 481, `DrawUserStats` 491 (`ws1`…), `Zoom` 349.
- `BattleGUI.SetAmmo` (NE `BattleGUI.cs:700-735`) sets `ammoText` = loaded, `ammo2Text` = reserve, and the `ammoStats` bar = `1 - a/b`. OURS `Game/Scripts/NEHud/BattleGUI.cs` already has `SetAmmo` (:227) and `SetZoom` (:148), with a matching guid.
- Ammo strings: `ShotController.GetAmmoCountString` 275 and `GetAmmoCount(i)` 3048 (reserve + loaded).

**Crosshair.**
- `SetCrossHair` (NE `ShotController.cs:733-794`) loads `Resources.Load("GUI/Battle/Crosshairs/crosshair_0"+type)`, falling back to `crosshair_00`. The textures exist in `OURS/Game/Resources/gui/battle/crosshairs`.
- The crosshair rotates through the `CrossHairRotationAnimation` clip (`AnimateCrossHair` 398-458).
- `CheckWeaponState` (1760) runs every 50 ms: Blocked, or `StraightScan` gives Ready / ReadyToFire.
- NE configurable procedural crosshair (505-800, `ContraCity/CrosshairAlphaTint` shader) is an NE addition.

---

## 2. Firing, ammo, reload and sniper zoom

### 2.1 Input and fire loop

`ShotController.Update` (NE 1194-1496, P 579-782):
- Fire1 (Mouse0) press sets `shot = true`; Mouse1 sets `shotAlt`; R reloads.
- With `shot`:
  - blocked: `EffectManager.weaponBlockEffect` (an empty method, NE `EffectManager.cs:1023`)
  - stream weapon (GG/FL/SNG/ACID): `weapon.Launch()` then `DoLaunch()`
  - otherwise `weapon.Fire()` then `DoShot()`
- FL/SNG release: `Stop()` then `DoStop()`. GG: alt-fire spin if `CanLaunch`. BL alt-fire: `DoBlow()`.
- Taunts (E/T/Y) and kick voting are mixed into the same loop. Drop `KickManager` (network); taunts are separate.

### 2.2 Ray, spread and deviation

`DoShot` (NE 1892-2188, P 995-1315):
- `radius = Deviation/500` (viewport units). Sniper zoomed: 0.
- Non-shotgun: `rx, ry = U(0, r) - r/2` (a square).
- NE shotgun: a disc of radius `min(r/2, 0.014)`. This is an NE change; P uses the square for all weapons.
- `ray = Camera.main.ViewportPointToRay(0.5+rx, 0.5+ry)`.
- Penta anti-cheat inside `DoShot`: `deviationMultiplier != 500` triggers `SendEnterBaseRequest(7)`; zero deviation on GG/HG/MG unzoomed triggers the same (P 1069-1083). **Drop.**

### 2.3 Per-type branch (`switch (byte)type`)

| Type | Ray/projectile | Target filter | Local effect | FP anim | Camera shake (FPSCamera.Shake) |
|---|---|---|---|---|---|
| 1/2 melee | `new Shot(o + d*Distance)` LAUNCH, sent, then `DelayedShot` after 0.2 s (NE 2247-2291, P 1317-1332): `StraightShot(center ray, maxDist = Distance)`, `Targets.Clear()`, `SegmentShot` cone, `SendShot` (SHOT) | cone | `batEffect` via `ShotEffect` | `"Shot"` on weapon.Transform | – |
| 3 HG | `StraightShot` | `GunShot` | `machineGunEffect` | `"Shot"` on `HandGunWeapon` | if `Attributes` contains "S" or `[79][76]`: random (-1,0,0) or (0, ±1, 0) |
| 4 MG | `StraightShot` | `GunShot` | `machineGunEffect` | `"Shot"` on `MachineGunWeapon` | – |
| 5/11/12 FL/SNG/ACID | `StraightShot` (skipped when `PlayerManager.WaterBlock`) | `SegmentShot` | `flamerEffect` | – | – |
| 6 GG | `StraightShot` | `GunShot` | `machineGunEffect` | `"Shot"` | – |
| 7 SG | `StraightShot` | `SegmentShot` | `shotGunEffect` | `"Shot"` on `ShotGunWeapon` | (-2,0,0); airborne shotgun jump: `CharacterMotor.SetExplosionForce(shot.Origin, Jump * (NE ×0.931, NE cooldown 0.45 s) × (Novapump 0.7))` |
| 8 RL | `RocketShot(ray, Target, type, Velocity)` | – | `rocketLauncherEffect` | `"Shot"`, played in `ShotEffect` ME_AFTER_SERVER (`PlayerManager.cs:9847-9862`) | – |
| 9 GL | NE: `GetFovIndependentLobRay` (1881, aim +0.15 viewport y, FOV-normalised); P: viewport (0.5+rx, **0.65**+ry). Then `GrenadeShot(..., Velocity, Life, stick=false)`; NE uses `VorchunGrenadeShot` for id 104/45 | – | `grenadeLauncherEffect` | `"Shot"` (ShotEffect) | – |
| 10 SR | `StraightShot`; **P: `SR_Wildcat*` uses `CrossbowShot`** (P 1196-1205, NE dropped this) | `GunShot` | `machineGunEffect` | `"Shot"` on `SniperRifleWeapon` | NE (-1, -0.35, 0) additive; P random (-3..-1, -2..-1, 0) additive |
| 15 BL | `CheckBlow()` (3 sticky max: detonates the oldest), lob ray, `GrenadeShot(stick=true)` | – | `bombLauncherEffect` | `"Shot"` (ShotEffect) | – |

**Shot geometry.** `啫.StraightShot` (NE `啫.cs:912-1080`, P `ShotCalculator.cs:213-395`):
- Layer mask `~772`: layers 2, 8 and 9 are ignored (bots use `~516`).
- P: `Physics.SphereCast(r=0.2)`. NE: `CastPreciseShot` (417) prefers an exact raycast on actor bones and ignores dead players.
- Hit-name switch: `Bip01 *` / `Ship*` is a player; `Grenade`, `Mine` and `Turret` are PLAYER_ITEM; `Monster` is a BOT.
- Hit zone: NE `GetPlayerHitZoneByHit` 640-691 (`Bip01 Head` / `ShipCabin` = CABIN; groin = ENGINE).
- `maxDistance` check: `d == 0` means unlimited.
- Returns `Shot(hit.point, hit.normal, type)` with an optional target.
- **Anti-cheat** (drop): `CheckNickname` / `SendEnterBaseRequest(28)`, and the layer-mask check `SendEnterBaseRequest(26)` (NE 990-1000, 1070-1078).
- `TryGetBlockingGeometryBeforePlayer` (431) is NE-added.

**Target filters:**
- `GunShot` (NE 2161, P 1634) clears the target when it is a teammate.
- `SegmentShot` (NE 2173-2277, P 1646-1870) is a cone:
  - origin is player pos + (0, 3.5, 0)
  - range is `weapon.Distance` (NE: 18 for FL/SNG, `GetSegmentDamageDistance` 82)
  - angle ≤ `weapon.Angle` (radians), plus `CheckShotRay` line of sight
  - TD bots use Distance/5
- **NE shotgun** keeps only the direct target or one "assist" target (`TryAddShotgunAssistTarget` 512, max 55 u). **P** adds every cone target besides the direct hit. Port P for 1:1.

### 2.4 Fire rate and recoil

- **Fire rate:** `shotTime` (from [94]) is gated in `CanFire`. `AddRapidity(type, ms)` (NE 2673) adds to it (pickups and enhancers).
- **Recoil:** there are no recoil animations on the body. FP recoil is the model's `Shot` clip, played by `ActorAnimator.ShotWeaponAnimation(name, t)` (NE `ActorAnimator.cs:1502-1565`):
  - sets speed 1.5, time 0, `Play(StopSameLayer)`, then queues `Idle` (loop) if present
  - `"Load"` plays Start then Load; `"Spin"` plays Load; `"Stop"` plays End; all at speed 1.5
  - P `ActorAnimator.cs:1236-1281`
- **Camera shake:** `FPSCamera.Shake(v, additive)` (NE `FPSCamera.cs:133-152`, P ~160-180).
  - `ProccessShaking` (NE 117-131) decays it each frame: `|v| × 0.1 × dt × 50`, threshold 0.01.
  - It is applied in the `FPSRotation` setter: `localEulerAngles = fpsRotation + shakeRotation` (NE 98-114, P 90-106).
  - SoldierCamera sets `FPSRotation` (P `SoldierCamera.cs:496`).
  - OURS has its own FPSCamera.cs with a different guid (`93e5…` vs original `6650071b09e3cfd98cdeda8341ac0e6c`).

### 2.5 Muzzle flash, tracers, impacts and sound

`EffectManager.machineGunEffect` (NE 1762-1940, P 1305-1456), shared by HG, MG, SR and GG:
- Locates the model's `Target` (or the L/R pair for dual weapons).
- Shot sound on `player.Audio`:
  - HG: `Glock_Shot` for HG_Glock_S, else `Makarov_Shot`
  - MG: `AK47_Shot`; `UMP45D_Shot` / `UMP45_Shot` for MG_UMP45* (note the P inversion bug, faithfully kept in NE); `UMP45_Shot` for MG_AUG (NE adds AssaultRifle03 and AK47R). Then `PlayAfterSeconds("<name>1", shotTime/2e7)`, the second half of a two-shot sound.
  - SR: `SVD_Shot`; `Crossbow_Shot` for Wildcat; NE adds `SR_Steyrf0rd`
  - GG: `M134_Shot` or `M249_Shot`, PlayLoop
  - SG: `Winchester1887_Shot` or `DB_Shot`
- **Wall impact** (no target): `sparkEffect(origin, normal, machineGunSparkPrefab)`, then `"BulletImpact"` on the spark's own AudioSource.
- **Muzzle flash:** `muzzleSparkEffect(Target, machineGunShaftSparkPrefab, isMe)` instantiates, parents to Target at local 0 and sets the layer to the weapon layer when isMe (NE 1963-1975, P 1458-1474).
- **Tracer:** `tracerEffect(Target.pos, shot.Origin, machineGunPrefab, false, width, time)` (NE 2018-2041, P 1524-1552). The prefab carries the `Tracer` component with `FlatSpeed 11` and a TrailRenderer whose width/time are set per type: HG 0.03/0.02 (NE), MG 0.1/0.03, SG 0.07, SR 0.1/0.1, GG 0.1 (M249 0.2).
- Wildcat crossbow: `CharacterManager.GetPrefab("CrossbowBoltPrefab")`, then `arrowTracerEffect` (ArrowTracer, FlatSpeed 9).
- NE-only `GetBattleRemoteTracerOrigin/Target` (1494-1537) is HTTP-battle remote correction. **Drop.**

**Shotgun pellets are visual only.** `shotGunEffect` (NE 1539-1610, P 1083-1151) draws 6 pellets: spark + tracer around `shot.Origin`.
- spark: `laserSparkPrefab` if there are targets, else `machineGunSparkPrefab`
- tracer: `machineGunPrefab`, width 0.15, time 0.03, randomShift (FlatSpeed 6)
- P spread: cube `|Origin - muzzle| × 0.07`; NE: disc `× 0.022`
- Damage is one shot (§2.3).

**Bullet holes and decals.** The original has **none**:
- NE `bulletImpactMarkEffect` (953-995) builds procedural disc meshes (`CC_BulletImpactMark*`), but it is a rebuilder addition and **never called**.
- `Explo` prefabs carry a `Projector` (scorch). It is disabled for BL and by quality/distance (`launcherEffect`).

**Hit on a player:**
- `EffectManager.HitEffect` (NE 3157-3201, P 2969) instantiates `healthHitPrefab` (shader `Particles/Multiply`) at the hit point, parented to the target. It plays `Bat_Shot_Enemy` except for explosives.
- Called from `PlayerShot` when damage > 0 (`PlayerManager.cs:9516-9519`).
- The shooter's damage popup is `PlayerManager.ShotDamage` (10001); local feedback is `GameHUD.ShotMe` / `IndicateDamage`.

**Shell ejection:** none in the code. Shell visuals, if any, live inside the `Shot` clips or the shaft-spark particles (uncertain).

**SoundManager** (NE `SoundManager.cs:80-166`, P ~85-125):
- `Play(source, name, mode)` loads `Resources.Load("Sounds/"+name)` and caches it.
- Modes: Play = PlayOneShot; PlayLoop = set clip and loop; PlayStop = stop then one-shot; Stop.
- Skipped when the source GameObject is inactive.
- `PlayAfterSeconds` at NE 387.
- OURS `Game/Scripts/Audio/SoundManager.cs` has a different guid (`f299…` vs `28556f8a…`).

### 2.6 Ammo and reload

**Consumption.** `ShotController.OnShot(type)` (NE 2920-2969, P 1662-1688) calls `ShootAmmo()` and `GameHUD.ShowWeapon()`.
- At 0 loaded with reserve > 0: auto-reload. NE does this through a coroutine (`QueueAutoReloadAfterFinalShot` 2971-2997).
- With no reserve: `SetNextWeapon()`, except for BL.
- Melee never consumes ammo.

**Reload.** `ShotController.Reload(w)` (NE 2475-2504, P 1520-1540):
- `w.Reload()` returns n, then `SendReload` (network).
- `ActorAnimator.ReloadWeaponAnimation(w.Transform, complex, n[, speed])` (NE `ActorAnimator.cs:1407-1469`, P 1175-1203):
  - simple: plays `Reload` (P: `Play(Queue)`, speed 1)
  - complex: `ReloadStart`, then `ReloadAmmo` × n, then `ReloadEnd` (all queued, speed 1 in P)
  - NE adds a speed multiplier (`GetReloadAnimationSpeedMultiplier` 2529-2607; shotgun 0.92, Remington tuned to 2.41 s), a Taurus `Reset` queue and an animation-length input block (`reloadAnimationBlockUntil`). All NE tuning.
- Then `EffectManager.reloadEffect(type, player, sname, n)` (NE 1612-1713, P 1153-1254):

| Weapon | Reload sound |
|---|---|
| HG | `Glock_Reload` for HG_Glock_S, else `Makarov_Reload` |
| MG | `TMP_Reload` for MG_UMP45 / MG_AUG*, else `AK47_Reload` |
| SG (not DB) | **multi-stage:** `Winchester1887_ReloadStart`; then for k < n (0 becomes 2) `PlayAfterSeconds("Winchester1887_ReloadAmmo", 0.3 + 0.5k)`; then `Winchester1887_ReloadEnd` |
| SG_DB | `DB_Reload` |
| RL | `RPG26_Reload` |
| SR | `Steyr_Reload` for SR_Steyr / SR_Arctic; `SVD_Reload` otherwise; Wildcat silent |
| GL/BL SnowLauncher | `SnowLauncher_ReloadStart`; `…_ReloadAmmo` at 0.7, 1.7, …; `…_ReloadEnd` |
| BL Sticky | `Steyr_Reload` × n at 0.7 + k |
| GG, FL | silent |

- Remote reload: `PlayerManager.ReloadWeapon` (NE 4894-4944, P 1536) calls `reloadEffect` plus the body clip `ActorAnimator.ReloadWeaponAnimation(string typeName, 噔)` (NE `ActorAnimator.cs:1274-1365`): `Blend("<X>ReloadClip", 1, 0.3)` (§5).
- Ammo pickups: `AddAmmoToReserve(type, amount[, all])` (NE 2685-2739, P 1618-1648). The NE "50/100 % of max" logic at 2702 is NE.

### 2.7 Sniper zoom

- `FPSCamera.Update` (NE 275-301): with `zoomAllowed()` (`ShotController.cs:3026`: SR, or `Attributes` contains "O") and the Zoom key (Mouse1), toggles `Zoom(on)` (NE 378-397).
- Zoom factor, `GetSniperZoomFactor` (3031-3046):

| Weapon | Factor |
|---|---|
| SR | 0.2 |
| SR with attribute "D" | 0.15 |
| "O" optic | 0.5 |

- `ApplyFieldOfView` (NE 352-376):
  - main camera FOV = base FOV × factor; weapon camera FOV = 60 × factor
  - hides `WeaponCamera` when zoomed with factor < 0.5
  - also sets `SoldierCamera.zoomFactor` and `SoldierController.zoomFactor` (mouse sensitivity)
- `ShotController.Zoom` setter (147-168) calls `GameHUD.Zoom`, which calls `BattleGUI.SetZoom` (NE `BattleGUI.cs:520-533`): the `Zoom/ZoomOverlay` mesh is scaled to the ortho size.
- Zoom zeroes deviation for SR (`DoShot`). Weapon change forces `Zoom = false`. `DoShot2x` is empty.
- The NE base FOV (`喞.FpsFieldOfView`, with zombie and zoom tweaks at 325-347) is partly NE.

---

## 3. Melee, shotgun, rockets, grenades and sticky bombs

### 3.1 Melee

- Melee `DoShot` and `DelayedShot` are covered in §2.3.
- Distance and angle come from [96]/[95]. NE clamps: OHCA 8 (≤ 16), THCA 9, angle 2.05 if invalid (`NormalizeMeleeCombatValues`). Zombie claw: 8.36 / 2.05, shotTime 700 ms.
- Sounds (`batEffect`, NE 1715-1738, P 1256-1281):

| LaunchMode | Effect |
|---|---|
| LAUNCH | `Bat_Launch` |
| SHOT with targets | `Bat_Shot_Enemy` (PlayStop) |
| SHOT without targets | spark `laserSparkPrefab` (scene: `BatSparkConcrete_15`) plus `Bat_Shot_Stone` |

- Remote body: `ShotWeaponAnimation("Bat" | "Katana" | "Zombie")` blends `"<X>ShotClip"` at speed 2 / 1.75 / 1.5 (NE `ActorAnimator.cs:1567-1585`), triggered from `ShotEffect` for LAUNCH (`PlayerManager.cs:9731-9750`).
- `CreateShortMeleeShot` (2293) is dead NE code.

### 3.2 Rockets

`RocketShot` (NE `啫.cs:1437-1475`, P `ShotCalculator.cs:746-787`):
- P: raycast to the aim point, spherecast r=0.5 from the muzzle. `launchTS = now + 200 ms`; `landing = launch + 100 × dist / velocity` ms.
- NE: 150 ms delay; fallback 220 u; `vel = max(v, 20)`, scaled `min(v×1.15, v+8)`; minimum 120 ms.

Effect and tracer:
- `rocketLauncherEffect` (NE 2092-2130, P 1597-1642): `RPG26_Shot`; `rocketLauncherShaftSparkPrefab` on Target; instantiates `whizbangPrefab` (scene: `rocket_15.prefab`) with `RocketTracer.Launch(shot, player, isMe)`; `player.RegisterItem(tracer)`.
- `RocketTracer` (NE `RocketTracer.cs:1-192`, P 163 lines):
  - `FixedUpdate` lerps `StartOrigin` to `Origin` by network time
  - every 100 ms (owner only) `PlayerManager.Scan(pos, 4)` for a proximity blow
  - `Blow` (145-166): `Shot(pos, dir, type)` with LaunchMode BLOW and the original TimeStamp, `啫.RadialShot(player, shot, false)`, `SendShot`, `UnregisterItem`, `Destroy` after 600 ms
  - uses legacy `ParticleEmitter.emit` and shader swaps (`Particles/Alpha Blended`, `Mobile/Diffuse`)

**Explosion damage area.** `RadialShot` (NE 1767-1866, P 1017-1231):
- **P radius:** `weapon.Distance × 1.5` ([96]); rocket fixed **30**. The loop includes the shooter (self damage was server-computed).
- **NE radius:** `GetExplosionRadius` (291-304) / `GetRadialDamageRadius` (212-239): RL 13.5, GL 9, BL 8 (× up to 1.18 if Distance > 350/300); GL id 45 → 20, id 104 → 13; clan enhancer ×1.1.
- NE `AddSelfExplosionTarget` (2105-2128): self within radius (player pos + 2y) for RL/GL/BL.
- NE `EnsureProjectileTargetDamage` adds a fallback EnergyDamage of 51/44/44 when no damage is set.

**Explosion visual and force.** `PlayerShot` (BLOW) calls `ShotEffect` (ROCKET/GRENADE/BOMB cases, `PlayerManager.cs:9845-9935`), which calls `EffectManager.launcherEffect` (NE 2619-2735, P 2364-2464):
- Instantiates `exploPrefab` (scene: `Explo_15`); `ExploSnowPrefab` for SnowLauncher.
- Projector off for BL, and off by quality distance² (Fast 0, Good 24000, Simple 48000, Beautiful 90000).
- Plays `RPG26_Explosion` on the explosion's AudioSource.
- **Knockback on the local player** through `CharacterMotor.SetExplosionForce(pos - (0, off, 0), F)`:

| | Radius R | Force F | Vertical offset | Modifiers |
|---|---|---|---|---|
| P | `Distance × 1.5` (RL 30) | `(R - d) × 1.5` | 5 | zombie Boss /3, Regular /2 |
| NE | 10.5 for RL/GL, else 14/9/8 fallback | `(R - d) × factor`, factor RL 6.2, GL 3.1 (fallbacks 0.45/0.3/0.22) | 6 | Vorchun ×1.2, RPG7B02 /3 (remote), BL ×0.33, Regular zombie ×0.7 extra |

  - An unhit BL explosion skips the force.
- NE extra rocket jumps are dead or no-op: `ApplyMelsRocketJumpImpulse` (`ShotController.cs:1800`, never called) and `ApplyNoSelfDamageRocketJump` (`IsNoSelfDamageRocketWeapon` returns false).

### 3.3 Grenades

`GrenadeShot` (NE 1592-1660, P 789-907) simulates a stepped trajectory:

| Parameter | P | NE |
|---|---|---|
| steps | `life/100` | `life/50`, clamped 22..60 |
| gravity per step | 0.2 | 0.16 |
| bounce retention | 0.3 | 0.2 |
| stop below | 0.5 | 0.45 |
| velocity | `v` (≤ 0 becomes 2) | GL `v≥15 ? clamp(v, 9, 18) : 9` |

- Stick (BL) stops on the first non-`Bip`/`Smerti` hit.
- Landing = `now + 200 + 1000 × steps / 20` ms; `shot.Trajectory` holds the points.
- NE `VorchunGrenadeShot` (1507) is NE-only.
- `grenadeLauncherEffect` (NE 2221-2262, P 1751-1798): `SnowLauncher_Shot` (used for all GLs); `grenadeLauncherShaftSparkPrefab`; `CharacterManager.GetPrefab("GrenadePrefab")` or `"SnowballPrefab"` (Snow), each with `GrenadeTracer`.
- `GrenadeTracer.FixedUpdate` (NE 139-204) interpolates along the Trajectory. The owner scans `Scan(pos - 3.5y, 6)`. `Blow` (206-227) mirrors the rocket.

### 3.4 Sticky bombs (BL)

- `bombLauncherEffect` (NE 2167-2219, P 1687-1749): `SnowLauncher_Shot`, `grenadeLauncherShaftSpark`.
- Prefab: `mortarWhizbangPrefab` (scene: `GrenadePrefab_15`), or `CharacterManager.GetPrefab("BombPrefab")` when the model name contains "Sticky". The mesh is tinted by team: red (1, .32, .32) or blue (.27, .52, 1).
- `BombTracer` (NE `BombTracer.cs:1-163`): follows the trajectory, then sits until `landing + life` ([90]) and blows. `InitBlow()` is the manual detonation.
- Alt-fire `DoBlow` (`ShotController.cs:2361`) detonates all of the player's bombs. Firing a fourth calls `CheckBlow` (2389), which blows the oldest. The bombs are shootable PLAYER_ITEM targets (`Grenade` collider name).

---

## 4. Flamer, snowgun, gatling spin, gym and workshop stats

### 4.1 Flamer, snowgun and acid thrower

- The mouse press calls `Launch()`, then `DoLaunch()` (NE 2331-2359, P 1334-1364): `ShotWeaponAnimation("Load")` (plays Start, then queues Load) and `"Flamer_Launch"`.
- While held, each `shotTime` fires (case 5/11/12): `StraightShot` plus `SegmentShot` (cone).
- `flamerEffect(shot)` (NE 2771-2790, P 2500-2519): `Snowgun_Shot` if the sname contains "Snowgun", else `Flamer_Shot`. Then `Flamer.fire(true)` on the `Flamer` component in children.
- `Flamer` (NE `Flamer.cs:1-69`):
  - toggles every legacy `ParticleEmitter.emit` under the flamer model
  - auto-off after 0.3 s without refire
  - `Start` swaps the shaders to `Particles/Additive`
- Release: `Stop()` then `DoStop()` plays `"Stop"` (End clip). `flamerEffect(BLOW)` calls `fire(false)` (`ResetWeapon`).
- The ACID branch uses `FT1`/`FT2` sounds (`acidThrowerEffect` 2737). No ACID weapon exists in the data; ignore it.
- Water: `PlayerManager.WaterBlock` suppresses shots.
- NE limits stream damage to 18 u (`FilterCloseRangeStreamTargets` 189). P uses `weapon.Distance`.

### 4.2 Gatling spin-up (GG)

**Weapon state:** `launchTime` is 1.5 s (0.4 s for M249/FNMAG); `CanFire` is false until it has elapsed.

**Inputs:**
- **Fire press:** `Launch()`, then `DoLaunch`:
  - sends a `Shot` with LAUNCH; the crosshair rotates
  - plays `"Load"` (Start, then Load) and `gatlingGunEffect(LAUNCH)`, which plays `M134_Launch`
- **Held after spin-up:** `DoShot` case 6, with `M134_Shot` / `M249_Shot` on PlayLoop.
- **Mouse1 with `[79][75]` (`canLaunch`):** `DoSpin` (NE 2418-2436) plays `"Spin"` (the Load clip) and `M134_Spin` on PlayLoop, so the barrel spins without firing.
- **Release:** `DoStop` (NE 2438-2469):
  - sends BLOW; plays `"Stop"` (End clip at speed 1.5)
  - `gatlingGunEffect(BLOW)` plays `M134_Stop` with PlayStop

**Remote side:** `ShotEffect` handles the GG LAUNCH/BLOW sounds (`PlayerManager.cs:9752-9772`).

**Ammo:** MG-ammo pickups also feed GG (`GetType() == 6 && a == 4`, NE 2725-2739).

### 4.3 Movement speed with weapon

`GetSpeedMultiplier` (NE 3108-3125, P 1808):
- base is `max(1, speedValue)` (from `[79][78]`)
- ×1.25 for OHCA or `Attributes` "R"
- `CheckSpeedMultiplier` (3127) is an anti-cheat helper

### 4.4 Workshop (mastery upgrades)

**NE data** (`Weapon.cs:296-367, 708-755`):

| JSON field | Weapon property |
|---|---|
| `mastcost` | `WorkshopCost` |
| `mastcritchance` | `WorkshopCritChancePercent` |
| `mastspeed` | `WorkshopSpeedPercent` |
| `mastpatron` | `WorkshopClipAmmo` |
| `mastzapas` | `WorkshopReserveAmmoBonus` |
| `mastreload` | `WorkshopReloadTime` |
| `mastdotdamage` | `WorkshopDotDamage` |
| `mastdotticks` | `WorkshopDotTicks` |

`HasConfiguredWorkshopStats` is true when any `mast*` field is present.

**When it applies:** only while `UpgradeID > 0` and the upgrade has not expired (`IsActiveWeaponWorkshopUpgrade`, `MainNetworkController.cs:3188`).

**Configured stats (`TryAddLocalWeaponForBattle`, `PlayerManager.cs:1690-1880`):**
- clip = `mastpatron`
- reserve = `(total - clip) + mastzapas`
- `[93]` and `[129]` = `max(50, mastreload)`
- `[79][78]` = `mastspeed`
- `[119]` = crit chance
- `[127]` / `[130]` = dot damage and ticks

**Fallback when no `mast*` fields exist:** stars and percents parsed from the Russian upgrade text (`ResolveWorkshop*`, 1945-2010), all NE heuristics:
- rapidity −3 % per star (`ApplyWorkshopPercentDecrease`, floor 50)
- distance +3 % per star
- clip +20 %, reserve +25 %, speed +5 %, crit +10 %, dot +50 %

In Penta the server applied upgrades, and the client only saw the final [92]/[94]/… plus `[79]`.

### 4.5 Gym

- Gym entries are `ne_items.json` `gym`: 16 skills with 5 steps each (health, armour, reload speed %, damage reduction, …; the text encoding is mangled in the JSON).
- No gym effect is applied in the weapon runtime (`噔` / ShotController / EffectManager): no matches in those files. Gym is referenced only in `WalkController.cs` and the UI/data classes `嘋`, `嘎`, `嘓`. In the original these were server-side stats. **Uncertain:** whether the gym's reload percentage reaches [93] on the server.

### 4.6 Offline damage (NE only; Penta was server-authoritative)

`PlayerManager.PlayerShot` (NE 9377-9536). For each PLAYER target, when the local player is the shooter, offline mode is on (`嗔.Enabled`), and the two are opponents:
1. If the target carries no damage: `dmg = ResolveBattleServerDamage(shooter, victim, shot, hitZone)` (8637-8662).
   - `[88]` from the shooter's `WeaponInfo` × the hit-zone multiplier (CABIN 1.6, ENGINE 1.2, BASE 1; `GetHitZoneDamageMultiplier` 8610), × 0.85 if the victim is a zombie, clamped 1..5000.
   - If `[88] ≤ 0`, use **`ComputeOfflineDamage(type, zone, zombie)`** (8912-8958): base table × zone multiplier (× 0.85 zombie), clamped 1..200:

| Weapon type | Base damage |
|---|---|
| melee | 30 |
| HG | 14 |
| MG | 12 |
| GG | 10 |
| SG | 28 |
| SR | 70 |
| RL | 53 |
| GL/BL | 48 |
| FL/SNG/ACID | 8 |
| other | 15 |

   - Then `+ GetLocalWearWeaponDamageBonus` (8664-8720): wear stats such as `pistol_close_damage_bonus` and `w<ID>_<tier>_damage_bonus`. The distance tier is close ≤ 0.34 × Distance, middle ≤ 0.67, otherwise long.
   - Then `ApplyTargetWearDefense` (8770-8860): the victim's wear defence keys 72..92 by type and weapon; total clamped 0..85 %; `dmg × (100 - def)/100`, minimum 1.
   - Result stored as EnergyDamage.
2. `ApplyOfflineDamage` (8977-9020): when HTTP battle is active, `BattleServerHttpClient.RequestDamage` (drop). Otherwise `ApplyDamageArmorFirstAndReport` (8467-8491) takes energy (armour) first, then health. When health reaches 0, `KillPlayer(victimId, {94 killer, 91 type, 68 zone})`. In zombie mode the hit is infected instead (`InfectOfflineTarget` 8960).
3. `StartOfflinePeriodicImpact` / `ProcessOfflinePeriodicImpacts` (9217 / 9279): DoT from `[131..133]`.
4. When the local player is hit by an offline bot: `ApplyDamageArmorFirst(LocalPlayer, …)`; at 0, `KillMe`. Also `GameHUD.ShotMe` and `ShotForce` (9620+, hit impulse; RL/GL excluded).
5. Bots: `OfflineBotSpawner` builds bot weapon info (`OfflineBotSpawner.cs:212, 350-370`).

`Shot.Crit` is carried, but no crit roll was found in the weapon code. **Uncertain:** whether crits roll anywhere offline.

---

## 5. Weapon on the third-person body

**Mounts (EFL):** `Enemy_Fighter_locomotion/Bip01/…/Bip01 R Hand/WeaponRight/<TypeWeapon>` (`WeaponLook`).
- Under it sit the `<SystemName>` children as **static meshes**: MeshFilter, MeshRenderer and `RuntimeTextureLoader`, plus a `Target` child. No Animation.
- Example: `MachineGunWeapon/MG_AK47` (and children `Magazine`, `Shutter`, `Target`).
- The TP set has a few extras: OHCA_BasebalBat2, OHCA_Machete, HG_USP_O, Katana_old.
- The local Fighter prefab has **no** TP weapons. Its body is the hidden local avatar.

**Selection:** the base `WeaponController` (NE `WeaponController.cs:1-432`, P `WeaponController.cs:1-324`):
- `InitWeapon` 112-170 uses the same name matching and `Destroy_Geometry` as the first person.
- `OnChangeWeapon` 56-84, `HideWeapons` 172, `ShowWeapon` 217, `GetWeaponByType` 316.
- Taunts hide the weapon (`LaunchTaunt` 338, `FinishTaunt` 386: durations 1.5, 2.8, 5.6, 3.5, 8.5, 15.3 s).
- Remote `ChangeWeapon` (NE `PlayerManager.cs:4946-5023`) can carry `[94]` weapon info (NE HTTP addition, `:5005-5007`).

**Body clips:** one legacy `Animation` on the root holds 87 clips (all present in OURS `gamebundle/meshes/fighter/**`). `ActorAnimator.SetAnimationProperties` (NE 342-468) re-adds them under alias names:
- `<X>_Bend` becomes `<X>BendClip` (additive aiming via `SetupAdditiveAiming` / `SetupHeadAiming`)
- `<X>_Reload` becomes `…ReloadClip` (Additive, Once): `AK47_Reload` → `MachineGunWeaponReloadClip`, `Makarov_Reload` → `HandGunWeaponReloadClip`, `RPG26_Reload` → `RocketLauncherWeaponReloadClip`, `Milkor_Reload` → `GrenadeLauncherWeaponReloadClip`, `Winchester1887_Reload` → `ShotGunWeaponReloadClip`, `ShotgunReload` → `ShotgunReloadClip`, plus UMP45 / SteyrAUG / FNMAG / Wildcat / TMP / AssaultRifle02/03 / SniperRifle03 / Shotgun02 / GrenadeLauncher03 / M202A1 / SnowLauncher
- `ShotgunChange` becomes `Change`
- `Bat_Shot`/`Zombie_Shot`/`Katana_Shot` become `…ShotClip`; `Bat_Idle` becomes `BatIdleClip`; `TauntN` becomes `TauntNClip`

**On weapon change:** `PlayerManager.ChangeWeaponAnimation(animator, type, sname)` (NE 5025-5160, P 1595) calls `ActorAnimator.ChangeWeaponAnimation(name)` (NE 1115-1160), which cross-fades `name+"BendClip"`:

| Type | Bend clip name |
|---|---|
| OHCA | Bat |
| THCA | Katana |
| HG | Makarov |
| MG | AssaultRifle02 / AssaultRifle03 / SteyrAUG (MG_AUG) / UMP45 (MG_UMP45D, MG_UMP45V) / TMP (MG_UMP45) / AK47 |
| GG/FL/SNG | M249 / FNMAG / M134 |
| SG | Novapump (both Novapumps) / Spas (Spas, Remington) / Winchester1887 |
| RL | RPG7 / M202A1 / RPG26 |
| GL/BL | Shotgun02 (GL_EX41) / GrenadeLauncher03 / SnowLauncher / Milkor |
| SR | SniperRifle03 / Wildcat / Vintorez / SVD |

**Remote reload:** `ReloadWeaponAnimation(typeName, w)` (NE 1274-1365) blends the matching `…ReloadClip`. RPG7B02 is NE speed-tuned.

**Remote fire:** there is no body recoil for guns (`WeaponController.LaunchShot` calls the empty `ShotAnimationReset`). Melee blends `BatShotClip` / `KatanaShotClip` / `ZombieShotClip`. Muzzle flash and tracer use the TP `Target`, through `getWeaponTransformByName(player.transform, typeName)` (NE `EffectManager.cs:1303-1342`), which searches `WeaponLook` in the player's children. That is why the same `EffectManager` methods work for local and remote players.

**First-person arms clothing:** `ArmClothModel.Sync` (NE `ArmClothModel.cs`, called from `PlayerCustomisator.cs:255, 303`) is NE-only. It matches the FP arm `Shirts_*`/`Gloves_*` meshes to the equipped wear.

---

## 6. Network, anti-cheat and HTTP code to drop offline

| Hook | Location | Notes |
|---|---|---|
| `NetworkManager.RegisterShotBefore/After` and the `shotRegistered*` check, then `SendEnterBaseRequest(34)` | `NetworkManager.cs:60-70, 233-340` | |
| Photon `photonConnection.SendRequest(Shot / ReloadWeapon / ChangeWeapon)` | | |
| `shotPPlayerCounter` position piggy-back | | |
| `IsHttpBattleActive` / `BattleServerHttpClient.QueueShotAction / QueueReloadAction / RequestDamage` | | keep only the `TryDispatchOfflineShot` loopback, or call `PlayerManager.PlayerShot` directly |
| Shot anti-cheat: `SendEnterBaseRequest(7)` deviation checks | P `ShotController.cs:1069-1083` | |
| `(26)` layer-mask / wall check, `(27)`, `(28)` nickname check, `CheckNickname` / `CheckNicknameCheating` | NE `啫.cs:990-1000, 1070-1078, 1160-1266` | |
| `噕` combat-player security (`InitWeapon`, `Check`, `CheckColliders`) | `噕.cs`, `CombatPlayer.cs:980-1000` | |
| `ShotController.CheckSpeedMultiplier` | | |
| `InitDummyPlayer` / `UpdateDummyPlayer` (`喩.CheckAim`) | `ShotController.cs:1498-1556` | |
| `噑.EnableWallShotCheckData` | | |
| `噋.ShotStatistics` counters | `PlayerManager.cs` ShotEffect, PlayerShot | |
| Debug lines `噑.DebugEnableRTTX` / `GameHUDFPS` | | |
| `KickManager` voting | `ShotController.Update` | |
| `RegisterWeaponShotStat` / `GameScore.AddWeaponShot` | `ShotController.cs:1863` | NE accuracy stats; optional, for BattleStats |
| NE remote-tracer correction `GetBattleRemoteTracerOrigin/Target` | `EffectManager.cs:1494-1537` | |
| `NetworkManager.SendChange` attaching `[94]`; `PlayerManager.ChangeWeapon` applying `[94]` for remotes | | |
| NE rebalancing layers: `噔` `Normalize*` / `ApplyExact*` / `Is*`; `MainNetworkController` and `PlayerManager` `ResolveBattle*` / `ResolveLocalBattle*`; `ResolveWorkshop*` text parsing; `GetReloadAnimationSpeedMultiplier`; sequential-reload interrupt; Vorchun/Gadyuka/Mels special cases; `TryAddShotgunAssistTarget`; NE explosion radii and impulses; `bulletImpactMarkEffect` (dead) | | **not network**, but not original either: decide per item (the brief says New Era is the base, so these may be kept deliberately) |

**Offline glue to keep or rewrite:**
- the `嗔.Enabled` offline room
- `PlayerManager.ResolveBattleServerDamage` / `ComputeOfflineDamage` / `ApplyOfflineDamage` / `ApplyDamageArmorFirst*`
- `OfflineBotSpawner`, `OfflineBattlePickup`

---

## 7. Asset and class status for the port

### 7.1 Scripts whose guid must exist in OURS to load the prefabs

None of these exist in OURS:
- `WeaponLook` b47c18c4ff83b5838525f6131ad28c68
- `HandTextureLoader` a8dc0c17eb7a9f31d64635e67e4fe511
- `ArmClothModel` 4380021096a0a7478c7a2377f720d725 (NE only)
- `Flamer` 382677f5174d5f584ffa2f7db23e4659
- `Flame` 77b93b34d20f926d6fc860613ac4d195
- `RuntimeTextureSwitch` c6d30dc9b55b8d493c434372211558c3
- `ShotController` e82b41bda8745fca1fd947aa95e1e69e
- `WeaponController` daaff92dbbe19e48a13f43665ef8e560
- `ActorAnimator` 145fdbed412cd98da8602e833fb9ddd8
- `EffectManager` 9a8e04050779e497b7f4a13a3e01001c (already referenced by OURS scenes such as `Contra/_Scenes/Angar.unity`, with all prefab fields wired)
- `DeleteAfterSeconds` 4932d6a4f6f8b8edefea4c21932d57c2
- `TriggerDeleteAfterSeconds` 8ffee918743b0378753a569a5390f54a
- `Tracer` cfceafe3d9facc779a05478759cefed9
- `RocketTracer` e9e70253e08d08a671f1bb9cc28066df
- `GrenadeTracer` 2e625077851b0a7de17f307f43875314
- `BombTracer` e522e61469d84c555f3cc0917e8e9dbb
- `ArrowTracer` 3157d6d42dbc233cc694a38edf338ab7
- `AudioSourceVC` 4c7405720a452413d79d25cfbfe70b12
- `MouseOverLook` 7316a1e47856fb5a5ac0e9ed897d1413
- `WalkController` 7b0843e6b99813f13172f55635b1b2b4
- `PlayerLocal` 8a107523645b76b3828bb3723b078825
- `PlayerRemote` ff0ef6e7434b219d57dfa5e5da80eba2
- `ItemTracer` ec2e792b33c35dcc03dd0630e9c657cd (base class)
- `RigidBodyForce` 84681867570d052eff9abeb6928de6d3
- `Item` c30496d6ae6d6f907456e10a94e01ec6

**Different guid in OURS:** `FPSCamera` (orig 6650071b…), `SoldierCamera` (211457b9…), `SoldierController` (0ffe723d…), `CharacterMotor` (73aa644d…), `SoundManager` (28556f8a…).

**Matching guid in OURS:** `RuntimeTextureLoader`, `PlayerCustomisator`, `BattleGUI`, `BattleGUICamera`, `ClothModel`, `ShaderManager`.

### 7.2 Effect prefabs

The scene `EffectManager` fields resolve to OURS `Contra/GameObject/`:

| Field | Prefab |
|---|---|
| `laserSparkPrefab` | `BatSparkConcrete_15` |
| `grenadeLauncherShaftSparkPrefab` | `grenadeLaucherShaftSpark_15` |
| `rocketLauncherShaftSparkPrefab` | `RocketLauncherShaftSpark_15` |
| `machineGunShaftSparkPrefab` | `MachineGunShaftSpark_15` (with Light) |
| `machineGunSparkPrefab` | `MachineGunSpark_15` (AudioSource) |
| `machineGunPrefab` | `MachineGun_15` (TrailRenderer + Tracer) |
| `exploPrefab` | `Explo_15` (Projector, Animation, AudioSource) |
| `exploDeactivationPrefab` | `ExploDeactivation_15` |
| `whizbangPrefab` | `rocket_15` (RocketTracer, Tracer, TriggerDeleteAfterSeconds, Light) |
| `mortarWhizbangPrefab` | `GrenadePrefab_15` |
| `healthHitPrefab` | `HealthHit_15` |

All other fields are null (laser, plasma, mines, turrets and so on are unused for these weapons).

`CharacterManager.GetPrefab` prefabs exist in OURS `prefabs/soldier/`: `GrenadePrefab`, `SnowballPrefab`, `BombPrefab`, `ExploSnowPrefab`, `CrossbowBoltPrefab`.

**Every one of these uses legacy particles** (`EllipsoidParticleEmitter` 15 / `ParticleAnimator` 12 / `ParticleRenderer` 26). They are unsupported in Unity 6 and must be converted to `ParticleSystem`. The code also calls `ParticleEmitter.emit` (`RocketTracer`, `GrenadeTracer`, `Flamer`) and `ParticleRenderer` (`HitEffect`). FL itself contains 11 legacy emitters (flamer flames).

The NE `Resources/effects/*` fallback (`EnsureFallbackPrefabs` 997-1021) exists nowhere and is not needed.

### 7.3 Missing from OURS

**Sounds.** All weapon sounds are missing from OURS `Game/Resources/sounds`, which holds only 23 non-weapon clips. Every one exists in `W:/Contra City Extract/NewEra/ExportedProject/Assets/Resources/sounds/*.ogg` (and many in `W:/Contra City Extract/Sounds_wav/*.wav`):
- Shots: `AK47_Shot`, `AK47_Shot1`, `UMP45_Shot`, `UMP45_Shot1`, `UMP45D_Shot`, `UMP45D_Shot1`, `Makarov_Shot`, `Glock_Shot`, `SVD_Shot`, `SR_Steyrf0rd`, `Crossbow_Shot`, `M134_Shot`, `M249_Shot`, `Winchester1887_Shot`, `DB_Shot`, `RPG26_Shot`, `SnowLauncher_Shot`, `Snowgun_Shot`, `Flamer_Shot`
- Launch/spin/stop: `Flamer_Launch`, `M134_Launch`, `M134_Spin`, `M134_Stop`, `Bat_Launch`
- Reloads: `AK47_Reload`, `TMP_Reload`, `Makarov_Reload`, `Glock_Reload`, `SVD_Reload`, `Steyr_Reload`, `RPG26_Reload`, `DB_Reload`, `Winchester1887_ReloadStart`/`Ammo`/`End`, `SnowLauncher_ReloadStart`/`Ammo`/`End`
- Impacts: `Bat_Shot_Enemy`, `Bat_Shot_Stone`, `BulletImpact`, `RPG26_Explosion`
- `FT1` and `FT2` (acid thrower) exist nowhere; they are unused.

**First-person model animation clips** (the guids are missing in OURS; found in `W:/Contra City Extract/NewEra_AllBundles/ExportedProject/Assets/asset_bundles/1test weapon/`):
- `HG_Desert`, `HG_DesertB01` (w_id 24 in data): Shot/Reset/Reload (`Shot_0.anim` …)
- `HG_USP` (w_id 18): `Shot_2.anim` …
- `SR_Vintorez` (w_id 107): `Shot.anim` …
- `GG_FNMAG` (w_id 20): `Shot_1.anim` … (4 clips)

**Materials** (missing in OURS; found in the same `1test weapon/materials/`): `FN_MAG.mat` (GG_FNMAG bullets), `Vintorez_Color.mat`, `USP.mat`, `Ford.mat` (SR_Steyrf0rd), and cloth `Shirts_yautzha*` / `Gloves_yautzha` / `*_stalkerb` materials. **No weapon meshes are missing.**

**Present in OURS:** body animation clips (86/87, none missing); FP model clips for every other weapon; crosshair textures; the `BattleIcoWeapon` skin.
