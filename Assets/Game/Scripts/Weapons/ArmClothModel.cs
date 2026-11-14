// Port of the New Era ArmClothModel (NE_Scripts_Decrypted/ArmClothModel.cs; New Era only, Penta has no first-person arm clothes).
// Same class name, fields and script GUID (4380021096a0a7478c7a2377f720d725): 9196 arm meshes "Shirts_<name>" / "Gloves_<name>" inside
// every first-person weapon model of Fighter_locomotion. Sync (called by PlayerCustomisator.SetDefaultCloth / SetCloth for Shirts and
// Gloves, NE PlayerCustomisator.cs:255, 303) shows the arm mesh whose name token [1] equals the worn shirt / glove name token [1]
// and hides its base mesh (Shirts_torso01 / Gloves_hands01, or BaseOverride); a base is shown when no worn arm covers it.
// Our weapon container is under the eye camera, not under the soldier's PlayerCustomisator, so Sync takes the root and the worn names.
using System;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Character/Arm Cloth Model")]
public class ArmClothModel : MonoBehaviour
{
    public enum ArmWearType { Shirts = 0, Gloves = 1 }

    public ArmWearType WearType;
    public string BaseShirtName = "Shirts_torso01";
    public string BaseGloveName = "Gloves_hands01";
    public GameObject[] BaseOverride;

    static readonly char[] Separator = { '_' };
    static bool syncing;
    string systemName = string.Empty;
    bool nameParsed;
    GameObject[] baseObjects;

    public string SystemName
    {
        get
        {
            if (!nameParsed)
            {
                nameParsed = true;
                systemName = ParseSystemName(gameObject.name);
                if (systemName.Length == 0) Debug.LogWarning("[ArmClothModel] Broken arm name (expected Shirts_xxx or Gloves_xxx): " + gameObject.name, this);
            }
            return systemName;
        }
    }

    public ArmWearType EffectiveType
    {
        get
        {
            string text = gameObject.name;
            if (text.StartsWith("Shirts_", StringComparison.Ordinal)) return ArmWearType.Shirts;
            if (text.StartsWith("Gloves_", StringComparison.Ordinal)) return ArmWearType.Gloves;
            return WearType;
        }
    }

    /// <summary>ArmClothModel.Sync (NE :76-130) over every arm model under <paramref name="root"/>; <paramref name="wornShirt"/> /
    /// <paramref name="wornGlove"/> = PlayerCustomisator.CurrentClothes[Shirts / Gloves] (model names like "Shirts_thanos").</summary>
    public static void Sync(Transform root, string wornShirt, string wornGlove)
    {
        if (root == null || syncing) return;
        var arms = root.GetComponentsInChildren<ArmClothModel>(true);
        if (arms.Length == 0) return;
        syncing = true;
        try
        {
            string shirt = ParseSystemName(wornShirt), glove = ParseSystemName(wornGlove);
            var bases = new List<GameObject>();
            var covered = new List<GameObject>();
            foreach (var arm in arms)
            {
                if (arm == null) continue;
                string want = arm.EffectiveType != ArmWearType.Shirts ? glove : shirt;
                string own = arm.SystemName;
                bool on = want.Length > 0 && own.Length > 0 && own == want;
                if (arm.gameObject.activeSelf != on) arm.gameObject.SetActive(on);
                foreach (var b in arm.GetBases())
                {
                    if (b == null) continue;
                    if (!bases.Contains(b)) bases.Add(b);
                    if (on && !covered.Contains(b)) covered.Add(b);
                }
            }
            foreach (var b in bases)
            {
                bool show = !covered.Contains(b);
                if (b.activeSelf != show) b.SetActive(show);
            }
        }
        finally { syncing = false; }
    }

    GameObject[] GetBases()
    {
        if (baseObjects != null) return baseObjects;
        var list = new List<GameObject>();
        if (BaseOverride != null)
            foreach (var o in BaseOverride) if (o != null && o != gameObject) list.Add(o);
        if (list.Count == 0)
        {
            string name = EffectiveType != ArmWearType.Shirts ? BaseGloveName : BaseShirtName;
            var parent = transform.parent;
            if (parent != null && !string.IsNullOrEmpty(name))
            {
                var t = parent.Find(name);
                if (t == null)
                    foreach (var c in parent.GetComponentsInChildren<Transform>(true))
                        if (c != transform && c.name == name) { t = c; break; }
                if (t == null) Debug.LogWarning("[ArmClothModel] Base arm '" + name + "' not found near '" + gameObject.name + "', both will be visible", this);
                else if (t.GetComponent<ArmClothModel>() == null) list.Add(t.gameObject);
            }
        }
        baseObjects = list.ToArray();
        return baseObjects;
    }

    static string ParseSystemName(string a)
    {
        if (string.IsNullOrEmpty(a)) return string.Empty;
        var parts = a.Split(Separator);
        if (parts.Length < 2) return string.Empty;
        string text = parts[1];
        int i = text.IndexOf(" (", StringComparison.Ordinal);
        if (i > 0) text = text.Substring(0, i);
        return text;
    }
}
