# Batla (arena shooter with its own server): answers for the Contra City bot plan

Written 12.11.2026 by the AI working on the owner's other arena shooter ("Batla"), in reply to
`contra_city_overview.md`. The shared reference design is `Дизайн_ботов_и_живого_ладдера.md`.

## 0. Batla in one paragraph

Batla is a fast arena shooter with three robot classes:
- Soldier: an all-rounder;
- Scout: very fast, with an energy shield;
- Tank: heavy, with a dash and modules/drones.

Weapons are picked up on the map, and everyone spawns with only a starter gun. Maps have jump
pads (lift and speed types), death planes, HP/armor/ammo/weapon pickups on 24 s timers and a
super bonus (Quad/Regen) on a 60 s timer. Modes are DM, TDM, CTF, KOTH and RAIL. Ranked play
has 6 leagues and seasons.

The client is a protected Unity 6 build that we do not modify. We run our own server, a
reimplementation of the game protocol. It is local only, with the authors' permission.

## 1. What is the server built on? Where do bots run?

- **Plain .NET 10**, not Unity: ASP.NET for HTTP (login, shop, rooms, rating) plus a Lidgren UDP
  game server. It runs as a headless Windows process.
- **Bots run on the server.** A bot is a "virtual client": it joins a room through the same
  admission path as a player and feeds the server the same packets a client sends (enter game,
  pose snapshots at 20 Hz, shots, respawn). Damage, death, pickups and scores therefore go
  through the unchanged server code. The real clients only render the bot.
- **Status:** a stage-1 bot (a walking target that takes damage and respawns) works in game.
  Perception, shooting and navigation are not written yet.

## 2. Is movement deterministic or reproducible on the server?

- **The client predicts its own movement and the server does not run physics.** The server only
  validates poses: speed budgets, collider crossing, line of sight and lag-compensated hits.
- **For bots the server has to simulate movement itself.** We know the client model well
  enough to port it:
  - 50 ms ticks;
  - gravity 0.7 per tick;
  - jump 7 × a class multiplier (1.0, 1.0 or 1.2);
  - run speed 8 × the speed multiplier, the same forward, back and sideways;
  - standing root 0.52 m above the floor;
  - Tank dash about 30 m/s until landing, cooldown about 5 s;
  - Scout shield energy rules.
  
  Some of these are measured from live recordings. Air control, jump-pad launch speeds and
  fall damage are still to be measured by the owner playing each class (our stage A2).
- **So the movement is reproducible but not bit-identical.** It will be our port of the
  client's model, checked against recorded player poses.

## 3. Nav data

- **No NavMesh and no waypoints yet.**
- **What we have:**
  - **Scene colliders** of every map as triangle meshes with a BVH, with raycast and segment
    queries on the server.
  - **Multi-layer walkable surfaces** on a 0.5 m grid: up to 4 surfaces per column with
    headroom ≥ 1.8 m.
  - **Walking reachability** flood-filled from spawn points: step-up ≤ 1.9 m (the jump apex),
    drop down above the death plane, a clear segment at chest height.
  - **Gameplay objects from the scene files** for all 21 maps, with positions and parameters:
    spawns with team, weapon and ammo places, HP and armor boxes, super bonus, flags, KOTH
    points, death planes, **527 jump pads** with force fields.
  - **Map cards** (a top-down PNG per map) with automatic spot candidates: visibility, sight
    distance, height advantage and cover. The owner will confirm spots in game with a chat
    command.
  - **A few recorded player paths** at 20 Hz.
- **Jump pads are essential on our side.** On some maps the strong weapons are reachable only
  through a pad (on one map only 26 % of surfaces are walk-reachable). The shared nav graph
  must have jump-pad links with a launch model, not just walk, jump and drop links.

## 4. Which C# / .NET can the shared core target?

- The server is `net10.0`. Our vendored Lidgren is `netstandard2.0`.
- **Proposal: the core targets `netstandard2.1`.** Unity 6 supports it, and a `net10.0` server
  can reference it.
- **No UnityEngine types in the core.** Use `System.Numerics.Vector3`, which is available in
  .NET Standard 2.1 and in Unity, or our own small struct with converters in each adapter.
- **Both games use Unity world space** (Y up, metres), so coordinates need no conversion.
  Tick-based APIs should take `dt` and not assume a frame rate: our server steps bots at 20 Hz.

## 5. Which project first?

**Recommendation: start the shared core now, integrate it first in Contra City, and plug it
into Batla as soon as our movement and nav are ready.**

Why:
- **Contra City is the better test bench for the core.** All mechanics are already ported, it
  runs offline, and you have fast automated runs with screenshots. Perception, the aim model,
  the utility brain, DNA 0–100 and in-match memory can be tuned there quickly.
- **Batla is deliberately in stage A,** studying mechanics and maps before any bot logic. Its
  bot-specific parts (server-side movement port, jump-pad links, Scout shield, Tank dash,
  weapon pickups and "don't fight unarmed") come after that. Writing the brain now in Batla
  would mean writing it blind.
- **The live ladder, population, seasons and background simulation** are Batla's main goal, but
  they are game-agnostic. They should live in the core as well, behind abstractions for maps,
  weapons and match results. Contra City can reuse them too, for example for bot clan wars
  (ВККС), where a bot carrying «Вождь» has really won it.

## 6. On the proposed split (agreed, with additions)

The **core** is plain C# with no engine types:
- **Perception:**
  - sight cone;
  - line of sight through an interface;
  - hearing: shots, landings, jump pads, explosions, limited by distance and reaction.
- **Aim model:** reaction, error per weapon kind (hitscan, projectile, pellets), target motion.
- **Nav graph:**
  - nodes on layered surfaces;
  - links: walk, jump, drop, **jump pad**, bunnyhop, rocket or shotgun jump (game-specific
    link kinds as data);
  - A*;
  - a serialised format per map.
- **Utility brain** with trainable weights; danger maps per map zone; `LearningRate`.
- **DNA 0–100** and in-match memory (who killed me, revenge).
- **Later:** rating, leagues, seasons, population, activity, generations, and the
  background-simulation "second body" on a zone graph.

The **adapter per game** contains:
- **Body:** move, jump, crouch, look, fire, reload, switch, plus class actions (dash, shield).
- **World query:** line of sight, raycast, floor below.
- **Weapon catalogue:** role, effective range, hitscan or projectile, speed, splash,
  self-damage, ammo.
- **Map baker:** colliders and scene markers into the nav graph and pickup points.
- **Modes and objectives:** flags, points.
- **Match result feed** for learning and rating.

Suggested core interfaces to agree on first:

```csharp
public interface IWorldQuery {
    bool LineOfSight(Vector3 from, Vector3 to);
    bool Raycast(Vector3 origin, Vector3 dir, float max, out Vector3 hit, out Vector3 normal);
    float? FloorBelow(Vector3 point, float maxDrop);
}
public interface IBotBody {          // implemented by each game
    Vector3 Position { get; } Vector3 Velocity { get; } Vector3 View { get; }
    bool IsAlive { get; } int Health { get; } int Armor { get; }
    void Move(Vector2 wishDir, bool jump, bool crouch); void Look(Vector3 dir);
    void Fire(bool primary, bool secondary); void SwitchWeapon(int slotOrId);
    void ClassAction(string action);  // "dash", "shield", ... game-defined
}
public interface IBotBrain { void Tick(float dt, BotSenses senses, IBotBody body); }
```

One repository or package for the core, referenced by both projects, as you proposed.

## 7. Questions back

1. **Nav-graph format.** Will you bake it in Unity (editor or play mode) or as a standalone
   step? A shared serialisable format lets our server bake from its colliders with the same
   schema.
2. **Bunnyhop, rocket and shotgun jumps in the graph.** Do you model them as link kinds with
   preconditions (weapon in hand, health cost)? Our jump pads would fit the same pattern.
3. **Time step.** Can your bot brain run at a fixed 20 Hz, independent of the render frame?
   Our server cannot run faster for bots.
