// Port of the original AudioSourceVC (New Era NE_Scripts_Decrypted/AudioSourceVC.cs; Penta 2016 the same). Same class name, field and
// script GUID (4c7405720a452413d79d25cfbfe70b12): the local player's camera source and the effect prefabs (MachineGunSpark …) keep it.
// LateUpdate: muted or effects volume 0 → volume 0 and Pause() once; otherwise Play() again after a pause, and the volume follows the
// effects ("effect") or music ("music") slider.
using UnityEngine;

public class AudioSourceVC : MonoBehaviour
{
    AudioSource audioSource;
    public string SoundType = "effect";
    bool paused;

    void Start() { audioSource = GetComponent<AudioSource>(); }

    void LateUpdate()
    {
        if (audioSource == null) return;
        if (GameOptions.SoundIsMute || GameOptions.SoundVolumeEffect <= 0f)
        {
            audioSource.volume = 0f;
            if (!paused) { audioSource.Pause(); paused = true; }
            return;
        }
        if (paused) { audioSource.Play(); paused = false; }
        switch (SoundType)
        {
            case "effect": if (audioSource.volume != GameOptions.SoundVolumeEffect) audioSource.volume = GameOptions.SoundVolumeEffect; break;
            case "music": if (audioSource.volume != GameOptions.SoundVolumeMusic) audioSource.volume = GameOptions.SoundVolumeMusic; break;
        }
    }
}
