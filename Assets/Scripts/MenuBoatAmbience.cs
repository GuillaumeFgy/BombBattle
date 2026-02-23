using UnityEngine;

/// <summary>
/// Spawns 4 decorative boats that wander around the map during the menu/lobby phase.
/// Assign one ship prefab per slot in the Inspector, then wire this component into
/// UIManager so HideLobby/ShowLobby toggle its visibility.
/// </summary>
public class MenuBoatAmbience : MonoBehaviour
{
    [Header("Boat Prefabs")]
    [SerializeField] private GameObject[] boatPrefabs = new GameObject[4];

    [Header("Movement")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float wanderRate = 25f;          // max °/sec random steering
    [SerializeField] private float boundaryRadius = 35f;      // steer back toward center beyond this
    [SerializeField] private float boundaryTurnRate = 80f;    // °/sec when correcting for boundary
    [SerializeField] private float separationRadius = 14f;    // steer away from nearby boats inside this
    [SerializeField] private float separationTurnRate = 160f; // °/sec when separating (quadratic at close range)
    [SerializeField] private float collisionRadius = 3f;      // hard minimum half-distance between boats

    private Transform[] _boats;
    private float[] _angles;

    // Spread boats on the 4 cardinal axes, heading tangentially so they don't
    // converge on each other at startup.
    private static readonly Vector3[] StartPositions =
    {
        new Vector3( 22f, 0f,  0f),   // East  → heading North
        new Vector3(-22f, 0f,  0f),   // West  → heading South
        new Vector3(  0f, 0f, 22f),   // North → heading East
        new Vector3(  0f, 0f,-22f),   // South → heading West
    };

    private static readonly float[] StartAngles = { 0f, 180f, 90f, -90f };

    private void Start()
    {
        int count = Mathf.Min(boatPrefabs.Length, StartPositions.Length);
        _boats  = new Transform[count];
        _angles = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (boatPrefabs[i] == null) continue;

            GameObject go = Instantiate(
                boatPrefabs[i],
                StartPositions[i],
                Quaternion.Euler(0f, StartAngles[i], 0f),
                transform);

            _boats[i]  = go.transform;
            _angles[i] = StartAngles[i];
        }
    }

    private void Update()
    {
        if (_boats == null) return;

        for (int i = 0; i < _boats.Length; i++)
        {
            if (_boats[i] == null) continue;
            Steer(i);
            Advance(i);
        }

        ResolveOverlaps();
    }

    // Applies wander, boundary avoidance, and inter-boat separation to boat i's heading.
    // Wander is suppressed while actively separating so random noise can't fight avoidance.
    private void Steer(int i)
    {
        float   dt  = Time.deltaTime;
        Vector3 pos = _boats[i].position;

        // 1. Separation: steer away from nearby boats (quadratic — much stronger at close range)
        bool separating = false;
        for (int j = 0; j < _boats.Length; j++)
        {
            if (i == j || _boats[j] == null) continue;

            Vector3 diff = pos - _boats[j].position;
            float   dist = diff.magnitude;

            if (dist < separationRadius && dist > 0.01f)
            {
                separating = true;
                float awayAngle = Mathf.Atan2(diff.x, diff.z) * Mathf.Rad2Deg;
                float t         = 1f - dist / separationRadius;
                float strength  = t * t * separationTurnRate;          // quadratic falloff
                _angles[i] = Mathf.MoveTowardsAngle(_angles[i], awayAngle, strength * dt);
            }
        }

        // 2. Wander: small random nudge — skipped while actively separating
        if (!separating)
            _angles[i] += Random.Range(-wanderRate, wanderRate) * dt;

        // 3. Boundary avoidance: turn back toward center when too far out
        float distFromCenter = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);
        if (distFromCenter > boundaryRadius)
        {
            float toCenter = Mathf.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg;
            _angles[i] = Mathf.MoveTowardsAngle(_angles[i], toCenter, boundaryTurnRate * dt);
        }
    }

    // Moves boat i forward along its current heading.
    private void Advance(int i)
    {
        float   rad = _angles[i] * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        _boats[i].position += dir * speed * Time.deltaTime;
        _boats[i].rotation  = Quaternion.Euler(0f, _angles[i], 0f);
    }

    // Hard position correction: if two boats are closer than 2 * collisionRadius,
    // push them apart equally so they can never visually overlap.
    private void ResolveOverlaps()
    {
        float minDist = collisionRadius * 2f;

        for (int i = 0; i < _boats.Length; i++)
        {
            if (_boats[i] == null) continue;
            for (int j = i + 1; j < _boats.Length; j++)
            {
                if (_boats[j] == null) continue;

                Vector3 diff = _boats[i].position - _boats[j].position;
                float   dist = diff.magnitude;

                if (dist < minDist && dist > 0.001f)
                {
                    Vector3 push = diff.normalized * (minDist - dist) * 0.5f;
                    _boats[i].position += push;
                    _boats[j].position -= push;
                }
            }
        }
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);
}
