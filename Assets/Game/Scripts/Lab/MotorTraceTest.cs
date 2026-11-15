// ContraCity.exe -motortest : bots step 1 check. On «Комиссариат» the player runs a scripted course (running, strafing, turning,
// bunnyhop, shotgun jumps, a rocket jump, running with the bat), every movement step is recorded, then replayed on ghosts:
//   motor level  — the motor's recorded inputs (proves CharacterMotor.Step reproduces the live motor),
//   key level    — the recorded keys through SoldierController (what a bot will drive),
//   benchmark    — many ghosts replaying the course in one frame (how fast the simulation runs).
// Log lines "[motortest] ..." in the -logFile; screenshot uitest/motortest_paths.png (red = live, green = key-level replay).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class MotorTraceTest
{
    static void Log(string s) => Debug.Log("[motortest] " + s);

    public static IEnumerator Run(string shotDir)
    {
        Profile.ResetProfile();
        var room = new RoomSettings(); room.Map = MapList.Get("Bit_map") ?? MapList.All.First(); room.Mode = GameMode.TEAM_DEATHMATCH;
        GameFlow.Instance.StartBattle(room);
        while (GameFlow.Instance.Current != GameFlow.State.Battle) yield return null;
        yield return new WaitForSeconds(1.5f);
        var B = UIRoot.Instance.Battle;
        B.JoinSelectedBattleTeam(1); yield return new WaitForSeconds(1f);
        B.RequestRespawn();
        float until = Time.time + 15f;
        while ((GameFlow.Instance.Player == null || GameFlow.Instance.Player.GetComponent<PlayerVitals>().Dead) && Time.time < until) yield return null;
        yield return new WaitForSeconds(2f);
        var player = GameFlow.Instance.Player;
        if (player == null) { Log("no player"); yield break; }
        var sc = player.GetComponent<SoldierController>();
        var look = player.GetComponent<SoldierCamera>();
        var shc = ShotController.Instance;
        var vit = player.GetComponent<PlayerVitals>();
        Log($"start: map Bit_map, position {player.transform.position}, runSpeed {sc.runSpeed:F2}, gravity {player.GetComponent<CharacterMotor>().movement.gravity}, health {vit.Health}/{vit.Armor}");

        float yaw = player.transform.eulerAngles.y;
        if (look) { look.SetYaw(yaw); look.SetPitch(0f); }
        shc.SetWeapon(3); yield return new WaitForSeconds(0.8f);

        var trace = new MotorTrace();
        trace.Begin(player);
        SoldierController.Keys K(bool l = false, bool r = false, bool f = false, bool b = false, bool jump = false) { var k = SoldierController.Keys.Moves(l, r, f, b); k.jump = jump; return k; }
        IEnumerator Hold(SoldierController.Keys k, float sec, float yawSpeed = 0f)
        {
            sc.testKeys = k;
            for (float t = 0f; t < sec; t += Time.deltaTime) { if (yawSpeed != 0f && look) { yaw += yawSpeed * Time.deltaTime; look.SetYaw(yaw); } yield return null; }
        }

        // 1. stand, run, strafe, run while turning
        yield return Hold(K(), 0.4f);
        yield return Hold(K(f: true), 1.2f);
        yield return Hold(K(r: true, f: true), 0.8f);
        yield return Hold(K(f: true), 1.2f, 120f);
        yield return Hold(K(l: true), 0.6f);
        // 2. bunnyhop: jump held while running and turning
        yield return Hold(K(f: true, jump: true), 2.5f, -60f);
        yield return Hold(K(), 0.6f);
        // 3. shotgun jumps: back to the direction of travel, jump, fire in the air (×3)
        shc.SetWeapon(5); yield return new WaitForSeconds(0.7f);
        for (int i = 0; i < 3; i++)
        {
            if (look) look.SetPitch(35f);
            yield return Hold(K(b: true, jump: true), 0.15f);
            yield return Hold(K(b: true), 0.12f);
            ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
            yield return Hold(K(b: true), 0.9f);
        }
        if (look) look.SetPitch(0f);
        yield return Hold(K(), 0.5f);
        // 4. rocket jump: look down, jump, fire
        shc.SetWeapon(6); yield return new WaitForSeconds(0.8f);
        if (look) look.SetPitch(85f);
        yield return Hold(K(f: true, jump: true), 0.12f);
        ShotController.TestFire = true; yield return null; yield return null; ShotController.TestFire = false;
        yield return Hold(K(f: true), 2.2f);
        if (look) look.SetPitch(0f);
        // 5. bat: run and hop (air speed cap with the bat factor)
        shc.SetWeapon(1); yield return new WaitForSeconds(0.6f);
        yield return Hold(K(f: true, jump: true), 2f, 45f);
        yield return Hold(K(), 0.8f);
        sc.testKeys = null;
        trace.End();

        // what the course covered
        int air = 0, impulses = 0, jumps = 0; float maxH = 0f, maxSpeed = 0f, y0 = trace.motorStart.position.y; bool wasGround = true;
        Vector3 prev = trace.motorStart.position; float sumDt = 0f;
        foreach (var e in trace.entries.Where(x => x.kind == MotorTrace.Kind.Motor))
        {
            if (!e.groundedAfter) air++;
            if (e.explosion != Vector3.zero) impulses++;
            if (wasGround && !e.groundedAfter) jumps++;
            wasGround = e.groundedAfter;
            maxH = Mathf.Max(maxH, e.posAfter.y - y0);
            var d = e.posAfter - prev; d.y = 0f; if (e.dt > 0f) maxSpeed = Mathf.Max(maxSpeed, d.magnitude / e.dt);
            prev = e.posAfter; sumDt += e.dt;
        }
        int soldierSteps = trace.entries.Count(x => x.kind == MotorTrace.Kind.Soldier);
        Log($"course: {trace.Duration:F2} s, motor steps {trace.MotorSteps} (avg dt {1000f * sumDt / Mathf.Max(1, trace.MotorSteps):F2} ms), controller steps {soldierSteps}, airborne steps {air}, take-offs {jumps}, explosion impulses {impulses}, max height +{maxH:F2}, max horizontal speed {maxSpeed:F2}, end {player.transform.position}, health {vit.Health}/{vit.Armor}");
        int sBefore = 0; for (int i = 0; i + 1 < trace.entries.Count; i++) if (trace.entries[i].kind == MotorTrace.Kind.Soldier && trace.entries[i + 1].kind == MotorTrace.Kind.Motor) sBefore++;
        Log($"order: controller step directly before a motor step in {sBefore} of {trace.MotorSteps} frames");

        // replays
        var ghost = MotorTrace.CreateGhost(player, "Ghost_replay");
        yield return null;   // let Awake / Start run
        var rm = trace.Replay(ghost, false, false);
        Log($"replay motor level: {rm.steps} steps, max error {rm.maxError:G4}, final error {rm.finalError:G4}, first step off by > 0.001: {rm.firstOff}");
        var rk = trace.Replay(ghost, true, true);
        Log($"replay key level: {rk.steps} steps, max error {rk.maxError:G4}, final error {rk.finalError:G4}, first step off by > 0.001: {rk.firstOff}");
        var rk2 = trace.Replay(ghost, true, false);
        Log($"replay key level again (same ghost): max error {rk2.maxError:G4} (repeatability)");

        // benchmark: N ghosts, the whole course each, in one frame
        foreach (int n in new[] { 10, 100 })
        {
            var ghosts = new List<GameObject>();
            for (int i = 0; i < n; i++) ghosts.Add(MotorTrace.CreateGhost(player, "Ghost_bench" + i));
            yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float worst = 0f;
            foreach (var g in ghosts) worst = Mathf.Max(worst, trace.Replay(g, true, false).maxError);
            sw.Stop();
            double steps = (double)n * trace.MotorSteps, sec = sw.Elapsed.TotalSeconds;
            Log($"benchmark {n} ghosts: {steps:F0} motor steps (+ controller steps) in {sec * 1000:F1} ms = {steps / sec:F0} steps/s, {n * trace.Duration / sec:F1}× real time for {n} fighters, worst error {worst:G4}");
            foreach (var g in ghosts) Object.Destroy(g);
            yield return null;
        }

        // picture: live path (red) and the key-level replay (green), from above
        var live = trace.entries.Where(x => x.kind == MotorTrace.Kind.Motor).Select(x => x.posAfter + Vector3.up * 0.5f).ToArray();
        var rep = rk.path.Select(p => p + Vector3.up * 0.8f).ToArray();
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        LineRenderer Line(string name, Vector3[] pts, Color c, float w)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.sharedMaterial = mat; lr.startColor = lr.endColor = c; lr.startWidth = lr.endWidth = w;
            lr.positionCount = pts.Length; lr.SetPositions(pts); return lr;
        }
        Line("LivePath", live, new Color(1f, 0.1f, 0.1f, 1f), 1.2f);
        Line("ReplayPath", rep, new Color(0.1f, 1f, 0.2f, 1f), 0.5f);
        var bounds = new Bounds(live[0], Vector3.zero); foreach (var p in live) bounds.Encapsulate(p);
        var camGo = new GameObject("MotorTestCam"); var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = Mathf.Max(bounds.extents.x / cam.aspect, bounds.extents.z) + 12f;
        // just above the highest point of the path, so roofs over it are clipped away
        cam.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + 4f, bounds.center.z), Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; cam.depth = 50; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        yield return null; yield return null; yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        Directory.CreateDirectory(shotDir);
        File.WriteAllBytes(Path.Combine(shotDir, "motortest_paths.png"), tex.EncodeToPNG());
        Object.Destroy(tex);
        Log("picture: uitest/motortest_paths.png");
        Log("done");
    }
}
