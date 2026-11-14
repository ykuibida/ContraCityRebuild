// New Era BattleGUICamera (decrypted client code, unchanged).
using UnityEngine;

public class BattleGUICamera : MonoBehaviour
{
	private Transform health;

	private Camera guiCamera;

	private Vector3 healthPos = new Vector3(60f, 53f, 1f);

	private Transform armor;

	private Vector3 armorPos = new Vector3(197f, 53f, 1f);

	private Transform ammo;

	private Vector3 ammoPos = new Vector3(10f, 53f, 1f);

	private Transform impact;

	private Vector3 impactPos = new Vector3(10f, 125f, 1f);

	private Transform overlay;

	public Vector3 overlayPos = new Vector3(1f, 1f, 1f);

	private Transform killstreak;

	public Vector3 killstreakPos = new Vector3(-17f, 558f, 1f);

	private void Start()
	{
		guiCamera = GetComponent<Camera>();
		health = base.transform.parent.Find("Health");
		armor = base.transform.parent.Find("Armor");
		ammo = base.transform.parent.Find("Ammo");
		impact = base.transform.parent.Find("Impact");
		overlay = base.transform.parent.Find("Overlay");
		killstreak = base.transform.parent.Find("KillStreak");
	}

	private void Update()
	{
		if (guiCamera == null)
		{
			guiCamera = GetComponent<Camera>();
		}
		if (guiCamera == null)
		{
			return;
		}
		Rect pixelRect = guiCamera.pixelRect;
		if (!(pixelRect.width <= 1f) && !(pixelRect.height <= 1f))
		{
			guiCamera.orthographicSize = pixelRect.height;
			float xMin = pixelRect.xMin;
			float yMin = pixelRect.yMin;
			float xMax = pixelRect.xMax;
			float yMax = pixelRect.yMax;
			healthPos.x = xMin + 60f;
			healthPos.y = yMin + 53f;
			armorPos.x = xMin + 197f;
			armorPos.y = yMin + 53f;
			ammoPos.x = xMax - 77f;
			ammoPos.y = yMin + 53f;
			impactPos.x = xMax - 40f;
			impactPos.y = yMin + 125f;
			killstreakPos.x = xMin + pixelRect.width * 0.5f - 40f;
			killstreakPos.y = yMax - 120f;
			if (health != null)
			{
				health.position = guiCamera.ScreenToWorldPoint(healthPos);
			}
			if (armor != null)
			{
				armor.position = guiCamera.ScreenToWorldPoint(armorPos);
			}
			if (ammo != null)
			{
				ammo.position = guiCamera.ScreenToWorldPoint(ammoPos);
			}
			if (impact != null)
			{
				impact.position = guiCamera.ScreenToWorldPoint(impactPos);
			}
			if (killstreak != null)
			{
				killstreak.position = guiCamera.ScreenToWorldPoint(killstreakPos);
			}
			overlayPos.x = xMin + pixelRect.width * 0.5f;
			overlayPos.y = yMin + pixelRect.height * 0.5f;
			if (overlay != null)
			{
				overlay.position = guiCamera.ScreenToWorldPoint(overlayPos);
				Transform transform = overlay;
				Vector3 localScale = overlay.localScale;
				transform.localScale = new Vector3(pixelRect.width, localScale.y, pixelRect.height);
			}
		}
	}
}
