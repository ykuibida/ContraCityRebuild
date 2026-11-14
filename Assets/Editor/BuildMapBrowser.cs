// Builds the standalone Windows map browser.
// Batch: Unity -batchmode -projectPath <this> -executeMethod BuildMapBrowser.Build -quit
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildMapBrowser
{
    const string ViewerFolder = "Assets/Viewer";
    const string MenuScenePath = ViewerFolder + "/" + MapBrowser.MenuScene + ".unity";
    const string OutPath = "Builds/ContraMaps/ContraMaps.exe";

    [MenuItem("Contra City/Build map browser (Windows)")]
    public static void Build()
    {
        CreateMenuScene();

        var viewers = Directory.GetFiles(ViewerFolder, "*_Viewer.unity").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList();
        var scenes = new[] { MenuScenePath }.Concat(viewers).Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        EditorBuildSettings.scenes = scenes;

        PlayerSettings.companyName = "ContraCityRebuild";
        PlayerSettings.productName = "Contra City Maps";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.Select(s => s.path).ToArray(),
            locationPathName = OutPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log($"[Contra] Build {report.summary.result}: {Path.GetFullPath(OutPath)}  size={report.summary.totalSize / (1024 * 1024)} MB  errors={report.summary.totalErrors}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    static void CreateMenuScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("MenuCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        camGo.tag = "MainCamera";
        new GameObject("MapBrowser").AddComponent<MapBrowser>();
        Directory.CreateDirectory(ViewerFolder);
        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }
}
