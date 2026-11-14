// Lobby 3D view = original CharacterCameraManager: a background camera with the wooden corridor
// (gui/backgrounds/fullscreenfix/bg_gamebg02) and the character camera (fov 26, soldier at local (-3.174,-3.71,23),
// yaw 165), both with pixelRect = (colX, H-581-113, 760, 581). Mouse drag in (colX+115, 175, 187, 420) rotates.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterPreview : MonoBehaviour
{
    // Layer 31 belongs to the New Era battle HUD: BattleGUI strips it from every other camera's culling mask, which made the
    // lobby soldier vanish after the first battle. The soldier uses its own layer and the masks are re-asserted every frame.
    const int BgLayer = 30, FighterLayer = 29;
    public bool Active;
    GameObject stage, fighter;
    Camera bgCam, charCam;
    string lastOutfit = "";
    float yaw = 165f;
    Rect rect;
    bool dragging; float lastX;

    void Start()
    {
        stage = new GameObject("_LobbyStage");
        DontDestroyOnLoad(stage);
        stage.transform.position = new Vector3(0, -5000, 0);

        // background corridor
        var bgGo = new GameObject("BackgroundCamera");
        bgGo.transform.SetParent(stage.transform, false);
        bgCam = bgGo.AddComponent<Camera>();
        bgCam.orthographic = true; bgCam.orthographicSize = 0.5f; bgCam.nearClipPlane = 0.1f; bgCam.farClipPlane = 10f;
        bgCam.clearFlags = CameraClearFlags.SolidColor; bgCam.backgroundColor = Color.black;
        bgCam.cullingMask = 1 << BgLayer; bgCam.depth = 0;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.layer = BgLayer;
        quad.transform.SetParent(bgGo.transform, false);
        quad.transform.localPosition = new Vector3(0, 0, 5);
        quad.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("data/lobby_bg");
        quad.transform.localScale = new Vector3(760f / 581f, 1f, 1f);

        // character camera (MainMenuCamera: fov 26, depth-only)
        var cGo = new GameObject("CharacterCamera");
        cGo.transform.SetParent(stage.transform, false);
        cGo.transform.localPosition = new Vector3(100, 1, -10);
        charCam = cGo.AddComponent<Camera>();
        charCam.fieldOfView = 26f; charCam.nearClipPlane = 0.3f; charCam.farClipPlane = 1000f;
        charCam.clearFlags = CameraClearFlags.Depth; charCam.cullingMask = 1 << FighterLayer; charCam.depth = 1;
        // the three active directional lights of the New Era MainMenu scene (world rotations, colour, intensity; gamma space)
        var lights = new[]
        {
            (new Quaternion(0.5498875f, 0.6879697f, 0.1568704f, 0.4468928f), new Color(0.7969481f, 0.8573054f, 1f), 1.7f),
            (new Quaternion(-0.3341697f, -0.7164759f, 0.4744201f, 0.3871931f), new Color(0.7969481f, 0.8573054f, 1f), 0.4f),
            (new Quaternion(0.20644681f, -0.079650596f, 0.54233164f, 0.81050104f), new Color(0.977612f, 0.9285324f, 0.882769f), 1.7f),
        };
        foreach (var (rot, col, inten) in lights)
        {
            var l = new GameObject("Directional light").AddComponent<Light>();
            l.transform.SetParent(stage.transform, false);
            l.transform.localRotation = rot;
            l.type = LightType.Directional; l.color = col; l.intensity = inten; l.cullingMask = 1 << FighterLayer;
        }

        fighter = Dresser.CreateFighter(cGo.transform);
        if (fighter)
        {
            SetLayer(fighter.transform, FighterLayer);
            fighter.transform.localPosition = new Vector3(-3.174131f, -3.71f, 23f);
            fighter.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            Dresser.Dress(fighter, new Dictionary<int, GameItem>());
        }
        SetActive(false);
    }

    static void SetLayer(Transform t, int layer) { t.gameObject.layer = layer; foreach (Transform c in t) SetLayer(c, layer); }

    void SetActive(bool on) { if (bgCam) bgCam.enabled = on; if (charCam) charCam.enabled = on && fighter; }

    public void SetRect(Rect guiRect) { rect = guiRect; }

    public void SetOutfit(Dictionary<int, GameItem> outfit)
    {
        var key = string.Join(",", outfit.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + (kv.Value != null ? kv.Value.Id : 0)));   // GameItem has no ToString: key on ids
        if (key == lastOutfit || !fighter) return;
        lastOutfit = key;
        Dresser.Dress(fighter, outfit);
        SetLayer(fighter.transform, FighterLayer);
    }

    /// <summary>Test hook (-uitest): freeze the soldier on a clip at a normalized time.</summary>
    public void DebugPose(string clip, float t)
    {
        var anim = fighter ? fighter.GetComponentInChildren<Animation>() : null;
        if (!anim || anim[clip] == null) return;
        anim.Play(clip); anim[clip].normalizedTime = t; anim[clip].speed = 0f; anim.Sample();
    }

    void Update()
    {
        if (!bgCam) return;
        SetActive(Active);
        if (!Active) return;
        bgCam.cullingMask = 1 << BgLayer; charCam.cullingMask = 1 << FighterLayer;
        var pr = new Rect(rect.x, rect.y, rect.width, rect.height);
        bgCam.pixelRect = pr; charCam.pixelRect = pr;
        // rotate zone in GUI coordinates (y from top) -> mouse is bottom-up
        var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        var zone = new Rect(LobbyUI.ColX + 115, 175, 187, 420);
        if (Input.GetMouseButtonDown(0) && zone.Contains(mouse)) { dragging = true; lastX = mouse.x; }
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging && fighter)
        {
            yaw += -(mouse.x - lastX) / 2f; lastX = mouse.x;
            fighter.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        }
    }
}
