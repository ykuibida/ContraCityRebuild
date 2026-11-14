// Port of the original WeaponLook (New Era NE_Scripts_Decrypted/WeaponLook.cs; Penta 2016 has the same fields). Same class name,
// fields and script GUID (b47c18c4ff83b5838525f6131ad28c68), so the eleven type nodes of Fighter_locomotion/Fighter/MainCamera/Weapon
// (OneHandedColdArmsWeapon … BombLauncherWeapon) keep their data. Update() is empty in the original: the component is only the marker
// that ShotController.InitWeapon looks for (NE ShotController.cs:1029-1036).
using UnityEngine;

[AddComponentMenu("Weapon Control/WeaponLook")]
public class WeaponLook : MonoBehaviour
{
    public float sensitivityX = 200f;
    public float sensitivityY = 200f;
    public float minimumX = -360f;
    public float maximumX = 360f;
    public float minimumY = -13f;
    public float maximumY = 25f;
    public bool Active;
}
