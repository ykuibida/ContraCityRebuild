# Playtest 1 fixes (12.11.2026)

Test: `-uicompare` lines `logic reload`, `logic arc`, `logic self`; screenshots `reload_*_done`, `reload_*_interrupt`, `self_*`.

## Grenade launcher stuck in the loading pose («Гранатин», also «Гадюка»)

The Milkor model's `ReloadEnd` clip is empty in every source: the New Era bundles, its cache and the Penta-era web cache (1 s, no
curves). «Ворчун», «Павлик М.» and the sticky launchers have real ones. After a per-round reload (ReloadStart, ReloadAmmo × n,
ReloadEnd) the launcher stayed where ReloadAmmo left it. Fix (`ShotController.PlayEmptyReloadEndSubstitute`): when ReloadEnd has no curves,
ReloadStart plays backwards in its place, both at the end of the reload and when a shot interrupts it (the close-before-shot path).
The timing and fire blocks are unchanged. The reload logic itself is New Era's and works (timeline in the test).

## Grenade arcs

Measured (`logic arc`, level / 20° up): «Ворчун» (New Era's own data and VorchunGrenadeShot) 96 / 190 units, peak +3 / +23.
«Павлик М.» on the plain GrenadeShot with the rebuild data (vel 10, life 3800) peaked at +44, twice «Ворчун». New Era sold no
grenade launcher on that path; Penta used another formula with server numbers that are lost. «Гранатин» and «Павлик М.» now take
the same path as «Ворчун» and «Гадюка» (owner's request for «Гранатин»; the same rule for «Павлик»). Every grenade launcher now flies
by New Era's «Ворчун» physics. The sticky launchers keep GrenadeShot(stick): they stop at the first contact.

## Accuracy

The port is New Era's: `PlayerManager.ResolveLocalBattleDeviation` (:2498-2545) uses the data's accuracy, else 8 for every rifle, else the
data's dev, else sniper 0, shotgun 35, pistol 6, others 8. No New Era sniper has dev or accuracy, so they hit without the scope. Penta 2016
used the server's spread unscoped and 0 only when zoomed (`ShotController.cs:1072-1080`); those server numbers are lost. Left as New Era
until the owner decides.

## Damage feedback and effects on yourself

- Blood overlay: `BattleUI.ShotMe` = GameHUD.ShotMe / ShotMePeriodic (NE GameHUD.cs:3957-3975). It shows on every local damage (fall, own
  shot, effect tick, through `PlayerVitals.Damaged`), holds 0.5 s and fades over 0.5 s (:3771-3788). Cleared on spawn and death
  (ClearTransientBattleEffects). The BattleGUI prefab draws it (`Overlay/Blood`).
- Effect types now come from the weapon data (`BattleWeapons.ResolvePeriodicImpactType` = NE :2031-2056; it was a stub returning 0).
- `LocalImpacts`: New Era's offline periodic impacts (PlayerManager :9128-9330: type from the data or the hard-coded weapons, 4 damage /
  3 ticks by default, a tick every 0.5 s armor first, refresh on a new hit, icon 2 s on the hit and 0.65 s per tick) applied to the
  shooter himself. **This is a deviation at the owner's request**: offline, New Era applies them only to opponents (:9219); online, the
  server decided.
- Slow (not in New Era: no slow code, no slow icon in the prefab): weapons with `slow_ms` (rebuild data: «Гранатин» 4800 ms) set
  `SoldierController.impactSpeedMultiplier` = `LocalImpacts.SlowFactor` 0.6 (**proposal**). The icon is `battlestate_slow`, shown in the
  unused Electro slot of the BattleGUI Impact row.
- Not ported: the body effects (flames, frost material, blood bursts: the local body is hidden in first person), damage numbers, the
  attacker direction arrow (needs an attacker other than yourself).
