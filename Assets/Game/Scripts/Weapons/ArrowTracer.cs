// Port of the original ArrowTracer (New Era NE_Scripts_Decrypted/ArrowTracer.cs; Penta 2016 ArrowTracer.cs). Same class name, fields and
// script GUID (3157d6d42dbc233cc694a38edf338ab7): the crossbow bolt (CharacterManager "CrossbowBoltPrefab", SR_Wildcat). It flies like
// the Tracer (Speed = direction × flatSpeed per FixedUpdate) and, unless `destroy` (a fighter was hit), stays stuck deepToTarget (2) into
// the hit point (the component removes itself; the prefab's DeleteAfterSeconds removes the bolt).
using UnityEngine;

public class ArrowTracer : MonoBehaviour
{
    public Vector3 target = Vector3.zero;
    public Vector3 initDir = Vector3.zero;
    TrailRenderer trail;
    public float flatSpeed = 150f;
    public Vector3 Speed = Vector3.zero;
    float flatSpeedSqr = 300f;
    float deepToTarget = 2f;
    public bool destroy;
    float lastDist;

    public Vector3 Target
    {
        set
        {
            target = value;
            Vector3 v = initDir = target - transform.position;
            v.Normalize();
            v.x *= flatSpeed; v.y *= flatSpeed; v.z *= flatSpeed;
            Speed.x = v.x; Speed.y = v.y; Speed.z = v.z;
            float d = (target - transform.position).sqrMagnitude;
            if (d < flatSpeedSqr * 5f || Speed == Vector3.zero)
            {
                if (destroy) { gameObject.SetActive(false); Object.Destroy(gameObject); return; }
                Stick();
            }
            lastDist = d;
        }
    }

    void Stick()
    {
        Vector3 n = initDir.normalized;
        n.Scale(new Vector3(deepToTarget, deepToTarget, deepToTarget));
        transform.position = target - n;
        Object.Destroy(this);
    }

    public TrailRenderer Trail { get { if (trail == null) trail = GetComponent<TrailRenderer>(); return trail; } }
    public float FlatSpeed { set { flatSpeed = value; flatSpeedSqr = flatSpeed * flatSpeed; } }

    void Start() { flatSpeed = 1f; flatSpeedSqr = flatSpeed * flatSpeed; }

    void FixedUpdate()
    {
        if (target == Vector3.zero) return;
        transform.position = transform.position + Speed;
        float d = (target - transform.position).sqrMagnitude;
        if (d < flatSpeedSqr || d > lastDist)
        {
            if (destroy) { gameObject.SetActive(false); Object.Destroy(gameObject); return; }
            Stick();
        }
        lastDist = d;
    }
}
