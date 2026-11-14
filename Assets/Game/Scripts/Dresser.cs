// Fighter for the lobby preview, dressed exactly like New Era / Penta PlayerCustomisator.SetCloth
// (research: Catalog/ne_wear_dressing.md):
//  - model name = "<Slot>_" + (model_sname without its "<Slot>_" prefix, else sname)
//  - in a slot, every ClothModel whose name.Split('_')[1] equals the model name's second token is shown (incl. "_eNN" parts), the rest hidden
//  - empty slot -> PlayerCustomisator default; any hat hides "*_hair" heads; a mask ending in "_H" hides the Heads slot
//  - texture: Resources "Textures/Cloth/<RuntimeTextureLoader.textureID>" on a per-renderer material copy
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Dresser
{
    public static readonly string[] SlotNames = { "", "Hats", "Masks", "Gloves", "Shirts", "Pants", "Boots", "Backpacks", "Others", "Heads" };

    static GameObject cachedTemplate;
    /// <summary>The fighter template of the lobby scene, kept (inactive, DontDestroyOnLoad) so battles can build the soldier too.</summary>
    static GameObject Template()
    {
        if (cachedTemplate) return cachedTemplate;
        var holder = GameObject.Find("FighterTemplate");
        if (!holder || holder.transform.childCount == 0) return null;
        var keep = new GameObject("_FighterTemplateKeep");
        Object.DontDestroyOnLoad(keep);
        keep.SetActive(false);
        cachedTemplate = Object.Instantiate(holder.transform.GetChild(0).gameObject, keep.transform, false);
        cachedTemplate.name = "FighterTemplateCopy";
        return cachedTemplate;
    }

    public static GameObject CreateFighter(Transform parent)
    {
        var src = Template();
        if (!src) return null;
        var go = Object.Instantiate(src, parent, false);
        go.name = "Fighter";
        go.SetActive(true);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) r.isKinematic = true;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        // Hide everything that is not body/clothing: faction sign, nickname plate, spawn shield, weapons, effects.
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (!r.GetComponentInParent<ClothModel>(true)) r.enabled = false;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        var anim = go.GetComponentInChildren<Animation>();
        if (anim)
        {
            // The menu soldier plays the Animation's default clip (prefab: IdleLong, playAutomatically) — nothing in
            // CharacterCameraManager / ActorAnimator switches it in the lobby (checked against the New Era home screenshot).
            anim.wrapMode = WrapMode.Loop;
            if (anim.clip != null) anim.Play(anim.clip.name);
            else if (anim["IdleLong"] != null) anim.Play("IdleLong");
        }
        return go;
    }

    public static string ModelSystemName(GameItem it)
    {
        string slot = SlotNames[Mathf.Clamp(it.Wt, 0, 9)];
        string text = it.Str("model_sname");
        if (text != "") { if (text.StartsWith(slot + "_")) text = text.Substring(slot.Length + 1); }
        else text = it.SName;
        return slot + "_" + text;
    }

    static string Token(string s, int i) { var p = s.Split('_'); return p.Length > i ? p[i] : ""; }

    /// <summary>PlayerCustomisator.DefaultClothes: the fighter's default model name per slot (1 Hats … 9 Heads).</summary>
    public static string[] DefaultClothes(GameObject fighter)
    {
        var pc = fighter ? fighter.GetComponentInChildren<PlayerCustomisator>(true) : null;
        var defaults = new string[10];
        if (pc) { defaults[1] = pc.Hat; defaults[2] = pc.Mask; defaults[3] = pc.Glove; defaults[4] = pc.Shirt; defaults[5] = pc.Pant; defaults[6] = pc.Boot; defaults[7] = pc.Backpack; defaults[8] = pc.Other; defaults[9] = pc.Head; }
        for (int s = 1; s <= 9; s++) defaults[s] = defaults[s] ?? "";
        return defaults;
    }

    /// <summary>PlayerCustomisator.CurrentClothes: the worn model name per slot, the default one for an empty slot.</summary>
    public static string[] CurrentClothes(GameObject fighter, Dictionary<int, GameItem> outfit)
    {
        var current = DefaultClothes(fighter);
        for (int s = 1; s <= 9; s++) if (outfit.TryGetValue(s, out var it) && it != null) current[s] = ModelSystemName(it);
        return current;
    }

    /// <summary>The texture the first-person hands get (PlayerCustomisator.SetHandTextureID, NE :308-322): CombatPlayer.InitWear first
    /// resets the gloves (SetCloth(Gloves, "") → the default glove model), then puts on the worn glove (NE CombatPlayer.cs:1561-1578);
    /// every shown glove model passes its RuntimeTextureLoader.textureID unless it is a Thanos / santa / mummy / business / stalker
    /// texture, the last one wins. "" = the hands keep the prefab texture.</summary>
    public static string HandTextureID(GameObject fighter, Dictionary<int, GameItem> outfit)
    {
        string hand = "";
        if (!fighter) return hand;
        var names = new List<string> { DefaultClothes(fighter)[3] };
        if (outfit.TryGetValue(3, out var g) && g != null) names.Add(ModelSystemName(g));
        var models = fighter.GetComponentsInChildren<ClothModel>(true);
        foreach (var want in names)
        {
            if (want == "") continue;
            foreach (var cm in models)
            {
                if (cm.WearType != 3 || Token(cm.gameObject.name, 1) != Token(want, 1)) continue;
                var rtl = cm.GetComponent<RuntimeTextureLoader>();
                if (rtl == null) continue;
                string id = rtl.textureID ?? "";
                if (!id.Contains("CLOTH_Gloves_Thanos") && !id.Contains("santa") && id != "CLOTH_shirt_mummy_Color" && !id.Contains("business") && !id.Contains("stalker"))
                    hand = id;
            }
        }
        return hand;
    }

    public static void Dress(GameObject fighter, Dictionary<int, GameItem> outfit)
    {
        if (!fighter) return;
        var current = CurrentClothes(fighter, outfit);
        bool hatOn = outfit.ContainsKey(1);
        bool maskHidesHead = Token(current[2], 2) == "H";

        foreach (var cm in fighter.GetComponentsInChildren<ClothModel>(true))
        {
            int s = cm.WearType;
            if (s < 1 || s > 9) continue;
            string want = current[s];
            bool on = want != "" && Token(cm.gameObject.name, 1) == Token(want, 1);
            if (s == 9 && hatOn && Token(cm.gameObject.name, 2) == "hair") on = false;
            if (s == 9 && maskHidesHead) on = false;
            cm.gameObject.SetActive(on);
            var r = cm.GetComponent<Renderer>(); if (r) r.enabled = on;
            if (on) ApplyTexture(cm.gameObject);
        }
    }

    static void ApplyTexture(GameObject go)
    {
        var rtl = go.GetComponent<RuntimeTextureLoader>();
        var r = go.GetComponent<Renderer>();
        if (!rtl || !r || string.IsNullOrEmpty(rtl.textureID)) return;
        var tex = Resources.Load<Texture2D>("Textures/Cloth/" + rtl.textureID);
        if (!tex) return;
        var mats = r.materials;   // per-renderer copies: shared materials would recolour other items
        int i = rtl.materialID > 0 ? rtl.materialID - 1 : 0;
        if (i < mats.Length && mats[i]) mats[i].SetTexture("_MainTex", tex);
        r.materials = mats;
    }
}
