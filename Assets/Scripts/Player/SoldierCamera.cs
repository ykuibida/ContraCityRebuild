// First-person mouse look of the New Era SoldierCamera (NE_Scripts_Decrypted/SoldierCamera.cs:334-432; Penta 2016 = reference):
// Neutral / Normal / Smooth modes, invert Y, aim sensitivity blended in while aiming (or zoomed), body yaw, camera pitch.
using System.Collections.Generic;
using UnityEngine;

public class SoldierCamera : MonoBehaviour
{
    public float yMinLimit = -90f, yMaxLimit = 90f;
    public float minimumX = -360f, maximumX = 360f;
    public float zoomFactor = 1f;
    public int frameCounter = 20;

    float x, y;
    float rotationX, rotationY;
    readonly List<float> rotArrayX = new List<float>(), rotArrayY = new List<float>();
    FPSCamera fps;
    SoldierController soldier;
    public float aimTransitionSpeed = 15f; // contra.city: blend to aim sensitivity while aiming
    float aimBlend;

    void Start()
    {
        GameOptions.Load();
        fps = GetComponentInChildren<FPSCamera>();
        soldier = GetComponent<SoldierController>();
        x = rotationX = transform.eulerAngles.y;
    }

    public void SetYaw(float yaw) { x = rotationX = yaw; y = rotationY = 0f; rotArrayX.Clear(); rotArrayY.Clear(); }
    /// <summary>Test hook (-uicompare): look up / down by <paramref name="pitch"/> degrees.</summary>
    public void SetPitch(float pitch) { y = rotationY = pitch; rotArrayY.Clear(); }

    void Update()
    {
        // (no cursor-lock gate: neither Penta nor New Era has one — BattleUI disables this component while a menu is open)
        aimBlend = Mathf.Lerp(aimBlend, soldier && soldier.aim ? 1f : 0f, Time.deltaTime * aimTransitionSpeed);   // NE UpdateSmoothAimBlend (sniper zoom too, later)
        switch (GameOptions.MouseMode)
        {
            case CameraOrbitMode.Normal: GetInputNormal(); break;
            case CameraOrbitMode.Smooth: GetInputSmooth(); break;
            default: GetInputNeutral(); break;
        }
        transform.localRotation = Quaternion.Euler(transform.localEulerAngles.x, x, transform.localEulerAngles.z);
        if (fps) fps.FPSRotation = new Vector3(y, 0f, 0f);
    }

    float MouseX => Input.GetAxis("Mouse X");
    float MouseY => Input.GetAxis("Mouse Y") * (GameOptions.InvertMouseY ? -1f : 1f);
    /// <summary>喞.MouseSensityX × Lerp(1, AimSensitivityRatio, blend); ratio = AimSensitivity / Sensivity clamped 0.01..100.</summary>
    float Sens => GameOptions.MouseSensityX * Mathf.Lerp(1f, GameOptions.Sensivity <= 0.0001f ? 1f : Mathf.Clamp(GameOptions.AimSensitivity / GameOptions.Sensivity, 0.01f, 100f), aimBlend);

    void GetInputNormal()   // NE :334-342: 0.004, no deltaTime
    {
        x += MouseX * 0.004f * Sens * zoomFactor;
        y -= MouseY * 0.004f * Sens * zoomFactor;
        y = ClampAngle(y, yMinLimit, yMaxLimit);
    }

    void GetInputNeutral()
    {
        const float k = 0.02f;
        x += MouseX * Sens * k * zoomFactor;
        y -= MouseY * Sens * k * zoomFactor;
        y = ClampAngle(y, yMinLimit, yMaxLimit);
    }

    void GetInputSmooth()
    {
        const float k = 0.01f;
        rotationY += MouseY * Sens * k * zoomFactor;
        rotationX += MouseX * Sens * k * zoomFactor;
        rotArrayY.Add(rotationY); rotArrayX.Add(rotationX);
        if (rotArrayY.Count >= frameCounter) rotArrayY.RemoveAt(0);
        if (rotArrayX.Count >= frameCounter) rotArrayX.RemoveAt(0);
        float ay = 0f, ax = 0f;
        foreach (var v in rotArrayY) ay += v;
        foreach (var v in rotArrayX) ax += v;
        ay = ClampAngle(ay / rotArrayY.Count, yMinLimit, yMaxLimit);
        ax = ClampAngle(ax / rotArrayX.Count, minimumX, maximumX);
        x = ax; y = -ay;
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f) angle += 360f;
        if (angle > 360f) angle -= 360f;
        return Mathf.Clamp(angle, min, max);
    }
}
