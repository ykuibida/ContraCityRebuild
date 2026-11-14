// Health / armor ("energy") of the local player; receives map pickups (ItemMapXML).
// Base = New Era (W:/Contra City Extract/NE_Scripts_Decrypted/PlayerManager.cs):
//   pickups — PickItem :4520-4570, ApplyLocalHealthPickup / ApplyLocalArmorPickup / GetPickupRecoveredValue :4593-4649;
//   damage — ApplyDamageArmorFirstAndReport :8467-8490, ApplyLocalFallDamage :8497-8513 (fall damage comes from CharacterMotor).
// No death below the map: New Era has none (its pit kill zone is a stub), CharacterMotor shows the under-map warning instead.
using System;
using UnityEngine;

public class PlayerVitals : MonoBehaviour, IPickupReceiver
{
    public int MaxHealth = 100;
    public int Health = 100;
    public int MaxArmor = 100;   // New Era ApplyLocalArmorPickup: armor max 100
    public int Armor = 0;

    /// <summary>Raised on an ammo pickup (size, value); the weapon system adds it to the reserve (ShotController.AddAmmoToReserve).</summary>
    public event Action<int, int> AmmoPickup;
    public event Action<PickupKind, int> PickedUp;

    public bool Dead => Health <= 0;

    void OnEnable() => ItemMapXML.Receivers.Add(this);
    void OnDisable() => ItemMapXML.Receivers.Remove(this);

    /// <summary>GetPickupRecoveredValue: a small kit (size 1, or size 0 with value 50) adds half of the maximum (rounded up),
    /// a big kit fills to the maximum; nothing when already full.</summary>
    static int RecoveredValue(int cur, int max, int size, int value)
    {
        max = Math.Max(1, max); cur = Math.Max(0, cur);
        if (cur >= max) return cur;
        if (size == 1 || (size == 0 && value == 50)) return Math.Min(max, cur + Math.Max(1, (max + 1) / 2));
        return max;
    }

    public bool TryPickup(PickupKind kind, int size, int value)
    {
        if (Dead) return false;
        switch (kind)
        {
            case PickupKind.Health:
            {
                int max = Math.Max(100, MaxHealth);              // ApplyLocalHealthPickup: max(100, local battle max health)
                int v = RecoveredValue(Health, max, size, value);
                if (v <= Health) return false;
                Health = v;
                SoundManager.Play(SoundManager.LocalPlayerAudio, "pick up energy container", AudioPlayMode.Play);
                break;
            }
            case PickupKind.Armor:
            {
                int v = RecoveredValue(Armor, 100, size, value);
                if (v <= Armor) return false;
                Armor = v;
                SoundManager.Play(SoundManager.LocalPlayerAudio, "pick up energy container", AudioPlayMode.Play);
                break;
            }
            case PickupKind.Ammo:
                // PickItem: the ammo box is always taken (RemoveItem runs after the switch); its reserve goes to the weapons
                SoundManager.Play(SoundManager.LocalPlayerAudio, "pick up ammo container", AudioPlayMode.Play);
                AmmoPickup?.Invoke(size, value);
                break;
            default: return false;
        }
        PickedUp?.Invoke(kind, value);
        return true;
    }

    public void ResetForSpawn()
    {
        Health = MaxHealth;
        Armor = 0;
    }

    /// <summary>Raised once when health reaches 0: (killer name or null for suicide, killer weapon system name).</summary>
    public event Action<string, string> Died;
    public event Action<int> Damaged;

    /// <summary>% of incoming damage removed (BattleStats: Железный Феликс + general defence of clothes). New Era applies these on
    /// its battle server for weapon damage; kept here for that — fall damage does not use it (ApplyLocalFallDamage).</summary>
    public int DamageTakenReduction;
    /// <summary>Fall damage factor (BattleStats: «Легкое Приземление» ×0.5), used by CharacterMotor.</summary>
    public float FallDamageMultiplier = 1f;

    /// <summary>ApplyDamageArmorFirstAndReport: armor takes the damage first, the rest goes to health.</summary>
    void ApplyArmorFirst(int amount)
    {
        int toArmor = Math.Min(Math.Max(0, Armor), amount);
        int toHealth = Math.Min(Math.Max(0, Health), Math.Max(0, amount - toArmor));
        Armor = Math.Max(0, Armor - toArmor); Health = Math.Max(0, Health - toHealth);
    }

    /// <summary>Weapon damage (later): the damage-taken reduction, then armor first.</summary>
    public void TakeDamage(int amount, string killer = null, string weapon = null)
    {
        if (Dead || amount <= 0) return;
        if (DamageTakenReduction > 0) amount = Mathf.Max(1, Mathf.RoundToInt(amount * (1f - DamageTakenReduction / 100f)));
        ApplyArmorFirst(amount);
        Damaged?.Invoke(amount);
        if (Dead) Died?.Invoke(killer, weapon);
    }

    /// <summary>PlayerManager.ApplyDamageArmorFirst (NE :8467-8495): health + energy damage, armor takes it first, no reduction;
    /// KillMe at 0 (own explosions: PlayerShot :9449-9458).</summary>
    public void ApplyDamageArmorFirst(int health, int energy, string weapon = null)
    {
        int amount = Math.Max(0, health) + Math.Max(0, energy);
        if (Dead || amount <= 0) return;
        ApplyArmorFirst(amount);
        Damaged?.Invoke(amount);
        if (Dead) Died?.Invoke(null, weapon);
    }

    /// <summary>PlayerManager.ApplyLocalFallDamage: armor first, no reduction; KillMe when health reaches 0.</summary>
    public void ApplyFallDamage(int amount)
    {
        if (Dead || amount <= 0) return;
        ApplyArmorFirst(amount);
        Damaged?.Invoke(amount);
        if (Dead) Died?.Invoke(null, null);
    }

    /// <summary>Instant death (time over kills everyone — PlayerManager TimeOver), no armor, no reduction.</summary>
    public void Kill(string killer = null, string weapon = null)
    {
        if (Dead) return;
        Health = 0;
        Died?.Invoke(killer, weapon);
    }
}
