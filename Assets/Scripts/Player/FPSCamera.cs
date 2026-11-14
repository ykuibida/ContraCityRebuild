// Port of the original FPSCamera (eye height + shake) — Penta 2016 FPSCamera.cs:115-287 = New Era FPSCamera.cs:6-317.
using UnityEngine;

/// <summary>Eye-height lerp (stand 6.5 / crouch 4.7 / crouch-walk 5.2) and camera shake from the original FPSCamera.</summary>
public class FPSCamera : MonoBehaviour
{
    public float CroachHeight = 4.7f, CroachWalkHeight = 5.2f, NormalHeight = 6.5f;
    public float Rapidity = 12f;
    float targetHeight = 6.5f, error = 0.05f;
    bool croach, walk, moving;
    float shakeThreshold = 0.01f, shakeAccMultiplier = 0.1f;
    Vector3 fpsRotation, shakeRotation;
    bool shaking;

    public bool Croach { get => croach; set { if (croach != value) { croach = value; RefreshState(); } } }
    public bool Walk { get => walk; set { if (walk != value) { walk = value; RefreshState(); } } }
    public Vector3 FPSRotation
    {
        get => fpsRotation;
        set { fpsRotation = value; transform.localEulerAngles = fpsRotation + shakeRotation; }
    }

    public void Shake(Vector3 v, bool additional = false) { shakeRotation = additional ? shakeRotation + v : v; shaking = true; }

    void RefreshState()
    {
        targetHeight = croach && walk ? CroachWalkHeight : croach ? CroachHeight : NormalHeight;
        moving = true;
    }

    // ---------------- zoom (NE FPSCamera.cs:24-70, 275-301, 325-397) ----------------
    public float zoomFactor = 1f;
    public bool isZooming;
    float SniperZoomFactor => ShotController.Instance != null && ShotController.Instance.CurrentWeapon != null ? ShotController.Instance.GetSniperZoomFactor() : 1f;

    /// <summary>GetBaseMainFov (NE :333-345): the field-of-view option (zombies × 1.5), clamped 40..110.</summary>
    static float GetBaseMainFov() => Mathf.Clamp(GameOptions.FpsFieldOfView, 40f, 110f);
    float GetWeaponLayerFov() => 60f * zoomFactor;   // NE :347-350

    /// <summary>ApplyFieldOfView (NE :352-376): the weapon camera is off while zoomed with a factor under 0.5; eye FOV = base × factor,
    /// weapon camera 60 × factor, the mouse sensitivity (SoldierCamera.zoomFactor) follows the factor.</summary>
    public void ApplyFieldOfView()
    {
        var shots = ShotController.Instance;
        var main = GetComponent<Camera>();
        var wc = shots != null ? shots.WeaponCamera : null;
        if (main == null || wc == null || shots == null) return;
        bool z = shots.Zoom;
        if ((SniperZoomFactor < 0.5f || !z) && wc.gameObject.activeSelf == z) wc.gameObject.SetActive(!z);
        wc.fieldOfView = GetWeaponLayerFov();
        var sc = transform.parent ? transform.parent.GetComponent<SoldierCamera>() : null;
        if (sc != null) sc.zoomFactor = zoomFactor;
        main.fieldOfView = GetBaseMainFov() * zoomFactor;
    }

    void Zoom(bool a)   // NE :378-397
    {
        if (a) { if (zoomFactor != SniperZoomFactor) zoomFactor = SniperZoomFactor; }
        else { if (zoomFactor == 1f) return; zoomFactor = 1f; }
        ShotController.Instance.Zoom = a;
        ApplyFieldOfView();
    }

    /// <summary>NE :325-331. Our eye camera stays active while the TPS camera shows (New Era switched its GameObject off), so an
    /// inactive eye camera skips this like the original's inactive object.</summary>
    void LateUpdate() { if (ShotController.Instance != null && GetComponent<Camera>().enabled) ApplyFieldOfView(); }

    void Update()
    {
        if (shaking)
        {
            float step = shakeRotation.magnitude * shakeAccMultiplier * Time.deltaTime * 50f;
            Vector3 rest = shakeRotation - shakeRotation.normalized * step;
            if (rest.magnitude <= step || rest.magnitude < shakeThreshold) { shakeRotation = Vector3.zero; shaking = false; }
            else shakeRotation = rest;
        }
        var shots = ShotController.Instance;   // NE :279-301 — the Zoom key toggles; blocked (reload) drops the zoom, ready again restores it
        if (shots != null && shots.CurrentWeapon != null && GetComponent<Camera>().enabled)
        {
            if (shots.zoomAllowed())
            {
                if (Input.GetKeyDown(TRInput.Zoom)) { Zoom(!isZooming); isZooming = !isZooming; }
                if (shots.IsBlocked) { if (isZooming) Zoom(false); }
                else if (isZooming) Zoom(true);
            }
            else if (isZooming) { Zoom(false); isZooming = false; }
        }
        if (!moving) return;
        float yPos = transform.localPosition.y;
        float d = (targetHeight - yPos) * Rapidity * Time.deltaTime;
        if (Mathf.Abs(d) < error) d = error * Mathf.Sign(targetHeight - yPos);
        yPos += d;
        // NE :312-317 / Penta :279-287: on arrival only `moving` is cleared — the snapped value is NOT written (the eye rests just short)
        if (Mathf.Abs(yPos - targetHeight) <= error) { moving = false; return; }
        transform.localPosition = new Vector3(transform.localPosition.x, yPos, transform.localPosition.z);
    }
}
