# Stat proposals for weapons missing from the New Era data

## Owner's decision (12.11.2026)

- In the game (`Resources/data/rebuild_items.json`, `enabled: true`): «Вождь» (MG_UMP45VKKS_O, id 73), «Гадюка» (GL_Milkor_A, 45),
  «Павлик М.» (GL_SnowLauncher, 46), «Гранатин» (GL_Milkor, 44). The grenade-launcher numbers below are approved. «Вождь»: the
  proposal plus crit damage 20, above every NE weapon (max 15: «Рейдер», «Кобра», «Повстанец»).
- Levels: 10 for every added weapon (owner: New Era weapons need at most level 10 — 28 at 10, 3 at 5, 8 at 0). «Вождь» price 3950 (as «Рейдер») — the owner does not mind; prices may be rebalanced later.
- In the file but switched off (`enabled: false`) until the owner approves stats: «Липучкомёт» BL_Sticky and BL_StickyB02.
  Both have full first-person models and the full clip set (Shot, Reload, ReloadStart/Ammo/End, Reset). Their proposed numbers
  are «Ворчун»'s; B02 adds a bleed effect.
- Not added: shotguns and every other missing weapon. They stay recorded in `missing_weapons.md` and on the review page.
- Later: a switch for this whole group. `ItemDB.RebuildWeaponsEnabled` already gates it and can be wired to bots or an option. New Era
  nerfed these weapons because they "almost kill the game".

## Final grenade / sticky balance (owner-approved 12.11.2026)

Rule: «Ворчун» (NE 104, unchanged: 60 + fire 2×4 = 68, radius 13, 3 / 11, level 5, 1750) stays the strongest. "Strength" is hit damage plus the effect's total.

| Weapon | Hit | Effect | Strength | Blast radius | Ammo | Level, price |
|---|---|---|---|---|---|---|
| Ворчун (NE) | 60 | fire 2×4 | 68 | 13 | 3 / 11 | 5, 1750 |
| Гадюка | 54 | poison 3×4, no slow | 66 | 9 (as Гранатин) | 4 / 15 | 10, 1121 |
| Павлик М. | 52 | cold 2×4 | 60 | 9 | 4 / 14 | 10, 900 |
| Гранатин | 50 | slow | 50 | 9 | 4 / 15 | 10, 950 |
| Липучкомёт | 50 | none | 50 | 8 | 3 / 11 | 10, 1000 |
| Липучкомёт B02 | 52 | bleed 2×4 | 60 | 8 | 3 / 11 | 10, 1300 |

Deviation from New Era at the owner's request: NE `啫.cs:291-304` gives «Гадюка» radius 20. It now uses the plain grenade-launcher
radius (`ShotCalculator.GetExplosionRadius`). The relaunch's slow on «Гадюка» is dropped (the owner remembers poison only). Both sticky
launchers are switched on. The first proposals (Гадюка 90, Павлик 80, Гранатин 81) are replaced.

## Proposals

Status 12.11.2026: proposals only. Nothing is in the game. The owner reviews and approves each weapon on the review page
(https://claude.ai/artifact/HggG93uHecUthKeyNmsJ6T, db collection `weapons`, doc id = sname) before anything is ported.

## Why proposals at all

45 weapons have a model or icon but no item record in the captured New Era server data (`Resources/data/ne_items.json`).
Neither NE nor Penta 2016 numbers for them exist in any extract or online source found (see `missing_weapons.md`).
The only numbers are in the 2026 relaunch patch notes, https://contra.city/devlog ("было → стало"). That is the relaunch's
own balance, not the 2016 original: «Лом» starts there at 42 body damage, while NE has 28–30.

## Method

Files: `balance_proposal/scale.py` → `scale.json`, `balance_proposal/propose.py` → `proposals.json`.

1. Reference moment T1 = before patch 1.0.182 (11.10.2026): the "было" side of «Баланс подробно 1.0.182» (devlog lines
   391–425), or the "было" side of 1.0.183 for a weapon unchanged on 11.10.
2. Shared weapons (in NE data and the devlog at T1): Лом, Дружинник, Политрук, Сокол, Спекулянт, Засад, Смертобой,
   Звездочет (near only; their 70 / 150 mid/far are broken values), Писец, Клык, Троллебузина.
   Ratio = NE body damage (mean of min/max, `ne_weapon_stats_server.csv`) / relaunch body damage, per range.
3. Class factor = median of the class's ratios: pistols ×1.082, rifles ×1.103, snipers ×0.917, melee ×0.69 (Лом only),
   explosive ×1.286 (Троллебузина only), machine guns and fallback ×1.067 (all ratios; no shared machine gun).
   Rate: NE interval / (60000 / rpm) on 8 pairs, median ×1.076.
4. Missing weapon at T1 × class factor; the weapon keeps its own distance fall-off. NE convention: min = max − 2,
   machine guns and explosives min = max.
5. «Вождь» and «Гранатин» come from the owner's words (12.11.2026), compared with the strongest NE weapons (see the
   `basis` text in `proposals.json`).

## Reliability

Median NE / relaunch = ×1.07, but individual weapons range from ×0.58 (Сокол) to ×2.75 (Спекулянт): the relaunch rebalanced
each weapon separately. Machine guns have no shared weapon; melee and explosive have one each. The relaunch cut all rifles
by 20 % on 30.08.2026 (before T1). The shared rifles carry the same cut, but the proposed rifles (17 near) are still weaker
than most NE rifles (19–31). They need the owner's check first.
