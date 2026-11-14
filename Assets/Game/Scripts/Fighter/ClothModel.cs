// Data stand-in for the original ClothModel (same class name, fields and script GUID), so the clothing
// objects of Enemy_Fighter_locomotion keep their slot data. WearType: 1 Hats .. 9 Heads.
using UnityEngine;

public class ClothModel : MonoBehaviour
{
    public int WearType;
    public bool Default;
}
