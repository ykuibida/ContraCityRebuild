using UnityEngine;

/// <summary>Kind of map pickup, derived from the marker's object name (HealthKit, AmmoKitSmall, ArmorKit...).</summary>
public enum PickupKind { Health, Ammo, Armor, Unknown }

/// <summary>Implemented by whoever can pick items up (the player, later bots). Return true if the item was used.</summary>
public interface IPickupReceiver
{
    bool TryPickup(PickupKind kind, int subType, int value);
}

/// <summary>
/// Map pickup marker. Same class name, fields and script GUID as the original Penta/contra.city ItemMapXML,
/// so the markers already stored in the 17 map scenes bind to it with their data:
/// SubType (as in the original), Value (100 = big, 50 = small), RespawnDelay in milliseconds.
/// The visible model is a child named "_PickupModel", placed by Contra City/Place pickups on all maps.
/// </summary>
public class ItemMapXML : MonoBehaviour
{
    public int SubType;
    public int Value;
    public int RespawnDelay = 15000;

    public const string ModelName = "_PickupModel";
    const float SpinDegreesPerSecond = 90f;
    /// <summary>Original LocalItemTracer.touchDistance: an item is taken when a receiver comes closer than this.</summary>
    public const float TouchDistance = 8f;
    /// <summary>Everyone who can pick items up (player, bots) registers here.</summary>
    public static readonly System.Collections.Generic.List<MonoBehaviour> Receivers = new System.Collections.Generic.List<MonoBehaviour>();

    GameObject model;
    float respawnAt = -1f;
    Animation legacyAnimation;

    public PickupKind Kind => KindFromName(name);
    public bool Available => respawnAt < 0f;

    public static PickupKind KindFromName(string objectName)
    {
        if (objectName.StartsWith("Health")) return PickupKind.Health;
        if (objectName.StartsWith("Ammo")) return PickupKind.Ammo;
        if (objectName.StartsWith("Armor")) return PickupKind.Armor;
        return PickupKind.Unknown;
    }

    void Start()
    {
        // The same component also marks team spawn points (Respawn_T0..T2) and flags (FlagPoint1/2): no pickup there.
        if (Kind == PickupKind.Unknown) { enabled = false; return; }
        var t = transform.Find(ModelName);
        model = t ? t.gameObject : null;
        legacyAnimation = GetComponent<Animation>();
        if (legacyAnimation && legacyAnimation.clip == null) legacyAnimation = null;

    }

    void Update()
    {
        if (respawnAt >= 0f && Time.time >= respawnAt)
        {
            respawnAt = -1f;
            if (model) model.SetActive(true);
        }
        // Spin the model unless the original legacy animation already does it.
        if (model && model.activeSelf && legacyAnimation == null)
            model.transform.Rotate(0f, SpinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
    }

    void LateUpdate()
    {
        if (!Available) return;
        Vector3 p = transform.position;
        foreach (var r in Receivers)
        {
            if (r == null || !r.isActiveAndEnabled) continue;
            if ((r.transform.position - p).sqrMagnitude > TouchDistance * TouchDistance) continue;
            if (r is IPickupReceiver receiver && receiver.TryPickup(Kind, SubType, Value))
            {
                // New Era PlayerManager.RemoveItem (offline): respawnable pickups come back after a fixed 7 s (RespawnDelay of the marker unused)
                respawnAt = Time.time + 7f;
                if (model) model.SetActive(false);
                return;
            }
        }
    }
}
