// Free camera: hold right mouse button to look around, WASD to move, Q/E down/up, Shift to go faster.
// Number keys 1-9 switch the animation of every fighter in the scene.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FlyCamera : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float lookSpeed = 0.15f;

    static readonly string[] AnimKeys = { "Idle", "IdleLong", "Walk", "Run2", "Jump", "Taunt1", "Taunt3", "CroachIdle", "Zombie_Run" };

    float yaw, pitch;

    void Start()
    {
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
        Vector2 mouseDelta; bool looking; Vector3 move = Vector3.zero; bool fast; int animKey = -1;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; var mouse = Mouse.current;
        if (kb == null || mouse == null) return;
        looking = mouse.rightButton.isPressed;
        mouseDelta = mouse.delta.ReadValue();
        if (kb.wKey.isPressed) move.z += 1; if (kb.sKey.isPressed) move.z -= 1;
        if (kb.dKey.isPressed) move.x += 1; if (kb.aKey.isPressed) move.x -= 1;
        if (kb.eKey.isPressed) move.y += 1; if (kb.qKey.isPressed) move.y -= 1;
        fast = kb.leftShiftKey.isPressed;
        for (int i = 0; i < AnimKeys.Length; i++)
            if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) animKey = i;
#else
        looking = Input.GetMouseButton(1);
        mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
        if (Input.GetKey(KeyCode.W)) move.z += 1; if (Input.GetKey(KeyCode.S)) move.z -= 1;
        if (Input.GetKey(KeyCode.D)) move.x += 1; if (Input.GetKey(KeyCode.A)) move.x -= 1;
        if (Input.GetKey(KeyCode.E)) move.y += 1; if (Input.GetKey(KeyCode.Q)) move.y -= 1;
        fast = Input.GetKey(KeyCode.LeftShift);
        for (int i = 0; i < AnimKeys.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) animKey = i;
#endif

        if (looking)
        {
            yaw += mouseDelta.x * lookSpeed;
            pitch = Mathf.Clamp(pitch - mouseDelta.y * lookSpeed, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        transform.position += transform.TransformDirection(move) * moveSpeed * (fast ? 4f : 1f) * Time.deltaTime;

        if (animKey >= 0)
            foreach (var anim in FindObjectsByType<Animation>(FindObjectsSortMode.None))
                if (anim.GetClip(AnimKeys[animKey]) != null) anim.CrossFade(AnimKeys[animKey], 0.2f);
    }
}
