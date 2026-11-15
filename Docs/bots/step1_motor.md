# Bots step 1: movement that can be simulated (15.11.2026)

## What changed

- `Assets/Scripts/Player/CharacterMotor.cs`:
  - `Step(dt, now)` takes the frame time and the clock from outside. `Update` calls `Step(Time.deltaTime, Time.time)`, so the
    player gets the same values as before. The maths did not change: only `Time.deltaTime` → `dt` and `Time.time` → `now`.
  - `CaptureState` / `RestoreState` snapshot every field the next step reads.
  - `CopySettingsFrom` copies the tuning.
  - `simulated` mode: no UI hooks, no round lock, no input reset; the bat air-speed factor is given.
  - The under-map raycasts skip the ghost layer (27).
- `Assets/Scripts/Player/SoldierController.cs`: the keyboard is read into a `Keys` value, and `Step(keys, dt, now)` does the rest.
  The player path is `Update` → read keyboard → `Step(keys, Time.deltaTime, Time.time)`. A bot fills `Keys` itself. It also gets
  `CaptureState` / `RestoreState` / `CopySettingsFrom`, `simulated` mode (no sounds; the weapon speed is given) and `testKeys`.
- `Assets/Game/Scripts/Lab/MotorTrace.cs`: records the player's steps in the order Unity ran them: keys, frame time, clock, view,
  motor inputs, explosion impulses, result. It replays them on ghosts. A ghost has the same capsule and code, sits on layer 27,
  collides with the map like the player, but not with the player or other ghosts.
- `Assets/Game/Scripts/Lab/MotorTraceTest.cs`, `ContraCity.exe -motortest`: a scripted course on Комиссариат, then the replays
  and a benchmark.

## Result (two runs, two different spawn points)

| | Run 1 | Run 2 |
|---|---|---|
| Course | 18.6 s, 17 836 steps, 7 take-offs, 4 explosion impulses (3 shotgun + 1 rocket), up to +16.3 high, up to 41 u/s | 18.7 s, 17 772 steps, 6 take-offs, 4 impulses, +12.9, 41.5 u/s |
| Motor-level replay, max deviation | 0.00005 | 0.00003 |
| Key-level replay (what a bot drives), max deviation | 0.00005 | 0.00003 |
| Repeat on the same ghost | identical | identical |

The deviations are float rounding: the fighter is 6.5 units tall, so this is a millionth of his height. No step was off by more than 0.001. Picture
`Docs/bots/motortest_paths.png`: red is the live path, green the key-level replay on top of it.

**Speed.** About 150 000–200 000 movement steps per second on this PC: running the controller and the motor, with collisions
against the map. The test ran at ~1000 fps, so its steps are ~1 ms. At 60 steps per second of game time that is about 2 500
fighter-seconds per second, e.g. 100 fighters ≈ 25× real time, 1 000 fighters ≈ 2.5× real time (movement only; shooting and AI
come on top).

**Player unchanged.** The full `-uicompare` run gives the same movement numbers as before the change:
- run 17.09;
- bat 21.36;
- RPG knockback about +48 up, moved about 18.

0 exceptions. The other differences in that log come from the random spawn point: grenade arcs against different walls,
brightness at a different spot.

## Finding: movement depends on the frame rate (as in New Era)

The ported movement integrates per frame. Some of it is explicitly tied to the frame, for example the key-axis ramp
(`ColleagueAxisAccel`: 0.05 per frame below 250 fps). The same keys at 60 fps and at 1000 fps give slightly different paths.
Simulated bots have to pick a step: the typical frame rate of real players (60? 144?). To decide with the owner; the lab can
measure how much jump distances change with it.
