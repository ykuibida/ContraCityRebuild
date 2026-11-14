// New Era KillStreak (decrypted client code, unchanged).
using UnityEngine;

public class KillStreak : MonoBehaviour
{
	public TextMesh Text;

	public Transform BG;

	public string TextMessage = string.Empty;

	public float BG_Size = 0.5f;

	public float BG_Shift = 50f;

	private Vector3 targetBGSize = new Vector3(200f, 26f, 70f);

	private Vector3 targetTextSize = new Vector3(26f, 26f, 0f);

	private float TimeShow = 2.5f;

	private float FadeOutTime = 0.5f;

	private float targetTime;

	private bool fading;

	private static readonly Color WhiteTextColor = new Color(1f, 1f, 1f, 1f);

	public void Show(string a, bool b)
	{
		if (Text == null)
		{
			Debug.LogWarning("[KillStreak] Text is null. Message skipped: " + a);
			return;
		}
		base.gameObject.SetActive(true);
		Text.text = a;
		Text.transform.localScale = targetTextSize;
		SetTextColor(WhiteTextColor, 1f);
		Renderer component = Text.GetComponent<Renderer>();
		Bounds bounds = ((!(component != null)) ? new Bounds(Vector3.zero, new Vector3(100f, 26f, 1f)) : component.bounds);
		float num = Mathf.Max(100f, bounds.max.x - bounds.min.x);
		targetBGSize = new Vector3(num * BG_Size + BG_Shift, 26f, 70f);
		if (BG != null)
		{
			BG.localScale = targetBGSize;
			Renderer renderer = BG.GetComponent<Renderer>();
			if (renderer == null)
			{
				renderer = BG.GetComponentInChildren<Renderer>();
			}
			SetRendererAlpha(renderer, 1f);
		}
		targetTime = Time.time + TimeShow;
		fading = false;
	}

	private void Update()
	{
		if (!base.gameObject.activeSelf || Time.time <= targetTime)
		{
			return;
		}
		float num = 1f - Mathf.Clamp01((Time.time - targetTime) / Mathf.Max(0.01f, FadeOutTime));
		SetTextAlpha(num);
		if (BG != null)
		{
			Renderer renderer = BG.GetComponent<Renderer>();
			if (renderer == null)
			{
				renderer = BG.GetComponentInChildren<Renderer>();
			}
			SetRendererAlpha(renderer, num);
		}
		if (num <= 0f && !fading)
		{
			fading = true;
			base.gameObject.SetActive(false);
		}
	}

	private void SetTextColor(Color a, float b)
	{
		a.a = b;
		Text.color = a;
		Renderer component = Text.GetComponent<Renderer>();
		SetRendererColor(component, a);
	}

	private void SetTextAlpha(float a)
	{
		if (!(Text == null))
		{
			Color color = Text.color;
			color.a = a;
			Text.color = color;
			Renderer component = Text.GetComponent<Renderer>();
			SetRendererAlpha(component, a);
		}
	}

	private static void SetRendererAlpha(Renderer a, float b)
	{
		if (!(a == null) && !(a.material == null))
		{
			Material material = a.material;
			if (material.HasProperty("_Color"))
			{
				Color color = material.color;
				color.a = b;
				material.color = color;
			}
			if (material.HasProperty("_TintColor"))
			{
				Color color2 = material.GetColor("_TintColor");
				color2.a = b;
				material.SetColor("_TintColor", color2);
			}
		}
	}

	private static void SetRendererColor(Renderer a, Color b)
	{
		if (!(a == null) && !(a.material == null))
		{
			Material material = a.material;
			if (material.HasProperty("_Color"))
			{
				material.color = b;
			}
			if (material.HasProperty("_TintColor"))
			{
				material.SetColor("_TintColor", b);
			}
		}
	}
}
