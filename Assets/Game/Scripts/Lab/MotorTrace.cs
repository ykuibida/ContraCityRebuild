// Bots step 1 (15.11.2026): record the player's movement steps and replay them on simulated fighters ("ghosts") that run the same
// CharacterMotor / SoldierController code, stepped by hand instead of by Unity's Update. If the replay lands on the same
// positions, the movement can be simulated 1:1: many fighters per frame, faster than real time (the map lab, bots).
using System.Collections.Generic;
using UnityEngine;

public class MotorTrace
{
    public enum Kind : byte { Soldier, Motor }

    /// <summary>One step of the live player, in the order Unity ran it.</summary>
    public struct Entry
    {
        public Kind kind;
        public float dt, now;
        public Quaternion rotation;
        // a SoldierController step: the keys and what the weapon / effect systems had set
        public SoldierController.Keys keys;
        public float weaponMul, impactMul; public bool hasWeapon, forcesWalk, dead;
        // a CharacterMotor step: its inputs (as SoldierController left them) and its result
        public Vector3 inputMove, externalDesired, explosion; public bool inputJump, canControl;
        public float maxF, maxB, maxS, gAcc, gDec, aAcc, aDec, baseH, extraH, bat;
        public Vector3 posAfter; public bool groundedAfter;
    }

    public CharacterMotor.State motorStart;
    public SoldierController.State soldierStart;
    public readonly List<Entry> entries = new List<Entry>();
    public int MotorSteps { get; private set; }
    public float Duration => entries.Count == 0 ? 0f : entries[entries.Count - 1].now - entries[0].now;

    CharacterMotor motor; SoldierController soldier; Entry pending;

    /// <summary>Start between frames (from a coroutine), so the start state is not half a frame.</summary>
    public void Begin(GameObject player)
    {
        motor = player.GetComponent<CharacterMotor>(); soldier = player.GetComponent<SoldierController>();
        motorStart = motor.CaptureState(); soldierStart = soldier.CaptureState();
        entries.Clear(); MotorSteps = 0;
        soldier.OnStep = OnSoldier; motor.OnBeforeStep = OnMotorBefore; motor.OnAfterStep = OnMotorAfter;
    }

    public void End()
    {
        if (soldier) soldier.OnStep = null;
        if (motor) { motor.OnBeforeStep = null; motor.OnAfterStep = null; }
    }

    void OnSoldier(SoldierController s, SoldierController.Keys k, float dt, float now)
    {
        entries.Add(new Entry
        {
            kind = Kind.Soldier, dt = dt, now = now, rotation = s.transform.rotation, keys = k,
            weaponMul = ShotController.Instance != null ? ShotController.Instance.GetSpeedMultiplier() : 1f,
            hasWeapon = ShotController.Instance != null && ShotController.Instance.CurrentWeapon != null,
            impactMul = s.impactSpeedMultiplier, forcesWalk = s.weaponForcesWalk, dead = s.dead,
        });
    }

    void OnMotorBefore(CharacterMotor m)
    {
        var mv = m.movement;
        pending = new Entry
        {
            kind = Kind.Motor, rotation = m.transform.rotation,
            inputMove = m.inputMoveDirection, inputJump = m.inputJump, canControl = m.canControl, externalDesired = m.externalDesiredVelocity,
            explosion = m.explosionForce, bat = m.BatAirFactor(),
            maxF = mv.maxForwardSpeed, maxB = mv.maxBackwardsSpeed, maxS = mv.maxSidewaysSpeed,
            gAcc = mv.maxGroundAcceleration, gDec = mv.maxGroundDecceration, aAcc = mv.maxAirAcceleration, aDec = mv.maxAirDecceration,
            baseH = m.jumping.baseHeight, extraH = m.jumping.extraHeight,
        };
    }

    void OnMotorAfter(CharacterMotor m)
    {
        pending.dt = m.LastDt; pending.now = m.LastNow;
        pending.posAfter = m.transform.position; pending.groundedAfter = m.grounded;
        entries.Add(pending); MotorSteps++;
    }

    // ---------------- ghosts ----------------

    static bool layerReady;

    /// <summary>The ghost layer collides with everything the player's layer (2) collides with, but not with the player or other ghosts.</summary>
    public static void SetupGhostLayer()
    {
        if (layerReady) return;
        for (int l = 0; l < 32; l++) Physics.IgnoreLayerCollision(CharacterMotor.GhostLayer, l, Physics.GetIgnoreLayerCollision(2, l));
        Physics.IgnoreLayerCollision(CharacterMotor.GhostLayer, 2, true);
        Physics.IgnoreLayerCollision(CharacterMotor.GhostLayer, CharacterMotor.GhostLayer, true);
        layerReady = true;
    }

    /// <summary>A simulated fighter with the template's capsule and tuning; nothing steps it but its owner.</summary>
    public static GameObject CreateGhost(GameObject template, string name)
    {
        SetupGhostLayer();
        var go = new GameObject(name) { layer = CharacterMotor.GhostLayer };
        var tcc = template.GetComponent<CharacterController>();
        var cc = go.AddComponent<CharacterController>();
        cc.height = tcc.height; cc.radius = tcc.radius; cc.center = tcc.center; cc.stepOffset = tcc.stepOffset;
        cc.skinWidth = tcc.skinWidth; cc.slopeLimit = tcc.slopeLimit; cc.minMoveDistance = tcc.minMoveDistance;
        var m = go.AddComponent<CharacterMotor>();
        m.externallyStepped = true; m.simulated = true;
        m.CopySettingsFrom(template.GetComponent<CharacterMotor>());
        var s = go.AddComponent<SoldierController>();
        s.externallyStepped = true; s.simulated = true;
        s.CopySettingsFrom(template.GetComponent<SoldierController>());
        return go;
    }

    public struct Result { public int steps; public float maxError, finalError; public int firstOff; public List<Vector3> path; }

    /// <summary>Replays on a ghost. keyLevel = false: the motor's recorded inputs (tests the motor alone); true: the recorded keys
    /// through SoldierController (tests what a bot will drive). Compares every motor step with the live position.</summary>
    public Result Replay(GameObject ghost, bool keyLevel, bool keepPath, float tolerance = 0.001f)
    {
        var m = ghost.GetComponent<CharacterMotor>(); var s = ghost.GetComponent<SoldierController>();
        m.RestoreState(motorStart); s.RestoreState(soldierStart);
        var r = new Result { firstOff = -1, path = keepPath ? new List<Vector3>(MotorSteps) : null };
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.kind == Kind.Soldier)
            {
                if (!keyLevel) continue;
                ghost.transform.rotation = e.rotation;
                s.simWeaponSpeedMultiplier = e.weaponMul; s.simHasWeapon = e.hasWeapon;
                s.impactSpeedMultiplier = e.impactMul; s.weaponForcesWalk = e.forcesWalk; s.dead = e.dead;
                s.Step(e.keys, e.dt, e.now);
                continue;
            }
            ghost.transform.rotation = e.rotation;
            if (!keyLevel)
            {
                var mv = m.movement;
                m.inputMoveDirection = e.inputMove; m.inputJump = e.inputJump; m.canControl = e.canControl; m.externalDesiredVelocity = e.externalDesired;
                mv.maxForwardSpeed = e.maxF; mv.maxBackwardsSpeed = e.maxB; mv.maxSidewaysSpeed = e.maxS;
                mv.maxGroundAcceleration = e.gAcc; mv.maxGroundDecceration = e.gDec; mv.maxAirAcceleration = e.aAcc; mv.maxAirDecceration = e.aDec;
                m.jumping.baseHeight = e.baseH; m.jumping.extraHeight = e.extraH;
            }
            m.explosionForce = e.explosion;   // set by weapons / explosions during the frame
            m.simBatAirFactor = e.bat;
            m.Step(e.dt, e.now);
            float err = Vector3.Distance(m.transform.position, e.posAfter);
            if (err > r.maxError) r.maxError = err;
            if (err > tolerance && r.firstOff < 0) r.firstOff = r.steps;
            r.finalError = err;
            r.path?.Add(m.transform.position);
            r.steps++;
        }
        return r;
    }
}
