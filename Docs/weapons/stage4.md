# Weapons stage 4: the flamethrower (stream weapons)

Base: New Era (`W:/Contra City Extract/NE_Scripts_Decrypted/`); Penta 2016 is the reference. Test: `-uicompare` cases `fire_flamer`,
`fire_flamer_off` and the `[uicompare] logic flamer` lines. Line numbers without a file are NE `ShotController.cs`.

The only stream weapon with an item is «Примус» (FL_N1, w_id 25: 50 / 100, 16 damage, fire 4×4). The acid thrower (11) and the
snow gun (12, SNG_Snowgun) share the same code. It is ported as is, but no item exists.

## What is ported, and from where

| Piece | Our file | Source |
|---|---|---|
| Fire loop: Launch on press ("Load" clip, "Flamer_Launch"), Stop on release ("Stop" clip) | `ShotController.Update`, `DoLaunch`, `DoStop` (stage 1/2) | `:1398-1448`, `:2331-2359`, `:2438-2469` |
| Stream shot (5 / 11 / 12): every interval (90 ms with no `rap`) a StraightShot, SegmentShot through the stream, SendShot, flamerEffect, the crosshair kick; PlayerManager.WaterBlock (no water offline) | `ShotController.DoShot` | `:2035-2048`; interval `BattleWeapons` (stage 1) |
| Ammo: one round per shot (ShotEffect ME_AFTER_SERVER → true → OnShot) | `ShotController.ShotEffect`, `OnShot` | NE `PlayerManager.cs:9724-9947` |
| Effects: "Snowgun_Shot" for the snow gun, else "Flamer_Shot"; the Flamer stream on; BLOW puts it out; acidThrowerEffect ("FT1" / "FT2") | `EffectManager.flamerEffect`, `acidThrowerEffect` | NE `EffectManager.cs:2740-2792` |
| Flamer: same class / GUID; emitters on with fire(true), off 0.3 s after the last call; renderers 0 / 1 and the nozzle "Flame" → Particles/Additive | `Flamer.cs` | NE `Flamer.cs` |
| ResetWeapon on the local death (KillMe): the gatling / stream go quiet, slot = first weapon, DoStop, crosshair rotation stopped | `ShotController.ResetWeapon`, `GameFlow.OnLocalDied` | `:3076-3105`, NE `PlayerManager.cs:7388` |

## Particles of the character prefabs

The flamer stream, the nozzle's pilot flame and the burning-body "Flame" emitters (legs, arms, head, spine) are legacy emitters in
`Fighter_locomotion` / `Enemy_Fighter_locomotion`. Unity refuses to save those prefabs ("missing script": they keep original scripts
the rebuild has no class for). So `LegacyParticleConverter.ConvertInstance` converts their scene copies when the build makes the templates
(`BuildGame`: WeaponTemplate, FighterTemplate). It uses the same updater rules, with 11 emitters each. The original emit flag carries
over: the body flames and the stream start off, the pilot flame is lit (as in the original).

Test: the Flamer has 2 systems, 0 particles before firing, 51 alive after 1.5 s of fire, out 0.6 s after release; ammo 50 → 35 (15 shots
at 90 ms). Screenshots: the fire stream; after release, the blue pilot flame only.

## Not in stage 4

- Hits on fighters (damage, the fire effect 4×4, the burning-body flames on a target): with bots.
- Acid thrower and snow gun: no items exist (the code paths are in place).
