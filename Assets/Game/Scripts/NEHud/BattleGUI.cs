// New Era BattleGUI (decrypted client code), without the network FPS/ping panel.
// The prefab Resources/gui/battle/prefab/BattleGUI is a 3D HUD with its own orthographic camera (layer 31):
// Health / Armor / Ammo blocks (TextMesh numbers, BattleStatsProgress fill), Impact icons, Overlay, KillStreak, Zoom.
using UnityEngine;
using UnityEngine.Rendering;

public class BattleGUI : MonoBehaviour
{
    Transform health, armor, ammo;
    TextMesh healthText, armorText, ammoText, ammo2Text;
    BattleStatsProgress healthStats, armorStats, ammoStats;
    BattleGUIImpact impact;
    KillStreak killStreak;
    MeshRenderer zombieOverlayRender, bloodOverlayRender, zoomOverlayRender;
    Camera battleGUICamera;
    bool isInit, guiMaterialsFixed, showAmmo = true;
    int prev_health = -1, prev_maxHealth = -1, prev_armor = -1, prev_maxArmor = -1, prev_ammo = -1, prev_maxAmmo = -1, prev_ammoReserve = -1;

    public Camera GuiCamera { get { Init(); return battleGUICamera; } }

    void Start() { Init(); }

    // Map fog (linear 250..410 on most maps) must not reach the HUD: its meshes sit deep in Z on the orthographic
    // GUI camera (BattleStatsProgress pushes the fill up to z = 760) and would turn into flat fog colour.
    static bool fogHooked, fogSaved;
    static Camera hudCam;
    static void HookFog()
    {
        if (fogHooked) return;
        fogHooked = true;
        Camera.onPreRender += c => { if (c != null && c == hudCam) { fogSaved = RenderSettings.fog; RenderSettings.fog = false; } };
        Camera.onPostRender += c => { if (c != null && c == hudCam) RenderSettings.fog = fogSaved; };
    }

    void Init()
    {
        if (isInit) return;
        health = transform.Find("Health");
        armor = transform.Find("Armor");
        ammo = transform.Find("Ammo");
        impact = transform.Find("Impact").GetComponent<BattleGUIImpact>();
        killStreak = transform.Find("KillStreak").GetComponentInChildren<KillStreak>(true);
        zombieOverlayRender = transform.Find("Overlay").Find("Zombie").GetComponent<MeshRenderer>();
        bloodOverlayRender = transform.Find("Overlay").Find("Blood").GetComponent<MeshRenderer>();
        zoomOverlayRender = transform.Find("Zoom").Find("ZoomOverlay").GetComponent<MeshRenderer>();
        zoomOverlayRender.enabled = false;
        healthText = health.Find("Text").GetComponent<TextMesh>();
        armorText = armor.Find("Text").GetComponent<TextMesh>();
        ammoText = ammo.Find("Text").GetComponent<TextMesh>();
        ammo2Text = ammo.Find("Text2").GetComponent<TextMesh>();
        isInit = true;
        if (impact != null) impact.HideAll();
        if (zombieOverlayRender != null) zombieOverlayRender.enabled = false;
        if (bloodOverlayRender != null) bloodOverlayRender.enabled = false;
        NormalizeGuiMaterials();
        battleGUICamera = GetComponentInChildren<BattleGUICamera>(true).GetComponent<Camera>();
        hudCam = battleGUICamera; HookFog();
        ForceCameraVisible();
        if (bloodOverlayRender != null && bloodOverlayRender.material != null)
        {
            var sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh != null && sh.isSupported) bloodOverlayRender.material.shader = sh;
        }
    }

    void OnDisable() { if (battleGUICamera != null) battleGUICamera.enabled = false; }
    void OnEnable() { ForceCameraVisible(); }

    public void SetCameraEnabled(bool a)
    {
        Init();
        if (battleGUICamera == null) return;
        if (a) ForceCameraVisible(); else battleGUICamera.enabled = false;
    }

    void ForceCameraVisible()
    {
        if (battleGUICamera == null) return;
        SetLayerRecursively(transform, 31);
        NormalizeGuiMaterials();
        battleGUICamera.gameObject.SetActive(true);
        battleGUICamera.gameObject.layer = 31;
        battleGUICamera.enabled = true;
        battleGUICamera.orthographic = true;
        battleGUICamera.clearFlags = CameraClearFlags.Depth;
        battleGUICamera.cullingMask = int.MinValue;
        battleGUICamera.depth = 1000f;
        battleGUICamera.nearClipPlane = -1000f;
        battleGUICamera.farClipPlane = 1000f;
        HideBattleGuiLayerFromOtherCameras();
    }

    void HideBattleGuiLayerFromOtherCameras()
    {
        const int mask = int.MinValue;
        foreach (var c in Camera.allCameras)
            if (c != null && c != battleGUICamera && (c.cullingMask & mask) != 0) c.cullingMask &= ~mask;
    }

    static void SetLayerRecursively(Transform a, int b)
    {
        if (a == null) return;
        a.gameObject.layer = b;
        for (int i = 0; i < a.childCount; i++) SetLayerRecursively(a.GetChild(i), b);
    }

    void NormalizeGuiMaterials()
    {
        if (guiMaterialsFixed) return;
        NormalizeGuiRoot(health); NormalizeGuiRoot(armor); NormalizeGuiRoot(ammo);
        guiMaterialsFixed = true;
    }

    static void NormalizeGuiRoot(Transform a)
    {
        if (a == null) return;
        var sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var sh2 = Shader.Find("GUI/Text Shader");
        foreach (var mr in a.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var m = mr.material;
            if (m == null) continue;
            if (mr.GetComponent<TextMesh>() != null) { if (sh2 != null) m.shader = sh2; }
            else if (sh != null) m.shader = sh;
            if (m.HasProperty("_Color")) { var c = m.color; if (c.a < 0.95f) { c.a = 1f; m.color = c; } }
            m.renderQueue = 4000;
        }
    }

    void EnsureZoomOverlayMaterial()
    {
        if (zoomOverlayRender == null) return;
        var m = zoomOverlayRender.material; if (m == null) return;
        var s = m.shader;
        if (s != null && s.isSupported && !s.name.Contains("InternalErrorShader")) return;
        var tex = m.mainTexture; bool hasCol = m.HasProperty("_Color"); var col = hasCol ? m.color : Color.white;
        var s2 = Shader.Find("Unlit/Transparent");
        if (s2 == null || !s2.isSupported) s2 = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (s2 == null || !s2.isSupported) return;
        m.shader = s2;
        if (tex != null) m.mainTexture = tex;
        if (m.HasProperty("_Color")) m.color = hasCol ? col : Color.white;
    }

    public void SetZoom(bool a)
    {
        if (zoomOverlayRender == null) return;
        if (a) EnsureZoomOverlayMaterial();
        if (zoomOverlayRender.enabled != a)
        {
            zoomOverlayRender.transform.localScale = new Vector3(1980f / 4095f * battleGUICamera.orthographicSize, 1890f / 4095f * battleGUICamera.orthographicSize, 0f);
            zoomOverlayRender.enabled = a;
        }
    }

    public void SetBloodOverlay(float a)
    {
        Init();
        if (bloodOverlayRender == null) return;
        float k = Mathf.Clamp01(a);
        if (k > 0f)
        {
            if (!bloodOverlayRender.enabled) { ForceCameraVisible(); bloodOverlayRender.enabled = true; }
            var m = bloodOverlayRender.material;
            if (m != null)
            {
                m.renderQueue = 3990;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.31f, 0.31f, 0.31f, k));
                else if (m.HasProperty("_Color")) { var c = m.color; c.a = k; m.color = c; }
            }
        }
        else if (bloodOverlayRender.enabled) bloodOverlayRender.enabled = false;
    }

    static void R(Transform t, string n, bool on) { var c = t.Find(n); if (c) { var r = c.GetComponent<MeshRenderer>(); if (r) r.enabled = on; } }

    /// <summary>Team 1 red, 2 blue, other (DM) green — as in New Era.</summary>
    public void SetTeam(short team, GameMode mode)
    {
        Init();
        ForceCameraVisible();
        foreach (var n in new[] { "Blue", "Green", "Red", "Zombie", "Background" }) R(health, n, false);
        foreach (var n in new[] { "Blue", "Green", "Red", "Background", "Text" }) R(armor, n, false);
        foreach (var n in new[] { "Blue", "Green", "Red" }) R(ammo, n, false);
        killStreak.gameObject.SetActive(false);
        zombieOverlayRender.enabled = false;
        if (mode == GameMode.ZOMBIE && team == 1)
        {
            R(health, "Zombie", true); zombieOverlayRender.enabled = true; return;
        }
        R(health, "Background", true); R(armor, "Background", true); R(armor, "Text", true);
        string col = team == 1 ? "Red" : team == 2 ? "Blue" : "Green";
        R(health, col, true); R(armor, col, true); R(ammo, col, true);
        healthStats = health.Find(col).GetComponent<BattleStatsProgress>();
        armorStats = armor.Find(col).GetComponent<BattleStatsProgress>();
        ammoStats = ammo.Find(col).GetComponent<BattleStatsProgress>();
        prev_health = prev_armor = prev_ammo = -1;
    }

    public void SetHealth(int a, int b)
    {
        Init();
        if (prev_health == a && prev_maxHealth == b) return;
        prev_health = a; prev_maxHealth = b;
        healthText.text = a.ToString();
        if (b < a) b = a;
        if (b <= 0) b = 1;
        if (healthStats != null) healthStats.SetValue(1f - Mathf.Clamp01(a / (float)b));
    }

    public void SetArmor(int a, int b)
    {
        Init();
        if (prev_armor == a && prev_maxArmor == b) return;
        prev_armor = a; prev_maxArmor = b;
        if (b < a) b = a;
        armorText.text = a.ToString();
        if (armorStats == null) return;
        if (b <= 0) { armorStats.SetValue(1f); return; }
        armorStats.SetValue(1f - Mathf.Clamp01(a / (float)b));
    }

    /// <summary>a = in clip, b = clip size, c = reserve. All zero hides the block (melee).</summary>
    public void SetAmmo(int a, int b, int c)
    {
        Init();
        if (prev_ammo == a && prev_maxAmmo == b && prev_ammoReserve == c) return;
        prev_ammo = a; prev_maxAmmo = b; prev_ammoReserve = c;
        if (a == 0 && b == 0 && c == 0) { if (showAmmo) { showAmmo = false; ammo.gameObject.SetActive(false); } return; }
        if (!showAmmo) { showAmmo = true; ammo.gameObject.SetActive(true); }
        ammoText.text = a.ToString();
        ammo2Text.text = c.ToString();
        if (b == 0 && a == 0) { a = 1; b = a; }
        if (ammoStats != null) ammoStats.SetValue(1f - 100f / b * a * 0.01f);
    }

    public void ShowImpact(BattleGUIImpact.ImpactType a, float b = 0f) { Init(); if (impact != null) impact.Show(a, b); }
    public void HideState(BattleGUIImpact.ImpactType a) { Init(); if (impact != null) impact.Hide(a); }
    public void HideAllImpacts() { Init(); if (impact != null) impact.HideAll(); SetBloodOverlay(0f); }

    public void SetKillStreak(string a, bool b)
    {
        Init();
        if (killStreak == null) return;
        killStreak.gameObject.SetActive(true);
        killStreak.Show(a, b);
    }
}
