// New Era BattleStatsProgress (decrypted client code, unchanged).
using UnityEngine;

public class BattleStatsProgress : MonoBehaviour
{
	private Material material;

	private void Start()
	{
		material = GetComponent<MeshRenderer>().material;
	}

	private void Update()
	{
	}

	public void SetValue(float a)
	{
		if (material != null)
		{
			material.mainTextureOffset = Vector2.Lerp(new Vector2(0f, 0f), new Vector2(0f, -1f), a);
			base.transform.localPosition = Vector3.Lerp(new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.1f, 760f), a);
		}
		else
		{
			Debug.LogError("[BattleStatsProgress] material is null");
		}
	}
}
