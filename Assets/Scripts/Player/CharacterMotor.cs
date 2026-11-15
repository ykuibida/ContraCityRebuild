// Port of the New Era CharacterMotor (W:/Contra City Extract/NE_Scripts_Decrypted/CharacterMotor.cs). Penta 2016 is the reference
// (Penta_Original/Export_WebPlayer_2016/.../CharacterMotor.cs). Base = New Era by the owner's decision (2026-11-11).
// Kept: UpdateFunction (NE :245-391), round intermission lock (:264-277, 393-401), input velocity with the "Colleague" strafe sustain
// and ground-normal skip (:815-857, 1159-1200), gravity / jumping (:952-1009), air speed clamp (:882-928), local fall damage
// (:602-710, offline path → PlayerManager.ApplyLocalFallDamage), under-map recovery / warning (:403-600).
// Dropped: anti-cheat (喰 motorSecurity, Check, lstccounter), moving platforms (prefab movingPlatform.enabled = 0),
// remote-head support (:1077, needs other players), pit kill zone (:756 stub returns false).
// Values = the original fighter prefab Fighter_locomotion (gravity 60, maxFallSpeed 400, useFixedUpdate 0).
// Rebuild (bots step 1, 15.11.2026): the frame time and clock come in through Step(dt, now) instead of Time.deltaTime / Time.time,
// so the same maths can run many times per frame for simulated fighters (MotorTrace, the map lab). For the player Update calls
// Step(Time.deltaTime, Time.time): identical values, identical behaviour. CaptureState / RestoreState snapshot every field the
// next step reads.
using System;
using UnityEngine;

[Serializable]
public class CharacterMotorMovement
{
    public float maxForwardSpeed = 10f;
    public float maxSidewaysSpeed = 10f;
    public float maxBackwardsSpeed = 10f;
    public bool clampSpeedOnSlopes = false;
    public AnimationCurve maxSpeedOnSlope = new AnimationCurve(new Keyframe(-90f, 10f), new Keyframe(0f, 10f), new Keyframe(90f, 0f));
    // NB: as in the original, "Acceleration" is used when slowing down and "Decceration" when speeding up.
    public float maxGroundAcceleration = 400f;
    public float maxGroundDecceration = 100f;
    public float maxAirAcceleration = 10f;
    public float maxAirDecceration = 10f;
    public float gravity = 60f;
    public float maxFallSpeed = 400f;
    [NonSerialized] public CollisionFlags collisionFlags;
    [NonSerialized] public Vector3 velocity;
    [NonSerialized] public Vector3 frameVelocity = Vector3.zero;
    [NonSerialized] public Vector3 hitPoint = Vector3.zero;
    [NonSerialized] public Vector3 lastHitPoint = new Vector3(float.PositiveInfinity, 0f, 0f);
}

[Serializable]
public class CharacterMotorJumping
{
    public bool enabled = true;
    public float baseHeight = 2f;
    public float extraHeight = 2f;
    public float perpAmount = 0f;
    public float steepPerpAmount = 0.5f;
    [NonSerialized] public bool jumping;
    [NonSerialized] public bool holdingJumpButton;
    [NonSerialized] public float lastStartTime;
    [NonSerialized] public float lastButtonDownTime = -100f;
    [NonSerialized] public Vector3 jumpDir = Vector3.up;
}

[Serializable]
public class CharacterMotorSliding
{
    public bool enabled = false;
    public float slidingSpeed = 15f;
    public float sidewaysControl = 1f;
    public float speedControl = 0.4f;
}

[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    public CharacterMotorJumping jumping = new CharacterMotorJumping();
    public CharacterMotorMovement movement = new CharacterMotorMovement();
    public CharacterMotorSliding sliding = new CharacterMotorSliding();
    public bool canControl = true;
    public bool useFixedUpdate = false;
    public Vector3 explosionForce = Vector3.zero;
    float explosionSpeed = 1f;

    [NonSerialized] public Vector3 inputMoveDirection = Vector3.zero;
    [NonSerialized] public bool inputJump;
    [NonSerialized] public bool grounded = true;
    [NonSerialized] public Vector3 groundNormal = Vector3.zero;
    /// <summary>NE: extra desired horizontal velocity (zombie hit push); zero for humans.</summary>
    [NonSerialized] public Vector3 externalDesiredVelocity = Vector3.zero;
    Vector3 lastGroundNormal = Vector3.zero;
    Transform tr;
    CharacterController controller;
    PlayerVitals vitals;
    SoldierController soldier;

    /// <summary>Physics layer of simulated fighters: collides with the map like the player (layer 2) but not with the player or each other.</summary>
    public const int GhostLayer = 27;
    /// <summary>Stepped by its owner (simulation) instead of Update.</summary>
    [NonSerialized] public bool externallyStepped;
    /// <summary>A simulated fighter: no UI hooks, no round lock, no input reset; the bat air-speed factor comes from <see cref="simBatAirFactor"/>.</summary>
    [NonSerialized] public bool simulated;
    [NonSerialized] public float simBatAirFactor = 1.25f;
    /// <summary>Recording hooks (MotorTrace): before and after every Step.</summary>
    [NonSerialized] public Action<CharacterMotor> OnBeforeStep, OnAfterStep;
    float dt, now;   // the current step's frame time and clock (Time.deltaTime / Time.time for the player)
    public float LastDt => dt;
    public float LastNow => now;

    /// <summary>NE IsRoundIntermissionMovementLocked: GameHUD state TimeOver / TimeOverReady / RoundStarting (set by BattleUI).</summary>
    public static Func<bool> RoundIntermissionLocked;
    /// <summary>NE GameHUD.CheatMessagePersistent (under-map warning, "cheat_msg"), set by BattleUI.</summary>
    public static Action UnderMapWarning;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        vitals = GetComponent<PlayerVitals>();
        soldier = GetComponent<SoldierController>();
        tr = transform;
    }

    bool Dead => vitals != null && vitals.Dead;

    void Update() { if (!useFixedUpdate && !externallyStepped) Step(Time.deltaTime, Time.time); }
    void FixedUpdate() { if (useFixedUpdate && !externallyStepped) Step(Time.deltaTime, Time.time); }

    /// <summary>One motor update with the given frame time and clock (seconds).</summary>
    public void Step(float deltaTime, float time)
    {
        if (controller == null) Awake();
        dt = deltaTime; now = time;
        OnBeforeStep?.Invoke(this);
        UpdateFunction();
        OnAfterStep?.Invoke(this);
    }

    /// <summary>Copies the tuning (prefab values and what SoldierController.ForceBattleMovementValues set) from another motor.</summary>
    public void CopySettingsFrom(CharacterMotor o)
    {
        var a = movement; var b = o.movement;
        a.maxForwardSpeed = b.maxForwardSpeed; a.maxSidewaysSpeed = b.maxSidewaysSpeed; a.maxBackwardsSpeed = b.maxBackwardsSpeed;
        a.clampSpeedOnSlopes = b.clampSpeedOnSlopes; a.maxSpeedOnSlope = b.maxSpeedOnSlope;
        a.maxGroundAcceleration = b.maxGroundAcceleration; a.maxGroundDecceration = b.maxGroundDecceration;
        a.maxAirAcceleration = b.maxAirAcceleration; a.maxAirDecceration = b.maxAirDecceration;
        a.gravity = b.gravity; a.maxFallSpeed = b.maxFallSpeed;
        jumping.enabled = o.jumping.enabled; jumping.baseHeight = o.jumping.baseHeight; jumping.extraHeight = o.jumping.extraHeight;
        jumping.perpAmount = o.jumping.perpAmount; jumping.steepPerpAmount = o.jumping.steepPerpAmount;
        sliding.enabled = o.sliding.enabled; sliding.slidingSpeed = o.sliding.slidingSpeed; sliding.sidewaysControl = o.sliding.sidewaysControl; sliding.speedControl = o.sliding.speedControl;
        canControl = o.canControl; useFixedUpdate = o.useFixedUpdate; explosionSpeed = o.explosionSpeed;
    }

    // NE :245-391
    void UpdateFunction()
    {
        if (Dead) { ResetLocalFallTracking(); return; }                          // NE :259-263 (Penta :271 too)
        if (!simulated && RoundIntermissionLocked != null && RoundIntermissionLocked())        // NE :264-277
        {
            inputMoveDirection = Vector3.zero; inputJump = false;
            movement.velocity = Vector3.zero; movement.frameVelocity = Vector3.zero; explosionForce = Vector3.zero;
            jumping.jumping = false; jumping.holdingJumpButton = false; jumping.lastButtonDownTime = -100f;
            ResetLocalFallTracking();
            return;
        }
        Vector3 velocity = movement.velocity;
        velocity = ApplyInputVelocityChange(velocity);
        velocity = ApplyGravityAndJumping(velocity);
        velocity = ApplyExplosionForces(velocity);
        velocity = ClampLocalAirSpeedToBatRunSpeedLimit(velocity);
        UpdateLocalFallTrackingWhileAirborne();

        Vector3 motion = velocity * dt;
        float pushDown = Mathf.Max(controller.stepOffset, new Vector3(motion.x, 0f, motion.z).magnitude);
        if (grounded) motion -= pushDown * Vector3.up;
        groundNormal = Vector3.zero;
        movement.collisionFlags = controller.Move(motion);
        movement.lastHitPoint = movement.hitPoint;
        lastGroundNormal = groundNormal;
        movement.velocity = velocity;
        if (movement.velocity.y < velocity.y - 0.001)
        {
            if (movement.velocity.y < 0f) movement.velocity.y = velocity.y;
            else jumping.holdingJumpButton = false;
        }
        if (grounded && !IsGroundedTest())
        {
            grounded = false;
            BeginLocalFallTracking();
            SendMessage("OnFall", SendMessageOptions.DontRequireReceiver);
            tr.position += pushDown * Vector3.up;
        }
        else if (!grounded && IsGroundedTest())
        {
            grounded = true;
            jumping.jumping = false;
            ReportLocalFallDamageOnLand();
            ResetLocalFallTracking();
            SendMessage("OnLand", SendMessageOptions.DontRequireReceiver);
        }
        UpdateLocalUnderMapWarning();
    }

    // NE :815-857
    Vector3 ApplyInputVelocityChange(Vector3 velocity)
    {
        if (!canControl) inputMoveDirection = Vector3.zero;
        Vector3 desired = GetDesiredHorizontalVelocity();
        if (externalDesiredVelocity != Vector3.zero) { desired.x += externalDesiredVelocity.x; desired.z += externalDesiredVelocity.z; }
        if (grounded) desired = ColleagueGroundVelocity(desired, groundNormal);
        else velocity.y = 0f;
        Vector3 change = desired - velocity;
        float max = GetMaxAcceleration(grounded, desired.sqrMagnitude - velocity.sqrMagnitude) * dt;
        if (change.sqrMagnitude > max * max) change = change.normalized * max;
        if (grounded || canControl) velocity += change;
        if (grounded) velocity.y = Mathf.Min(velocity.y, 0f);
        return velocity;
    }

    // NE :858-880
    Vector3 ApplyExplosionForces(Vector3 v)
    {
        if (explosionForce == Vector3.zero) return v;
        v.x += explosionForce.x; v.z += explosionForce.z;
        if (grounded) { grounded = false; if (explosionForce.y > 0f) v.y += explosionForce.y; }
        else v.y += explosionForce.y;
        explosionForce = Vector3.zero;
        return v;
    }

    // NE :882-928 — horizontal air speed ≤ runSpeed × 1.25 (× bat speed value) × 2.7; not for zombies
    Vector3 ClampLocalAirSpeedToBatRunSpeedLimit(Vector3 v)
    {
        if (grounded || (soldier != null && soldier.isZombie)) return v;
        float run = soldier != null && soldier.runSpeed > 0.01f ? soldier.runSpeed : 16.119999f;
        float k = BatAirFactor();
        float limit = Mathf.Max(0.1f, run * k * 2.7f);
        float sq = v.x * v.x + v.z * v.z;
        if (sq <= limit * limit) return v;
        float m = Mathf.Sqrt(sq);
        if (m <= 0.0001f) return v;
        v.x *= limit / m; v.z *= limit / m;
        return v;
    }

    /// <summary>NE :898-910: Mathf.Max(1, bat.SpeedValue) × 1.25 when the baseball bat is in the weapon set, else 1.25.</summary>
    public float BatAirFactor()
    {
        if (simulated) return simBatAirFactor;
        float k = 1.25f;
        var shots = ShotController.Instance;
        if (shots != null && shots.Weapons != null)
            foreach (var w in shots.Weapons)
                if (w != null && string.Equals(w.SystemName, "OHCA_BasebalBat", System.StringComparison.OrdinalIgnoreCase)) { k = Mathf.Max(1f, w.SpeedValue) * 1.25f; break; }
        return k;
    }

    /// <summary>NE Reset (:930-943).</summary>
    public void ResetMotion()
    {
        movement.velocity = Vector3.zero;
        inputMoveDirection = Vector3.zero;
        movement.frameVelocity = Vector3.zero;
        hasUnderMapPreviousPosition = false; hasLastSafeGroundY = false; hasLastSafeGroundPosition = false;
        nextUnderMapGeometryCheckTime = 0f; nextUnderMapRecoveryTime = 0f;
        if (!simulated) Input.ResetInputAxes();
    }

    /// <summary>NE SetExplosionForce (:944-950).</summary>
    public void SetExplosionForce(Vector3 source, float power)
    {
        explosionForce = (transform.position - source).normalized * power * explosionSpeed;
    }

    // NE :952-1009
    Vector3 ApplyGravityAndJumping(Vector3 v)
    {
        if (!inputJump || !canControl) { jumping.holdingJumpButton = false; jumping.lastButtonDownTime = -100f; }
        if (inputJump && jumping.lastButtonDownTime < 0f && canControl) jumping.lastButtonDownTime = now;
        if (grounded) v.y = Mathf.Min(0f, v.y) - movement.gravity * dt;
        else
        {
            v.y = movement.velocity.y - movement.gravity * dt;
            if (jumping.jumping && jumping.holdingJumpButton && now < jumping.lastStartTime + jumping.extraHeight / CalculateJumpVerticalSpeed(jumping.baseHeight))
                v += jumping.jumpDir * movement.gravity * dt;
            v.y = Mathf.Max(v.y, -movement.maxFallSpeed);
        }
        if (grounded)
        {
            if (jumping.enabled && canControl && now - jumping.lastButtonDownTime < 0.2)
            {
                grounded = false;
                jumping.jumping = true;
                jumping.lastStartTime = now;
                jumping.lastButtonDownTime = -100f;
                jumping.holdingJumpButton = true;
                jumping.jumpDir = TooSteep() ? Vector3.Slerp(Vector3.up, groundNormal, jumping.steepPerpAmount) : Vector3.Slerp(Vector3.up, groundNormal, jumping.perpAmount);
                v.y = 0f;
                v += jumping.jumpDir * CalculateJumpVerticalSpeed(jumping.baseHeight);
                SendMessage("OnJump", SendMessageOptions.DontRequireReceiver);
            }
            else jumping.holdingJumpButton = false;
        }
        return v;
    }

    // NE :1010-1028 — `GetType() == Type.GetType("BoxCollider")` is never true (no namespace), so the hit normal is always used
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Vector3 n = hit.normal;
        if (n.y > 0f && n.y > groundNormal.y && hit.moveDirection.y < 0f)
        {
            groundNormal = (hit.point - movement.lastHitPoint).sqrMagnitude > 0.001 || lastGroundNormal == Vector3.zero ? n : lastGroundNormal;
            movement.hitPoint = hit.point;
            movement.frameVelocity = Vector3.zero;
        }
    }

    // NE :1038-1060
    Vector3 GetDesiredHorizontalVelocity()
    {
        Vector3 local = tr.InverseTransformDirection(inputMoveDirection);
        float max = MaxSpeedInDirection(local);
        if (movement.clampSpeedOnSlopes && grounded && movement.velocity.sqrMagnitude > max * max * 0.0001)
        {
            float a = Mathf.Asin(movement.velocity.normalized.y) * 57.29578f;
            float g = Mathf.Acos(groundNormal.y) * 57.29578f;
            if (Mathf.Abs(a) > g) a = g * Mathf.Sign(a);
            max = Mathf.Clamp(max, 0f, movement.maxSpeedOnSlope.Evaluate(a));
        }
        return tr.TransformDirection(local * max);
    }

    Vector3 AdjustGroundVelocityToNormal(Vector3 hVelocity, Vector3 normal)
    {
        Vector3 side = Vector3.Cross(Vector3.up, hVelocity);
        return Vector3.Cross(side, normal).normalized * hVelocity.magnitude;
    }

    // NE :1159-1167
    Vector3 ColleagueGroundVelocity(Vector3 v, Vector3 n)
    {
        v = ApplyColleagueStrafeSustain(v);
        if (n.y < 0.5f) return v;
        return AdjustGroundVelocityToNormal(v, n);
    }

    // NE :1169-1200 — after 0.18 s of strafing the sideways part eases to 90 % over 0.22 s
    float strafeSustainSign, strafeSustainSince;
    Vector3 ApplyColleagueStrafeSustain(Vector3 v)
    {
        Vector3 right = transform.right; right.y = 0f;
        float sq = right.sqrMagnitude;
        if (sq < 1E-06f) return v;
        right /= Mathf.Sqrt(sq);
        float d = Vector3.Dot(v, right);
        float sign = d > 0.1f ? 1f : d < -0.1f ? -1f : 0f;
        float time = now;
        if (sign == 0f) { strafeSustainSign = 0f; return v; }
        if (sign != strafeSustainSign) { strafeSustainSign = sign; strafeSustainSince = time; }
        float t = time - strafeSustainSince - 0.18f;
        if (t <= 0f) return v;
        float k = Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(t / 0.22f));
        return v - right * (d * (1f - k));
    }

    bool IsGroundedTest() => groundNormal.y > 0.01;

    float GetMaxAcceleration(bool onGround, float speedChange)
    {
        if (onGround) return speedChange < 0f ? movement.maxGroundAcceleration : movement.maxGroundDecceration;
        return speedChange < 0f ? movement.maxAirAcceleration : movement.maxAirDecceration;
    }

    public float CalculateJumpVerticalSpeed(float h) => Mathf.Sqrt(2f * h * movement.gravity);
    bool TooSteep() => groundNormal.y <= Mathf.Cos(controller.slopeLimit * ((float)Math.PI / 180f));

    float MaxSpeedInDirection(Vector3 dir)
    {
        if (dir == Vector3.zero) return 0f;
        float z = (dir.z <= 0f ? movement.maxBackwardsSpeed : movement.maxForwardSpeed) / movement.maxSidewaysSpeed;
        Vector3 n = new Vector3(dir.x, 0f, dir.z / z).normalized;
        return new Vector3(n.x, 0f, n.z * z).magnitude * movement.maxSidewaysSpeed;
    }

    // ---------------- state snapshot (rebuild: simulation, replays, lab experiments) ----------------
    /// <summary>Everything the next Step reads, apart from the inputs (inputMoveDirection, inputJump, speeds, explosionForce).</summary>
    public struct State
    {
        public Vector3 position; public Quaternion rotation;
        public Vector3 velocity, frameVelocity, hitPoint, lastHitPoint;
        public CollisionFlags collisionFlags;
        public bool grounded; public Vector3 groundNormal, lastGroundNormal, explosionForce;
        public bool jumpingNow, holdingJumpButton; public float lastStartTime, lastButtonDownTime; public Vector3 jumpDir;
        public float strafeSustainSign, strafeSustainSince;
        public bool fallTrackingActive; public float fallPeakY, fallStartTime, fallMaxDownSpeed;
        public bool hasUnderMapPreviousPosition, hasLastSafeGroundY, hasLastSafeGroundPosition, underMapWarningShown;
        public Vector3 underMapPreviousPosition, lastSafeGroundPosition; public float lastSafeGroundY;
        public float nextUnderMapGeometryCheckTime, nextUnderMapWarningCheckTime, nextUnderMapRecoveryTime;
    }

    public State CaptureState() => new State
    {
        position = transform.position, rotation = transform.rotation,
        velocity = movement.velocity, frameVelocity = movement.frameVelocity, hitPoint = movement.hitPoint, lastHitPoint = movement.lastHitPoint,
        collisionFlags = movement.collisionFlags,
        grounded = grounded, groundNormal = groundNormal, lastGroundNormal = lastGroundNormal, explosionForce = explosionForce,
        jumpingNow = jumping.jumping, holdingJumpButton = jumping.holdingJumpButton, lastStartTime = jumping.lastStartTime,
        lastButtonDownTime = jumping.lastButtonDownTime, jumpDir = jumping.jumpDir,
        strafeSustainSign = strafeSustainSign, strafeSustainSince = strafeSustainSince,
        fallTrackingActive = fallTrackingActive, fallPeakY = fallPeakY, fallStartTime = fallStartTime, fallMaxDownSpeed = fallMaxDownSpeed,
        hasUnderMapPreviousPosition = hasUnderMapPreviousPosition, hasLastSafeGroundY = hasLastSafeGroundY,
        hasLastSafeGroundPosition = hasLastSafeGroundPosition, underMapWarningShown = underMapWarningShown,
        underMapPreviousPosition = underMapPreviousPosition, lastSafeGroundPosition = lastSafeGroundPosition, lastSafeGroundY = lastSafeGroundY,
        nextUnderMapGeometryCheckTime = nextUnderMapGeometryCheckTime, nextUnderMapWarningCheckTime = nextUnderMapWarningCheckTime,
        nextUnderMapRecoveryTime = nextUnderMapRecoveryTime,
    };

    public void RestoreState(State s)
    {
        if (controller == null) Awake();
        bool en = controller.enabled;
        if (en) controller.enabled = false;           // teleport without a sweep
        transform.SetPositionAndRotation(s.position, s.rotation);
        if (en) controller.enabled = true;
        movement.velocity = s.velocity; movement.frameVelocity = s.frameVelocity; movement.hitPoint = s.hitPoint; movement.lastHitPoint = s.lastHitPoint;
        movement.collisionFlags = s.collisionFlags;
        grounded = s.grounded; groundNormal = s.groundNormal; lastGroundNormal = s.lastGroundNormal; explosionForce = s.explosionForce;
        jumping.jumping = s.jumpingNow; jumping.holdingJumpButton = s.holdingJumpButton; jumping.lastStartTime = s.lastStartTime;
        jumping.lastButtonDownTime = s.lastButtonDownTime; jumping.jumpDir = s.jumpDir;
        strafeSustainSign = s.strafeSustainSign; strafeSustainSince = s.strafeSustainSince;
        fallTrackingActive = s.fallTrackingActive; fallPeakY = s.fallPeakY; fallStartTime = s.fallStartTime; fallMaxDownSpeed = s.fallMaxDownSpeed;
        hasUnderMapPreviousPosition = s.hasUnderMapPreviousPosition; hasLastSafeGroundY = s.hasLastSafeGroundY;
        hasLastSafeGroundPosition = s.hasLastSafeGroundPosition; underMapWarningShown = s.underMapWarningShown;
        underMapPreviousPosition = s.underMapPreviousPosition; lastSafeGroundPosition = s.lastSafeGroundPosition; lastSafeGroundY = s.lastSafeGroundY;
        nextUnderMapGeometryCheckTime = s.nextUnderMapGeometryCheckTime; nextUnderMapWarningCheckTime = s.nextUnderMapWarningCheckTime;
        nextUnderMapRecoveryTime = s.nextUnderMapRecoveryTime;
    }

    // ---------------- local fall damage (NE :602-710, offline path → PlayerManager.ApplyLocalFallDamage) ----------------
    bool fallTrackingActive; float fallPeakY, fallStartTime, fallMaxDownSpeed;
    void BeginLocalFallTracking() { fallTrackingActive = true; fallPeakY = tr.position.y; fallStartTime = now; fallMaxDownSpeed = 0f; }
    void UpdateLocalFallTrackingWhileAirborne()
    {
        if (grounded) return;
        if (!fallTrackingActive) BeginLocalFallTracking();
        if (tr.position.y > fallPeakY) fallPeakY = tr.position.y;
        if (movement.velocity.y < 0f) fallMaxDownSpeed = Mathf.Max(fallMaxDownSpeed, -movement.velocity.y);
    }
    void ResetLocalFallTracking() { fallTrackingActive = false; fallPeakY = 0f; fallStartTime = 0f; fallMaxDownSpeed = 0f; }
    void ReportLocalFallDamageOnLand()
    {
        if (!fallTrackingActive || now - fallStartTime < 0.45f || vitals == null || Dead) return;
        float h = Mathf.Max(0f, fallPeakY - tr.position.y);
        int d = h < 30f || fallMaxDownSpeed < 18f ? 0 : h >= 60f ? 80 : 40;   // CalculateLocalFallDamage (:653-660)
        if (d <= 0 || (soldier != null && soldier.isZombie)) return;          // ReportLocalFallDamage (:685-709): zombies skip
        if (vitals.FallDamageMultiplier != 1f) d = Mathf.Max(1, Mathf.RoundToInt(d * vitals.FallDamageMultiplier));   // «Легкое Приземление» e_id 3
        vitals.ApplyFallDamage(d);
    }

    // ---------------- under-map recovery / warning (NE :403-600) ----------------
    bool hasUnderMapPreviousPosition, hasLastSafeGroundY, hasLastSafeGroundPosition, underMapWarningShown;
    Vector3 underMapPreviousPosition, lastSafeGroundPosition; float lastSafeGroundY;
    float nextUnderMapGeometryCheckTime, nextUnderMapWarningCheckTime, nextUnderMapRecoveryTime;
    readonly RaycastHit[] underMapRaycastHits = new RaycastHit[16];

    void UpdateLocalUnderMapWarning()
    {
        if (Dead) return;
        Vector3 p = tr.position;
        if (!hasUnderMapPreviousPosition)
        {
            underMapPreviousPosition = p; hasUnderMapPreviousPosition = true;
            if (grounded) { lastSafeGroundY = p.y; lastSafeGroundPosition = p; hasLastSafeGroundY = hasLastSafeGroundPosition = true; }
            return;
        }
        if (underMapWarningShown) { if (!simulated) UnderMapWarning?.Invoke(); return; }
        if (grounded)
        {
            lastSafeGroundY = p.y; lastSafeGroundPosition = p; hasLastSafeGroundY = hasLastSafeGroundPosition = true;
            underMapPreviousPosition = p; nextUnderMapGeometryCheckTime = now + 0.1f;
            return;
        }
        if (now >= nextUnderMapGeometryCheckTime)
        {
            Vector3 a = underMapPreviousPosition;
            underMapPreviousPosition = p; nextUnderMapGeometryCheckTime = now + 0.1f;
            float drop = hasLastSafeGroundY ? lastSafeGroundY - p.y : 0f, dy = p.y - a.y;
            if (hasLastSafeGroundY && drop >= 1.5f && dy <= -0.08f && HasCrossedSolidLevelGeometry(a, p))
            {
                if (!TryRecoverFromUnderMapGeometryCross()) ShowPersistentUnderMapWarning("geometry-cross");
                return;
            }
        }
        if (hasLastSafeGroundY && now >= nextUnderMapWarningCheckTime)
        {
            nextUnderMapWarningCheckTime = now + 0.1f;
            if (lastSafeGroundY - p.y >= 15f && !HasSolidLevelGeometryBelow(80f)) ShowPersistentUnderMapWarning("void-fall");
        }
    }

    bool TryRecoverFromUnderMapGeometryCross()
    {
        if (!hasLastSafeGroundPosition || now < nextUnderMapRecoveryTime) return false;
        Vector3 target = lastSafeGroundPosition + Vector3.up * Mathf.Max(0.18f, controller.skinWidth * 2f);
        float dist = Vector3.Distance(tr.position, target);
        if (dist > 8f) return false;
        nextUnderMapRecoveryTime = now + 1f;
        bool en = controller.enabled;
        if (en) controller.enabled = false;
        tr.position = target;
        if (en) controller.enabled = true;
        movement.velocity = Vector3.zero; movement.frameVelocity = Vector3.zero; explosionForce = Vector3.zero;
        inputJump = false; jumping.jumping = false; jumping.holdingJumpButton = false; jumping.lastButtonDownTime = -100f;
        grounded = true; groundNormal = Vector3.up; lastGroundNormal = Vector3.up;
        ResetLocalFallTracking();
        underMapPreviousPosition = target; hasUnderMapPreviousPosition = true;
        lastSafeGroundY = target.y; lastSafeGroundPosition = target; hasLastSafeGroundY = hasLastSafeGroundPosition = true;
        nextUnderMapGeometryCheckTime = now + 0.1f; nextUnderMapWarningCheckTime = now + 0.1f;
        Debug.LogWarning($"[UnderMap] Recovered CharacterController after confirmed map-geometry tunnelling. Distance={dist:F2}, Position={target}");
        return true;
    }

    void ShowPersistentUnderMapWarning(string reason)
    {
        if (!underMapWarningShown) Debug.LogWarning($"[UnderMap] Persistent warning shown. Reason={reason}, SafeY={lastSafeGroundY:F2}, Current={tr.position}");
        underMapWarningShown = true;
        if (!simulated) UnderMapWarning?.Invoke();
    }

    bool HasCrossedSolidLevelGeometry(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; float m = d.magnitude;
        if (m < 0.05f || m > 6f) return false;
        Vector3 dir = d / m, c = a + tr.rotation * controller.center;
        float off = Mathf.Max(0.2f, controller.height * 0.28f);
        return RayCrossedSolidLevelGeometry(c, dir, m) || RayCrossedSolidLevelGeometry(c + Vector3.up * off, dir, m) || RayCrossedSolidLevelGeometry(c - Vector3.up * off, dir, m);
    }

    bool RayCrossedSolidLevelGeometry(Vector3 o, Vector3 dir, float len)
    {
        int n = Physics.RaycastNonAlloc(o, dir, underMapRaycastHits, len, ~(1 << GhostLayer), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = underMapRaycastHits[i]; var col = h.collider;
            if (col == null || col == controller || col.isTrigger) continue;
            var t = col.transform;
            if (t == null || t == tr || t.IsChildOf(tr) || col.attachedRigidbody != null) continue;
            if (len - h.distance >= Mathf.Max(0.35f, controller.radius * 0.6f) && Vector3.Dot(dir, h.normal) <= -0.05f) return true;
        }
        return false;
    }

    bool HasSolidLevelGeometryBelow(float dist)
    {
        Vector3 o = tr.position + Vector3.up * Mathf.Max(0.25f, controller.radius * 0.5f);
        int n = Physics.RaycastNonAlloc(o, Vector3.down, underMapRaycastHits, dist, ~(1 << GhostLayer), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = underMapRaycastHits[i].collider;
            if (col == null || col == controller) continue;
            var t = col.transform;
            if (t != null && t != tr && !t.IsChildOf(tr)) return true;
        }
        return false;
    }
}
