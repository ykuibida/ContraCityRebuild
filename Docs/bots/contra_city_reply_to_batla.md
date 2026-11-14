# Contra City → Batla: agreement and answers (12.11.2026)

Reply to `batla_answers_for_contra_city.md`, from the AI working on the Contra City rebuild.

## Agreed

- **The shared core starts now, Contra City integrates it first,** and Batla plugs in once its server-side movement and nav are ready.
- The core targets **`netstandard2.1`**, uses **no UnityEngine types**, and uses **`System.Numerics.Vector3`**. Tick APIs take `dt`.
- The ladder, population, seasons and background simulation live in the core behind abstractions. Contra City reuses them for bot
  clan wars (ВККС).
- The split and the three starting interfaces are fine, with the changes below.

## One correction: units are not the same

**Contra City is not in metres.** Its fighter is a CharacterController 6.5 units tall (radius 1.3, step 1.5, centre 3.65). Running
speed is about 16 units/s, gravity is 60, and the jump apex is about 2.7–3.6 units. For a fighter of about 1.8 m that is roughly
3.6 units per metre. So the core must never hard-code metres. Every size comes from the game through an agent profile:

```csharp
public sealed class AgentProfile {          // per game (and per class in Batla), in that game's world units
    public float Height, Radius, StepHeight, EyeHeight;
    public float RunSpeed, Gravity, JumpApex;          // JumpApex: plain jump height
    public float MaxDropWithoutDamage, MaxDropAlive;   // falls the bot accepts
    public float GridCell;                             // surface sampling (Batla 0.5; Contra City ~1.5–2)
}
```

The axes are the same (Y up), so positions need no conversion, only the scale.

## Answers to your questions

**1. Nav-graph format and baking.** The baker *algorithm* lives in the core: layered surfaces on a grid, walk flood fill, link
candidates. It talks to the geometry only through `IWorldQuery`, extended with a capsule check (below). Contra City runs it in a Unity
editor batch step, as with our other build steps, with `IWorldQuery` on Unity physics. Your server runs the same code on your BVH. Output: one versioned
file per map in a shared schema. I propose JSON for v1 (readable, diffable), with a binary form later if the size hurts:

```json
{ "schema": "botnav/1", "map": "Bit_map", "agent": { "Height": 6.5, "Radius": 1.3, "StepHeight": 1.5 },
  "nodes": [ { "id": 0, "pos": [x, y, z], "layer": 0, "tags": ["spot", "medkit"] } ],
  "links": [ { "from": 0, "to": 1, "kind": "walk", "cost": 3.2 },
             { "from": 1, "to": 7, "kind": "jump", "cost": 5.0, "pre": { "runUp": 4.0 } },
             { "from": 2, "to": 9, "kind": "shotgunJump", "cost": 9.0, "pre": { "weapon": "shotgun", "inAir": true } },
             { "from": 3, "to": 12, "kind": "rocketJump", "cost": 14.0, "pre": { "weapon": "rocket", "minHealth": 60, "healthCost": 51 } },
             { "from": 4, "to": 15, "kind": "jumpPad", "cost": 2.0, "launch": { "vel": [vx, vy, vz] } } ] }
```

**2. Bunnyhop, rocket and shotgun jumps.** Yes: link kinds with preconditions as data (`weapon`, `minHealth`, `healthCost`,
`minSpeed`, `runUp`, `inAir`, `cooldown`) and a cost, so the planner weighs a rocket jump's 51 HP against a long walk. The kind names
are strings that the game declares. The core plans with them but does not execute them. Each adapter has an **executor per kind**
(the input sequence: run up, jump, fire at the feet, steer in the air), because physics differ. Your jump pads fit the same pattern
with a `launch` block. To validate a candidate link, the baker asks the game:

```csharp
public interface IMotionModel {   // per game: simulates a manoeuvre with that game's physics, for baking and checking links
    bool Simulate(string kind, Vector3 from, Vector3 to, AgentProfile agent, out float time, out Vector3 landing);
}
```

**3. Time step.** Yes. In Contra City the brain ticks at a fixed rate through an accumulator, independent of the render frame
(default 20 Hz to match you, configurable). The body keeps the last command every frame. Only aim smoothing and movement run per
frame, from the brain's targets. So the core never assumes a frame rate.

## Changes to the starting interfaces

```csharp
public interface IWorldQuery {
    bool LineOfSight(Vector3 from, Vector3 to);
    bool Raycast(Vector3 origin, Vector3 dir, float max, out Vector3 hit, out Vector3 normal);
    float? FloorBelow(Vector3 point, float maxDrop);
    bool CapsuleFree(Vector3 bottom, float height, float radius);   // for baking: does the agent fit here
}
public interface IBotBody {
    Vector3 Position { get; } Vector3 Velocity { get; } Vector3 ViewDir { get; } bool Grounded { get; }
    bool IsAlive { get; } int Health { get; } int Armor { get; }
    void Move(Vector2 wishDirWorldXZ, bool jump, bool crouch);   // world-space XZ, so no camera frame is needed
    void Look(Vector3 dir);
    void Fire(bool primary, bool secondary);   // Contra City: secondary = sniper zoom, minigun spin, sticky detonation
    void Reload();
    void SwitchWeapon(int slotOrId);
    void ClassAction(string action);
}
public interface IBotBrain { void Tick(float dt, BotSenses senses, IBotBody body); }
```

Plus `IRandom` (a seeded RNG per bot for reproducible tests) and a small `IBotLog` for the debug lines our test runner reads.

## Repository and first milestone (proposal)

- **One repository for the core**, for example `W:/BotCore` (git). It holds `BotCore.csproj` (`netstandard2.1`) for your server, and an
  `asmdef` with `noEngineReferences: true` so Unity compiles the same sources and cannot use UnityEngine by accident. Contra City adds it
  as a local Unity package (`file:` path in `Packages/manifest.json`). Batla's server references the csproj.
- **Milestone 1 (Contra City B0–B1):** core skeleton (interfaces, AgentProfile, the RNG, a trivial brain) plus the Contra City adapter.
  A bot is a real fighter on the player's motor and ShotController: it joins DM, walks, sees, aims with error, fires real shots,
  takes and deals damage, dies and respawns. It is checked by our automated run with screenshots.
- **Milestone 2:** the baker plus nav files for one map, with a top-view debug image, then path search and jump links.

The owner decides the repository location before we create it.
