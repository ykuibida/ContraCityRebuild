// Port of the original Tracer (New Era NE_Scripts_Decrypted/Tracer.cs; Penta 2016 Tracer.cs). Same class name, fields and script GUID
// (cfceafe3d9facc779a05478759cefed9): the bullet trail prefab "MachineGun". Setting Target gives the step Speed = direction × flatSpeed
// (per FixedUpdate); too close a target (< 5 × flatSpeed²) destroys the tracer at once. Start resets flatSpeed to 1 (only the stop
// distance uses it then, as in the original). The tracer moves Speed every FixedUpdate and is destroyed when it is within flatSpeed of
// the target or starts moving away from it.
using UnityEngine;

public class Tracer : MonoBehaviour
{
    public Vector3 target = Vector3.zero;
    TrailRenderer trail;
    public float flatSpeed = 150f;
    public Vector3 Speed = Vector3.zero;
    float flatSpeedSqr = 300f;
    float lastDist;

    public Vector3 Target
    {
        set
        {
            target = value;
            Vector3 v = target - transform.position;
            v.Normalize();
            v.x *= flatSpeed; v.y *= flatSpeed; v.z *= flatSpeed;
            Speed.x = v.x; Speed.y = v.y; Speed.z = v.z;
            float d = (target - transform.position).sqrMagnitude;
            if (d < flatSpeedSqr * 5f || Speed == Vector3.zero) { gameObject.SetActive(false); Destroy(gameObject); }
            lastDist = d;
        }
    }

    public TrailRenderer Trail { get { if (trail == null) trail = GetComponent<TrailRenderer>(); return trail; } }

    public float FlatSpeed { set { flatSpeed = value; flatSpeedSqr = flatSpeed * flatSpeed; } }

    void Start() { flatSpeed = 1f; flatSpeedSqr = flatSpeed * flatSpeed; }

    void FixedUpdate()
    {
        if (target == Vector3.zero) return;
        transform.position = transform.position + Speed;
        float d = (target - transform.position).sqrMagnitude;
        if (d < flatSpeedSqr || d > lastDist) { gameObject.SetActive(false); Destroy(gameObject); }
        lastDist = d;
    }
}
