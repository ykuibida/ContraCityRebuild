// Port of the New Era RuntimeTextureLoader (NE_Scripts_Decrypted/RuntimeTextureLoader.cs; Penta 2016 RuntimeTextureLoader.cs streamed
// the same textures from asset bundles through TextureLoader). Same class name, fields and script GUID (318430b40d0cc3047f8246d41cd8baa5),
// so the clothes, the first-person weapon models (Folder "Cloth/") and the map objects (Folder "Map/") keep their data.
//   Start → ForceLoad: picks the material (materialID 0 = sharedMaterial, else sharedMaterials[materialID − 1]), sets the shader the
//   New Era client sets for that texture (NE :161-381), then loads Resources "Textures/<Folder><textureID>" asynchronously into _MainTex
//   (NE :401-416) and makes silencer parts opaque (NE :19-66).
// Unity 6 differences (marked "U6"): the built-in shaders are named "Legacy Shaders/…" ("Reflective/Diffuse" → "Legacy Shaders/Reflective/
// Diffuse"), so FindShader tries the original name, then the "Legacy Shaders/" one. A texture that is not in Resources is not written
// (New Era wrote null; Penta's ApplyTexture applied only on success) — "concrete_03_d" and "shotgun" are missing in New Era too.
using System.Collections;
using UnityEngine;

public class RuntimeTextureLoader : MonoBehaviour
{
    Material material;
    public string textureID = string.Empty;
    public string Folder = "Cloth/";
    public int materialID;

    /// <summary>U6: Shader.Find with the "Legacy Shaders/" name of the old built-in shaders.</summary>
    public static Shader FindShader(string name)
    {
        var s = Shader.Find(name);
        if (s == null && !name.StartsWith("Legacy Shaders/")) s = Shader.Find("Legacy Shaders/" + name);
        return s;
    }

    static void SetShader(Material m, string name)
    {
        var s = FindShader(name);
        if (m != null && s != null) m.shader = s;
    }

    bool ShouldForceOpaqueWeaponPart()
    {
        string a = textureID != null ? textureID.ToLower() : string.Empty;
        string b = gameObject != null && gameObject.name != null ? gameObject.name.ToLower() : string.Empty;
        string c = material != null && material.name != null ? material.name.ToLower() : string.Empty;
        return a.IndexOf("silencer") >= 0 || b.IndexOf("silencer") >= 0 || c.IndexOf("silencer") >= 0;
    }

    void ForceOpaqueWeaponPart()
    {
        if (!ShouldForceOpaqueWeaponPart() || material == null) return;
        Shader shader = FindShader("Legacy Shaders/Diffuse");
        if (shader == null) shader = FindShader("Diffuse");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");
        if (shader == null) shader = FindShader("VertexLit");
        if (shader != null) material.shader = shader;
        if (material.HasProperty("_Color")) { var col = material.color; col.a = 1f; material.color = col; }
        if (material.HasProperty("_TintColor")) { var col = material.GetColor("_TintColor"); col.a = 1f; material.SetColor("_TintColor", col); }
        material.renderQueue = -1;
    }

    bool IsFirstPersonOrPickupVisual()
    {
        for (var p = transform; p != null; p = p.parent)
        {
            string n = (p.name ?? string.Empty).ToLower();
            if (n == "weaponcamera" || n == "fpscamera" || n.IndexOf("weapon") >= 0) return true;
        }
        string g = (gameObject.name ?? string.Empty).ToLower();
        return g.IndexOf("pickup") >= 0 || g.IndexOf("health") >= 0 || g.IndexOf("armor") >= 0 || g.IndexOf("ammo") >= 0 || g.IndexOf("kit") >= 0;
    }

    static Shader ResolveLitShader(bool reflective)
    {
        if (reflective) { var r = FindShader("Reflective/Diffuse"); if (r != null) return r; }
        Shader s = FindShader("Legacy Shaders/Diffuse");
        if (s == null) s = FindShader("Diffuse");
        if (s == null) s = Shader.Find("Mobile/Diffuse");
        if (s == null) s = FindShader("VertexLit");
        return s;
    }

    static void EnsureOriginalSkyboxShader()
    {
        if (RenderSettings.skybox == null) return;
        var s = Shader.Find("Skybox/Cubemap");
        if (s != null && s.isSupported) RenderSettings.skybox.shader = s;
    }

    protected void LoadTexture(string a)
    {
        if (material != null && a != string.Empty)
        {
            if (a == "steel_7" || a == "wood_06a") Debug.LogError(string.Format("{0} on {1}", a, name));
            LoadTextureFromResources(a, "_MainTex");
        }
    }

    void Start() { ForceLoad(); }

    static Texture2D Icicle() => (Texture2D)Resources.Load("Textures/Cloth/icicle_Color");

    public void ForceLoad()
    {
        var r = gameObject.GetComponent<Renderer>();
        if (r == null) return;
        if (materialID == 0)
        {
            material = r.sharedMaterial;
            if (material == null) return;
            if (Folder == "Cloth/")
            {
                if (textureID == null || (!textureID.StartsWith("CLOTH") && !textureID.StartsWith("guybrush")))
                {
                    switch (textureID)
                    {
                        case "milkor_grenade_Color": case "christmas_candy": case "christmas_candy2": case "bat_wooden_color":
                        case "katana_Color": case "katana_Blood_Color": case "scythe_Color": case "Crowbar_color": case "Stickybomb_Diesad":
                            SetShader(r.sharedMaterial, "Legacy Shaders/Diffuse");
                            break;
                        case "icicle_Color":
                            r.sharedMaterial.mainTexture = Icicle();
                            SetShader(r.sharedMaterial, "Legacy Shaders/Transparent/Specular");
                            break;
                        case "torch_color":
                        {
                            SetShader(r.sharedMaterial, "Legacy Shaders/Diffuse");
                            var ps = gameObject.GetComponentInChildren<ParticleSystemRenderer>();
                            if (ps != null) SetShader(ps.sharedMaterial, "Particles/Additive");
                            break;
                        }
                        case "n1_Color":
                            SetShader(r.sharedMaterial, "Reflective/Diffuse");
                            break;
                        case "Taurus_Color_02":
                        {
                            SetShader(r.sharedMaterial, "Reflective/Diffuse");
                            if (gameObject.name == "Taurus_LOD0")
                            {
                                var t = transform;
                                for (int i = 0; i < 7 && t != null; i++) t = t.parent;
                                if (t != null && t.childCount > 2)
                                {
                                    var a = t.GetChild(1).GetComponent<SkinnedMeshRenderer>(); if (a) SetShader(a.sharedMaterial, "Legacy Shaders/Diffuse");
                                    var b = t.GetChild(2).GetComponent<SkinnedMeshRenderer>(); if (b) SetShader(b.sharedMaterial, "Legacy Shaders/Diffuse");
                                }
                            }
                            break;
                        }
                        default:
                            if (gameObject.name.StartsWith("Gloves")) SetShader(r.sharedMaterial, "Legacy Shaders/Diffuse");
                            else { var s = ResolveLitShader(!IsFirstPersonOrPickupVisual()); if (s != null) r.sharedMaterial.shader = s; }
                            break;
                    }
                }
                else
                {
                    if (textureID == "CLOTH_mask_01_color_02" || textureID == "CLOTH_mask_01_color_03" || textureID == "CLOTH_Masks_Goggles_Business01_Color")
                        SetShader(r.sharedMaterial, "Legacy Shaders/Transparent/Diffuse");
                    if (r.sharedMaterial.name != "icicle_Color_") SetShader(r.sharedMaterial, "Legacy Shaders/Diffuse");   // NE :260-263 (overrides the line above)
                    else r.sharedMaterial.mainTexture = Icicle();
                }
            }
            else
            {
                EnsureOriginalSkyboxShader();
                if (textureID == "wire_var2_color") SetShader(r.sharedMaterial, "Legacy Shaders/Transparent/Diffuse");
                else if (textureID == "glass_02") SetShader(r.sharedMaterial, "Transparent/Diffuse");
                else SetShader(r.sharedMaterial, "Legacy Shaders/Diffuse");
            }
        }
        else
        {
            var mats = r.sharedMaterials;
            if (materialID - 1 >= mats.Length) return;
            material = mats[materialID - 1];
            if ((textureID == "CLOTH_Mask_Gasmask02_Color" || textureID == "CLOTH_Mask_Gasmask01_Color") && materialID == 2)
            {
                SetShader(r.sharedMaterial, "Legacy Shaders/Transparent/Diffuse");
                foreach (var rr in GetComponents<Renderer>())
                {
                    var m = rr.materials;   // NE :297 / :307 — renderer.materials (per-renderer copies)
                    if (m.Length > 1) SetShader(m[1], "Legacy Shaders/Diffuse");
                }
            }
        }
        // IL_06e2
        ForceOpaqueWeaponPart();
        if (r.sharedMaterial != null && r.sharedMaterial.name == "icicle_Color_") r.sharedMaterial.mainTexture = Icicle();
        else LoadTexture(textureID);
    }

    protected void LoadTextureFromResources(string a, string b)
    {
        if (material != null && a != string.Empty) StartCoroutine(LoadAsyncFromResources("Textures/" + Folder + a, b));
    }

    IEnumerator LoadAsyncFromResources(string a, string b)
    {
        var request = Resources.LoadAsync(a);
        yield return request;
        var texture = request.asset as Texture;
        if (texture != null) material.SetTexture(b, texture);   // U6 / Penta: only on success (see the header)
        ForceOpaqueWeaponPart();
    }
}
