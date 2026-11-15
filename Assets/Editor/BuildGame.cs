// Builds the playable game: lobby scene first, then all map viewer scenes.
// Batch: Unity -batchmode -projectPath <this> -executeMethod BuildGame.Build -quit
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildGame
{
    const string LobbyPath = "Assets/Game/Scenes/_Lobby.unity";

    [MenuItem("Contra City/Build game (Windows)")]
    /// <summary>Shaders that game code looks up with Shader.Find (BattleGUI.NormalizeGuiRoot etc.) must be in the build.</summary>
    static void EnsureAlwaysIncludedShaders(params string[] names)
    {
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var n in names)
        {
            var sh = Shader.Find(n);
            if (sh == null) { Debug.LogWarning("[BuildGame] shader not found: " + n); continue; }
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
            if (has) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            Debug.Log("[BuildGame] always include " + n);
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    public static void Build()
    {
        MapViewerTools.FixMaterials();            // materials still on the exported stub shaders (e.g. the New Era "1test weapon" models)
        LegacyParticleConverter.ConvertAll();   // Unity 4 legacy particles of the effect prefabs → ParticleSystem (Unity's updater rules)
        EnsureAlwaysIncludedShaders("Unlit/Transparent", "Legacy Shaders/Particles/Additive", "Legacy Shaders/Particles/Alpha Blended", "Unlit/Texture",
            // RuntimeTextureLoader / HandTextureLoader / ShotController.SetDiffuseShader (New Era Shader.Find names, Unity 6 spelling)
            "Legacy Shaders/Diffuse", "Legacy Shaders/Transparent/Diffuse", "Legacy Shaders/Transparent/Specular", "Legacy Shaders/Reflective/Diffuse",
            "Legacy Shaders/VertexLit", "Mobile/Diffuse", "Skybox/Cubemap", "Legacy Shaders/Particles/Multiply",
            "Legacy Shaders/Particles/~Additive-Multiply", "Projector/Multiply");
        CreateLobbyScene();
        var viewers = Directory.GetFiles("Assets/Viewer", "*_Viewer.unity").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList();
        var scenes = new[] { LobbyPath }.Concat(viewers).ToArray();
        EditorBuildSettings.scenes = scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        PlayerSettings.productName = "Contra City";
        PlayerSettings.companyName = "ContraCityRebuild";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = "Builds/ContraCity/ContraCity.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
        });
        Debug.Log("[BuildGame] " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB");
        // the map lab (bots step 2): a shortcut next to the game
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            File.WriteAllText("Builds/ContraCity/Лаборатория.bat", "@echo off\r\nstart \"\" \"%~dp0ContraCity.exe\" -lab Bit_map\r\n");
    }

    static void CreateLobbyScene()
    {
        // lobby corridor material (keeps the Unlit/Texture shader in the build)
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Resources/gui/backgrounds/fullscreenfix/bg_gamebg02.png");
        var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = tex };
        AssetDatabase.CreateAsset(mat, "Assets/Game/Resources/data/lobby_bg.mat");
        Directory.CreateDirectory("Assets/Game/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("LobbyCamera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        cam.cullingMask = 0;
        cam.gameObject.AddComponent<AudioListener>();
        // Fighter used by the lobby preview (New Era's dressed prefab); kept inactive, cloned at runtime.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Contra/asset_bundles/gamebundle/prefabs/soldier/Enemy_Fighter_locomotion.prefab");
        var holder = new GameObject("FighterTemplate");
        holder.transform.position = new Vector3(0, -9000, 0);
        if (prefab)
        {
            var f = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            LegacyParticleConverter.ConvertInstance(f, AssetDatabase.GetAssetPath(prefab));   // body flames (the prefab cannot be saved: missing scripts)
            f.SetActive(false);
        }
        else Debug.LogWarning("[BuildGame] fighter prefab not found");
        // First-person weapons: the original Fighter_locomotion/Fighter/MainCamera/Weapon container (Animation with WeaponChangeAnimation…,
        // 11 WeaponLook type nodes with every model) kept inactive; WeaponRig clones it under the eye camera of the local player.
        var fpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Contra/asset_bundles/gamebundle/prefabs/soldier/Fighter_locomotion.prefab");
        var wHolder = new GameObject("WeaponTemplate");
        wHolder.transform.position = new Vector3(0, -9000, 0);
        if (fpPrefab)
        {
            var fp = (GameObject)PrefabUtility.InstantiatePrefab(fpPrefab);
            PrefabUtility.UnpackPrefabInstance(fp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            LegacyParticleConverter.ConvertInstance(fp, AssetDatabase.GetAssetPath(fpPrefab));   // the flamer / snow gun streams, the torch
            var weapon = fp.transform.Find("Fighter/MainCamera/Weapon");
            if (weapon) { weapon.SetParent(wHolder.transform, false); weapon.gameObject.SetActive(false); }   // keeps the prefab local pose under MainCamera
            else Debug.LogWarning("[BuildGame] Fighter/MainCamera/Weapon not found");
            Object.DestroyImmediate(fp);
        }
        else Debug.LogWarning("[BuildGame] Fighter_locomotion prefab not found");
        // CharacterManager.GetPrefab: the "CharacterData" prefabs (grenade, snowball, sticky bomb, snow explosion, crossbow bolt)
        var cp = new GameObject("CharacterPrefabs").AddComponent<CharacterPrefabs>();
        cp.Prefabs = new[] { "GrenadePrefab", "SnowballPrefab", "BombPrefab", "ExploSnowPrefab", "CrossbowBoltPrefab" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Contra/asset_bundles/gamebundle/prefabs/soldier/" + n + ".prefab"))
            .Where(g => g != null).ToArray();
        Debug.Log("[BuildGame] character prefabs: " + cp.Prefabs.Length);
        EditorSceneManager.SaveScene(scene, LobbyPath);
    }
}
