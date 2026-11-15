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

## Fixes after the owner's first look at Форпост (15.11.2026)

The owner saw two kinds of wrong grey: ordinary floor and the snow on a roof marked unreachable, and pipes leading to a roof
missing altogether.

- **Tile seams.** The NavMesh is built in tiles. Along a seam the polygons of two tiles do not share vertices, so the old region
  test split one floor into islands and greyed out walkable floor. An open edge with surface right across it, at the same height,
  is now joined as a seam.
- **Thin surfaces.**
  - The surface is baked with radius 0.4 instead of 1.3, which had eroded pipes, beams and roof edges away.
  - Jump starts on thin surfaces sit 0.4 inside the edge.
- **Doors closing again.** Opened doors were closing again through their own animation. That gave the owner's run 1 164 jumps
  instead of 9 863. The door animation is now switched off once the door is open.
- **Click snapping.** A click next to the surface snaps to the nearest walkable point.
- **The test's map-after-map runs** kept the previous map's NavMesh. The figures of maps 2–4 in the table above are wrong for that
  reason.

Clean figures:

| Map | Regions reachable / all | Reachable surface | Jumps |
|---|---|---|---|
| Форпост | 63 / 120 | 41 100 (63 %) | 4 409 |
| Комиссариат | 60 / 233 | 56 300 (16 %) | 9 633 |
| Ангар, задний двор | 38 / 131 | 26 700 (30 %) | 3 659 |
| Монетный двор | 65 / 155 | 39 400 (17 %) | 7 373 |

Routes: 12 of 12 found and walked. The cache format is version 2, with a fingerprint of the surface, so old caches are rebuilt by
themselves.

## The walk grid replaces Unity's NavMesh (15.11.2026)

**What the owner's walk on Форпост showed** (G mode, `Docs/bots/lab_walks/ArenaRing_20261115_053332.csv`):
- 488 of his 1 773 grounded steps were on floor the NavMesh did not have at all. These were holes, not "unreachable" regions: bare
  grey snow showed through where the overlay was missing.
- Unity's NavMesh drops floor the fighter walks on: bumpy snow, slopes, the ground by the garages. It has its own slope limit
  (≤ 60°), voxels and tolerances, while the fighter's CharacterController has slopeLimit 90 and stepOffset 1.5.

**The fix.** `LabWorld` now builds its own walk grid with the same physics the fighter uses:
- a column every 1 unit;
- a ray from the top finds every floor;
- a cell is a floor where the fighter's capsule fits standing;
- neighbours are joined within the step or the slope the motor climbs.

Regions, edges, snapping, walking paths (A* on the grid) and the bot's walking all use it.

Invisible ceilings stay grey but are not drawn: grey is shown only up to a jump above the highest reachable floor. On Форпост there
is a 30 000-cell plane at +44.

**Result:**
- **The owner's walk on Форпост:** 1 772 of 1 773 grounded steps are on the grid and reachable; before, it was 1 285.
- **Four maps:**

  | Map | Regions reachable | Jumps found | Search |
  |---|---|---|---|
  | Форпост | 131 | 17 222 | 5.6 min |
  | Комиссариат | 86 | 16 595 | 5.7 min |
  | Ангар, задний двор | 61 | 10 823 | 3.3 min |
  | Монетный двор | 65 | 15 351 | 3.3 min |

  The first opening of a new map now takes several minutes; these four are cached.
- **Routes:** 11 of 12 walked. On Монетный двор one route ended short of its target; to look at.
- **Spawns:** on Ангар (задний двор) 4 of 23 spawn markers are not on the grid; to look at.

## Why grey: the leftovers explained and fixed (15.11.2026)

The autotest now explains the biggest grey regions by itself (`-labtest`). For each one it finds the closest pair of cells, grey and reachable. It then lists what lies between them: every floor in each column and, for a rejected floor, the colliders the fighter's capsule touches. It also saves a picture `uitest/lab_<map>_why_*.png`. A walking leg that ends away from its target logs where the bot first left the ground.

**Found and fixed (all maps):**
- **Floors under floors of one collider.** A physics ray reports one hit per collider. One MeshCollider often holds a whole building: on Ангар, задний двор, `concrete_0402` is the roof (y 4.9) and the spawn room floor (y −13.7). The grid saw only the roof. Now each column is walked down floor by floor, with a new ray from just under each hit. All 23 spawns of Ангар, задний двор are now on the grid (before: 19), and that map has a route without rockets.
- **Base doors (AutomaticDoor).** On Форпост the base door is 24 high and 33 wide. Closed, it shuts the base exit. Opened, it rises 23 and shuts the walkway above it. The lab cannot keep it in one state, so it shows the door open and switches its colliders off. Which team may pass, and when, is a rule for the bots (see plan.md).
- **Landings on the very edge.** The capsule's round bottom caught on a corner and counted as standing for a moment, then slid off. Now, after a landing, the fighter stands 0.3 s with no keys. The jump counts only if it is still on the floor.
- **Walking off ledges.**
  - After a backwards shotgun jump the bot faced back and held W while turning, which walked it off the container. Now it presses W only once it faces within 40° of the way.
  - The walking path also pays extra for edge cells, so it keeps a cell away from drops, and the bot follows every cell.

**What stays grey on Форпост, and why (checked from the pictures and the column lists):**
- **Snow outside the ring wall.** It is separated by the wall `wood_0300`.
- **The ground under the two bases.** It is 21 below the base floor and closed in.
- **The base roofs.** They are beyond the ring wall and no jump reaches them.
- **The invisible ceilings at +44 and +57.** They are not drawn.

**Result (fresh search):**

| Map | Regions reachable | Jumps | Spawns on the grid | Search |
|---|---|---|---|---|
| Форпост | 135 | 17 264 | 15/15 | 5.9 min |
| Комиссариат | 87 | 17 055 | 21/21 | 6.4 min |
| Ангар, задний двор | 58 | 10 630 | 23/23 | 5.0 min |
| Монетный двор | 68 | 15 822 | 19/19 | 5.2 min |

- **Routes:** 12 of 12 walked to the target.
- **Owner's walk on Форпост:** 1 772 of 1 773 steps are reachable.
- **Without rockets:**
  - Комиссариат: the hardest of the three test targets still needs a rocket.
  - Ангар, задний двор: now has a route.
