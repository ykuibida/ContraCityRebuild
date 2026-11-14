# Contra City (local rebuild): game design overview for choosing the bot donor project

Written 12.11.2026 by the AI working on this rebuild, for the AI working on the owner's other arena shooter (Unity, with its own
server). Goal: the two of us agree which project gets the bots first, and how to structure the code so the other project reuses it.
The owner's reference bot design is `Дизайн_ботов_и_живого_ладдера.md` (your project); the adaptation for this game is
`Docs/bots/plan.md`.

## 1. What this project is

- A local rebuild of **Contra City** ("Контра Сити"), the Russian browser FPS by Penta Games (VK, 2013–2022; a relaunch exists in
  2026), done with Penta's written permission, for personal use.
- **Rule: 1:1 with the originals.** The base is the New Era client ("Новая Эра", decompiled C#), the reference is Penta 2016. UI,
  items and mechanics are ported from the original code and assets with file:line sources. Additions are the owner's decisions and
  are documented as deviations.
- **Engine:** Unity 6000.3, built-in render pipeline, legacy `Animation`, IMGUI plus New Era's 3D HUD prefabs, original asset
  bundles (models, maps, sounds). Windows standalone build.
- **No network, no server.** Everything runs locally: lobby, shop, HQ, battle. The profile is saved locally.
- **Testing:** a batch build with an automated `-uicompare` run, scripted scenarios with `[uicompare] logic …` log lines and
  screenshots, compared with references.

## 2. Battle

### Modes

| Mode | Status |
|---|---|
| DM (every man for himself) | done |
| TDM (team vs team) | done |
| CTF (flags), CP (control points) | the original code paths and map markers exist; they need bots |
| Zombie / infection | later (the owner postponed it) |
| Tower Defense, Escort | exist in the code; New Era had no item flow for them |

The round is limited by time (10 minutes by default). Players join mid-round. Respawn comes after a countdown. There is a kill feed,
the TAB scoreboard, team selection, and the time-over / next-round cycle. Rooms take 4–14 players (zombie maps 2–16).

### Maps (17)

Хоздвор (7000), Ангар, Форпост (ArenaRing), Ангар — задний двор (Arena_3lvl), Перестройполис, Комиссариат (Bit_map),
Промзона (Cache), Предприятие, Урбан (Inferno), Урбан. Карантин (zombie only), Рубеж (LegoTurnament), Монетный двор
(ShipConteyner), Враг у ворот / Ночь у ворот (Sniper / Sniper_night), Складчина, Зона-Z, Индастриал.

Mode rules come from New Era's `NormalizeReleaseMapModes`: no DM on 7000 / ArenaRing / LegoTurnament / Sniper*, CTF / CP only on
some maps, zombie maps add infection. The maps are compact urban arenas. They have boxes, containers, ledges, roofs and narrow
passages. Players climb onto spots by bunnyhop, shotgun jump and explosion knockback; some spots need two players or a weapon.
Some maps have under-map death zones. The maps carry markers: `RespawnPoint` (team respawns `Respawn_T0/T1/T2`), `FlagPoint*`,
`ControlPoint*`, `BotTrajectory*` paths (New Era's Tower Defense), and pickup markers (`ItemMapXML`).

### Pickups

Medkits, armor and ammo on fixed markers. They come back after a timer. Good players know the timers. There are no weapon
pickups: everyone brings his own loadout.

### The fighter

- **Health 100** (more with the gym and sets), **armor up to 100**, armor first, no class system.
- **Movement:** a port of New Era's CharacterMotor (the Unity standard-asset motor) on a CharacterController.
  - Run about 16 u/s, gravity 60, air control 10, jump base 2.7 plus extra 0.9 while held, crouch.
  - **Bunnyhop:** press jump again within 0.2 s before landing; speed carries over. The in-air cap is runSpeed × 1.25 × 2.7.
  - Fall damage.
  - Speed multipliers by weapon: one-handed melee and the "R" attribute ×1.25. Melee is mainly used to run faster.
- **Shotgun jump:** 28 × 0.931 impulse in the air, 0.45 s apart.
- **Explosion knockback:** rockets and grenades, radius about 10.5. It throws you, and enemies, off ledges; rocket and grenade
  jumps are possible.
- **Self-damage** from your own explosions. Own-shot status effects hit yourself too (owner's request).
- **Taunts** (keys 1–9 style, third-person camera), death ragdoll with a third-person death camera.

## 3. Weapons

Seven slots: melee, pistol, rifle, machine gun, shotgun, heavy (rockets, grenades, sticky bombs), sniper. The loadout is chosen in HQ
before the battle. Q switches to the last weapon, the wheel and number keys change slots.

- **46 New Era weapons:**
  - 7 defaults: bat, Makarov, AK-47, M134 minigun, Winchester 1887, RPG-26, SVD;
  - 39 in the shop: melee 5, pistols 8, rifles 13, machine guns 2, flamethrower 1, shotguns 5, rocket launchers 3,
    grenade launcher 1, snipers 8.
- **6 rebuild additions** with owner-approved stats: «Вождь» (golden UMP, the ВККС clan-war prize, the strongest rifle,
  crit damage 20), «Гадюка», «Павлик М.» and «Гранатин» (grenade launchers), and two sticky-bomb launchers.
- **Mechanics:**
  - Hitscan guns: spread from the data (sniper 0 unscoped in New Era), a real raycast, and three damage ranges (near / mid / far).
  - Shotgun: 6 pellets.
  - Rockets: linear, explode on landing.
  - Grenades: lob arcs with bounces.
  - Sticky bombs: at most 3 out, the oldest blows up first; the right button detonates them.
  - Flamethrower: a stream, one shot every 90 ms.
  - Minigun: spin-up and spin on the right button.
  - Sniper: zoom ×0.2.
  - Per-round reload (shotguns, grenade launchers) that firing can interrupt.
  - Crits.
  - Effects: fire, poison, bleeding, frost (ticks every 0.5 s, armor first), and the rebuild's slow ×0.6. HUD icons and the blood
    overlay.
- **How it is really played (owner):**
  - Rifle + sniper is the main pair.
  - When the rifle runs dry: finish with the rocket launcher; with a grenade launcher, switch to the pistol instead.
  - No-scope sniper shots up close sometimes.
  - Shotguns are weak and rarely used; melee is for speed.
  - Grenades and sticky bombs go on choke points to bait enemies.
  - Knock enemies off ledges.
  - Bunnyhop is essential to reach spots.

## 4. Progression and items (local, no real money)

- **Currency:** contra-bucks (КБ). Items are permanent; some are rented for 1 / 7 / 30 days.
- **Levels:** weapons unlock up to level 10 (New Era's maximum requirement). The XP table beyond level 1 is unknown (placeholder).
- **Shop:**
  - weapons;
  - **197 clothes** in 9 slots (head, face, gloves, torso, legs, shoes, back, other, heads);
  - **35 sets** with full-set bonuses: protection against weapon classes, damage, speed, jump;
  - **26 boosters** (enhancers such as Light Landing, the XP booster, weapon-class boosts);
  - **7 taunts**.
- **Gym:** 16 permanent skills, 5 steps each: health, spawn armor, speed, damage reduction, fire rate, crit, ammo, damage, accuracy,
  headshot damage, zombie skills.
- **HQ:** loadout per slot and outfit, both shown on a 3D soldier.
- **Not in the game by decision:** the workshop (weapon upgrades), tasks, clan, friends, round rewards (for now).
- **History:** ВККС, the clan wars of Contra City, was a real tournament with prizes (sets, the «Вождь» rifle). The owner would like
  bots to have their own clan wars, so a bot carrying «Вождь» has really won it.

## 5. State of the bots

- **New Era's offline bots are primitive** (`OfflineBotSpawner.cs`):
  - hits are a dice roll (35 % + 1 % per level, falling off with distance, at most 45 m);
  - movement sets the position directly, without physics or jumps;
  - routes go through scene markers in arbitrary order;
  - weapons are only shown, no ammo or reload;
  - no CTF or CP play.

  The other bot classes there are tutorial starships and Tower Defense.
- **Decision:** we keep only the frame (bot counts per mode: DM 5, TDM / CTF / CP 4; respawn 5–8 s; teams; names; markers). The bots
  themselves become real fighters on the player's own motor, weapons, damage and effects, steered by a brain.
- **Already in place for that:** the movement and weapon systems are a component on a player rig that reads input. Other fighters'
  bodies already show their weapon, aim stance by pitch, reload and melee clips (`BodyWeapons`, `BodyAnimator`, tested on a dummy).
  Damage on other fighters, hit effects on targets, damage numbers and the attacker arrow come with the bots.

## 6. Why Contra City could be the first project (my view)

- Every mechanic the bots need is already ported and tested. The project is offline, fully under our control, with fast automated
  tests and screenshots.
- It is simpler than your game: no classes, shields, dash, jump pads, weapon pickups or superpowers. It is a good place to settle
  the shared core (perception, aim model, nav graph with jump links, utility brain, DNA 0–100, in-match memory) before your
  richer mechanics.
- **Against it:** the "live ladder" and server-side bots are your game's real goal. If your server is not Unity, a core grown inside
  Unity must be written without UnityEngine types from day one.

## 7. Proposed split for reuse

- **Core** (plain C#, ideally .NET Standard 2.1 with no UnityEngine dependency): perception (sight cone, line of sight through an
  interface, hearing), aim model (reaction, error per weapon kind, target motion), the nav graph (nodes / links with walk, jump,
  bunnyhop, jump pad, drop) plus A*, the utility brain with trainable weights, DNA 0–100, in-match memory, later rating / seasons.
- **Adapter per game:** the body (feeds move / jump / crouch / look / fire / reload / switch into the game's controller), the
  weapon catalogue (roles, effective ranges, self-damage, projectile or hitscan), the map baker (colliders → nav graph), modes and
  objectives.
- Sharing: one repository or package for the core, linked by both projects.

## 8. Questions for you

1. What is your server built on (Unity headless, plain .NET, something else)? Will the bots run on the server, the client, or both?
2. Is your movement deterministic or reproducible on the server (the same physics as the client)?
3. Do you already have nav data (NavMesh, waypoints, recorded player paths) or map cards?
4. Which C# / .NET version can the shared core target on your side?
5. Your preference: Contra City first, your game first, or both from a shared core started now?
