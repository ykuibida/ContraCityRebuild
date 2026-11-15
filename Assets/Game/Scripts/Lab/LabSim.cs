// Bots step 2 (map lab): single movement experiments on a simulated fighter ("ghost", see MotorTrace). The ghost runs the player's
// SoldierController + CharacterMotor (step 1) at a fixed frame time; shotgun and rocket pushes use the game's own shot maths:
//   shotgun (ShotController DoShot case 7): StraightShot through the crosshair; in the air, with a 0.45 s cooldown, the motor gets
//     SetExplosionForce(hit point, 28 × 0.9310345) — pushed away from where the pellets hit (×0.7 for SG_Novapump);
//   rocket (case 8 + EffectManager.launcherEffect): RocketShot → it blows up at the hit point after its flight time; within 10.5
//     the push is (10.5 − distance) × 6.2 away from (blast − (0, 6, 0)).
// Assumptions to calibrate against live play later (step 4): the crosshair ray from the standing eye (PlayerRig eye x/z, FPSCamera
// height 6.5), no pellet spread, the rocket from the eye instead of the weapon muzzle, the fire frame's push applied before that
// frame's motor step. Self-damage is not counted.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class LabSim
{
    public enum Move { Jump, RunJump, Bunnyhop, Shotgun, Rocket, Bat }

    public static string RuName(Move m)
    {
        switch (m)
        {
            case Move.Jump: return "Прыжок с места"; case Move.RunJump: return "Прыжок с разбега"; case Move.Bunnyhop: return "Распрыжка";
            case Move.Shotgun: return "Дробовик"; case Move.Rocket: return "Ракетный прыжок"; default: return "Бита + прыжки";
        }
    }

    public static Color ColorOf(Move m)
    {
        switch (m)
        {
            case Move.Jump: return new Color(1f, 1f, 1f); case Move.RunJump: return new Color(1f, 0.9f, 0.2f); case Move.Bunnyhop: return new Color(0.3f, 1f, 0.3f);
            case Move.Shotgun: return new Color(1f, 0.5f, 0.1f); case Move.Rocket: return new Color(1f, 0.15f, 0.15f); default: return new Color(0.4f, 0.7f, 1f);
        }
    }

    public class Params
    {
        public Move move = Move.Jump;
        public Vector3 feet;                // start position (feet)
        public float yaw;                   // direction of travel
        public float runUp = 0.6f;          // seconds of running before the (first) jump
        public int hops = 4;                // bunnyhop / bat: how many jumps (jump held)
        public float pitch = 50f;           // shotgun / rocket: camera pitch, + = down
        public float fireDelay = 0.15f;     // shotgun / rocket: seconds after take-off before the first shot
        public int shots = 2;               // shotgun: shots in the air (clip 3, one per max(shot time, 0.45 s))
        public bool backwards = true;       // shotgun: back to the direction of travel, moving with S (the usual technique)
        public float fps = 60f;             // simulation frame rate
        public float maxTime = 6f;
        public Params Clone() => (Params)MemberwiseClone();
    }

    public struct Event { public float t; public string what; public Vector3 at; }

    public class Result
    {
        public readonly List<Vector3> path = new List<Vector3>();
        public readonly List<Event> events = new List<Event>();
        public Vector3 start, takeoff, landing; public float takeoffT = -1f, landingT = -1f, time;
        public float maxRise, maxDownSpeed; public bool landed, onWalkable; public int fallDamage;
        public float Distance => Horizontal(landing - takeoff);
        public float Rise => landing.y - start.y;
        public string Summary() => landed
            ? $"полёт {landingT - takeoffT:F2} с, дальность {Distance:F1}, по высоте {Rise:+0.0;-0.0}, выше старта до +{maxRise:F1}, {(onWalkable ? "приземлился на проходимое" : "приземлился вне проходимого")}{(fallDamage > 0 ? $", урон от падения {fallDamage}" : "")}"
            : $"не приземлился за {time:F1} с (выше старта до +{maxRise:F1})";
    }

    static float Horizontal(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ---------------- the ghost ----------------

    /// <summary>A simulated fighter built like PlayerRig (CharacterController 6.5 × r1.3, centre 3.65, step 1.5, skin 0.45, slope 90)
    /// with the battle movement values of the given stats multiplier (GameFlow spawn: ForceBattleMovementValues).</summary>
    public static GameObject CreateGhost(string name, float statsMultiplier, bool visible)
    {
        MotorTrace.SetupGhostLayer();
        var go = new GameObject(name) { layer = CharacterMotor.GhostLayer };
        var cc = go.AddComponent<CharacterController>();
        cc.height = 6.5f; cc.radius = 1.3f; cc.center = new Vector3(0f, 3.65f, 0f);
        cc.stepOffset = 1.5f; cc.skinWidth = 0.45f; cc.slopeLimit = 90f; cc.minMoveDistance = 0f;
        var m = go.AddComponent<CharacterMotor>(); m.externallyStepped = true; m.simulated = true;
        var s = go.AddComponent<SoldierController>(); s.externallyStepped = true; s.simulated = true;
        s.ForceBattleMovementValues(statsMultiplier);
        if (visible)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "Body"; body.layer = CharacterMotor.GhostLayer;
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 3.65f, 0f); body.transform.localScale = new Vector3(2.6f, 3.25f, 2.6f);
            body.GetComponent<Renderer>().sharedMaterial = LabDraw.Mat(new Color(0.6f, 1f, 0.6f, 0.45f));
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 6.27f, 1.2f); nose.transform.localScale = new Vector3(0.5f, 0.5f, 1.2f);
            nose.GetComponent<Renderer>().sharedMaterial = LabDraw.Mat(new Color(1f, 1f, 1f, 0.8f));
        }
        return go;
    }

    // ---------------- weapons (the profile's, as the battle builds them) ----------------

    static List<CombatWeapon> weapons;
    static CombatWeapon Weapon(WeaponType t)
    {
        if (weapons == null) weapons = BattleWeapons.Build(Profile.Current).Select(kv => new CombatWeapon(kv.Key, kv.Value)).ToList();
        return weapons.FirstOrDefault(w => w.Type == t);
    }
    public static string WeaponName(WeaponType t) { var w = Weapon(t); return w != null ? w.SystemName : "—"; }

    static readonly Vector3 EyeLocal = new Vector3(0.008388519f, 6.5f, 0.6456909f);   // PlayerRig eye x/z, FPSCamera NormalHeight
    static Ray Crosshair(Transform g, float yaw, float pitch) =>
        new Ray(g.position + Quaternion.Euler(0f, yaw, 0f) * EyeLocal, Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);

    // ---------------- one experiment ----------------

    public static Result Run(GameObject ghost, Params p)
    {
        var m = ghost.GetComponent<CharacterMotor>(); var s = ghost.GetComponent<SoldierController>();
        var r = new Result { start = p.feet };
        var cc = ghost.GetComponent<CharacterController>();
        MotorTrace.IgnorePlayer(ghost);
        var st = m.CaptureState();
        st.position = p.feet; st.rotation = Quaternion.Euler(0f, p.yaw, 0f);
        st.velocity = st.frameVelocity = st.explosionForce = Vector3.zero; st.grounded = true; st.groundNormal = st.lastGroundNormal = Vector3.up;
        st.jumpingNow = st.holdingJumpButton = false; st.lastButtonDownTime = -100f; st.fallTrackingActive = false;
        st.hasUnderMapPreviousPosition = st.hasLastSafeGroundY = st.hasLastSafeGroundPosition = st.underMapWarningShown = false;
        m.RestoreState(st);
        s.RestoreState(new SoldierController.State { impactSpeedMultiplier = 1f });

        var weaponType = p.move == Move.Shotgun ? WeaponType.SHOT_GUN : p.move == Move.Rocket ? WeaponType.ROCKET_LAUNCHER : p.move == Move.Bat ? WeaponType.ONE_HANDED_COLD_ARMS : WeaponType.MACHINE_GUN;
        var w = Weapon(weaponType);
        s.simHasWeapon = w != null;
        // ShotController.GetSpeedMultiplier of the weapon in hand: max(1, SpeedValue), × 1.25 for melee or attribute "R"
        s.simWeaponSpeedMultiplier = w == null ? 1f : Mathf.Max(1f, w.SpeedValue) * (w.Type == WeaponType.ONE_HANDED_COLD_ARMS || w.Attributes.Contains("R") ? 1.25f : 1f);
        m.simBatAirFactor = 1.25f;
        var bat = Weapon(WeaponType.ONE_HANDED_COLD_ARMS);
        if (bat != null && string.Equals(bat.SystemName, "OHCA_BasebalBat", System.StringComparison.OrdinalIgnoreCase)) m.simBatAirFactor = Mathf.Max(1f, bat.SpeedValue) * 1.25f;
        float shotInterval = w != null ? Mathf.Max(w.ShotTime / 10000f / 1000f, 0.45f) : 0.45f;

        float dt = 1f / Mathf.Clamp(p.fps, 20f, 1000f), t = 0f, now = 100f;
        float viewYaw = p.move == Move.Shotgun && p.backwards ? p.yaw + 180f : p.yaw;
        ghost.transform.rotation = Quaternion.Euler(0f, viewYaw, 0f);
        int shotsLeft = p.move == Move.Shotgun ? Mathf.Clamp(p.shots, 0, 3) : p.move == Move.Rocket ? 1 : 0;
        float nextShot = -1f, rocketBlowAt = -1f; Vector3 rocketAt = Vector3.zero;
        bool airborne = false; int takeoffs = 0; float peakY = p.feet.y;
        // bunnyhop / bat / shotgun chain: the motor jumps on landing only if jump was pressed within the last 0.2 s
        // (CharacterMotor: now − lastButtonDownTime < 0.2, the press time is taken on a fresh press), so holding it is not enough:
        // after 0.1 s in the air the jump is pressed again every other frame (as a scroll-wheel jump), then the landing re-jumps
        bool chain = p.move == Move.Bunnyhop || p.move == Move.Bat || p.move == Move.Shotgun;
        int jumpsWanted = p.move == Move.Bunnyhop || p.move == Move.Bat ? Mathf.Max(1, p.hops) : p.move == Move.Shotgun ? 3 : 1;
        float takeoffAt = -1f; int frame = 0;
        float jumpAt = p.move == Move.Jump || p.move == Move.Shotgun && p.runUp <= 0f ? 0.05f : p.runUp;
        r.path.Add(p.feet);

        while (t < p.maxTime)
        {
            // controls of this frame
            bool forward = p.move != Move.Jump && !(p.move == Move.Shotgun && p.backwards);
            bool back = p.move == Move.Shotgun && p.backwards && (p.runUp > 0f || airborne || takeoffs > 0);
            // jump held: the motor gives extra height while it is held just after take-off (CharacterMotor extraHeight), so a single
            // jump is held 0.25 s; bunnyhop / bat hold it until the last take-off is over (holding it re-jumps on every landing)
            bool more = p.move == Move.Shotgun ? shotsLeft > 0 : takeoffs < jumpsWanted;
            bool jump;
            if (!chain) jump = t >= jumpAt && t < jumpAt + 0.25f;
            else if (t < jumpAt) jump = false;
            else if (!airborne) jump = takeoffs == 0 || more;
            else jump = t - takeoffAt < 0.1f || (more && (frame & 1) == 0);
            frame++;
            var k = SoldierController.Keys.Moves(false, false, forward, back); k.jump = jump;
            s.Step(k, dt, now);

            // weapons fired this frame (before the motor step, see the header)
            if (shotsLeft > 0 && airborne && t >= nextShot)
            {
                var ray = Crosshair(ghost.transform, viewYaw, p.pitch);
                cc.enabled = false;   // the live player is on layer 2, which the shot masks leave out; the ghost is not
                if (p.move == Move.Shotgun && !m.grounded)
                {
                    var shot = ShotCalculator.StraightShot(ray, null, w != null ? w.GetType() : (int)WeaponType.SHOT_GUN, 0, false);
                    if (shot != null)
                    {
                        float power = 28 * 0.9310345f * (w != null && w.SystemName.Contains("SG_Novapump") ? 0.7f : 1f);
                        m.SetExplosionForce(shot.Origin, power);
                        r.events.Add(new Event { t = t, what = $"дробовик: толчок {power:F1} от {shot.Origin}", at = shot.Origin });
                    }
                    else r.events.Add(new Event { t = t, what = "дробовик: никуда не попал, толчка нет", at = ray.origin });
                    nextShot = t + shotInterval; shotsLeft--;
                }
                else if (p.move == Move.Rocket)
                {
                    var shot = ShotCalculator.RocketShot(ray, null, w != null ? w.GetType() : (int)WeaponType.ROCKET_LAUNCHER, w != null ? w.Velocity : 20f);
                    rocketAt = shot.Origin;
                    rocketBlowAt = t + (shot.LandingTimeStamp - shot.TimeStamp + 150) / 1000f;
                    r.events.Add(new Event { t = t, what = $"ракета: выстрел, взрыв через {rocketBlowAt - t:F2} с в {rocketAt}", at = rocketAt });
                    shotsLeft--;
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
            r.path.Add(pos);
            peakY = Mathf.Max(peakY, pos.y);
            if (m.movement.velocity.y < 0f) r.maxDownSpeed = Mathf.Max(r.maxDownSpeed, -m.movement.velocity.y);
            if (!m.grounded && !airborne)
            {
                airborne = true; takeoffs++; takeoffAt = t;
                if (takeoffs == 1) { r.takeoff = pos; r.takeoffT = t; }
                nextShot = Mathf.Max(nextShot, t + p.fireDelay);   // each take-off: the shot comes fireDelay later (and not before the weapon is ready)
                r.events.Add(new Event { t = t, what = "отрыв", at = pos });
            }
            else if (m.grounded && airborne)
            {
                airborne = false;
                r.events.Add(new Event { t = t, what = "приземление", at = pos });
                bool finished = p.move == Move.Shotgun ? shotsLeft == 0 : takeoffs >= jumpsWanted && shotsLeft == 0;
                if (finished && rocketBlowAt < 0f) { r.landed = true; r.landing = pos; r.landingT = t; break; }
            }
            if (takeoffs == 0 && t > jumpAt + 1f) break;   // never left the ground
        }
        r.time = t;
        r.maxRise = peakY - p.feet.y;
        if (r.landed)
        {
            r.onWalkable = LabWorld.OnWalkable(r.landing);
            float h = Mathf.Max(0f, peakY - r.landing.y);   // CharacterMotor.ReportLocalFallDamageOnLand: 40 from 30, 80 from 60, if falling ≥ 18
            r.fallDamage = h < 30f || r.maxDownSpeed < 18f ? 0 : h >= 60f ? 80 : 40;
        }
        return r;
    }
}
