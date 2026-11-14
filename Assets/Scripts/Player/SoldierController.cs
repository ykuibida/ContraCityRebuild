// Port of the New Era SoldierController (W:/Contra City Extract/NE_Scripts_Decrypted/SoldierController.cs; Penta 2016 =
// reference). Base = New Era by the owner's decision (2026-11-11). Keyboard input → CharacterMotor; also the key state of
// PlayerLocal.ReadKeyboardInput (NE PlayerLocal.cs:118-187) that drives FPSCamera.Walk and the body animation / footsteps.
// Dropped: anti-cheat (lstccounter, CheckSpeedMultiplier → SendEnterBaseRequest), chat typing reset (no battle chat yet).
using UnityEngine;

/// <summary>PlayerLocal keyState (嗧).</summary>
public enum KeyState { Still, Walking, WalkBack, WalkStrafeLeft, WalkStrafeRight, Runing, RuningBack, RunStrafeLeft, RunStrafeRight }

[RequireComponent(typeof(CharacterMotor))]
public class SoldierController : MonoBehaviour
{
    // ForceBattleMovementValues (NE :366-401) fills these: base × 1.04 × runner (Бегун) × wear speed
    public float runSpeed = 16.12f;
    public float runStrafeSpeed = 14.04f;
    public float walkSpeed = 7.28f;
    public float walkStrafeSpeed = 6.76f;
    public float crouchRunSpeed = 5.72f;
    public float crouchRunStrafeSpeed = 5.2f;
    public float crouchWalkSpeed = 3.64f;
    public float crouchWalkStrafeSpeed = 3.328f;
    public float customAxisAcceleration = 0.05f;   // NE :389

    [Tooltip("Automated tests: overrides keyboard movement when non-zero")] public Vector3 testMove;
    public bool testJump;

    [Header("Set by other systems")]
    public bool dead;
    public bool isZombie;
    public bool isZombieBoss;
    public bool weaponForcesWalk;              // gatling spinning, sniper zoom, flamer — set by the weapon system (NE :714-719)
    /// <summary>Rebuild slow (LocalImpacts: «Гранатин»'s slow_ms); New Era has no slow on the client.</summary>
    public float impactSpeedMultiplier = 1f;
    public float weaponSpeedMultiplier = 1f;   // ShotController.GetSpeedMultiplier, applied only with a weapon (NE :521-531)
    public bool hasWeapon;

    [Header("State (read only)")]
    public bool walk, crouch, inAir, aim, fire;
    public float idleTimer;
    public Vector3 moveDir;
    public float inAirSeconds;
    public KeyState keyState;
    float lastAir;

    CharacterMotor motor;
    FPSCamera fpsCamera;
    float horizontalAxis, verticalAxis;

    void Awake() { motor = GetComponent<CharacterMotor>(); }

    void Start()
    {
        fpsCamera = GetComponentInChildren<FPSCamera>();
        GameOptions.Load();
    }

    void OnEnable()
    {
        moveDir = Vector3.zero;
        walk = aim = fire = inAir = false;
        horizontalAxis = verticalAxis = 0f;
    }

    /// <summary>NE ForceBattleMovementValues (:366-401): runner = 1 + clamp(Бегун, 0, 10) %, wear = 1 + speed_percent %
    /// (clothes + complete sets); k = 1.04 × runner × wear (here: 1.04 × BattleStats.SpeedMultiplier).</summary>
    public void ForceBattleMovementValues(float statsMultiplier)
    {
        float k = 1.04f * statsMultiplier;
        runSpeed = 15.5f * k; runStrafeSpeed = 13.5f * k;
        walkSpeed = 7f * k; walkStrafeSpeed = 6.5f * k;
        crouchRunSpeed = 5.5f * k; crouchRunStrafeSpeed = 5f * k;
        crouchWalkSpeed = 3.5f * k; crouchWalkStrafeSpeed = 3.2f * k;
        customAxisAcceleration = 0.05f;
        if (!motor) motor = GetComponent<CharacterMotor>();
        var m = motor.movement;
        m.maxGroundAcceleration = 9999f; m.maxGroundDecceration = 9999f; m.maxAirAcceleration = 350f; m.maxAirDecceration = 350f;   // overwritten in Update
        m.maxForwardSpeed = runSpeed; m.maxBackwardsSpeed = runSpeed; m.maxSidewaysSpeed = runStrafeSpeed;
        motor.jumping.baseHeight = 1.72f;
        motor.jumping.extraHeight = 0.9f;   // NE :399 (base height is set to 2.7 every frame in Update)
    }

    // NE :403-549
    void Update()
    {
        if (motor == null) return;
        if (UIRoot.Instance != null && UIRoot.Instance.Battle != null && UIRoot.Instance.Battle.MenuOpen) return;   // NE :449-452 GameHUD.IsShowMenu: the last input stays
        GetUserInputs();
        if (!motor.canControl) motor.canControl = true;
        if (!dead)
        {
            moveDir = testMove != Vector3.zero ? testMove : new Vector3(horizontalAxis, 0f, verticalAxis);
        }
        else { moveDir = Vector3.zero; motor.canControl = false; }
        if (moveDir.sqrMagnitude > 1f) moveDir = moveDir.normalized;
        motor.inputMoveDirection = transform.TransformDirection(moveDir);
        motor.inputJump = (Input.GetKey(TRInput.Jump) || testJump) && !crouch;
        var m = motor.movement;
        m.maxForwardSpeed = !walk ? (crouch ? crouchRunSpeed : runSpeed) : (crouch ? crouchWalkSpeed : walkSpeed);
        float strafe = !walk ? (crouch ? crouchRunStrafeSpeed : runStrafeSpeed) : (crouch ? crouchWalkStrafeSpeed : walkStrafeSpeed);
        if (isZombie)
        {
            // NE :485-512 (no hit stagger / hit push yet — they come with weapons)
            m.maxGroundAcceleration = 700f; m.maxGroundDecceration = 880f; m.maxAirAcceleration = 70f; m.maxAirDecceration = 400f;
            motor.jumping.baseHeight = ZombieJumpHeight();
            float f = walk ? 11f : 26f;
            m.maxForwardSpeed = f; m.maxBackwardsSpeed = f; m.maxSidewaysSpeed = strafe;
            motor.externalDesiredVelocity = Vector3.zero;
        }
        else
        {
            // NE :513-531
            motor.externalDesiredVelocity = Vector3.zero;
            m.maxGroundAcceleration = 252f; m.maxGroundDecceration = 88f;
            m.maxAirAcceleration = 10f; m.maxAirDecceration = 10f;
            motor.jumping.baseHeight = 2.7f;
            m.maxBackwardsSpeed = m.maxForwardSpeed;
            m.maxSidewaysSpeed = strafe;
            // NE :521-531 — ShotController.GetSpeedMultiplier (CheckSpeedMultiplier = anti-cheat report, dropped)
            var shots = ShotController.Instance;
            weaponSpeedMultiplier = shots != null ? shots.GetSpeedMultiplier() : 1f;
            hasWeapon = shots != null && shots.CurrentWeapon != null;
            if (hasWeapon && weaponSpeedMultiplier != 1f)
            {
                m.maxForwardSpeed *= weaponSpeedMultiplier; m.maxBackwardsSpeed *= weaponSpeedMultiplier; m.maxSidewaysSpeed *= weaponSpeedMultiplier;
            }
            if (impactSpeedMultiplier != 1f)
            {
                m.maxForwardSpeed *= impactSpeedMultiplier; m.maxBackwardsSpeed *= impactSpeedMultiplier; m.maxSidewaysSpeed *= impactSpeedMultiplier;
            }
        }
        if (fpsCamera) fpsCamera.Croach = crouch;                       // PlayerLocal: SetCrouch → fpsCamera.Croach
        if (moveDir != Vector3.zero) idleTimer = 0f;
        bool air = !motor.grounded;
        inAirSeconds = air ? inAirSeconds + Time.deltaTime : 0f;
        if (air != inAir) { inAir = air; lastAir = Time.time; }         // NE :537-543
        else if (inAir && Time.time - lastAir > 1.7f)                    // NE :544-548 EffectManager.FallDownEffect
        {
            SoundManager.Play(SoundManager.LocalPlayerAudio, "FallDown", AudioPlayMode.Play);
            lastAir = Time.time;
        }
        ReadKeyboardInput();
    }

    /// <summary>NE GetZombieJumpHeight (:651-674): 6.5, «Zombi» 6, «Zombi_2» 7.</summary>
    static float ZombieJumpHeight()
    {
        var map = GameFlow.Instance != null ? GameFlow.Instance.BattleMap : "";
        if (string.Equals(map, "Zombi", System.StringComparison.OrdinalIgnoreCase)) return 6f;
        if (string.Equals(map, "Zombi_2", System.StringComparison.OrdinalIgnoreCase)) return 7f;
        return 6.5f;
    }

    /// <summary>NE ColleagueAxisAccel (:721-727): per-frame axis step, frame-rate scaled only above 250 fps.</summary>
    static float ColleagueAxisAccel(float a)
    {
        float n = Time.deltaTime * 250f;
        return n >= 1f ? a : a * Mathf.Max(n, 0.05f);
    }

    // NE :551-641
    void GetUserInputs()
    {
        bool left = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) || Input.GetKey(TRInput.LeftStrafe);
        bool right = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) || Input.GetKey(TRInput.RightStrafe);
        bool up = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) || Input.GetKey(TRInput.Forward);
        bool down = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S) || Input.GetKey(TRInput.Backward);
        float d = ColleagueAxisAccel(customAxisAcceleration);
        if (left && !right) { if (horizontalAxis > 0f) horizontalAxis = 0f; horizontalAxis = Mathf.MoveTowards(horizontalAxis, -1f, d); }
        else if (right && !left) { if (horizontalAxis < 0f) horizontalAxis = 0f; horizontalAxis = Mathf.MoveTowards(horizontalAxis, 1f, d); }
        else horizontalAxis = Mathf.MoveTowards(horizontalAxis, 0f, d);
        if (up && !down) { if (verticalAxis < 0f) verticalAxis = 0f; verticalAxis = Mathf.MoveTowards(verticalAxis, 1f, d); }
        else if (down && !up) { if (verticalAxis > 0f) verticalAxis = 0f; verticalAxis = Mathf.MoveTowards(verticalAxis, -1f, d); }
        else verticalAxis = Mathf.MoveTowards(verticalAxis, 0f, d);
        horizontalAxis = Mathf.Clamp(horizontalAxis, -1f, 1f);
        verticalAxis = Mathf.Clamp(verticalAxis, -1f, 1f);
        fire = (Input.GetButton("Fire1") || Input.GetKey(TRInput.Fire1)) && !dead;
        aim = (Input.GetButton("Fire2") || Input.GetKey(KeyCode.Mouse1)) && !dead;
        idleTimer += Time.deltaTime;
        if (aim || fire) idleTimer = 0f;
        bool crouchNow = Input.GetKey(TRInput.Crouch);
        if (crouchNow != crouch) idleTimer = 0f;                         // NE :473-478 (SetCrouch)
        crouch = crouchNow || dead;
        bool moving = horizontalAxis != 0f || verticalAxis != 0f || testMove != Vector3.zero;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        walk = (shift && moving && !dead) || !moving || crouch || weaponForcesWalk;
    }

    /// <summary>PlayerLocal.ReadKeyboardInput (NE PlayerLocal.cs:118-187): bound keys only, opposite keys cancel, priority
    /// right › left › forward › back; FPSCamera.Walk = one of them is held (the crouch eye height 4.7 vs 5.2 depends on it).</summary>
    void ReadKeyboardInput()
    {
        bool r = Input.GetKey(TRInput.RightStrafe), l = Input.GetKey(TRInput.LeftStrafe), f = Input.GetKey(TRInput.Forward), b = Input.GetKey(TRInput.Backward);
        if (testMove != Vector3.zero) { r = testMove.x > 0f; l = testMove.x < 0f; f = testMove.z > 0f; b = testMove.z < 0f; }
        bool held = true;
        if (r && !l) keyState = walk ? KeyState.WalkStrafeRight : KeyState.RunStrafeRight;
        else if (l && !r) keyState = walk ? KeyState.WalkStrafeLeft : KeyState.RunStrafeLeft;
        else if (f && !b) keyState = walk ? KeyState.Walking : KeyState.Runing;
        else if (b && !f) keyState = walk ? KeyState.WalkBack : KeyState.RuningBack;
        else { keyState = KeyState.Still; held = false; }
        if (fpsCamera) fpsCamera.Walk = held;
    }

    /// <summary>ActorAnimator.IsRunning: the four run key states.</summary>
    public bool IsRunningState => keyState == KeyState.Runing || keyState == KeyState.RuningBack || keyState == KeyState.RunStrafeLeft || keyState == KeyState.RunStrafeRight;
}


