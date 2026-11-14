// New Era BattleGUIImpact (decrypted client code; enum renamed): state icons Fire/Blood/Poison/Electro/Frost/Biohazard/Stunning.
using System;
using System.Collections.Generic;
using UnityEngine;

public class BattleGUIImpact : MonoBehaviour
{
	public enum ImpactType
	{
		Fire = 1,
		Blood = 2,
		Poison = 3,
		Electro = 4,
		Frost = 5,
		Biohazard = 6,
		Stunning = 7,
		Slow = 8   // rebuild: «Гранатин»'s slow, icon battlestate_slow shown in the Electro slot (see AddSlowIcon)
	}

	private Transform state;

	private Dictionary<ImpactType, Transform> transformList = new Dictionary<ImpactType, Transform>();

	private Dictionary<ImpactType, MeshRenderer> renderList = new Dictionary<ImpactType, MeshRenderer>();

	private Dictionary<ImpactType, Animation> animationList = new Dictionary<ImpactType, Animation>();

	private Dictionary<ImpactType, float> offsetDisable = new Dictionary<ImpactType, float>();

	private Transform fire;

	private MeshRenderer fireRender;

	private Animation fireAnimator;

	private Transform blood;

	private MeshRenderer bloodRender;

	private Animation bloodAnimator;

	private Transform poison;

	private MeshRenderer poisonRender;

	private Animation poisonAnimator;

	private bool isInit;

	private void Awake()
	{
		Init();
		HideAll();
	}

	private void Start()
	{
		Init();
		HideAll();
	}

	private void Update()
	{
	}

	private void FixedUpdate()
	{
		Init();
		foreach (KeyValuePair<ImpactType, MeshRenderer> render in renderList)
		{
			if (render.Value != null && render.Value.enabled && offsetDisable[render.Key] < Time.time)
			{
				Hide(render.Key);
			}
		}
	}

	private void Init()
	{
		if (isInit)
		{
			return;
		}
		foreach (int value in Enum.GetValues(typeof(ImpactType)))
		{
			ImpactType key = (ImpactType)value;
			Transform transform = base.transform.Find(key.ToString());
			if (!(transform == null))
			{
				MeshRenderer component = transform.GetComponent<MeshRenderer>();
				Animation componentInChildren = transform.GetComponentInChildren<Animation>();
				transformList[key] = transform;
				renderList[key] = component;
				animationList[key] = componentInChildren;
				offsetDisable[key] = 0f;
				if (component != null)
				{
					component.enabled = false;
				}
			}
		}
		isInit = true;
		AddSlowIcon();
	}

	/// <summary>Rebuild: the prefab has no slow icon; a copy of the Electro icon (unused in the rebuild) with gui/battle/icons/battlestate_slow.</summary>
	private void AddSlowIcon()
	{
		if (renderList.ContainsKey(ImpactType.Slow) || !transformList.TryGetValue(ImpactType.Electro, out var electro) || electro == null) return;
		var tex = Resources.Load<Texture2D>("gui/battle/icons/battlestate_slow");
		if (tex == null) return;
		var go = UnityEngine.Object.Instantiate(electro.gameObject, electro.parent, false);
		go.name = "Slow";
		var r = go.GetComponent<MeshRenderer>();
		if (r != null) { r.material = new Material(r.sharedMaterial); r.material.mainTexture = tex; r.enabled = false; }
		transformList[ImpactType.Slow] = go.transform;
		renderList[ImpactType.Slow] = r;
		animationList[ImpactType.Slow] = go.GetComponentInChildren<Animation>();
		offsetDisable[ImpactType.Slow] = 0f;
	}

	public void Show(ImpactType a, float b)
	{
		Init();
		MeshRenderer value;
		Transform value2;
		if (!renderList.TryGetValue(a, out value) || !transformList.TryGetValue(a, out value2) || value == null)
		{
			return;
		}
		Animation value3;
		animationList.TryGetValue(a, out value3);
		if (!value.enabled)
		{
			value.enabled = true;
			Show(value2);
			if (value3 != null && value3["Transparency"] != null)
			{
				value3["Transparency"].speed = 0.3f;
				value3["Transparency"].time = 0f;
				value3.Play("Transparency");
			}
		}
		offsetDisable[a] = ((b == 0f) ? 0f : (Time.time + b));
	}

	public void Hide(ImpactType a)
	{
		Init();
		MeshRenderer value;
		if (renderList.TryGetValue(a, out value) && value != null)
		{
			value.enabled = false;
		}
		Animation value2;
		if (animationList.TryGetValue(a, out value2) && value2 != null && value2["Transparency"] != null)
		{
			value2["Transparency"].speed = 0f;
			value2["Transparency"].time = 0f;
			value2.Stop("Transparency");
		}
		offsetDisable[a] = 0f;
	}

	public void HideAll()
	{
		Init();
		ImpactType[] array = (ImpactType[])Enum.GetValues(typeof(ImpactType));
		for (int i = 0; i < array.Length; i++)
		{
			Hide(array[i]);
		}
	}

	private void OnDisable()
	{
		HideAll();
	}

	private void OnEnable()
	{
		Init();
		HideAll();
	}

	private void Hide(Transform a)
	{
	}

	private void Show(Transform a)
	{
	}
}
