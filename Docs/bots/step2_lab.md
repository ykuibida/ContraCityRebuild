# Bots step 2: the map lab, base (15.11.2026)

## How to open it

`Builds/ContraCity/Лаборатория.bat` (every build writes it), or `ContraCity.exe -lab Bit_map` (any map: `-lab Angar`, …). The lab
opens the map without a battle.

| Control | What it does |
|---|---|
| Right mouse + WASD, Q / E | fly the camera (Shift faster) |
| Ctrl + left click | put the bot there, facing where the camera looks |
| Alt + left click | turn the bot towards a point |
| Space | run the experiment |
| C | clear the paths |
| F | camera to the bot |

The panel on the left has:
- the layer toggles (markers, labels, walkable surface, paths);
- the move and its settings;
- the simulation frame rate;
- the last result with its events.

## What is in it

- **Markers the map already has** (`LabWorld`): team spawns (DM / red / blue), medkits, ammo, armor, flags, control points, base
  gates. Each is a coloured pole with a label.
- **Walkable surface for the fighter's size** (Unity NavMesh baked at runtime from the map's colliders):
  - agent sizes: radius 1.3, height 6.5, step 1.5, slope 60°;
  - left out: triggers, fighters, and `GlobalCollider` (every map has one: the huge boundary box around the level);
  - coloured by height: floor cyan → green → yellow → roofs magenta.
- **Experiments** (`LabSim`) run on a simulated fighter that uses the player's own movement code (step 1), at a chosen frame rate.

  | Move | How it is done |
  |---|---|
  | Прыжок с места, с разбега | one jump |
  | Распрыжка | jump pressed again just before each landing; the motor re-jumps only if jump was pressed within 0.2 s |
  | Дробовик | back to the direction of travel, moving with S; a shot in the air after each take-off; jump again on landing while shots are left |
  | Ракетный прыжок | the rocket under the feet |
  | Бита + прыжки | the bat's ×1.25 run speed |

  Shotgun and rocket pushes go through the game's own shot maths:
  - **shotgun:** StraightShot; the push is 26.07 away from where the pellets hit, with a 0.45 s cooldown;
  - **rocket:** RocketShot; it blows up after its flight time, and the push is (10.5 − distance) × 6.2 from 6 below the blast.

  The result shows:
  - the path, the landing spot and the hit points;
  - air time, distance, height gained, peak;
  - whether it landed on walkable ground;
  - fall damage by the game's rule.

## Automated check (`ContraCity.exe -labtest Bit_map`)

Комиссариат: 351 colliders. The walkable surface:
- is baked in 70–100 ms;
- covers heights −19.7…51.5;
- has 1 910 triangles.

All spawns (21), medkits and ammo lie on it. Armor 1 of 2 and control points 3 of 4 do not.

From three spawns, each facing its most open direction, at 60 fps:

| Move | Air | Distance | Peak |
|---|---|---|---|
| Прыжок с разбега | 0.67 s | 10.7 | +3.5 |
| Распрыжка ×4 | 2.7 s | 39–44 | |
| Бита + 4 прыжка | | 45–55 | ×1.25 the bunnyhop |
| Дробовик, 2 выстрела | 1.8 s | 58–73 | +5.5 |
| Ракета под ноги | 1.5 s | 31 | +11.2 |

The run jump uses 0.6 s of run-up. None of these landings gives fall damage.

The movement test of step 1 still replays exactly (max error 0.00003–0.00006). Pictures: `lab_overview.png` (the walkable surface
from above), `lab_moves_top.png` (the experiments from one spawn: landing discs white = jump, yellow = running jump,
green = bunnyhop, blue = bat, orange = shotgun, red = rocket).

**Frame rate matters (found in step 1, measured here).**

| fps | Running jump | 4 bunnyhops |
|---|---|---|
| 30 / 60 | 10.7 | 46–47 |
| 144 / 250 | 11.2 | 48.5 |

That is +4…5 %. The simulation frame rate for bots is still to be chosen.

## Fixed on the way

- **Ghost and layer-2 geometry.** Ghosts ignored the player's layer (2) to avoid bumping into the player, so they also passed
  through map geometry on layer 2. Ghosts now collide with layer 2 and ignore only the live player.
- **Shots hitting the bot.** The game's shot masks leave out layer 2, where the player is; the ghost is on layer 27. So the bot's
  shotgun hit its own capsule. The lab now switches the ghost's collider off for the moment of the shot.

## Assumptions to calibrate against live play (step 4)

- the crosshair ray comes from the standing eye (6.5);
- no pellet spread;
- the rocket comes from the eye, not the weapon muzzle;
- the push of the fire frame comes before that frame's movement step;
- self-damage is not counted.

The recorder from step 1 can measure all of these against a live shotgun or rocket jump.
