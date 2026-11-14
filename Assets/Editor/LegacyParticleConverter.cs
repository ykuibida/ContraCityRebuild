// Converts the Unity 4 legacy particles of the original effect prefabs (EllipsoidParticleEmitter 15, ParticleAnimator 12,
// ParticleRenderer 26) into ParticleSystem components. Unity 6 no longer loads the legacy classes, so their values are read
// from the prefab YAML on disk; the mapping is Unity's own "Legacy Particle System Updater" v1.6 (Karl Jones, Unity,
// https://gist.github.com/karljj1/58f16e936d13a94ae6a2f741fd271d91), rule for rule:
//   start size / lifetime = min..max; duration = maxEnergy; random rotation 0..360 when rndRotation; loop; scaling mode Shape;
//   one shot = no loop, one burst of minEmission..maxEmission repeating every maxEnergy; else rate over time = min..max emission;
//   emitterVelocityScale = inherit velocity; local / world velocity ± rndVelocity = velocity over lifetime (local / world space),
//   only rndVelocity = ±rndVelocity in world space; rndAngularVelocity = rotation over lifetime 0..value (deg → rad);
//   ellipsoid = sphere shape with radius max(ellipsoid) and scale ellipsoid / max;
//   colour animation = 5 colour / alpha keys at 0, .25, .5, .75, 1; force ± rndForce = randomized force over lifetime;
//   sizeGrow = size over lifetime (1 + sizeGrow)^(t · energy); autodestruct = stop action Destroy;
//   damping < 1 = velocity curves × damping^t; render mode Billboard / Stretch / Sorted / Horizontal / Vertical, camera velocity
//   scale, length scale, velocity scale, max particle size, shadows, material, UV animation tiles = texture sheet.
// The originals are copied to LegacyBackup/ (outside Assets) before a prefab is rewritten; a prefab that already has a
// ParticleSystem on an emitter object is skipped, so the step can run before every build.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LegacyParticleConverter
{
    const string BackupRoot = "LegacyBackup";

    sealed class Doc { public int ClassId; public string Body; }

    [MenuItem("Contra City/Convert legacy particles")]
    public static void ConvertAll()
    {
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Contra" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        int converted = 0, systems = 0;
        foreach (var path in paths)
        {
            string text = File.ReadAllText(path);
            if (!Regex.IsMatch(text, @"^--- !u!15 ", RegexOptions.Multiline)) continue;
            int n = Convert(path, text);
            if (n > 0) { converted++; systems += n; }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[LegacyParticles] converted {converted} prefabs, {systems} emitters");
    }

    static Dictionary<string, Doc> Parse(string text)
    {
        var docs = new Dictionary<string, Doc>();
        foreach (Match m in Regex.Matches(text, @"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- |\z)", RegexOptions.Singleline | RegexOptions.Multiline))
            docs[m.Groups[2].Value] = new Doc { ClassId = int.Parse(m.Groups[1].Value), Body = m.Groups[3].Value };
        return docs;
    }

    static string Field(string body, string name)
    {
        var m = Regex.Match(body, "^  " + Regex.Escape(name) + @": ?(.*)$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
    static float F(string body, string name) { var s = Field(body, name); return s == null ? 0f : float.Parse(s, CultureInfo.InvariantCulture); }
    static int I(string body, string name) { var s = Field(body, name); return s == null ? 0 : int.Parse(s, CultureInfo.InvariantCulture); }
    static bool B(string body, string name) => I(body, name) != 0;
    static Vector3 V(string body, string name)
    {
        var s = Field(body, name);
        if (s == null) return Vector3.zero;
        var m = Regex.Match(s, @"x: ?([-\d.eE]+), y: ?([-\d.eE]+), z: ?([-\d.eE]+)");
        return new Vector3(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
    }
    /// <summary>ColorRGBA32 "rgba": r in the low byte … a in the high byte.</summary>
    static Color ColorKey(string body, int i)
    {
        var m = Regex.Match(body, @"colorAnimation\[" + i + @"\]:\s*\n\s*serializedVersion: \d+\s*\n\s*rgba: (\d+)");
        uint v = m.Success ? uint.Parse(m.Groups[1].Value) : 0xFFFFFFFFu;
        return new Color32((byte)(v & 255), (byte)((v >> 8) & 255), (byte)((v >> 16) & 255), (byte)(v >> 24));
    }
    static int UvInt(string body, string name) { var m = Regex.Match(body, @"^    " + Regex.Escape(name) + @": (.*)$", RegexOptions.Multiline); return m.Success ? (int)float.Parse(m.Groups[1].Value.Trim(), CultureInfo.InvariantCulture) : 1; }

    /// <summary>Child-index path from the prefab root to the GameObject (names repeat: "Particle System").</summary>
    static List<int> IndexPath(Dictionary<string, Doc> docs, string goId)
    {
        string Tr(string go) => Comps(docs, docs[go].Body).FirstOrDefault(c => c.cls == 4 || c.cls == 224).id;
        var path = new List<int>();
        string t = Tr(goId);
        while (true)
        {
            string father = Regex.Match(docs[t].Body, @"m_Father: \{fileID: (-?\d+)\}").Groups[1].Value;
            if (father == "0" || !docs.ContainsKey(father)) break;
            var kids = Regex.Matches(Regex.Match(docs[father].Body, @"m_Children:(.*?)\n  m_Father", RegexOptions.Singleline).Groups[1].Value, @"fileID: (-?\d+)").Cast<Match>().Select(x => x.Groups[1].Value).ToList();
            path.Insert(0, kids.IndexOf(t));
            t = father;
        }
        return path;
    }

    /// <summary>A GameObject's component list in either serialization: Unity 4 ("- 15: {fileID: N}") or 2017+
    /// ("- component: {fileID: N}", the class read from the referenced document) — the New Era bundle export
    /// (asset_bundles/gamebundle, e.g. ExploSnowPrefab) uses the later one.</summary>
    static List<(int cls, string id)> Comps(Dictionary<string, Doc> docs, string body)
    {
        var list = Regex.Matches(body, @"- (\d+): \{fileID: (-?\d+)\}").Cast<Match>().Select(m => (cls: int.Parse(m.Groups[1].Value), id: m.Groups[2].Value)).ToList();
        foreach (Match m in Regex.Matches(body, @"- component: \{fileID: (-?\d+)\}"))
            if (docs.TryGetValue(m.Groups[1].Value, out var d)) list.Add((d.ClassId, m.Groups[1].Value));
        return list;
    }

    /// <summary>The legacy emitters of a prefab's text: child-index path from the root, emitter / animator / renderer bodies.</summary>
    static List<(List<int> idx, string emitter, string animator, string renderer)> Jobs(string text)
    {
        var docs = Parse(text);
        var jobs = new List<(List<int> idx, string emitter, string animator, string renderer)>();
        foreach (var kv in docs.Where(d => d.Value.ClassId == 1))
        {
            var comps = Comps(docs, kv.Value.Body);
            var em = comps.FirstOrDefault(c => c.cls == 15);
            if (em.id == null || !docs.ContainsKey(em.id)) continue;
            string Body(int cls) { var c = comps.FirstOrDefault(x => x.cls == cls); return c.id != null && docs.TryGetValue(c.id, out var d) ? d.Body : null; }
            jobs.Add((IndexPath(docs, kv.Key), docs[em.id].Body, Body(12), Body(26)));
        }
        return jobs;
    }

    static int Apply(GameObject root, List<(List<int> idx, string emitter, string animator, string renderer)> jobs, string label)
    {
        int done = 0;
        foreach (var j in jobs)
        {
            var t = root.transform;
            foreach (int i in j.idx) t = t != null && i >= 0 && i < t.childCount ? t.GetChild(i) : null;
            if (t == null) { Debug.LogWarning("[LegacyParticles] object not found in " + label); continue; }
            if (t.GetComponent<ParticleSystem>() != null) continue;
            Upgrade(t.gameObject, j.emitter, j.animator, j.renderer);
            done++;
        }
        return done;
    }

    static bool HasMissingScripts(GameObject root) =>
        root.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0);

    /// <summary>Converts a scene copy of a prefab (a build template) by the prefab's own legacy data. For prefabs Unity refuses to
    /// save (missing scripts: the character prefabs Fighter_locomotion / Enemy_Fighter_locomotion keep original scripts the rebuild has
    /// no class for); a scene may hold such objects. `root` must be the instance of the prefab root.</summary>
    public static int ConvertInstance(GameObject root, string prefabPath)
    {
        string text = File.ReadAllText(prefabPath);
        int n = Apply(root, Jobs(text), prefabPath + " (scene copy)");
        Debug.Log($"[LegacyParticles] {prefabPath}: {n} emitters converted on the scene copy");
        return n;
    }

    static int Convert(string path, string text)
    {
        var jobs = Jobs(text);
        if (jobs.Count == 0) return 0;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (HasMissingScripts(root)) { Debug.Log("[LegacyParticles] " + path + ": has missing scripts, converted on its scene copy (ConvertInstance)"); return 0; }
            int done = Apply(root, jobs, path);
            if (done > 0)
            {
                string backup = Path.Combine(BackupRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.WriteAllText(backup, text);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            return done;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static ParticleSystem.MinMaxCurve MinMax(float min, float max) => Mathf.Approximately(min, max) ? new ParticleSystem.MinMaxCurve(min) : new ParticleSystem.MinMaxCurve(min, max);

    static ParticleSystem.MinMaxCurve ApplyDampen(ParticleSystem.MinMaxCurve c, float dampen)   // updater ApplyDampen
    {
        const float step = 0.2f;
        if (c.mode == ParticleSystemCurveMode.Constant)
        {
            if (Mathf.Approximately(c.constant, 0) || Mathf.Approximately(dampen, 0)) return new ParticleSystem.MinMaxCurve(0, AnimationCurve.Linear(0, 1, 1, 1));
            var ac = new AnimationCurve();
            for (float time = 0; time <= 1.0f; time += step) ac.AddKey(time, Mathf.Pow(dampen, time));
            while (ac.keys.Length > 2) ac.RemoveKey(1);
            return new ParticleSystem.MinMaxCurve(c.constant, ac);
        }
        var aMin = new AnimationCurve(); var aMax = new AnimationCurve();
        if (Mathf.Approximately(c.constantMin, c.constantMax) || Mathf.Approximately(dampen, 0))
            return new ParticleSystem.MinMaxCurve(0, AnimationCurve.Linear(0, 1, 1, 1), AnimationCurve.Linear(0, 1, 1, 1));
        float minAbs = Math.Abs(c.constantMin), maxAbs = Math.Abs(c.constantMax);
        float normalizedMin = minAbs / maxAbs;
        float minSign = c.constantMin < 0 ? -1 : 1, maxSign = c.constantMax < 0 ? -1 : 1;
        for (float time = 0; time <= 1.0f; time += step)
        {
            aMin.AddKey(time, minSign * normalizedMin * Mathf.Pow(dampen, time));
            aMax.AddKey(time, maxSign * Mathf.Pow(dampen, time));
        }
        while (aMin.keys.Length > 2) { aMin.RemoveKey(1); aMax.RemoveKey(1); }
        return new ParticleSystem.MinMaxCurve(maxAbs, aMin, aMax);
    }

    static ParticleSystem.MinMaxCurve SizeGrowCurve(float sizeGrow, float minTime, float maxTime)   // updater GenerateSizeGrowCurve
    {
        const float step = 0.2f;
        if (sizeGrow > 0)
        {
            if (Mathf.Approximately(minTime, maxTime))
            {
                float finalSize = Mathf.Pow(1.0f + sizeGrow, minTime);
                var ac = new AnimationCurve();
                for (float time = 0; time <= 1.0f; time += step) ac.AddKey(time, Mathf.Pow(1 + sizeGrow, time * maxTime) / finalSize);
                while (ac.keys.Length > 2) ac.RemoveKey(1);
                return new ParticleSystem.MinMaxCurve(finalSize, ac);
            }
            float finalSizeMax = Mathf.Pow(1.0f + sizeGrow, maxTime);
            var mn = new AnimationCurve(); var mx = new AnimationCurve();
            for (float time = 0; time <= 1.0f; time += step)
            {
                mn.AddKey(time, Mathf.Pow(1 + sizeGrow, time * minTime) / finalSizeMax);
                mx.AddKey(time, Mathf.Pow(1 + sizeGrow, time * maxTime) / finalSizeMax);
            }
            while (mn.keys.Length > 2) { mn.RemoveKey(1); mx.RemoveKey(1); }
            return new ParticleSystem.MinMaxCurve(finalSizeMax, mn, mx);
        }
        if (Mathf.Approximately(minTime, maxTime))
        {
            var ac = new AnimationCurve();
            for (float time = 0; time <= 1.0f; time += step) ac.AddKey(time, Mathf.Pow(-sizeGrow, time));
            while (ac.keys.Length > 2) ac.RemoveKey(1);
            return new ParticleSystem.MinMaxCurve(1, ac);
        }
        var cMin = new AnimationCurve(); var cMax = new AnimationCurve();
        float ratio = minTime / maxTime;
        for (float time = 0; time <= 1.0f; time += step)
        {
            cMin.AddKey(time, Mathf.Pow(-sizeGrow, time * ratio));
            cMax.AddKey(time, Mathf.Pow(-sizeGrow, time));
        }
        while (cMin.keys.Length > 2) { cMin.RemoveKey(1); cMax.RemoveKey(1); }
        return new ParticleSystem.MinMaxCurve(1, cMin, cMax);
    }

    static void Upgrade(GameObject go, string e, string a, string r)
    {
        var ps = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var main = ps.main; var shape = ps.shape; var emission = ps.emission;
        var vol = ps.velocityOverLifetime; var rol = ps.rotationOverLifetime; var col = ps.colorOverLifetime;
        var fol = ps.forceOverLifetime; var tsa = ps.textureSheetAnimation; var sol = ps.sizeOverLifetime; var inherit = ps.inheritVelocity;

        // Main
        main.startSpeed = 0.0f;
        main.maxParticles = 10000;
        shape.enabled = false;
        main.playOnAwake = B(e, "m_Emit");
        main.startSize = MinMax(F(e, "minSize"), F(e, "maxSize"));
        main.startLifetime = MinMax(F(e, "minEnergy"), F(e, "maxEnergy"));
        main.duration = Mathf.Max(0.05f, F(e, "maxEnergy"));   // U6: the duration must be ≥ 0.05 s
        main.startRotation = MinMax(0.0f, B(e, "rndRotation") ? Mathf.Deg2Rad * 360.0f : 0.0f);
        main.loop = true;
        main.simulationSpace = B(e, "Simulate in Worldspace?") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        // Emission
        float minEmission = F(e, "minEmission"), maxEmission = F(e, "maxEmission");
        if (B(e, "m_OneShot"))
        {
            main.loop = false;
            emission.rateOverTime = 0;
            emission.burstCount = 1;
            var burst = new ParticleSystem.Burst();
            burst.cycleCount = int.MaxValue;
            burst.repeatInterval = main.startLifetime.constantMax;
            burst.count = MinMax(minEmission, maxEmission);
            emission.SetBurst(0, burst);
        }
        else emission.rateOverTime = MinMax(minEmission, maxEmission);

        // Inherit velocity
        float velocityScale = F(e, "emitterVelocityScale");
        if (!Mathf.Approximately(velocityScale, 0.0f)) { inherit.enabled = true; inherit.curve = velocityScale; }

        // Velocity over lifetime
        Vector3 localVelocity = V(e, "localVelocity"), worldVelocity = V(e, "worldVelocity"), randomVelocity = V(e, "rndVelocity");
        Vector3? minVel = null, maxVel = null;
        if (localVelocity.sqrMagnitude > Mathf.Epsilon) { vol.enabled = true; vol.space = ParticleSystemSimulationSpace.Local; minVel = localVelocity - randomVelocity; maxVel = localVelocity + randomVelocity; }
        else if (worldVelocity.sqrMagnitude > Mathf.Epsilon) { vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World; minVel = worldVelocity - randomVelocity; maxVel = worldVelocity + randomVelocity; }
        else if (randomVelocity.sqrMagnitude > Mathf.Epsilon) { vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World; minVel = -randomVelocity; maxVel = randomVelocity; }
        if (minVel != null)
        {
            var mn = minVel.Value; var mx = maxVel.Value;
            if (mn == mx) { vol.x = mn.x; vol.y = mn.y; vol.z = mn.z; }
            else { vol.x = new ParticleSystem.MinMaxCurve(mn.x, mx.x); vol.y = new ParticleSystem.MinMaxCurve(mn.y, mx.y); vol.z = new ParticleSystem.MinMaxCurve(mn.z, mx.z); }
        }

        // Rotation over lifetime
        float angular = F(e, "rndAngularVelocity");
        if (angular > Mathf.Epsilon) { rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * angular, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f))); }

        // Shape (EllipsoidParticleEmitter)
        Vector3 ellipsoid = V(e, "m_Ellipsoid");
        float maxDim = Mathf.Max(ellipsoid.x, Mathf.Max(ellipsoid.y, ellipsoid.z));
        if (maxDim > 0) { shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = maxDim; shape.scale = ellipsoid / maxDim; }

        if (a != null)
        {
            if (B(a, "Does Animate Color?"))
            {
                var g = new Gradient();
                g.colorKeys = new[] { new GradientColorKey(ColorKey(a, 0), 0f), new GradientColorKey(ColorKey(a, 1), 0.25f), new GradientColorKey(ColorKey(a, 2), 0.5f), new GradientColorKey(ColorKey(a, 3), 0.75f), new GradientColorKey(ColorKey(a, 4), 1f) };
                g.alphaKeys = new[] { new GradientAlphaKey(ColorKey(a, 0).a, 0f), new GradientAlphaKey(ColorKey(a, 1).a, 0.25f), new GradientAlphaKey(ColorKey(a, 2).a, 0.5f), new GradientAlphaKey(ColorKey(a, 3).a, 0.75f), new GradientAlphaKey(ColorKey(a, 4).a, 1f) };
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(g);
            }
            Vector3 force = V(a, "force"), rndForce = V(a, "rndForce");
            if (force.sqrMagnitude > Mathf.Epsilon || rndForce.sqrMagnitude > Mathf.Epsilon)
            {
                fol.enabled = true; fol.randomized = true;
                fol.x = MinMax(-rndForce.x + force.x, rndForce.x + force.x);
                fol.y = MinMax(-rndForce.y + force.y, rndForce.y + force.y);
                fol.z = MinMax(-rndForce.z + force.z, rndForce.z + force.z);
            }
            float sizeGrow = F(a, "sizeGrow");
            if (!Mathf.Approximately(sizeGrow, 0))
            {
                sol.enabled = true;
                sol.size = SizeGrowCurve(sizeGrow, main.startLifetime.mode == ParticleSystemCurveMode.Constant ? main.startLifetime.constantMax : main.startLifetime.constantMin, main.startLifetime.constantMax);
            }
            if (B(a, "autodestruct")) main.stopAction = ParticleSystemStopAction.Destroy;
            float damping = F(a, "damping");
            if (Field(a, "damping") != null && damping < 1.0f)
            {
                vol.x = ApplyDampen(vol.x, damping); vol.y = ApplyDampen(vol.y, damping); vol.z = ApplyDampen(vol.z, damping);
            }
        }

        if (r != null)
        {
            switch (I(r, "m_StretchParticles"))
            {
                case 0: psr.renderMode = ParticleSystemRenderMode.Billboard; break;
                case 1: case 3: psr.renderMode = ParticleSystemRenderMode.Stretch; break;
                case 2: psr.renderMode = ParticleSystemRenderMode.Billboard; psr.sortMode = ParticleSystemSortMode.Distance; break;
                case 4: psr.renderMode = ParticleSystemRenderMode.HorizontalBillboard; break;
                case 5: psr.renderMode = ParticleSystemRenderMode.VerticalBillboard; break;
            }
            psr.cameraVelocityScale = F(r, "m_CameraVelocityScale");
            psr.lengthScale = F(r, "m_LengthScale");
            psr.velocityScale = F(r, "m_VelocityScale");
            psr.maxParticleSize = F(r, "m_MaxParticleSize");
            psr.shadowCastingMode = B(r, "m_CastShadows") ? ShadowCastingMode.On : ShadowCastingMode.Off;
            psr.receiveShadows = B(r, "m_ReceiveShadows");
            var mg = Regex.Match(r, @"m_Materials:\s*\n\s*- \{fileID: (-?\d+), guid: (\w+)");
            if (mg.Success) psr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(mg.Groups[2].Value));
            int xTile = UvInt(r, "x Tile"), yTile = UvInt(r, "y Tile");
            var cyc = Regex.Match(r, @"^    cycles: (.*)$", RegexOptions.Multiline);
            float cycles = cyc.Success ? float.Parse(cyc.Groups[1].Value.Trim(), CultureInfo.InvariantCulture) : 1;
            if (xTile > 1 || yTile > 1) { tsa.enabled = true; tsa.numTilesX = xTile; tsa.numTilesY = yTile; tsa.cycleCount = (int)cycles; }
        }
        else psr.enabled = false;   // an emitter without a ParticleRenderer drew nothing
    }
}
