// CharacterManager.GetPrefab (New Era NE_Scripts_Decrypted/噩.cs:219-222): the "CharacterData" bundle's GrenadePrefab, SnowballPrefab,
// BombPrefab, ExploSnowPrefab and CrossbowBoltPrefab by name. Here the lobby scene keeps them on a "CharacterPrefabs" object (built by
// BuildGame from gamebundle/prefabs/soldier), kept across the battle scene loads.
using UnityEngine;

public class CharacterPrefabs : MonoBehaviour
{
    public GameObject[] Prefabs = new GameObject[0];
    static CharacterPrefabs instance;

    /// <summary>Keeps the lobby object (call while the lobby scene is loaded).</summary>
    public static void Keep()
    {
        if (instance) return;
        var go = GameObject.Find("CharacterPrefabs");
        if (!go) return;
        instance = go.GetComponent<CharacterPrefabs>();
        DontDestroyOnLoad(go);
    }

    public static GameObject GetPrefab(string a)
    {
        Keep();
        if (!instance) return null;
        foreach (var p in instance.Prefabs) if (p != null && p.name == a) return p;
        return null;
    }
}
