# Weapons stage 5: the weapon on a fighter's body (third person)

Base: New Era (`W:/Contra City Extract/NE_Scripts_Decrypted/`). Test: `-uicompare` cases `body_slot1`…`body_slot7`, `body_aim_up`,
`body_aim_down`, `body_reload`, `body_bat_swing` and the `[uicompare] logic body` lines. It uses a dummy fighter 9 ahead of the
player with the profile's loadout.

## Where New Era shows it

Only on OTHER fighters (remote players, bots). The local fighter's body never shows a weapon:
- `CombatPlayer.ShowWeapons` has one caller, `setPlayerShipVisible(true)` (NE `CombatPlayer.cs:1852`);
- `KillMe` hides the weapons (`CombatPlayer.Kill :1378`);
- the local taunt shows the clothes (`InitWear(true)`) but no body weapon;
- the lobby / HQ preview turns every WeaponLook off (`PlayerCustomisator.cs:125-132`).

Our local body already behaves that way. So this stage is groundwork for bots and other players: nothing changes for the player now.

## What is ported, and from where

| Piece | Our file | Source |
|---|---|---|
| Body weapon slots: the WeaponLook type node by type name (+"L"/"R"), its system-name model activated (visible for slot 0), the others' renderers off | `BodyWeapons.InitWeapon` | NE `WeaponController.cs:112-170` |
| Weapon change: Change() timers, HideWeapons + ShowOnlyWeaponModel (exact name, else the first that starts with it), then the animation set | `BodyWeapons.OnChangeWeapon` | `WeaponController.cs:56-84, 172-305`, NE `PlayerManager.cs:4995-5016` |
| Zombie claw, taunt hide / show, ResumeWeapon, death hide | `BodyWeapons` | `WeaponController.cs:86-103, 338-431`, `CombatPlayer.cs:1378` |
| Weapon set per type / system name (Bat, Katana, Makarov, AK47 / UMP45 / TMP / SteyrAUG / AssaultRifle02/03, M134 / M249 / FNMAG, Winchester1887 / Novapump / Spas, RPG26 / RPG7 / M202A1, Milkor / SnowLauncher / GrenadeLauncher03 / Shotgun02, SVD / Wildcat / Vintorez / SniperRifle03) | `BodyAnimator.AnimationSet` | NE `PlayerManager.cs:5025-5158` |
| Clip setup: `<X>_Bend` → `<X>BendClip` (layer 1, mixed from Bip01 Spine1; Bat: neck + head), reload clips (additive), Bat / Zombie / Katana swing and idle | `BodyAnimator.SetAnimationProperties` | NE `ActorAnimator.cs:342-468, 632-668` |
| Aim: the bend clip's time = targetXRotation / 180 + 0.5 (0…0.96), cross-fade unless taunting, a zombie, or the set's reload / swing plays | `BodyAnimator.Update`, `IsBendAnimation` | `ActorAnimator.cs:469-630, 938-970` |
| Set change (old bend out 0.3 s, new in 0.3 s, fallback Shotgun / AK47 / Makarov / Bat / Katana) | `BodyAnimator.ChangeWeaponAnimation` | `ActorAnimator.cs:281-317, 1115-1158` |
| Remote reload: the body clip by type node and system name (RPG7B02 scaled to its reload time) | `BodyAnimator.ReloadWeaponAnimation`, `BodyWeapons.ReloadAnimation` | `ActorAnimator.cs:1274-1369`, `PlayerManager.cs:4937-4943` |
| Remote shot: melee LAUNCH → body swing (Bat ×2, Katana ×1.75, Zombie ×1.5); guns → "Shot" on the type node's model Animation (gatling: its Transform); flamer / launchers nothing | `BodyWeapons.ShotAnimation`, `BodyAnimator.ShotWeaponAnimation` | `PlayerManager.cs:9730-9750`, `BattleServerHttpClient.cs:7876-7940`, `ActorAnimator.cs:1502-1585` |

## Notes

- Not ported: 喩.Destroy_Geometry (a low-memory option that destroys unused models) and the Taurus visual reset after its shot.
- The body models carry no Animation of their own (the test finds none on the rifle), so in New Era a remote rifle shot plays no
  model clip either. The call is ported as is.
- The local fighter does not call these yet. New Era calls ChangeWeaponAnimation for the local body too, but the result is never
  visible (first person: body hidden; taunt: bend weight 0; death: ragdoll).
