# Weapons stage 2: firing, reload, effects, zoom, crosshair

Base: New Era (`W:/Contra City Extract/NE_Scripts_Decrypted/`); Penta 2016 is the reference. Test: `-uicompare` cases `fire_ak`, `reload_ak`,
`zoom_svd` and the `[uicompare] logic fire / AK / SVD` lines.

## What is ported, and from where

| Piece | Our file | Source |
|---|---|---|
| Fire loop (Fire1 / Mouse1 / R, shotgun and grenade-launcher reload interrupts, blocked weapon, gatling launch / stop, sticky-bomb alt) | `ShotController.Update` | NE `ShotController.cs:1194-1496` |
| Ray and spread | `ShotController.DoShot`: Deviation / 500 of the viewport, square ±r/2 (zoomed sniper rifle 0) | NE `:1892-2188` |
| Per type (stage 2) | HG 3, MG 4, GG 6, SR 10: StraightShot, GunShot, machineGunEffect, the type node's `Shot` clip, shake (HG with "S" / shaking: ±1; SR: (-1, -0.35, 0) additive) | NE `:1966-2160` |
| Shot geometry | `ShotCalculator`: precise cast (sphere 0.2, exact ray on actor colliders ≤ 1.5 behind), mask ~772 (layers 2, 8, 9 ignored), miss = no shot | NE `啫.cs:306-429, 783-839, 912-1080` |
| Offline dispatch | `ShotController.SendShot` → ShotEffect(ME_AFTER_SERVER) → `OnShot` | NE `NetworkManager.cs:221-231`, `PlayerManager.cs:9377-9536, 9724-9940` |
| Ammo | `OnShot`: one round; empty clip → auto-reload after the frame (reserve left), else next weapon | NE `:2920-2997` |
| Reload | `Reload`: clip Reload (or ReloadStart, ReloadAmmo × n, ReloadEnd), speed multiplier, fire blocked for the clip length on an empty / sequential reload, reload sounds | NE `:2475-2607, 2753-2837`, `ActorAnimator.cs:1367-1469`, `EffectManager.cs:1612-1713` |
| Weapon runtime | sequential shell reload, CanFire / Fire / Launch / Stop | NE `噔.cs` (stage 1 port) |
| Shot effects | `EffectManager.machineGunEffect`: shot sound (+ the "…1" half after shotTime / 2 for rifles), wall spark + "BulletImpact" on its own source, muzzle flash on the model's `Target` (weapon layer), tracer (flat speed 11, trail time / widths per type) | NE `EffectManager.cs:1303-1492, 1762-2054` |
| Effect prefabs | the scene's EffectManager fields (`MachineGunShaftSpark_11`, `MachineGunSpark_11`, `MachineGun_11`), `Tracer`, `DeleteAfterSeconds`, `AudioSourceVC` with their original GUIDs | NE `Tracer.cs`, `DeleteAfterSeconds.cs`, `AudioSourceVC.cs` |
| Legacy particles | `Editor/LegacyParticleConverter.cs`: 204 prefabs / 408 emitters converted at build time, by Unity's own Legacy Particle System Updater v1.6 rules (originals in `LegacyBackup/`) | [updater source](https://gist.github.com/karljj1/58f16e936d13a94ae6a2f741fd271d91) |
| Sounds | 43 weapon sounds from Penta 2016 `Resources/sounds`, `SR_Steyrf0rd` from New Era; `SoundManager.PlayAfterSeconds` | NE `SoundManager.cs:387-396` |
| Sniper zoom | `FPSCamera`: Mouse1 toggles; blocked (reload) drops it, ready restores it; FOV × 0.2 (attribute "D" 0.15, optic "O" 0.5), weapon camera 60 × factor and hidden under 0.5, mouse × factor; `BattleUI.Zoom` → `BattleGUI.SetZoom` (scope overlay); weapon change / taunt = zoom off | NE `FPSCamera.cs:24-70, 275-301, 325-397`, `ShotController.cs:142-168, 3026-3046`, `GameHUD.cs:520-524` |
| Crosshair | the map scene's 3D `GUICamera/Crosshairs/CrossHair`: texture `crosshair_0<type>`, root scale 0.65 (gatling 1), white / grey (blocked) / red (fighter under it) every 50 ms, the kick clip on every shot, the rotation clip slowing down; hidden while zoomed or without the eye camera | NE `ShotController.cs:398-839, 1760-1782`, `GameHUD.cs:526-533, 3763` |

## Notes

- The map scenes keep their `GUICamera` (orthographic, depth 1): it draws the crosshair and the damage indicator. The map-viewer build
  step had switched every scene camera off; `BattleUI.InitSceneHud` switches it on again (enabled in `Contra/_Scenes`). The damage indicator
  starts at the end of its clip, as in `GameHUD.ResetDamageIndicatorVisual`. The old GUI crosshair of `BattleUI` is gone.
- The eye camera FOV is set by `FPSCamera.ApplyFieldOfView` only (option clamped 40..110 × zoom factor).
- A shot into the sky hits nothing: no shot, no effect, no ammo used (StraightShot returns null) — as in the original.
- Firing waits for the reload clip after an empty clip (`IsReloadAnimationBlocked`), even when the reload time itself is shorter.
- Taunt keys only while not shooting (`!shot`).
- New Era's configurable procedural crosshair (option styles) is an addition of the rebuild with no option in our settings; the native
  texture path is used, as with the option off.

## Not in stage 2

- Targets, damage, hit effects (`HealthHit`, `Bat_Shot_Enemy`): there are no other fighters yet (bots / players later).
- `RegisterWeaponShotStat` (accuracy in the battle results): with the round rewards.
- Stage 3: melee, shotgun (6 visual pellets, the disc spread is already in DoShot), rockets, grenades, sticky bombs, the crossbow (SR_Wildcat).
  Check there: our effect prefabs are named with an "_11" suffix (`Explo_11`, `HealthHit_11`), while `DeleteAfterSeconds` tests the
  original names ("Explo(", "HealthHit") for its shader fixes.
- Stage 4: gatling spin-up / stop sounds and clips (DoLaunch / DoSpin / DoStop), flamer and snowgun particles, the workshop.
