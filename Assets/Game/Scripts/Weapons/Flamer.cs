// Port of the original Flamer (New Era NE_Scripts_Decrypted/Flamer.cs; Penta 2016 the same): same class name, fields and script GUID
// (382677f5174d5f584ffa2f7db23e4659), on WeaponLeft/flamethrower_n1 and WeaponLeft/SNG_Snowgun of the first-person models.
// The original drove legacy ParticleEmitter components; they are ParticleSystems now (Editor/LegacyParticleConverter.cs, Unity's own
// updater rules), so "emit" is the emission module plus Play (as ItemTracer.SetEmit). fire(true) lights the stream and keeps it lit
// for 0.3 s after the last call (each shot refreshes it), fire(false) puts it out; particles already out fade as before.
using System.Collections;
using UnityEngine;

public class Flamer : MonoBehaviour
{
    public float scale = 1f;
    public float speed = 1f;
    ParticleSystem[] flamers;
    float turnOffAt;

    public ParticleSystem[] Flamers
    {
        get
        {
            if (flamers == null) flamers = transform.GetComponentsInChildren<ParticleSystem>(true);
            return flamers;
        }
    }

    static void Additive(Renderer r)
    {
        var s = RuntimeTextureLoader.FindShader("Particles/Additive");
        if (r != null && r.sharedMaterial != null && s != null) r.sharedMaterial.shader = s;
    }

    void Start()
    {
        var r = gameObject.GetComponentsInChildren<ParticleSystemRenderer>();
        if (r.Length > 0) Additive(r[0]);
        if (r.Length > 1) Additive(r[1]);
        if (gameObject.name == "flamethrower_n1" && transform.parent != null && transform.parent.childCount > 1 && transform.parent.GetChild(1).name == "Flame")
            Additive(transform.parent.GetChild(1).GetComponent<ParticleSystemRenderer>());
        fire(false);
    }

    public void fire(bool a)
    {
        if (Flamers == null) return;
        foreach (var ps in Flamers)
        {
            if (ps == null) continue;
            var e = ps.emission;
            e.enabled = a;
            if (a && !ps.isPlaying) ps.Play();
        }
        if (a && isActiveAndEnabled) StartCoroutine(WaitAndFireOff(0.3f));
    }

    IEnumerator WaitAndFireOff(float a)
    {
        float t = Time.time;
        if (t + a > turnOffAt) turnOffAt = t + a;
        yield return new WaitForSeconds(a);
        if (Time.time + 0.01f >= turnOffAt) fire(false);
    }
}
