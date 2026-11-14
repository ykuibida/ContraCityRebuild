// Port of the original DeleteAfterSeconds (New Era NE_Scripts_Decrypted/DeleteAfterSeconds.cs; Penta 2016 the same without the shader
// fixes). Same class name, field and script GUID (4932d6a4f6f8b8edefea4c21932d57c2): every effect prefab destroys itself after `seconds`.
// The New Era shader fixes on the first renderer of a few effects are kept; their legacy ParticleRenderer is the converted
// ParticleSystemRenderer now (U6, Editor/LegacyParticleConverter.cs). The exported prefabs carry a "_NN" suffix (Explo_11, HealthHit_11),
// so the names are compared without it ("Explo_11(Clone)" → "Explo(Clone)").
using UnityEngine;

public class DeleteAfterSeconds : MonoBehaviour
{
    public float seconds = 2f;

    static void SetShader(Material m, string name)
    {
        var s = RuntimeTextureLoader.FindShader(name);
        if (m != null && s != null) m.shader = s;
    }

    void Start()
    {
        string n = System.Text.RegularExpressions.Regex.Replace(gameObject.name, @"_\d+(?=\(Clone\)|$)", "");
        if (n.StartsWith("Explo("))
        {
            var p = GetComponentsInChildren<Projector>();
            if (p.Length > 0) SetShader(p[0].material, "Projector/Multiply");
        }
        else if (n.StartsWith("HealthHit"))
        {
            var r = GetComponentsInChildren<ParticleSystemRenderer>();
            if (r.Length > 0) SetShader(r[0].material, "Particles/Multiply");
        }
        else if (n.StartsWith("Blood"))
        {
            var r = GetComponentsInChildren<ParticleSystemRenderer>();
            if (r.Length > 0) SetShader(r[0].material, "Particles/Multiply");
            if (r.Length > 1) SetShader(r[1].material, "Particles/Multiply");
        }
        else if (n.StartsWith("ExploSnowPrefab"))
        {
            var r = GetComponentsInChildren<ParticleSystemRenderer>();
            var p = GetComponentsInChildren<Projector>();
            if (p.Length > 0) SetShader(p[0].material, "Projector/Multiply");
            if (r.Length > 1) SetShader(r[1].material, "Particles/Additive");
        }
        else if (n.StartsWith("CrossbowBoltPrefab"))
        {
            var t = GetComponentInChildren<TrailRenderer>();
            if (t != null) SetShader(t.material, "Particles/Additive");
        }
        Destroy(gameObject, seconds);
    }
}
