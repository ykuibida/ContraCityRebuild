// Port of the original TriggerDeleteAfterSeconds (New Era NE_Scripts_Decrypted/TriggerDeleteAfterSeconds.cs). Same class name, field and
// script GUID (8ffee918743b0378753a569a5390f54a), on the rocket prefab (rocket_11): Trigger() destroys the object after `seconds`.
using UnityEngine;

public class TriggerDeleteAfterSeconds : MonoBehaviour
{
    public float seconds = 1f;
    public void Trigger() { Object.Destroy(gameObject, seconds); }
}
