// Bots steps 2–4 (map lab): movement experiments on a simulated fighter ("ghost", see MotorTrace). The ghost runs the player's
// SoldierController + CharacterMotor (step 1) at a fixed frame time (bots: 240 fps, the owner's choice); shotgun and rocket pushes
// use the game's own shot maths:
//   shotgun (ShotController DoShot case 7): StraightShot through the crosshair; in the air, with a 0.45 s cooldown, the motor gets
//     SetExplosionForce(hit point, 28 × 0.9310345) — pushed away from where the pellets hit (×0.7 for SG_Novapump);
//   rocket (case 8 + EffectManager.launcherEffect): RocketShot → it blows up at the hit point after its flight time; within 10.5
//     the push is (10.5 − distance) × 6.2 away from (blast − (0, 6, 0)).
// Assumptions to calibrate against live play: the crosshair ray from the standing eye (PlayerRig eye x/z, FPSCamera height 6.5),
// no pellet spread, the rocket from the eye instead of the weapon muzzle, the fire frame's push applied before that frame's motor
// step, 0.5 s to switch from the rocket launcher to the shotgun. Self-damage is not counted.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public static class LabSim
{
    public const float BotFps = 240f;

    public enum Move { Jump, RunJump, Bunnyhop, Shotgun, Rocket, RocketShotgun, Bat, Walk }   // Walk: run straight on (onto the next piece of floor, or off a ledge)

    public static string RuName(Move m)
    {
        switch (m)
        {
            case Move.Jump: return "Прыжок с места"; case Move.RunJump: return "Прыжок с разбега"; case Move.Bunnyhop: return "Распрыжка";
            case Move.Shotgun: return "Дробовик"; case Move.Rocket: return "Ракетный прыжок"; case Move.RocketShotgun: return "Ракета + дробовик";
            case Move.Walk: return "Бег / сход с края";
            default: return "Бита + прыжки";
        }
    }

    public static Color ColorOf(Move m)
    {
        switch (m)
        {
            case Move.Jump: return new Color(1f, 1f, 1f); case Move.RunJump: return new Color(1f, 0.9f, 0.2f); case Move.Bunnyhop: return new Color(0.3f, 1f, 0.3f);
            case Move.Shotgun: return new Color(1f, 0.5f, 0.1f); case Move.Rocket: return new Color(1f, 0.15f, 0.15f); case Move.RocketShotgun: return new Color(0.85f, 0.2f, 1f);
            case Move.Walk: return new Color(0.6f, 0.95f, 0.95f);
            default: return new Color(0.4f, 0.7f, 1f);
        }
    }

    public class Params
    {
        public Move move = Move.Jump;
        public Vector3 feet;                // start position (feet)
        public float yaw;                   // direction of travel
        public float runUp = 0.6f;          // seconds of running before the (first) jump
        public int hops = 4;                // bunnyhop / bat: how many jumps
        public float pitch = 50f;           // shotgun: camera pitch, + = down (the rocket is always fired at the feet, 85°)
        public float fireDelay = 0.15f;     // seconds after take-off before the first shot
        public int shots = 2;               // shotgun: shots in the air (clip 3, one per max(shot time, 0.45 s))
        public bool backwards = true;       // shotgun: back to the direction of travel, moving with S (the usual technique)
        public float fps = BotFps;          // simulation frame rate
        public float maxTime = 6f;
        public WeaponType hold = WeaponType.NONE;   // diagnostics: the weapon in hand instead of the move's own (walking with the bat)
        public Params Clone() => (Params)MemberwiseClone();

        /// <summary>How to do it by hand, in words.</summary>
        public string Recipe()
        {
            string run = runUp > 0.05f ? $"разбег {runUp:F1} с, " : "";
            switch (move)
            {
                case Move.Jump: return "прыжок с места";
                case Move.Walk: return $"просто бег вперёд {runUp:F1} с (на соседний кусок пола или вниз с края)";
                case Move.RunJump: return run + "прыжок";
                case Move.Bunnyhop: return run + $"распрыжка, {hops} прыжка";
                case Move.Bat: return "бита в руке, " + run + $"{hops} прыжка";
                case Move.Shotgun: return (backwards ? "спиной вперёд, назад на S, " : "") + run + $"прыжок и {shots} выстр. из дробовика под себя ({pitch:F0}° вниз) через {fireDelay:F2} с после каждого отрыва";
                case Move.Rocket: return run + $"прыжок, ракета под ноги через {fireDelay:F2} с";
                default: return run + $"прыжок, ракета под ноги, смена на дробовик, {shots} выстр. под себя ({pitch:F0}° вниз)";
            }
        }
    }

    public struct Event { public float t; public string what; public Vector3 at; }

    public class Result
    {
        public readonly List<Vector3> path = new List<Vector3>();
        public readonly List<Vector2> views = new List<Vector2>();   // yaw, pitch per step (for the "bot's eyes" camera)
        public readonly List<bool> air = new List<bool>();
        public readonly List<WeaponType> weapons = new List<WeaponType>();   // in hand, per step (the fighter model shows it)
        public readonly List<Event> events = new List<Event>();
        public float dt;
        public Vector3 start, takeoff, landing; public float takeoffT = -1f, landingT = -1f, time;
        public float maxRise, maxDownSpeed; public bool landed, onWalkable, reached; public int fallDamage;
        public float Distance => Horizontal(landing - takeoff);
        public float Rise => landing.y - start.y;
        public string Summary() => landed
            ? $"полёт {landingT - takeoffT:F2} с, дальность {Distance:F1}, по высоте {Rise:+0.0;-0.0}, выше старта до +{maxRise:F1}, {(onWalkable ? "приземлился на проходимое" : "приземлился вне проходимого")}{(fallDamage > 0 ? $", урон от падения {fallDamage}" : "")}"
            : $"не приземлился за {time:F1} с (выше старта до +{maxRise:F1})";
    }

    public static float Horizontal(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ---------------- the ghost ----------------

    /// <summary>A simulated fighter built like PlayerRig (CharacterController 6.5 × r1.3, centre 3.65, step 1.5, skin 0.45, slope 90)
    /// with the battle movement values of the given stats multiplier (GameFlow spawn: ForceBattleMovementValues).</summary>
    public static GameObject CreateGhost(string name, float statsMultiplier)
    {
        MotorTrace.SetupGhostLayer();
        var go = new GameObject(name) { layer = CharacterMotor.GhostLayer };
        var cc = go.AddComponent<CharacterController>();
        cc.height = 6.5f; cc.radius = 1.3f; cc.center = new Vector3(0f, 3.65f, 0f);
        cc.stepOffset = 1.5f; cc.skinWidth = 0.45f; cc.slopeLimit = 90f; cc.minMoveDistance = 0f;
        var m = go.AddComponent<CharacterMotor>(); m.externallyStepped = true; m.simulated = true;
        var s = go.AddComponent<SoldierController>(); s.externallyStepped = true; s.simulated = true;
        s.ForceBattleMovementValues(statsMultiplier);
        return go;
    }

    /// <summary>Stands the ghost at a point (feet), still.</summary>
    public static void Place(GameObject ghost, Vector3 feet, float yaw)
    {
        var m = ghost.GetComponent<CharacterMotor>();
        var st = m.CaptureState();
        st.position = feet; st.rotation = Quaternion.Euler(0f, yaw, 0f);
        st.velocity = st.frameVelocity = st.explosionForce = Vector3.zero; st.grounded = true; st.groundNormal = st.lastGroundNormal = Vector3.up;
        st.jumpingNow = st.holdingJumpButton = false; st.lastButtonDownTime = -100f; st.fallTrackingActive = false;
        st.hasUnderMapPreviousPosition = st.hasLastSafeGroundY = st.hasLastSafeGroundPosition = st.underMapWarningShown = false;
        st.strafeSustainSign = 0f;
        m.RestoreState(st);
        ghost.GetComponent<SoldierController>().RestoreState(new SoldierController.State { impactSpeedMultiplier = 1f });
    }

    // ---------------- weapons (the profile's, as the battle builds them) ----------------

    static List<CombatWeapon> weapons;
    static CombatWeapon Weapon(WeaponType t)
    {
        if (weapons == null) weapons = BattleWeapons.Build(Profile.Current).Select(kv => new CombatWeapon(kv.Key, kv.Value)).ToList();
        return weapons.FirstOrDefault(w => w.Type == t);
    }
    public static string WeaponName(WeaponType t) { var w = Weapon(t); return w != null ? w.SystemName : "—"; }
    public static bool Has(WeaponType t) => Weapon(t) != null;
    /// <summary>ShotController.GetSpeedMultiplier of a weapon in hand: max(1, SpeedValue), × 1.25 for one-handed melee or attribute "R".</summary>
    public static float SpeedMultiplier(WeaponType t) { var w = Weapon(t); return SpeedMultiplier(w); }
    static float SpeedMultiplier(CombatWeapon w) => w == null ? 1f : Mathf.Max(1f, w.SpeedValue) * (w.Type == WeaponType.ONE_HANDED_COLD_ARMS || w.Attributes.Contains("R") ? 1.25f : 1f);
    /// <summary>The shotgun's clip and the seconds to load one shell (the full reload / the clip).</summary>
    public static int ShotgunClip { get { var w = Weapon(WeaponType.SHOT_GUN); return w != null ? Mathf.Max(1, w.MaxLoadedAmmo) : 3; } }
    public static float ShotgunShellSeconds { get { var w = Weapon(WeaponType.SHOT_GUN); return w != null ? w.ReloadTime / 1e7f / Mathf.Max(1, w.MaxLoadedAmmo) : 0.5f; } }

    static void SetWeapon(GameObject ghost, CombatWeapon w)
    {
        var s = ghost.GetComponent<SoldierController>(); var m = ghost.GetComponent<CharacterMotor>();
        s.simHasWeapon = w != null;
        // ShotController.GetSpeedMultiplier of the weapon in hand: max(1, SpeedValue), × 1.25 for melee or attribute "R"
        s.simWeaponSpeedMultiplier = SpeedMultiplier(w);
        m.simBatAirFactor = 1.25f;
        var bat = Weapon(WeaponType.ONE_HANDED_COLD_ARMS);
        if (bat != null && string.Equals(bat.SystemName, "OHCA_BasebalBat", System.StringComparison.OrdinalIgnoreCase)) m.simBatAirFactor = Mathf.Max(1f, bat.SpeedValue) * 1.25f;
    }

    static readonly Vector3 EyeLocal = new Vector3(0.008388519f, 6.5f, 0.6456909f);   // PlayerRig eye x/z, FPSCamera NormalHeight
    public static Vector3 Eye(Vector3 feet, float yaw) => feet + Quaternion.Euler(0f, yaw, 0f) * EyeLocal;
    static Ray Crosshair(Transform g, float yaw, float pitch) => new Ray(Eye(g.position, yaw), Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);

    // ---------------- one move ----------------

    public static Result Run(GameObject ghost, Params p)
    {
        var m = ghost.GetComponent<CharacterMotor>(); var s = ghost.GetComponent<SoldierController>();
        var cc = ghost.GetComponent<CharacterController>();
        MotorTrace.IgnorePlayer(ghost);
        Place(ghost, p.feet, p.yaw);
        float dt = 1f / Mathf.Clamp(p.fps, 20f, 1000f), t = 0f, now = 100f;
        var r = new Result { start = p.feet, dt = dt };

        bool rocketFirst = p.move == Move.Rocket || p.move == Move.RocketShotgun;
        bool shotgun = p.move == Move.Shotgun || p.move == Move.RocketShotgun;
        var sg = Weapon(WeaponType.SHOT_GUN); var rl = Weapon(WeaponType.ROCKET_LAUNCHER);
        var inHand = p.hold != WeaponType.NONE ? Weapon(p.hold) : rocketFirst ? rl : shotgun ? sg : p.move == Move.Bat ? Weapon(WeaponType.ONE_HANDED_COLD_ARMS) : Weapon(WeaponType.MACHINE_GUN);
        SetWeapon(ghost, inHand);
        float sgInterval = sg != null ? Mathf.Max(sg.ShotTime / 1e7f, 0.45f) : 0.45f;

        bool backwards = shotgun && p.backwards;
        float viewYaw = backwards ? p.yaw + 180f : p.yaw;
        ghost.transform.rotation = Quaternion.Euler(0f, viewYaw, 0f);
        int rocketsLeft = rocketFirst ? 1 : 0, shotsLeft = shotgun ? Mathf.Clamp(p.shots, 0, 3) : 0;
        float nextShot = -1f, rocketBlowAt = -1f, pitchNow = 0f; Vector3 rocketAt = Vector3.zero;
        bool airborne = false; int takeoffs = 0; float peakY = p.feet.y;
        // bunnyhop / bat / shotgun chain: the motor jumps on landing only if jump was pressed within the last 0.2 s
        // (CharacterMotor: now − lastButtonDownTime < 0.2, the press time is taken on a fresh press), so holding it is not enough:
        // after 0.1 s in the air the jump is pressed again every other frame (as a scroll-wheel jump), then the landing re-jumps
        bool chain = p.move == Move.Bunnyhop || p.move == Move.Bat || shotgun;
        int jumpsWanted = p.move == Move.Bunnyhop || p.move == Move.Bat ? Mathf.Max(1, p.hops) : 1;
        float takeoffAt = -1f; int frame = 0;
        bool walk = p.move == Move.Walk;
        float jumpAt = walk ? float.MaxValue : p.move == Move.Jump || p.runUp <= 0.01f ? 0.02f : p.runUp;
        var holding = inHand != null ? inHand.Type : WeaponType.MACHINE_GUN;
        r.path.Add(p.feet); r.views.Add(new Vector2(viewYaw, 0f)); r.air.Add(false); r.weapons.Add(holding);

        while (t < p.maxTime)
        {
            bool more = shotgun ? shotsLeft > 0 || rocketsLeft > 0 : takeoffs < jumpsWanted;
            bool forward = p.move != Move.Jump && !backwards;
            bool back = backwards && (p.runUp > 0.01f || airborne || takeoffs > 0);
            bool jump;
            if (walk) jump = false;
            else if (!chain) jump = t >= jumpAt && t < jumpAt + 0.25f;
            else if (t < jumpAt) jump = false;
            else if (!airborne) jump = takeoffs == 0 || more;
            else jump = t - takeoffAt < 0.1f || (more && (frame & 1) == 0);
            frame++;
            var k = SoldierController.Keys.Moves(false, false, forward, back); k.jump = jump;
            s.Step(k, dt, now);

            // weapons fired this frame (before the motor step, see the header)
            if (airborne && t >= nextShot && (rocketsLeft > 0 || shotsLeft > 0))
            {
                cc.enabled = false;   // the live player is on layer 2, which the shot masks leave out; the ghost is not
                if (rocketsLeft > 0)
                {
                    pitchNow = 85f;
                    var shot = ShotCalculator.RocketShot(Crosshair(ghost.transform, viewYaw, pitchNow), null, rl != null ? rl.GetType() : (int)WeaponType.ROCKET_LAUNCHER, rl != null ? rl.Velocity : 20f);
                    rocketAt = shot.Origin;
                    rocketBlowAt = t + (shot.LandingTimeStamp - shot.TimeStamp + 150) / 1000f;
                    r.events.Add(new Event { t = t, what = $"ракета под ноги, взрыв через {rocketBlowAt - t:F2} с", at = rocketAt });
                    rocketsLeft--;
                    if (shotsLeft > 0) { nextShot = t + 0.5f; SetWeapon(ghost, sg); holding = WeaponType.SHOT_GUN; r.events.Add(new Event { t = t, what = "смена на дробовик (0.5 с)", at = ghost.transform.position }); }
                }
                else if (!m.grounded)
                {
                    pitchNow = p.pitch;
                    var shot = ShotCalculator.StraightShot(Crosshair(ghost.transform, viewYaw, pitchNow), null, sg != null ? sg.GetType() : (int)WeaponType.SHOT_GUN, 0, false);
                    if (shot != null)
                    {
                        float power = 28 * 0.9310345f * (sg != null && sg.SystemName.Contains("SG_Novapump") ? 0.7f : 1f);
                        m.SetExplosionForce(shot.Origin, power);
                        r.events.Add(new Event { t = t, what = $"дробовик: толчок {power:F1}", at = shot.Origin });
                    }
                    else r.events.Add(new Event { t = t, what = "дробовик: никуда не попал, толчка нет", at = ghost.transform.position });
                    nextShot = t + sgInterval; shotsLeft--;
                }
                cc.enabled = true;
            }
            if (rocketBlowAt >= 0f && t >= rocketBlowAt)
            {
                float d = Vector3.Distance(ghost.transform.position, rocketAt);
                if (d < 10.5f) { float power = (10.5f - d) * 6.2f; m.SetExplosionForce(rocketAt - new Vector3(0f, 6f, 0f), power); r.events.Add(new Event { t = t, what = $"взрыв в {d:F1} от бойца: толчок {power:F1}", at = rocketAt }); }
                else r.events.Add(new Event { t = t, what = $"взрыв в {d:F1} от бойца: дальше 10.5, толчка нет", at = rocketAt });
                rocketBlowAt = -1f;
            }

            m.Step(dt, now);
            t += dt; now += dt;
            var pos = ghost.transform.position;
            r.path.Add(pos); r.views.Add(new Vector2(viewYaw, pitchNow)); r.air.Add(!m.grounded); r.weapons.Add(holding);
            if (pitchNow > 0f && t > nextShot - 0.3f && rocketsLeft == 0 && shotsLeft == 0) pitchNow = Mathf.MoveTowards(pitchNow, 0f, 200f * dt);
            peakY = Mathf.Max(peakY, pos.y);
            if (m.movement.velocity.y < 0f) r.maxDownSpeed = Mathf.Max(r.maxDownSpeed, -m.movement.velocity.y);
            if (!m.grounded && !airborne)
            {
                airborne = true; takeoffs++; takeoffAt = t;
                if (takeoffs == 1) { r.takeoff = pos; r.takeoffT = t; }
                nextShot = Mathf.Max(nextShot, t + p.fireDelay);   // each take-off: the shot comes fireDelay later (not before the weapon is ready)
                r.events.Add(new Event { t = t, what = "отрыв", at = pos });
            }
            else if (m.grounded && airborne)
            {
                airborne = false;
                r.events.Add(new Event { t = t, what = "приземление", at = pos });
                bool finished = shotgun ? shotsLeft == 0 && rocketsLeft == 0 : takeoffs >= jumpsWanted && rocketsLeft == 0;
                if (finished && rocketBlowAt < 0f) { r.landed = true; r.landing = pos; r.landingT = t; break; }
            }
            // walking: done once the run time is over and the fighter stands (after a fall off a ledge, when it lands)
            if (walk && t >= p.runUp && m.grounded) { r.landed = true; r.landing = pos; r.landingT = t; if (r.takeoffT < 0f) { r.takeoff = p.feet; r.takeoffT = 0f; } break; }
            if (!walk && takeoffs == 0 && t > jumpAt + 1f) break;   // never left the ground
        }
        // after the landing the fighter stands 0.3 s with no keys: a landing on the very edge (the capsule's round bottom caught on a
        // corner, grounded for a moment, feet under the floor top) slides off — that is no landing (Монетный двор, container tops)
        if (r.landed)
        {
            var still = new SoldierController.Keys();
            for (float st = 0f; st < 0.3f; st += dt)
            {
                s.Step(still, dt, now); m.Step(dt, now); t += dt; now += dt;
                var pos = ghost.transform.position;
                r.path.Add(pos); r.views.Add(new Vector2(viewYaw, 0f)); r.air.Add(!m.grounded); r.weapons.Add(r.weapons.Count > 0 ? r.weapons[r.weapons.Count - 1] : WeaponType.NONE);
                if (!m.grounded) { r.landed = false; break; }
            }
            if (r.landed) r.landing = ghost.transform.position;
        }
        r.time = t;
        r.maxRise = peakY - p.feet.y;
        if (r.landed)
        {
            r.onWalkable = LabWorld.OnWalkable(r.landing, 1f);
            float h = Mathf.Max(0f, peakY - r.landing.y);   // CharacterMotor.ReportLocalFallDamageOnLand: 40 from 30, 80 from 60, if falling ≥ 18
            r.fallDamage = h < 30f || r.maxDownSpeed < 18f ? 0 : h >= 60f ? 80 : 40;
        }
        return r;
    }

    // ---------------- walking (a first bot steering: run along the NavMesh path with the real motor) ----------------

    /// <summary>Runs from where the ghost stands to <paramref name="target"/> along the walkable surface: each frame it faces the next
    /// corner of the grid path and holds W, with <paramref name="hold"/> in hand (the bat runs 25% quicker). reached = within 1.5 of
    /// the target.</summary>
    public static Result WalkTo(GameObject ghost, Vector3 target, WeaponType hold = WeaponType.MACHINE_GUN, float fps = BotFps)
    {
        var m = ghost.GetComponent<CharacterMotor>(); var s = ghost.GetComponent<SoldierController>();
        MotorTrace.IgnorePlayer(ghost);
        if (Weapon(hold) == null) hold = WeaponType.MACHINE_GUN;
        SetWeapon(ghost, Weapon(hold));
        float dt = 1f / fps, t = 0f, now = 100f;
        var start = ghost.transform.position;
        var r = new Result { start = start, dt = dt };
        var gridPath = LabWorld.Current != null ? LabWorld.Current.FindPath(start, target) : null;   // the walk grid (physics), not the NavMesh
        Vector3[] corners = gridPath != null && gridPath.Count > 1 ? gridPath.ToArray() : new[] { start, target };
        float length = 0f; for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        float limit = length / 12f + 3f;
        int c = 1; float yaw = ghost.transform.eulerAngles.y;
        r.path.Add(start); r.views.Add(new Vector2(yaw, 0f)); r.air.Add(false); r.weapons.Add(hold);
        while (t < limit)
        {
            var pos = ghost.transform.position;
            while (c < corners.Length - 1 && Horizontal(corners[c] - pos) < 1.1f) c++;
            var to = corners[Mathf.Min(c, corners.Length - 1)] - pos; to.y = 0f;
            if (Horizontal(target - pos) < 1.5f) { r.reached = true; break; }
            float want = to.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(to).eulerAngles.y : yaw;
            yaw = Mathf.MoveTowardsAngle(yaw, want, 720f * dt);
            ghost.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // W only once it faces the way (within 40°): after a backwards shotgun jump the bot stands facing back; holding W while
            // it turns walked it off the container it had just landed on (Монетный двор)
            bool facing = Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 40f;
            s.Step(SoldierController.Keys.Moves(false, false, facing, false), dt, now);
            m.Step(dt, now);
            t += dt; now += dt;
            r.path.Add(ghost.transform.position); r.views.Add(new Vector2(yaw, 0f)); r.air.Add(!m.grounded); r.weapons.Add(hold);
        }
        r.time = t; r.landing = ghost.transform.position;
        return r;
    }
}
