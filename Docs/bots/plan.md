# Bots: plan (draft, 12.11.2026)

New Era's offline bots are not ported 1:1. They roll dice for hits, move without physics and jumps, and pick routes at random
(`TODO.md`, `OfflineBotSpawner.cs`). The rebuild's bots are real fighters: the same CharacterMotor, ShotController, weapons and
damage as the player, steered by a "brain" instead of the keyboard. From New Era we keep only the frame: bots per mode (DM 5,
TDM/CTF/CP 4), respawn 5–8 s, teams, the name list, the map markers. The owner's reference design is a bot-ecosystem document
for another arena shooter of his (`J:/downloads/Дизайн_ботов_и_живого_ладдера.md`); this plan adapts it to Contra City.

## How Contra City is really played (owner, 12.11.2026)

- Main loadout: **rifle + sniper + grenade launcher or rocket launcher**; the shotgun and the bat are for moving. Everything
  else is secondary. Most of the shooting is the rifle.
- Many players **open with the rocket launcher** (see the enemy, fire a rocket) and finish with the rifle, or sniper + rifle.
  With a grenade launcher there is no point using it first.
- **Pistol** only to finish someone: when the sniper can't, or a rocket / grenade might not kill or might hit yourself, and
  sometimes up close where it beats a rocket, grenade or sniper. The sniper is sometimes fired no-scope up close.
- **Minigun: nobody ever plays it.**
- **The shotgun is a movement tool**, not a damage weapon: almost all movement is jumps, and the shotgun jump (fire in the air for
  the push) is, with bunnyhop, the main trick of the game. **Melee** is for running faster (bat ×1.25 speed). (Correction of
  12.11.2026: earlier this read "shotguns are rarely worth it".)
- **Grenades and sticky bombs** go on choke points: bait the enemy, catch him coming round the corner. Some players also lob
  grenades roughly where they think the enemy is, or even at random: a style worth having as a bot trait.
- **Knocking off**: a bot may blast an enemy off a ledge where the map allows (explosion knockback is real).
- **Medkits / armor on timers**: good players know when they come back (as in the reference design).
- **Bunnyhop is essential**, so a bot can reach the spots people use.
- **No bot economy**: bots may take any weapon and outfit.
- **Clan wars / ВККС**: bots may play their own clan wars; a bot seen in DM / TDM with «Вождь» has won it. That is prestige.
- Zombie mode: later.

## Positions, peeks and area denial (owner, 12.11.2026)

- **Long-range spots:** where a map has long lines, bots know them in advance and some stand there on purpose with the sniper,
  depending on style (careful holder or aggressive). Counter-Strike is similar enough that its positioning ideas apply: holding an
  angle, off-angles, pre-aiming common spots, peeking (wide swing, shoulder / jiggle peek), crossfires and trades in team modes,
  moving to a new spot after a sniper kill.
- **The anchor style:** some players, especially in team modes, stand on one point and stay while it keeps giving kills, above
  all if the spot is strong; they leave when it stops working.
- **Cover, above all in TDM:** DM and TDM are played almost the same, but in TDM people hold their positions more and hide behind
  cover, e.g. the containers and boxes on Рубеж, rather than stand in the open and get blown up. Never walk into the enemy spawn
  in TDM; at least find somewhere to hide first. In DM it depends on the player's level. Most of this is common to shooters and
  should be built in as obvious, logical behaviour.
- **No aimbot:** good aim is allowed (many players hit well), but it comes from the bot's skill stats within the fixed human
  limits.
- **Area denial without seeing anyone:** stickies or grenades (rarely a rocket) on a gate or entrance that opens, on a likely
  spawn exit, on a place where enemies gather (on Комиссариат a wooden box where enemies often crowd: blind lobbing there usually
  hits someone). After a miss: step aside, change the angle, throw again. The lab computes lob trajectories from positions to such
  targets with the real projectile physics.
- **Shotgun movement is the main way to move** (New Era's bunnyhop is weaker than in Counter-Strike). Chain: turn your back to
  where you want to go, jump + shotgun shot, jump + shot, ... (it boosts like a bunnyhop), then turn back and hop a little; or a
  shotgun jump up onto something. Bots find the best angles and timings in the lab rather than from hand-written numbers.

## Learning from mass matches (owner, 12.11.2026)

- Run thousands of DM / TDM matches headless: everything real (motor, weapons, projectiles, damage, pickups), only rendering off,
  faster than real time, bots with different DNA, weapons and styles.
- Compare the top of the table with the bottom: what the bots with the best K/D did that the worst did not. Watch the top bots
  in replays.
- From that, narrow the actions of future bots (stop the obvious nonsense), hard-code what makes them strong by default, then
  diversify characters again on top of the stronger base. Repeat.
- Guard rails: human limits (reaction, turn speed, aim error) stay fixed, so "strongest" means smarter, not superhuman; the owner
  judges realism in replays, because bots trained on bots can find odd tricks that no player would use.
- Map knowledge comes mainly from this automatic exploration and selection: the owner's own play covers well under 1% of what is
  needed and not every map. His recordings and notes are corrections and seeds.

## Replays (owner, 12.11.2026)

A match is saved as data, not video, and played back in the lab like a video. Model: Fortnite's replay system (owner's
reference). It records the match's network data (Unreal DemoNetDriver: replicated state, checkpoints + deltas between them), so
playback rebuilds the match and can look slightly different from live play, and stutters where the server lost data. The
viewer:
- switch to any player;
- a per-player timeline with icons for that player's kills and deaths;
- speed up or slow down;
- camera modes: the player's own view, third person orbiting, drone follow / attach, free drone.

Ours (offline, so nothing is lost):
- **Recording:** a snapshot of every fighter per tick (position, velocity, view, weapon, animation state, HP / armor) plus events
  (shots, hits, projectiles, explosions, pickups, kills, deaths), with a keyframe every few seconds for instant seeking. Playback
  draws from the data; it does not re-run the simulation.
- **Viewer:**
  - switch between players; each has a timeline of kill / death icons;
  - clicking an icon jumps to 1 s before it (0.5–1 s, adjustable) so the moment is clear;
  - play / pause / speed / scrub;
  - first-person, third-person, follow and free cameras;
  - a debug overlay of what the bot saw, heard and decided.
- **Used for:** watching the best bots and the ones they beat, judging realism, and the owner's own recorded play. (The owner may
  later build a similar lab for Batla.)

Sources: Epic's announcement of the Fortnite replay system (fortnite.com/news/fortnite-battle-royale-replay-system), Unreal's
replay system docs (dev.epicgames.com, "using the replay system in unreal engine"), the community Fortnite replay decompressor
docs (fortnitereplaydecompressor.readthedocs.io).

## Perception, memory and emotions (owner, 12.11.2026)

Bots see and hear like a player, no wall-hacks (the honesty rule of the Batla design). What is new here compared with the stages
below:
- **Hearing:** shots (louder weapons heard farther), explosions, footsteps and landings, reloads, pickups, pain. A sound gives a
  direction and a rough distance, less precise through walls.
- **Short memory of enemies:** the last seen / heard position, fading with time. The bot guesses where the enemy went (along the
  nav graph) and checks it: a grenade into the spot where he was, a pre-fire round the corner, a sticky at the exit he used, or an
  ambush where he should come out.
- **Match memory:** who kills the bot and who it kills (grudges, revenge, avoiding a dangerous player), which spots a given
  player uses ("he always sits on that roof"), pickup timers.
- **Emotions as temporary modifiers, set by DNA:** tilt after a losing streak (worse decisions), confidence after a streak
  (bolder), fear at low HP (retreat to a medkit), rage after being killed by the same player (goes for revenge).

## Owner's feedback on the lab (15.11.2026)

- **Bots are simulated at 240 fps** (the owner's choice; movement depends on the frame rate, see `step1_motor.md`).
- The lab looks good (the height colouring especially), but it is too hard to use. There are too many buttons and raw
  parameters (view pitch, shot delay). It should be more automatic: the lab picks the angles and timings itself.
- He could not see the bot or the result: Run and Clear looked dead. The result must be visible: the bot actually jumping
  along the path, the camera showing it.
- Places behind physical barriers (outside the playable area) are coloured as walkable too. Show and use only what is
  reachable, so bots never plan into them.
- Rocket jumps matter less than shotgun jumps but must be there. Players right after spawning (still invulnerable) often fire a
  rocket under themselves to fly off. Combos too: a rocket first, then shotgun shots in the air.

## Rockets for movement: a rare exception (owner, 15.11.2026)

A rocket under your own feet is a very rare move, or the bots blow themselves up. It is used only when:
- **Right after a respawn, while invulnerable** (no self-damage): on open maps, to boost forward, jump high, get somewhere, or land
  on an enemy from above. On Монетный двор the owner did this all the time: nobody camps you on the spawn, you fly forward, you
  surprise the enemy from above.
- **There is no other way** to a strong spot (high ground) and the bot has a lot of HP and a launcher with little self-damage
  («Аврора»).

Never with heavy self-damage launchers such as «Троллебузина» (RL_RPG7B02: 90 damage; its push is ÷3 in EffectManager) or
«Пучкомёт». The rule depends on each launcher's self-damage, so the weapon data must give it.

Not in the rebuild's item data: «Аврора» (and «Пучкомёт» under that name). Its self-damage is to be found.

In the lab, rockets are priced as "only when nothing else works". Bots get the full rule (invulnerability, HP, launcher) in step 10.

## Map lab (owner, 12.11.2026)

The working tool for stage A is a lab inside Unity, not a web page: only the real motor gives 1:1 jumps.
- **An overview map with jump schemes** (from where to where you can get, and how) that the owner checks for realism. If A → B
  is reachable, it is a link; how hard and how risky it is goes with the link.
- **Experiments:** put a bot on a spot, pick a move (jump, bunnyhop, shotgun / rocket jump, bat), see the trajectory; sweep all
  angles and timings from a spot to see everything reachable from it.
- **Mass runs:** the motor sped up (stepped faster than real time, no rendering), many bots at once with different DNA and skill,
  reproducible by seed: which jumps succeed for whom, where bots get stuck or fall.
- **Recording the owner's own play** (routes, jumps, spots marked with a command) as a direct source of map knowledge.
- **One map-knowledge file per map** (zones, movement links, tactical spots, the owner's notes, lessons from runs): the lab shows
  it and the bots read it, so what the owner approves is what the bots know. It is a graph of points, links and zones with
  numbers, so it also serves as input if learning is ever tried.
- First step: the motor takes its time step and input from outside (same maths), checked by replaying recorded input.

## Architecture (decision of 12.11.2026: no shared core)

- **No shared core with Batla for now.** The owner and an independent review agreed: Batla has no working bot to check a contract
  against, the two bodies differ in kind (real Unity motor here, a ported simulator on Batla's server), and bot quality lives in
  game-specific layers that a shared contract would flatten. Bots are built in Contra City first; Batla later reads the source and
  copies what it wants (recording the Contra commit it came from). A shared core is extracted only if, once Batla has its own
  working bot, the two copies turn out to be the same. `W:/BotCore` stays as a reference draft only.
- Inside Contra City the engine-free parts (perception, aim model, RNG, DNA, path search, memory) live in their own assembly
  without UnityEngine, with tests that run without Unity. Their interfaces are shaped for Contra City (separate intents for zoom,
  detonation, spin-up; full weapon data), not for a hypothetical second game.
- Libraries: Unity AI Navigation (NavMesh) is worth using as the ground layer for paths; the bot moves with its real motor and
  only reads the path. Jump links the NavMesh can't model (bunnyhop run-ups, shotgun / rocket jumps, bat dashes) are our own,
  found by simulating the real motor. No behaviour-tree assets, no ML-Agents.

## Stages (order of 12.11.2026; each ends with an automatic test, then the owner checks)

Pilot: Комиссариат, DM. Stage letters of the first draft (A, B0–B5, C1) map onto these steps.

| # | Step | Done when |
|---|---|---|
| 1 | **Simulatable motor:** CharacterMotor takes its time step and input from outside, same maths | replayed recorded input gives the same path 1:1 — **done 15.11.2026** (`step1_motor.md`) |
| 2 | **Lab base:** load the map, fly camera, existing markers (spawns, pickups, flags), walkable surface for the fighter size, single experiments (put a bot, pick a move, see the trajectory) | the owner can try a jump from any spot — **done 15.11.2026** (`step2_lab.md`) |
| 3 | **Replays:** a match or a run saved as data, played back with a timeline (kills / deaths marked), speed, scrub, free or follow camera | a recorded run plays back exactly |
| 4 | **Movement search:** jump, bunnyhop, shotgun jump and shotgun chains, rocket jump, bat; sweeps of angles and timings; the overview map with arcs | **owner checks realism** — **search, reachability and A→B routes done 15.11.2026** (`step4_lab_routes.md`); owner checks on Комиссариат, Ангар (задний двор), Монетный двор, Форпост pending |
| 5 | **Tactical layers and map-knowledge file:** sniper lines, choke points, gathering spots, gates / entrances, knock-off ledges, cover, grenade / sticky lobs onto targets; the owner's notes and recordings | **owner checks** |
| 6 | **Ghost runs:** many bots, sped up, moving by the found links | stats on which links work for whom |
| 7 | **Bot body (B0):** a real fighter without keyboard in DM: spawns, takes damage, dies, respawns; more than one shooter in the game, damage to bots, kill feed and score | it looks like another player |
| 8 | **Sight, hearing, aim, shooting (B1)** | accuracy tunable, misses real |
| 9 | **Movement in combat (B2, B3)** | reaches any spot, no sticking, no falls |
| 10 | **Weapons and positions (B4):** loadout and finishing rules, positions, peeks, the anchor style, area denial, knock-offs | the owner recognises "a Contra player" |
| 11 | **Memory and emotions** | |
| 12 | **Characters (C1):** DNA, skill levels, names, chat, taunts; the Batla design gone through point by point | bots differ and are recognisable |
| 13 | **Mass matches and selection:** thousands of headless DM / TDM matches, top vs bottom, replays of the best, stronger defaults, diversify again | stronger bots, still realistic for the owner |
| 14 | **Other maps** (the step 2–6 pipeline) and **modes (B5):** TDM roles, CTF, CP | |
| Later (owner decides) | rating / ВККС seasons for bots, a background world simulation | — |

## Open questions

- Rating / ВККС seasons: wanted in principle; the form is open.
- Background simulation of many bots with learning: not decided.
- Which game gets the bots first: Contra City (decided 12.11.2026). Second: most likely Blockade (owner, 14.11.2026), whose
  setting is closer to Contra City than Batla's. Its AI is meanwhile building the server-side foundation (a bot body on the
  server, since the client computes movement); its "Контра" mode (2 teams, blocks don't break) is the quick first target, the
  "Бой" mode (4 teams, a central control point, breaking and building blocks, mines) a separate layer later. Every game's bots
  are built ideally for that game on their own; the others may only read its code for ideas.
- Pilot: **Комиссариат (Bit_map), DM**. The owner knows it well and it has many bunnyhop / jump spots. The owner may still pick
  another map after looking through the list.
- Before any bot code: map study (stage A) and how the map scheme should look are discussed with the owner first.
