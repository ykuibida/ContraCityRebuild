// Port of the original ItemTracer (New Era NE_Scripts_Decrypted/ItemTracer.cs; Penta 2016 the same): the base of the fighter's flying
// or lying items (rockets, grenades, sticky bombs), registered by their launch time stamp (CombatPlayer.RegisterItem).
// SetEmit is the legacy ParticleEmitter.emit of the original effects: their particles are ParticleSystems now
// (Editor/LegacyParticleConverter.cs), so "emit" switches the emission on (playing the system) or off.
using UnityEngine;

public abstract class ItemTracer : MonoBehaviour
{
    protected long launchTime;
    protected WeaponType weaponType;
    public WeaponType WeaponType { get => weaponType; set => weaponType = value; }
    public long TimeStamp => launchTime;
    public abstract void Destroy();

    /// <summary>ParticleEmitter.emit = a for every emitter under <paramref name="t"/> (U6: the converted ParticleSystems).</summary>
    public static void SetEmit(Transform t, bool a)
    {
        foreach (var ps in t.GetComponentsInChildren<ParticleSystem>())
        {
            if (ps == null) continue;
            var e = ps.emission;
            e.enabled = a;
            if (a && !ps.isPlaying) ps.Play();
        }
    }
}
