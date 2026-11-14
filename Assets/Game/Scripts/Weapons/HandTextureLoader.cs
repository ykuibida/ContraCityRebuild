// Port of the original HandTextureLoader (New Era NE_Scripts_Decrypted/HandTextureLoader.cs; Penta 2016 has the empty class).
// Same class name and script GUID (a8dc0c17eb7a9f31d64635e67e4fe511): the "Gloves_hands01" mesh of every first-person weapon model.
// PlayerCustomisator.SetHandTextureID (NE :308-322) gives it the worn glove's texture before the model starts.
using UnityEngine;

public class HandTextureLoader : RuntimeTextureLoader
{
    void Start()
    {
        // NE :7-14 — the arm meshes next to the hands get the diffuse shader (child 3 of SNG_Snowgun, child 2 otherwise, not SG_Spas)
        var parent = transform.parent;
        if (parent != null)
        {
            if (parent.name == "SNG_Snowgun") { if (parent.childCount > 3) Diffuse(parent.GetChild(3).GetComponent<SkinnedMeshRenderer>()); }
            else if (parent.name != "SG_Spas") { if (parent.childCount > 2) Diffuse(parent.GetChild(2).GetComponent<SkinnedMeshRenderer>()); }
        }
        Diffuse(GetComponent<SkinnedMeshRenderer>());
        if (textureID != string.Empty) ForceLoad();
    }

    static void Diffuse(SkinnedMeshRenderer r)
    {
        if (r != null && r.sharedMaterial != null) r.sharedMaterial.shader = FindShader("Legacy Shaders/Diffuse");
    }
}
