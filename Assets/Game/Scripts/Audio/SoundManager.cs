// Port of the original SoundManager.Play / LoadSound and AudioSourceVC (Penta 2016 SoundManager.cs:88-137, AudioSourceVC.cs;
// New Era SoundManager.cs:80-166 — same logic):
//   • clips: Resources "Sounds/<name>" (folder sounds/, lookup ignores case), cached (also a missing clip);
//   • nothing plays (and Stop is skipped too) while muted or with the effects volume at 0, or on an inactive source;
//   • modes: Play = PlayOneShot, PlayLoop = set clip and Play + loop if not playing, PlayStop = Stop then PlayOneShot, Stop = clip null + Stop;
//   • SoundManager never sets a volume: a source has its own (1) × AudioListener.volume (master); only sources with an
//     AudioSourceVC ("effect") follow the effects slider — the local player's camera source; the soldier's Legs / Speaker do not.
using System.Collections.Generic;
using UnityEngine;

public enum AudioPlayMode { Play, PlayLoop, PlayStop, Stop }

public static class SoundManager
{
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    /// <summary>The local player's camera source (PlayerManager: LocalPlayer.Audio = MainCamera AudioSource with AudioSourceVC "effect").</summary>
    public static AudioSource LocalPlayerAudio;

    public static AudioClip LoadSound(string name)
    {
        if (!cache.TryGetValue(name, out var c))
        {
            c = Resources.Load<AudioClip>("Sounds/" + name);
            if (c == null) Debug.LogWarning("[SoundManager] sound not found: " + name);
            cache[name] = c;
        }
        return c;
    }

    public static void Play(AudioSource src, string name, AudioPlayMode mode)
    {
        if (GameOptions.SoundIsMute || GameOptions.SoundVolumeEffect <= 0f || src == null || !src.gameObject.activeSelf || !src.gameObject.activeInHierarchy) return;
        var clip = LoadSound(name);
        if (clip == null && mode != AudioPlayMode.Stop) return;
        switch (mode)
        {
            case AudioPlayMode.Play: src.PlayOneShot(clip); break;
            case AudioPlayMode.PlayLoop: src.clip = clip; if (!src.isPlaying) { src.Play(); src.loop = true; } break;
            case AudioPlayMode.PlayStop: src.clip = null; src.Stop(); src.PlayOneShot(clip); break;
            case AudioPlayMode.Stop: src.clip = null; src.Stop(); break;
        }
    }

    /// <summary>SoundManager.PlayAfterSeconds (NE SoundManager.cs:387-396): Play after a delay (coroutine on the sound host).</summary>
    public static void PlayAfterSeconds(AudioSource src, string name, AudioPlayMode mode, float seconds)
    {
        Host.StartCoroutine(Delayed(src, name, mode, seconds));
    }

    static System.Collections.IEnumerator Delayed(AudioSource src, string name, AudioPlayMode mode, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Play(src, name, mode);
    }

    sealed class SoundHost : MonoBehaviour { }
    static SoundHost host;
    static SoundHost Host
    {
        get
        {
            if (!host) { var go = new GameObject("SoundManager"); Object.DontDestroyOnLoad(go); host = go.AddComponent<SoundHost>(); }
            return host;
        }
    }

    static AudioSource announcer;
    /// <summary>New Era SoundManager announcer: a 2D source with volume = effects (here: one clip at a time).</summary>
    public static void PlayAnnouncer(string name)
    {
        if (GameOptions.SoundIsMute || GameOptions.SoundVolumeEffect <= 0f) return;
        var clip = LoadSound(name);
        if (clip == null) return;
        if (!announcer) { var go = new GameObject("Announcer"); Object.DontDestroyOnLoad(go); announcer = go.AddComponent<AudioSource>(); announcer.spatialBlend = 0f; announcer.playOnAwake = false; }
        announcer.volume = GameOptions.SoundVolumeEffect;
        announcer.PlayOneShot(clip);
    }

    /// <summary>Stop without a clip name (SoundManager.Play(src, name, Stop) only clears and stops; same gate).</summary>
    public static void Stop(AudioSource src)
    {
        if (GameOptions.SoundIsMute || GameOptions.SoundVolumeEffect <= 0f || src == null || !src.gameObject.activeInHierarchy) return;
        src.clip = null; src.Stop();
    }

    /// <summary>The soldier's AudioSource settings of the original prefab (Legs / MainCamera: 3D, logarithmic, 1..500).</summary>
    public static AudioSource Make3D(GameObject go, bool vcEffect)
    {
        var s = go.AddComponent<AudioSource>();
        s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 1f; s.maxDistance = 500f; s.dopplerLevel = 1f;
        s.playOnAwake = false; s.loop = false;
        if (vcEffect) go.AddComponent<AudioSourceVC>();   // SoundType "effect"
        return s;
    }
}
