# Bots step 4 (first part): the lab searches jumps itself and shows routes (15.11.2026)

Rework of the step 2 lab after the owner's feedback: too many buttons, the bot and its result not visible, unreachable places
drawn as walkable.

## How it works for the owner

`Builds/ContraCity/Лаборатория - <карта>.bat`: Комиссариат, Ангар (задний двор), Монетный двор, Форпост. Other maps:
`ContraCity.exe -lab <map>`.

1. **Left click** where the bot starts, then **left click** where it must get to.
2. The bot (the real fighter model, dressed like the profile, with run / jump animations) finds the way with running and jumps
   and goes it, in a loop. The panel says how, in words a player can follow, e.g. «бег 24 → ракетный прыжок (разбег 0,3 с,
   ракета под ноги) → дробовик (спиной вперёд, …)».
3. Speed 1× / ½× / ¼×. Camera: free, behind the bot, or through its eyes.
4. **«✓ Реально» / «✗ Нереально»** saves the route with its jumps to `Docs/bots/lab_marks/<map>.jsonl`. Claude reads them.
5. Toggles: surface, all jumps, markers. Colours of the surface by height, **grey = cannot be reached from the spawns**. Esc
   resets.

Camera as before: right mouse + WASD / Q E. No Ctrl clicks, no sliders.

## What the lab does by itself

- **Walk regions.** The walkable surface (NavMesh for the fighter's size) is split into regions: parts connected by walking. A
  crate top, a roof or a ledge is its own region.
- **Jump search** (`LabLinks`):
  - **Where it starts.** It begins in the regions of the spawns. Along every outer edge of a reachable region (ledges, the feet of
    walls and crates), every 5 units, in 3 directions (straight out, ±30°), the bot tries 8 moves:

    | Move | Settings |
    |---|---|
    | Прыжок с места | |
    | Прыжок с разбега | |
    | Распрыжка ×2 | |
    | Дробовик | 2 shots back-down, or 1 shot straight down |
    | Ракета под ноги | |
    | Ракета + дробовик | |
    | Бита ×2 | |

    It all runs at **240 fps**, on the player's own movement code, with the game's own shot maths.
  - **What counts as a jump.** A landing becomes a jump when it lands on walkable ground without fall damage, and either reaches
    another region or is a shortcut of at least 25 units. Regions reached this way are searched in turn. So only what can
    actually be reached from the spawns is searched and drawn.
  - **Cache.** The result goes to `Builds/ContraCity/LabData/<map>.links.json`, so the next opening is instant. The four maps are
    already in the cache from the test run.
- **Routes.**
  - **Search:** the quickest way over the found jumps, with walking in between. Per pair of regions it keeps up to 8 jumps spread
    over the start region, plus every jump right next to the start point.
  - **Playing:** the route is played on the bot. The walking parts are run by a first steering on the real motor: face the next
    corner of the NavMesh path, hold W. The jumps are the found moves.
- **Bases.** Automatic doors (AutomaticDoor) are opened at the start: in the game they open for a nearby player of their team, and
  in the lab nobody comes near. The base gates are inactive in the map scenes. Which team may pass where is a rule for the bots
  later.

## Automated check (`-labtest Bit_map,Arena_3lvl,ShipConteyner,ArenaRing`, 8 min for all four)

| Map | Regions reachable / all | Reachable surface | Jumps found | Search |
|---|---|---|---|---|
| Комиссариат | 20 / 125 | 41 600 (13 %) | 3 354 | 25 154 tries, 73 s |
| Ангар, задний двор | 22 / 189 | 56 400 (15 %) | 1 264 | 32 275 tries, 72 s |
| Монетный двор | 87 / 288 | 410 600 (69 %) | 31 894 | 135 966 tries, 213 s |
| Форпост | 43 / 322 | 110 200 (17 %) | 9 863 | 62 732 tries, 116 s |

A low share of the surface is expected: most of it is roofs, the outside of the fences and terrain no player gets to. On Форпост,
before the doors were opened, only the two spawn rooms were reachable.

Routes from a spawn to the three highest places the search reached on each map: **12 of 12 found, 12 of 12 reached by the bot.**

| Map | Route |
|---|---|
| Комиссариат | to +33 in 3 jumps: bat, rocket, shotgun |
| Ангар, задний двор | to +22 with one shotgun jump straight up; to the far side with 1 jump |
| Монетный двор | to +55 in 7 jumps, 27 s |
| Форпост | from the spawn room up to +47 in 4–5 jumps |

The step 1 movement test still replays exactly (deviation 0).

Pictures in this folder:
- `lab4_bit_map_links.png`: all jumps on Комиссариат;
- `lab4_bit_map_route_top.png`: a route from above;
- `lab4_shipconteyner_route_jump.png`, `lab4_arena3lvl_route_jump.png`: the bot mid-route;
- `lab4_arenaring_route_top.png`.

## What is not done yet / open

- **Not yet checked by the owner** on the maps he knows: the next step is his checks with the real / unreal marks.
- **The walk-up to a jump.**
  - Before a jump the bot stops at its start point and makes its own run-up, as the search simulated it; in a real game the run
    would carry on. A later step joins the run into the jump.
  - The walking parts are planned by straight-line distance; the bot walks the real NavMesh path.
- **The search ignores:**
  - fall damage (only landings without damage count);
  - self-damage of the rocket;
  - team rules for doors.
- **Still to calibrate** against live jumps: the shot origin, pellet spread, and the 0.5 s weapon switch.
