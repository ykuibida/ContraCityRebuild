// The local soldier's body in battle = the original fighter (Fighter_locomotion) dressed like in the lobby, animated by a port of the
// Penta 2016 / New Era ActorAnimator (locomotion part; weapons are not in the game yet), plus the taunt cycle of ShotController.LaunchTaunt /
// CancelTaunt / FinishTaunt with the TPSCamera (TPSCameraHolder on Bip01, camera 10 units behind, MouseOverLook.Intersect against walls).
// In first person the body is hidden (CombatPlayer.InitWear(false) → PlayerCustomisator.HideCloth); during a taunt it is shown.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FighterBody : MonoBehaviour
{
    // ActorAnimator serialized values of the original prefab
    const float RunAnimationSpeed = 1.3f, CrouchAnimationSpeed = 1.1f, WalkAnimationSpeed = 1.1f, JumpAnimationSpeed = 1f, IdleAnimationSpeed = 1f;
    // Taunt1..9 take only the upper body (mixing transform from Bip01 Spine1) except Taunt3 (ActorAnimator.SetAnimationProperties)
    static readonly bool[] TauntUpperBody = { true, true, false, true, true, true, true, true, true };

    public GameObject Body { get; private set; }
    public bool Taunting => activeTaunt > 0;
    /// <summary>ActorAnimator.isRunning: the last IsRunning state the weapon container animation was switched for.</summary>
    public bool IsRunning { get; private set; }
    public Camera TpsCamera { get; private set; }

    SoldierController sc;
    Camera fpsCamera;
    Animation anim;
    Transform bip, tpsHolder;
    string runName = "", strafeLeftName, strafeRightName, backName, idleName = "Idle", jumpName = "Jump";
    bool jumping, visible = true;
    int activeTaunt;
    Coroutine tauntRoutine;

    public static FighterBody Attach(GameObject player, Camera fps)
    {
        var fb = player.AddComponent<FighterBody>();
        fb.fpsCamera = fps;
        fb.Build();
        return fb;
    }

    void Build()
    {
        sc = GetComponent<SoldierController>();
        Body = Dresser.CreateFighter(transform);
        if (!Body) { Debug.LogWarning("[FighterBody] no fighter template"); return; }
        Body.name = "Body";
        Body.transform.localPosition = Vector3.zero; Body.transform.localRotation = Quaternion.identity;
        foreach (var c in Body.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        foreach (var l in Body.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
        Dress();
        anim = Body.GetComponentInChildren<Animation>();
        bip = FindDeep(Body.transform, "Bip01");
        SetupAnimations();
        // TPSCameraHolder (child of the fighter, follows Bip01) / TPSCamera at (0, 0, -10)
        tpsHolder = new GameObject("TPSCameraHolder").transform;
        tpsHolder.SetParent(transform, false);
        var camGo = new GameObject("TPSCamera");
        camGo.transform.SetParent(tpsHolder, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
        TpsCamera = camGo.AddComponent<Camera>();
        if (fpsCamera) TpsCamera.CopyFrom(fpsCamera);
        camGo.transform.localPosition = new Vector3(0f, 0f, -10f); camGo.transform.localRotation = Quaternion.identity;
        TpsCamera.fieldOfView = 60f;   // prefab Fighter_locomotion TPSCamera field of view 60 (Penta / New Era)
        TpsCamera.enabled = false;
        SetVisible(false);
    }

    /// <summary>Clothes of the profile (same rules as the lobby soldier).</summary>
    public void Dress()
    {
        if (!Body) return;
        var outfit = new Dictionary<int, GameItem>();
        foreach (var kv in Profile.Current.EquippedWear) { var it = ItemDB.Get(ItemKind.Wear, kv.Value); if (it != null) outfit[kv.Key] = it; }
        Dresser.Dress(Body, outfit);
        ShowTauntProps(0);
    }

    static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
        return null;
    }

    // ---------------- ActorAnimator ----------------
    void SetupAnimations()
    {
        if (!anim) return;
        anim.wrapMode = WrapMode.Loop;
        if (anim[jumpName] != null) { var j = anim[jumpName]; j.weight = 0f; j.speed = 0f; j.enabled = true; j.wrapMode = WrapMode.Once; }
        var spine1 = FindDeep(Body.transform, "Bip01 Spine1");
        for (int i = 1; i <= 10; i++)
        {
            string src = "Taunt" + i, clip = src + "Clip";
            if (anim[src] == null) continue;
            anim.AddClip(anim[src].clip, clip);
            var s = anim[clip];
            bool upper = i <= TauntUpperBody.Length && TauntUpperBody[i - 1];
            if (upper && spine1) s.AddMixingTransform(spine1);   // SetupAdditiveAiming
            s.blendMode = AnimationBlendMode.Blend; s.enabled = true; s.weight = 0f; s.layer = 1; s.time = 0f; s.speed = 0f; s.wrapMode = WrapMode.Once;
        }
        anim.Play(idleName);
    }

    void SetNames(string run, string left, string right, string back, string idle, float speed)
    {
        if (runName == run) return;
        runName = run; strafeLeftName = left; strafeRightName = right; backName = back; idleName = idle;
        foreach (var n in new[] { run, left, right, back }) if (anim[n] != null) anim[n].speed = speed;
        if (anim[idle] != null) anim[idle].speed = IdleAnimationSpeed;
    }

    void CrossFade(string n) { if (anim[n] != null) anim.CrossFade(n); }

    float tauntTime, tauntWeight; string tauntFadeOut;

    /// <summary>Taunt layer driven by hand (Blend(1, 0.2) in / Blend(0, 0.2) out): Unity 6 legacy Animation stops an upper-body
    /// (mixing transform) state on the first frame of a Blend from weight 0, so state, weight and time are set every frame.</summary>
    void DriveTaunt()
    {
        if (activeTaunt > 0)
        {
            var s = anim["Taunt" + activeTaunt + "Clip"];
            tauntTime += Time.deltaTime; tauntWeight = Mathf.MoveTowards(tauntWeight, 1f, Time.deltaTime / 0.2f);
            s.enabled = true; s.speed = 0f; s.time = Mathf.Min(tauntTime, s.length); s.weight = tauntWeight;
        }
        else if (tauntFadeOut != null)
        {
            var s = anim[tauntFadeOut];
            tauntWeight = Mathf.MoveTowards(tauntWeight, 0f, Time.deltaTime / 0.2f);
            s.weight = tauntWeight;
            if (tauntWeight <= 0f) { s.enabled = false; tauntFadeOut = null; }
        }
    }

    void Update()
    {
        if (!anim || !sc || Defeated) return;
        WeaponSway();
        DriveTaunt();
        bool crouch = sc.crouch, walk = sc.walk, inAir = sc.inAir;
        if (!inAir)
        {
            if (crouch) SetNames("CroachWalk", "CroachStrafeLeftWalk", "CroachStrafeRightWalk", "CroachWalkBackwards", "CroachIdle", CrouchAnimationSpeed);
            else if (walk) SetNames("Walk", "StrafeLeftWalk", "StrafeRightWalk", "WalkBackwards", "Idle", WalkAnimationSpeed);
            else SetNames("Run2", "StrafeRunLeft2", "StrafeRunRight2", "RunBackwards", "Idle", RunAnimationSpeed);
            jumping = false;
            // ActorAnimator.AnimationBlendKey with the local keyState (PlayerLocal.ReadKeyboardInput priority right › left › forward › back)
            Footsteps(sc.enabled && sc.IsRunningState);                    // ActorAnimator.Update: isRunning && !inAir
            switch (sc.keyState)
            {
                case KeyState.Walking: case KeyState.Runing: CrossFade(runName); break;
                case KeyState.WalkBack: case KeyState.RuningBack: CrossFade(backName); break;
                case KeyState.WalkStrafeLeft: case KeyState.RunStrafeLeft: CrossFade(strafeLeftName); break;
                case KeyState.WalkStrafeRight: case KeyState.RunStrafeRight: CrossFade(strafeRightName); break;
                default: CrossFade(idleName); break;
            }
            return;
        }
        Footsteps(false);
        if (!jumping && anim[jumpName] != null)
        {
            var j = anim[jumpName]; j.time = 0f; j.weight = 1f; j.speed = JumpAnimationSpeed;
            anim.CrossFade(jumpName, 0.3f);   // JumpStart
        }
        jumping = true;
    }

    /// <summary>ActorAnimator.Update (NE ActorAnimator.cs:861-879): when IsRunning (keyState Runing / RuningBack / RunStrafeLeft /
    /// RunStrafeRight) changes, the container of the first WeaponLook plays WeaponAnimation_ShotgunRun or _ShotgunIdle.</summary>
    void WeaponSway()
    {
        var ks = sc.keyState;
        bool running = ks == KeyState.Runing || ks == KeyState.RuningBack || ks == KeyState.RunStrafeLeft || ks == KeyState.RunStrafeRight;
        if (IsRunning == running) return;
        IsRunning = running;
        var shots = GetComponent<ShotController>();   // the first WeaponLook of the local fighter = a node of the first-person container
        var looks = shots && shots.Container ? shots.Container.GetComponentsInChildren<WeaponLook>() : new WeaponLook[0];
        if (looks.Length == 0) return;
        var a = looks[0].transform.parent.GetComponent<Animation>();
        if (a != null) ShotController.RunOrIdleWeaponAnimation(a, running);
    }

    void LateUpdate()
    {
        if (Defeated && TpsCamera) { var e = TpsCamera.transform.eulerAngles; TpsCamera.transform.eulerAngles = new Vector3(e.x, e.y, 0f); }   // ForceCameraRollUpright
        if (tpsHolder && bip) tpsHolder.position = bip.position;   // TPSCamera: parent follows Bip01
        if (TpsCamera && TpsCamera.enabled) Intersect();
    }

    /// <summary>MouseOverLook.Intersect: pull the camera in front of a wall between it and the soldier.</summary>
    void Intersect()
    {
        var cam = TpsCamera.transform;
        cam.localPosition = new Vector3(0f, 0f, -10f);
        var dir = cam.position - tpsHolder.position;
        var from = tpsHolder.position + new Vector3(0f, 1.5f, 0f);
        var ray = dir - new Vector3(0f, 1.5f, 0f);
        const int mask = ~1540;
        var hits = Physics.RaycastAll(from, ray, ray.magnitude, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));   // Physics.Raycast = nearest hit
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;
            cam.position = h.point + (from - h.point).normalized;
            break;
        }
    }

    void SetVisible(bool on)
    {
        visible = on;
        if (!Body) return;
        foreach (var cm in Body.GetComponentsInChildren<ClothModel>(true))
        {
            var r = cm.GetComponent<Renderer>(); if (r && cm.gameObject.activeSelf) r.enabled = on;
        }
    }

    /// <summary>PlayerCustomisator.InitTaunt: the taunt's own props are "Others" cloth models named Taunt&lt;id&gt;…</summary>
    void ShowTauntProps(int id)
    {
        if (!Body) return;
        foreach (var cm in Body.GetComponentsInChildren<ClothModel>(true))
            if (cm.WearType == 8 && cm.gameObject.name.StartsWith("Taunt"))
            {
                bool on = id > 0 && cm.gameObject.name.StartsWith("Taunt" + id) && !char.IsDigit(cm.gameObject.name.Length > ("Taunt" + id).Length ? cm.gameObject.name[("Taunt" + id).Length] : 'x');
                cm.gameObject.SetActive(on);
                var r = cm.GetComponent<Renderer>(); if (r) r.enabled = on;
            }
    }

    // ---------------- taunts (ShotController.LaunchTaunt / CancelTaunt / FinishTaunt) ----------------
    /// <summary>FinishTaunt durations: 1 → 1.5 s, 2–5 → 2.8, 6 → 5.6, 7 → 3.5, 8 → 8.5, 9 → 3.5, 10 → 15.3.</summary>
    static float TauntSeconds(int id) => id == 10 ? 15.3f : id == 9 ? 3.5f : id == 8 ? 8.5f : id == 7 ? 3.5f : id > 5 ? 5.6f : id > 1 ? 2.8f : 1.5f;

    public bool LaunchTaunt(int id)
    {
        if (id <= 0 || !anim || activeTaunt > 0) return false;   // Penta: no cooldown, a taunt is never interrupted by another
        string clip = "Taunt" + id + "Clip";
        if (anim[clip] == null) { Debug.LogWarning("[Taunt] Missing animation clip: " + clip); return false; }
        activeTaunt = id;
        SetVisible(true);
        ShowTauntProps(id);
        tauntTime = 0f; tauntWeight = 0f; tauntFadeOut = null;   // ActorAnimator.TauntAnimation: Blend(clip, 1, 0.2)
        var shots = GetComponent<ShotController>(); if (shots) shots.OnTauntStart();   // LaunchTaunt: shot off, Zoom(false), Reset on the weapon
        SwitchCamera(true);
        PlayTauntSound(id);
        tauntRoutine = StartCoroutine(FinishTaunt(id));
        return true;
    }

    AudioSource speaker, legs;
    AudioSource Child(string n)
    {
        var tr = Body ? FindDeep(Body.transform, n) : null;
        if (!tr) { if (!Body) return null; tr = new GameObject(n).transform; tr.SetParent(Body.transform, false); }
        var s = tr.GetComponent<AudioSource>();
        return s ? s : SoundManager.Make3D(tr.gameObject, false);   // Legs / Speaker: no AudioSourceVC → volume 1 × master
    }

    /// <summary>ShotController.LaunchTaunt → EffectManager.TauntEffect(ActorAnimator.SpeakerAudio, "Taunt&lt;id&gt;") → SoundManager.Play.</summary>
    void PlayTauntSound(int id)
    {
        if (!speaker) speaker = Child("Speaker");
        if (speaker) speaker.enabled = true;
        SoundManager.Play(speaker, "Taunt" + id, AudioPlayMode.Play);
    }

    /// <summary>ActorAnimator.Update: legs loop "Run_Concrete" while running (Run / RunBack / RunStrafeLeft / RunStrafeRight) and
    /// not in the air, otherwise Stop every frame; walking and crouching make no footsteps.</summary>
    void Footsteps(bool running)
    {
        if (!legs) legs = Child("Legs");
        if (!legs) return;
        legs.enabled = true;
        if (running) SoundManager.Play(legs, "Run_Concrete", AudioPlayMode.PlayLoop);
        else SoundManager.Play(legs, "Run_Concrete", AudioPlayMode.Stop);
    }

    IEnumerator FinishTaunt(int id)
    {
        yield return new WaitForSeconds(TauntSeconds(id));
        tauntRoutine = null;
        EndTaunt();
    }

    public void CancelTaunt()
    {
        if (activeTaunt <= 0) return;
        if (tauntRoutine != null) { StopCoroutine(tauntRoutine); tauntRoutine = null; }
        EndTaunt();
    }

    void EndTaunt()
    {
        if (anim && activeTaunt > 0) tauntFadeOut = "Taunt" + activeTaunt + "Clip";   // FinishTauntAnimation: Blend(clip, 0, 0.2)
        activeTaunt = 0;
        SwitchCamera(false);
        ShowTauntProps(0);
        SetVisible(false);
        var shots = GetComponent<ShotController>(); if (shots) shots.OnTauntEnd();   // FinishTaunt: ResetReloadWeaponAnimation
    }

    /// <summary>PlayerManager.CameraSwitch(FPSCamera, TPSCamera) and back; the FPS camera keeps the audio listener.</summary>
    void SwitchCamera(bool tps)
    {
        if (TpsCamera) TpsCamera.enabled = tps;
        if (fpsCamera) fpsCamera.enabled = !tps;
        var shots = GetComponent<ShotController>(); if (shots) shots.SyncWeaponCamera(!tps);   // PlayerManager.SyncLocalWeaponCamera
    }

    void OnDisable() { if (activeTaunt > 0) { activeTaunt = 0; SwitchCamera(false); } }

    // ---------------- death (PlayerManager.KillMe(false) :7382-7425 / ActivateLocalDeathCamera :7889-7925) ----------------
    public bool Defeated { get; private set; }
    /// <summary>KillMe: InitWear(true) shows the clothes, ActorAnimator.Defeat (NE :1706-1769) stops the Animation and turns the body
    /// into a ragdoll (rigidbodies dynamic with gravity, their colliders on for the local player, impulse clamped to 10 / 15 above
    /// 100 / 4000), then the TPS camera at (0, 0, -10) behind the body (it follows Bip01).</summary>
    public void Defeat(Vector3 impulse)
    {
        if (Defeated) return;
        Defeated = true;
        CancelTaunt();
        if (legs) SoundManager.Play(legs, "Run_Concrete", AudioPlayMode.Stop);   // ActorAnimator.Defeat → StopLegsAudio
        SetVisible(true);
        var bodies = Body ? Body.GetComponentsInChildren<Rigidbody>(true) : new Rigidbody[0];
        if (anim && bodies.Length > 0) { anim.Stop(); anim.enabled = false; }
        Vector3 force = impulse;
        float mag = force.magnitude;
        if (mag > 100f) force = force.normalized * (mag >= 4000f ? 15f : 10f);
        foreach (var rb in bodies)
        {
            if (!rb) continue;
            rb.isKinematic = false; rb.useGravity = true;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            var col = rb.GetComponent<Collider>(); if (col) col.enabled = true;
            if (force.sqrMagnitude > 0.0001f) rb.AddForce(force, ForceMode.Impulse);
        }
        if (TpsCamera)
        {
            TpsCamera.transform.localPosition = new Vector3(0f, 0f, -10f);
            TpsCamera.transform.localEulerAngles = Vector3.zero;
        }
        SwitchCamera(true);
    }
}
