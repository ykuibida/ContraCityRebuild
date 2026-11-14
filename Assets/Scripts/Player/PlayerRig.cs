// Builds the local first-person player like the original fighter prefab (Fighter_locomotion, Penta 2016):
// CharacterController 6.5 x r1.3 (center y 3.65, step 1.5, skin 0.45, slope 90), CharacterMotor, SoldierController,
// SoldierCamera (mouse look) and the eye camera at the prefab MainCamera position (0.0084, 6.268, 0.6457).
using System.Collections.Generic;
using UnityEngine;

public static class PlayerRig
{
    public static GameObject Create(Vector3 feet, float yaw, Camera template)
    {
        var root = new GameObject("Player");
        root.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));

        // prefab Fighter_locomotion: the CharacterController object ("Fighter") is on layer 2 (Ignore Raycast) — the base gates of the
        // own team (layer 12) do not collide with it
        root.layer = 2;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 6.5f; cc.radius = 1.3f; cc.center = new Vector3(0f, 3.65f, 0f);
        cc.stepOffset = 1.5f; cc.skinWidth = 0.45f; cc.slopeLimit = 90f; cc.minMoveDistance = 0f;

        root.AddComponent<CharacterMotor>();
        root.AddComponent<SoldierController>();
        root.AddComponent<PlayerVitals>();
        var look = root.AddComponent<SoldierCamera>();

        var eye = new GameObject("FPSCamera");
        eye.transform.SetParent(root.transform, false);
        eye.transform.localPosition = new Vector3(0.008388519f, 6.2682953f, 0.6456909f);
        eye.AddComponent<FPSCamera>();
        var cam = eye.AddComponent<Camera>();
        if (template) cam.CopyFrom(template);
        // CopyFrom also copies the template's transform: put the eye back on the fighter.
        eye.transform.localPosition = new Vector3(0.008388519f, 6.2682953f, 0.6456909f);
        eye.transform.localRotation = Quaternion.identity;
        GameOptions.Load();
        cam.fieldOfView = GameOptions.FpsFieldOfView; // Penta: fixed 55; contra.city: option "fpsfov"
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = Mathf.Max(template ? template.farClipPlane : 300f, 300f);
        cam.enabled = true;
        cam.tag = "MainCamera";
        cam.depth = 0f;                 // prefab MainCamera: depth 0, culling mask 4293637887 (all but layers 8 weapons, 11, 14, 18, 20)
        cam.cullingMask = unchecked((int)4293637887u);
        eye.AddComponent<AudioListener>();
        SoundManager.LocalPlayerAudio = SoundManager.Make3D(eye, true);   // MainCamera AudioSource + AudioSourceVC "effect" (LocalPlayer.Audio)

        look.SetYaw(yaw);
        var body = FighterBody.Attach(root, cam);   // the dressed soldier (hidden in first person, shown for taunts)
        ShotController.Attach(root, cam, body);     // first-person weapons (MainCamera/Weapon + WeaponCamera of the prefab)
        return root;
    }

    /// <summary>Team spawn points of a map: ItemMapXML markers named Respawn_T0/T1/T2 (kept under POINTS_RESCALE).</summary>
    public static List<Transform> SpawnPoints()
    {
        var list = new List<Transform>();
        foreach (var m in Object.FindObjectsByType<ItemMapXML>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (m.name.StartsWith("Respawn")) list.Add(m.transform);
        return list;
    }
}
