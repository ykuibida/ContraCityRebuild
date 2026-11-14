// Contra City map viewer tools.
// Menu: Contra City > Fix materials / Build viewer scenes / Open map > ...
// Batch: Unity -batchmode -projectPath <this> -executeMethod MapViewerTools.BatchAll -quit
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class MapViewerTools
{
    const string Root = "Assets/Contra";
    const string MapsFolder = Root + "/_Scenes";
    const string ViewerFolder = "Assets/Viewer";
    const string ShotsFolder = "MapShots"; // project root
    const string ShaderMapFile = "Assets/Editor/contra_shader_map.txt";
    const float EyeHeight = 1.7f;

    // All map scenes found in the maps folder.
    static string[] Maps => Directory.Exists(MapsFolder)
        ? Directory.GetFiles(MapsFolder, "*.unity").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()
        : new string[0];

    // ---------- Materials ----------

    // Original shaders were exported as empty stubs (not copied here), so materials point to missing shaders.
    // Map every material to the built-in shader with the same name (or a close replacement).
    [MenuItem("Contra City/Fix materials")]
    public static int FixMaterials()
    {
        var map = File.ReadAllLines(ShaderMapFile)
            .Select(l => l.Trim().Trim('﻿'))
            .Where(l => l.Contains("="))
            .Select(l => l.Split(new[] { '=' }, 2))
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        var rx = new Regex(@"m_Shader: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
        int fixedCount = 0;
        var notFound = new HashSet<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (mat.shader != null && mat.shader.name != "Hidden/InternalErrorShader" && mat.shader.isSupported) continue;

            string text = File.ReadAllText(path);
            var m = rx.Match(text);
            string original = m.Success && map.TryGetValue(m.Groups[1].Value, out var n) ? n : null;
            var shader = FindReplacement(original);
            if (shader == null) { notFound.Add(original ?? "<unknown>"); shader = Shader.Find("Legacy Shaders/Diffuse"); }
            mat.shader = shader; // properties with the same names (_MainTex, _Color, _Cutoff...) are kept
            EditorUtility.SetDirty(mat);
            fixedCount++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Contra] Materials fixed: {fixedCount}. Replaced by fallback: {string.Join(", ", notFound)}");
        return fixedCount;
    }

    static Shader FindReplacement(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var special = new Dictionary<string, string>
        {
            { "Custom/HalfLambert", "Legacy Shaders/Diffuse" },
            { "Myshaders/TransparentSelfIllumination", "Legacy Shaders/Transparent/Diffuse" },
            { "Myshaders/TransparentSelfIlluminationReflective", "Legacy Shaders/Transparent/Diffuse" },
            { "Legacy Shaders/GoldLegacy", "Legacy Shaders/Reflective/Specular" },
            { "Particles/Alpha Blended Premultiply", "Legacy Shaders/Particles/Alpha Blended Premultiply" },
            { "Projector/Multiply", "Legacy Shaders/Diffuse" },
            { "Myshaders/TransparentUnlit", "Unlit/Transparent" },
            { "Mobile/Skybox", "Skybox/6 Sided" },
            { "Particles/Multiply (Double)", "Legacy Shaders/Particles/Multiply (Double)" },
        };
        var candidates = new List<string>();
        if (special.TryGetValue(name, out var s)) candidates.Add(s);
        candidates.Add(name);
        candidates.Add("Legacy Shaders/" + name);
        if (name.StartsWith("Unlit/Transparent") || name.StartsWith("Hidden/Unlit/Transparent") || name.Contains("Premultiplied"))
            candidates.Add("Unlit/Transparent");
        if (name.StartsWith("Unlit/Text") || name.StartsWith("Hidden/Unlit/Text") || name.StartsWith("GUI/"))
            candidates.Add("GUI/Text Shader");
        foreach (var c in candidates)
        {
            var sh = Shader.Find(c);
            if (sh != null && sh.isSupported) return sh;
        }
        return null;
    }

    // ---------- Viewer scenes ----------

    [MenuItem("Contra City/Build viewer scenes")]
    public static void BuildViewerScenes() => BuildAll(false);

    public static void BatchAll() => BuildAll(true);

    static void BuildAll(bool screenshots)
    {
        FixMaterials();
        Directory.CreateDirectory(ViewerFolder);
        Directory.CreateDirectory(ShotsFolder);
        foreach (var map in Maps)
        {
            string path = $"{MapsFolder}/{map}.unity";
            if (!File.Exists(path)) { Debug.LogWarning("[Contra] Missing scene " + path); continue; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Unbatch(path, map);
            var cam = SetupViewer(map, out var overviewPos, out var overviewRot);
            if (screenshots && cam != null)
            {
                Shot(cam, $"{ShotsFolder}/{map}_player.png");
                // Diagnostic: same view without baked lightmaps, lit by a temporary sun.
                var savedLm = LightmapSettings.lightmaps;
                LightmapSettings.lightmaps = new LightmapData[0];
                var tmpSun = new GameObject("TmpSun").AddComponent<Light>();
                tmpSun.type = LightType.Directional; tmpSun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var savedAmb = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                Shot(cam, $"{ShotsFolder}/{map}_nolightmap.png");
                Object.DestroyImmediate(tmpSun.gameObject);
                RenderSettings.ambientLight = savedAmb;
                LightmapSettings.lightmaps = savedLm;
                var p = cam.transform.position; var r = cam.transform.rotation;
                cam.transform.SetPositionAndRotation(overviewPos, overviewRot);
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false; // the map fog hides everything from far away
                Shot(cam, $"{ShotsFolder}/{map}_overview.png");
                RenderSettings.fog = fog;
                cam.transform.SetPositionAndRotation(p, r);
            }
            EditorSceneManager.SaveScene(scene, $"{ViewerFolder}/{map}_Viewer.unity", true);
            Debug.Log($"[Contra] {map}: lightmaps={LightmapSettings.lightmaps.Length}, renderers={Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length}");
        }
        AssetDatabase.Refresh();
        Debug.Log("[Contra] Viewer scenes saved to " + ViewerFolder);
    }

    // Viewer scenes are saved to Assets/Viewer/<Map>_Viewer.unity — open them from the Project window.

    static void OpenMap(string map)
    {
        string viewer = $"{ViewerFolder}/{map}_Viewer.unity";
        if (File.Exists(viewer)) { EditorSceneManager.OpenScene(viewer); return; }
        EditorSceneManager.OpenScene($"{MapsFolder}/{map}.unity");
        SetupViewer(map, out _, out _);
    }

    // ---------- Static batching undo ----------

    // Mobile builds bake static objects into big "Combined Mesh (root: scene)" meshes in world space.
    // Each renderer keeps the list of its submeshes (m_SubsetIndices in Unity 5.4). In the editor that mesh
    // is drawn with the object's transform again, so the map "explodes". Cut each renderer's submeshes out
    // and convert them to the object's local space.
    static void Unbatch(string scenePath, string map)
    {
        var subsets = new Dictionary<long, int[]>();
        string text = File.ReadAllText(scenePath);
        var docRx = new Regex(@"^--- !u!23 &(\d+)[^\n]*\nMeshRenderer:(.*?)(?=^--- !u!|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        var idxRx = new Regex(@"m_SubsetIndices: ([0-9a-fA-F]*)");
        foreach (Match m in docRx.Matches(text))
        {
            var im = idxRx.Match(m.Groups[2].Value);
            if (!im.Success || im.Groups[1].Value.Length < 8) continue;
            string hex = im.Groups[1].Value;
            var list = new int[hex.Length / 8];
            for (int i = 0; i < list.Length; i++)
            {
                uint v = 0;
                for (int b = 0; b < 4; b++) v |= (uint)System.Convert.ToByte(hex.Substring(i * 8 + b * 2, 2), 16) << (8 * b);
                list[i] = (int)v;
            }
            subsets[long.Parse(m.Groups[1].Value)] = list;
        }

        string meshFolder = $"{ViewerFolder}/Meshes/{map}";
        Directory.CreateDirectory(meshFolder);
        var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] uv, Vector2[] uv2, Color[] c)>();
        int done = 0, skipped = 0, flippedSubmeshes = 0, clearedBatchInfo = 0, clearedStreams = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mf = r.GetComponent<MeshFilter>();
                var src = mf != null ? mf.sharedMesh : null;
                if (src == null || !src.name.StartsWith("Combined Mesh")) continue;
                long id = (long)GlobalObjectId.GetGlobalObjectIdSlow(r).targetObjectId;
                if (!subsets.TryGetValue(id, out var subs) || subs.Length == 0) { skipped++; continue; }

                if (!cache.TryGetValue(src, out var d))
                {
                    d = (src.vertices, src.normals, src.tangents, src.uv, src.uv2, src.colors);
                    cache[src] = d;
                }
                var toLocal = r.transform.worldToLocalMatrix;
                // Mirrored objects (negative scale): batching flipped their winding, flip it back.
                bool flip = r.transform.localToWorldMatrix.determinant < 0f;
                bool lightmapped = r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE;
                Vector4 lmSO = lightmapped ? r.lightmapScaleOffset : new Vector4(1f, 1f, 0f, 0f);
                var remap = new Dictionary<int, int>();
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>();
                var uvs = new List<Vector2>(); var uv2s = new List<Vector2>(); var cols = new List<Color>();
                var tris = new List<int[]>();
                foreach (int s in subs)
                {
                    if (s < 0 || s >= src.subMeshCount) { tris.Add(new int[0]); continue; }
                    var t = src.GetTriangles(s);
                    var outT = new int[t.Length];
                    for (int i = 0; i < t.Length; i++)
                    {
                        int vi = t[i];
                        if (!remap.TryGetValue(vi, out int ni))
                        {
                            ni = verts.Count; remap[vi] = ni;
                            verts.Add(toLocal.MultiplyPoint3x4(d.v[vi]));
                            if (d.n.Length > vi) norms.Add(toLocal.MultiplyVector(d.n[vi]).normalized);
                            if (d.t.Length > vi) { var tv = toLocal.MultiplyVector(d.t[vi]).normalized; tans.Add(new Vector4(tv.x, tv.y, tv.z, d.t[vi].w)); }
                            if (d.uv.Length > vi) uvs.Add(d.uv[vi]);
                            if (d.uv2.Length > vi)
                            {
                                // uv2 of the combined mesh already contains this renderer's lightmap scale/offset,
                                // and Unity applies it again from LightingData, so undo it here.
                                var uv = d.uv2[vi];
                                if (lmSO.x != 0f && lmSO.y != 0f) uv = new Vector2((uv.x - lmSO.z) / lmSO.x, (uv.y - lmSO.w) / lmSO.y);
                                uv2s.Add(uv);
                            }
                            if (d.c.Length > vi) cols.Add(d.c[vi]);
                        }
                        outT[i] = ni;
                    }
                    // Decide winding from the stored vertex normals: faces must point the same way as their normals.
                    bool flipThis = flip;
                    if (norms.Count == verts.Count && outT.Length >= 3)
                    {
                        float vote = 0f;
                        for (int i = 0; i + 2 < outT.Length; i += 3)
                        {
                            Vector3 a = verts[outT[i]], b = verts[outT[i + 1]], c = verts[outT[i + 2]];
                            Vector3 face = Vector3.Cross(b - a, c - a);
                            Vector3 n = norms[outT[i]] + norms[outT[i + 1]] + norms[outT[i + 2]];
                            vote += Mathf.Sign(Vector3.Dot(face, n)) * face.magnitude;
                        }
                        flipThis = vote < 0f;
                    }
                    if (flipThis)
                    {
                        flippedSubmeshes++;
                        for (int i = 0; i + 2 < outT.Length; i += 3) { int tmp = outT[i + 1]; outT[i + 1] = outT[i + 2]; outT[i + 2] = tmp; }
                    }
                    tris.Add(outT);
                }
                var mesh = new Mesh { name = r.gameObject.name };
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                if (norms.Count == verts.Count) mesh.SetNormals(norms);
                if (tans.Count == verts.Count) mesh.SetTangents(tans);
                if (uvs.Count == verts.Count) mesh.SetUVs(0, uvs);
                if (uv2s.Count == verts.Count) mesh.SetUVs(1, uv2s);
                if (cols.Count == verts.Count) mesh.SetColors(cols);
                mesh.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{meshFolder}/{id}.asset");
                mf.sharedMesh = mesh;
                // Unity upgraded m_SubsetIndices into m_StaticBatchInfo, so the renderer would still draw
                // submesh N of the new mesh (which only has submeshes 0..k). Clear it.
                var so = new SerializedObject(r);
                var first = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
                var count = so.FindProperty("m_StaticBatchInfo.subMeshCount");
                if (first != null && (first.intValue != 0 || (count != null && count.intValue != 0))) clearedBatchInfo++;
                if (first != null) first.intValue = 0;
                if (count != null) count.intValue = 0;
                var root = so.FindProperty("m_StaticBatchRoot");
                if (root != null) root.objectReferenceValue = null;
                // The converted scene points the Enlighten vertex stream at the old combined mesh;
                // its vertex count does not match, and Unity silently skips drawing the renderer.
                foreach (var prop in new[] { "m_EnlightenVertexStream", "m_AdditionalVertexStreams" })
                {
                    var vs = so.FindProperty(prop);
                    if (vs != null && vs.objectReferenceValue != null) { vs.objectReferenceValue = null; clearedStreams++; }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                done++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log($"[Contra] {map}: unbatched {done} renderers (no subset info: {skipped}, flipped submeshes: {flippedSubmeshes}, cleared batch info: {clearedBatchInfo}, cleared vertex streams: {clearedStreams})");
    }

    static Camera SetupViewer(string map, out Vector3 overviewPos, out Quaternion overviewRot)
    {
        // Turn off the game's own cameras (their scripts are missing anyway).
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None)) l.enabled = false;

        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && r.bounds.extents.magnitude < 1500f).ToArray();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one * 50);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        // If baked lighting did not survive the conversion, add a sun so the map is not pitch black.
        bool hasActiveLight = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l => l.enabled && l.type == LightType.Directional);
        if (LightmapSettings.lightmaps.Length == 0 && !hasActiveLight)
        {
            var sun = new GameObject("ViewerSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.65f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);
        }

        var go = new GameObject("ViewerCamera");
        var cam = go.AddComponent<Camera>();
        go.AddComponent<AudioListener>();
        var fly = go.AddComponent<FlyCamera>();
        fly.moveSpeed = 6f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = Mathf.Max(1000f, bounds.size.magnitude * 3f);
        cam.fieldOfView = 70f;
        cam.tag = "MainCamera";
        // The Unity 5.4 occlusion data does not match the converted scene and hides static geometry.
        cam.useOcclusionCulling = false;

        // Player view: stand on a respawn point and look toward the map centre.
        var spawn = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("RespawnPoint") || t.name.StartsWith("Respawn_"))
            .OrderBy(t => t.name).FirstOrDefault();
        Vector3 eye = spawn != null ? spawn.position + Vector3.up * EyeHeight
                                    : new Vector3(bounds.center.x, bounds.min.y + EyeHeight, bounds.min.z + bounds.size.z * 0.2f);
        var look = new Vector3(bounds.center.x, eye.y, bounds.center.z) - eye;
        if (look.sqrMagnitude < 0.01f) look = spawn != null ? spawn.forward : Vector3.forward;
        go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look.normalized, Vector3.up));

        // Overview: high and behind, looking at the centre (sky objects make the height useless, so use the ground plan).
        float plan = Mathf.Max(bounds.size.x, bounds.size.z);
        var ground = new Vector3(bounds.center.x, spawn != null ? spawn.position.y : bounds.min.y, bounds.center.z);
        overviewPos = ground + new Vector3(0f, plan * 0.45f, -plan * 0.55f);
        overviewRot = Quaternion.LookRotation((ground - overviewPos).normalized, Vector3.up);

        Debug.Log($"[Contra] {map}: bounds={bounds.size}, spawn={(spawn != null ? spawn.name : "none")}");
        return cam;
    }

    static void Shot(Camera cam, string file)
    {
        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.enabled = true;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }
}
