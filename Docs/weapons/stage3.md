# Weapons stage 3: melee, shotgun, rockets, grenades, sticky bombs, crossbow

Base: New Era (`W:/Contra City Extract/NE_Scripts_Decrypted/`); Penta 2016 is the reference. Test: `-uicompare` cases `melee_bat`,
`fire_shotgun`, `fire_rpg_flight`, `fire_rpg_blow`, `fire_vorchun`, `fire_crossbow` and the `[uicompare] logic bat / shotgun / RPG /
GL_… / SR_Wildcat…` lines. Line numbers without a file are NE `ShotController.cs`.

## What is ported, and from where

| Piece | Our file | Source |
|---|---|---|
| Melee (1 / 2): the swing is a LAUNCH shot ("Bat_Launch", the `Shot` clip), the hit 0.2 s later along the screen centre up to the weapon distance | `ShotController.DoShot`, `DelayedShot` | `:1957-1970`, `:2247-2289` |
| Shotgun (7): StraightShot + SegmentShot, 6 visual pellets, the `ShotGunWeapon` node's `Shot` clip, shake (-2, 0, 0) | `ShotController.DoShot` | `:2073-2106` |
| Shotgun jump: in the air, not while the weapon change blocks, 0.45 s apart: impulse 28 × 0.9310345 (Novapump × 0.7) from the shot origin | `CanApplyShotgunJumpImpulse`, `LocalJump` | `:2073-2106`; Jump = 28 offline (NE `CombatPlayer`) |
| Rocket launcher (8): RocketShot (linear flight, landing time from the distance / velocity) | `ShotController.DoShot`, `ShotCalculator.RocketShot` | `:2110-2120`, NE `啫.cs:1437-1475` |
| Grenade launcher (9): the lob ray (0.15 above the crosshair, FOV-independent, 55° reference); «Ворчун» (104) and Milkor_A (45) use the Vorchun trajectory, the rest GrenadeShot; «Ворчун» takes its round at once | `GetFovIndependentLobRay`, `ShotCalculator.GrenadeShot` / `VorchunGrenadeShot`, `PredictVorchunShotAmmo` | `:1881-1890`, `:2122-2138`, `:2880-2908`, NE `啫.cs:1477-1660` |
| Sticky bombs (15): CheckBlow (with 3 out, the oldest blows up first), GrenadeShot(stick), right button = DoBlow (all own bombs), life timer after landing | `CheckBlow`, `DoBlow`, `BombTracer` | `:2170-2182`, `:2361-2416`, NE `BombTracer.cs` |
| Projectile registry | `ShotController.RegisterItem / UnregisterItem / RegisteredItems` | NE `ShotController` registeredItems |
| Offline dispatch: targets (offline only the shooter: own explosions), ShotEffect(ME_AFTER_SERVER), OnShot | `ShotController.SendShot`, `ShotEffect` | NE `NetworkManager.cs:221-231`, `PlayerManager.cs:9377-9536, 9724-9947` |
| Self damage: within the radius of feet + 2; the fallback damage RL 51, GL / BL 44 as energy damage, armor first, no reduction | `ShotCalculator.AddSelfExplosionTarget`, `RadialShot`, `PlayerVitals.ApplyDamageArmorFirst` | NE `啫.cs:2173-2262`, `PlayerManager.cs:9440-9478, 10195-10221` |
| Explosion radius: «Гадюка» 20, «Ворчун» 13, else the weapon's | `ShotCalculator.GetExplosionRadius` | NE `啫.cs` |
| Projectiles | `RocketTracer`, `GrenadeTracer`, `BombTracer`, `ArrowTracer`, `ItemTracer`, `TriggerDeleteAfterSeconds` (original GUIDs) | NE files of the same names |
| Effects: batEffect, shotGunEffect, rocketLauncherEffect, grenadeLauncherEffect, bombLauncherEffect (team tint), HitEffect, arrowTracerEffect, the crossbow spark + bolt in machineGunEffect | `EffectManager` | NE `EffectManager.cs:1539-1610, 1715-1738, 1882-1892, 1992-2016, 2092-2130, 2167-2262` |
| Explosion: ExploSnowPrefab for the SnowLauncher, else exploPrefab; "RPG26_Explosion"; the scorch projector only within the quality distance (levels 0/1 → 0, 2 → 48000, 3 → 24000, else 90000) | `EffectManager.launcherEffect` | NE `EffectManager.cs:2619-2717` |
| Knockback: radius 10.5 (RL / GL), factor RL 6.2 / GL 3.1 («Ворчун» × 1.2, Trolleybusina / 3), power = (radius − distance) × factor, from 6 below the blast (else 5); BL × 0.33 and only with targets | `EffectManager.launcherEffect`, `CharacterMotor.SetExplosionForce` | NE `EffectManager.cs:715-820, 2690-2735`, `CharacterMotor.cs:944-950` |
| Character prefabs (GrenadePrefab, SnowballPrefab, BombPrefab, ExploSnowPrefab, CrossbowBoltPrefab) | `CharacterPrefabs` (holder from `gamebundle/prefabs/soldier`, kept across scenes) | NE `CharacterManager` prefab lookup |

## Notes

- **Minigun, owner's deviation from both clients.** In NE and Penta an empty clip does not stop the gatling: the barrel keeps
  spinning (and the spin sound keeps playing) until fire is released. At the owner's request the empty clip now stops it like a release
  (`ShotController.OnShot`: `GATLING_GUN && Stop() → DoStop()`, before the auto-reload). Everything else about the minigun is NE
  (launch, spin, stop clips, M134 sounds, crosshair rotation).
- **Projector/Multiply.** Every export only had a dummy shader of that name, so the explosion scorch was invisible. The shader is
  Unity's Standard Assets `Projector/Multiply` source (`Assets/Game/Shaders/ProjectorMultiply.shader`), always included in the build, as is
  `Legacy Shaders/Particles/~Additive-Multiply`.
- **Effect names.** The exported prefabs carry a `_NN` suffix (`Explo_11`, `HealthHit_11`). `DeleteAfterSeconds` drops it before
  comparing names, so its NE shader fixes apply again.
- **«Стальные подошвы».** The enhancer has `ReducedDamageItSelf = 5`, but New Era never reads it; our self damage has no reduction either.
- **Sticky bombs: «Липучкомёт» (`BL_Sticky`, `BL_StickyB02`).** New Era had it (`BL_` → BOMB_LAUNCHER: NE `MainNetworkController.cs:3565-3568`;
  icons, kill-feed styles, models, the HUD string `kwtBOMB_LAUNCHER`), but its item record is missing from the captured server data
  (`Resources/data/ne_items.json`: weapon types 1–10 only; no w_id or stats anywhere in the extracts). The code is ported as is (fire,
  CheckBlow, DoBlow, BombTracer); it has no item until stats are found.
- **Particle fix (12.11.2026): 7 New Era bundle prefabs kept their legacy particles.** The bundle export
  (`asset_bundles/gamebundle/prefabs/soldier`: ExploSnowPrefab, SnowballPrefab, GrenadePrefab, BombPrefab, Fighter_locomotion and both
  Enemy variants, 40 emitters) is saved in the 2017+ layout (`- component: {fileID}`). `LegacyParticleConverter` only read the Unity 4 layout
  (`- 15: {fileID}`), so it skipped them, and the «Павлик М.» snow blast and the grenade and snowball trails were invisible. The converter now reads
  both. Sound: no version has a separate snow-blast sound. NE plays "RPG26_Explosion" for every blast and "SnowLauncher_Shot"
  for every grenade-launcher shot (NE `EffectManager.cs:2227, 2676`).
- **Scorch fix (12.11.2026): the whole map went dark on every blast.** The exported `ExplosionSplatterMaterial*` (18) sat on a dummy
  diffuse shader, so the build stripped their `_ShadowTex` / `_FalloffTex`. `DeleteAfterSeconds` then switched them to Projector/Multiply,
  which used its default grey cookie and halved everything in the projector's frustum. The materials now use `Projector/Multiply`
  in the asset (as in the original bundle), and the Falloff textures have border mip maps (texel 0 = "no effect" survives the mips).
  Test `scorch up` / `scorch flight`: a local splatter (rocket, hit normal) / a short streak along the flight path (grenade, flight
  direction: NE `EffectManager.cs:2676-2679`, Penta 2016 `EffectManager.cs:2429-2432`, NE `啫.cs:1592-1660`), the rest of the map untouched.
- Test results: bat 1 spark on concrete; shotgun 3 → 2 shells, 10 sparks; RPG at the feet 126 / 25 → 100 / 0 (51) and a launch of
  about 50 units/s up; «Ворчун» 44 self damage; «Шершень» bolt stuck in the wall.

## Not in stage 3

- Hits on other fighters (damage, HealthHit, Bat_Shot_Enemy on a target): there are no bots or players yet.
- The damage HUD: the ShotMe overlay, IndicateDamage, the ShotDamage number.
- `RegisterWeaponShotStat` (accuracy in the battle results): with the round rewards.
- Flamer, acid thrower, snow gun (stream weapons): stage 4.
