using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the original pickup models (HealthKit, AmmoKitSmall, ...) on every ItemMapXML marker of the map scenes,
/// and renders check shots.
/// Batch: Unity -batchmode -projectPath <project> -executeMethod PickupPlacer.PlaceAll -quit -logFile <log>
/// </summary>
public static class PickupPlacer
{
    const string ScenesDir = "Assets/Contra/_Scenes";
    const string PrefabDir = "Assets/Contra/GameObject";
    const string ShotsDir = "PickupShots";

    [MenuItem("Contra City/Place pickups on all maps")]
    public static void PlaceAll()
    {
        var report = new StringBuilder("scene;markers;placed;unknown names;by kind/value/respawn\n");
        foreach (var path in Directory.GetFiles(ScenesDir, "*.unity").OrderBy(p => p))
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var markers = Object.FindObjectsByType<ItemMapXML>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int placed = 0;
            var unknown = new HashSet<string>();
            var kinds = new SortedDictionary<string, int>();
            var root = GameObject.Find("_Pickups") ?? new GameObject("_Pickups");
            foreach (var m in markers)
            {
                string baseName = BaseName(m.name);
                if (ItemMapXML.KindFromName(baseName) == PickupKind.Unknown) { unknown.Add(m.name); continue; }
                // Remove copies made by the first version of this tool.
                foreach (Transform c in m.transform.Cast<Transform>().ToList())
                    if (c.name == ItemMapXML.ModelName && c.GetComponent<MeshRenderer>() && m.transform.childCount > 1)
                        Object.DestroyImmediate(c.gameObject);
                // The original model is already a child of the marker (MEDKIT_BIG, ARMOR_SMALL...): mark it as the pickup model.
                var model = m.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault();
                if (!model) { unknown.Add(m.name + "(no model)"); continue; }
                model.gameObject.name = ItemMapXML.ModelName;
                model.gameObject.SetActive(true);
                // Markers sit under the inactive POINTS_RESCALE group; move pickups to an active root, keeping world placement.
                if (m.transform.parent != root.transform) m.transform.SetParent(root.transform, true);
                m.gameObject.SetActive(true);
                placed++;
                string key = $"{baseName} v{m.Value} r{m.RespawnDelay} st{m.SubType}";
                kinds[key] = kinds.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Append($"{Path.GetFileNameWithoutExtension(path)};{markers.Length};{placed};{string.Join(" ", unknown)};" +
                          string.Join(", ", kinds.Select(k => $"{k.Key} x{k.Value}")) + "\n");
            Shots(Path.GetFileNameWithoutExtension(path), markers);
        }
        File.WriteAllText("pickups_report.csv", report.ToString(), new UTF8Encoding(true));
        Debug.Log("[PickupPlacer] done\n" + report);
    }

    static string BaseName(string n)
    {
        int i = n.IndexOf('_');
        if (i > 0) n = n.Substring(0, i);
        int j = n.IndexOf(' ');
        return j > 0 ? n.Substring(0, j) : n;
    }

    static Vector3 Div(Vector3 a, Vector3 b) =>
        new Vector3(a.x / NZ(b.x), a.y / NZ(b.y), a.z / NZ(b.z));

    static float NZ(float v) => Mathf.Abs(v) < 1e-6f ? 1e-6f : v;

    /// <summary>Close-up of up to three markers of different kinds per map, 3 m away.</summary>
    static void Shots(string map, ItemMapXML[] markers)
    {
        Directory.CreateDirectory(ShotsDir);
        var pick = markers.GroupBy(m => BaseName(m.name)).Select(g => g.First()).Take(3).ToList();
        var go = new GameObject("_ShotCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 60; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500;
        var rt = new RenderTexture(640, 360, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
        int k = 0;
        foreach (var m in pick)
        {
            var target = m.transform.Find(ItemMapXML.ModelName);
            Vector3 p = target ? target.position : m.transform.position;
            go.transform.position = p + new Vector3(2.2f, 1.2f, 2.2f);
            go.transform.LookAt(p);
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{ShotsDir}/{map}_{k++}_{BaseName(m.name)}.png", tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
    }
}
