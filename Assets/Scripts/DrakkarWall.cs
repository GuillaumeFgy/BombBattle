using UnityEngine;
using Unity.Netcode;

public class DrakkarWall : NetworkBehaviour
{
    public float speed = 10f;
    public float lifetime = 3f;
    public float pushForce = 25f;

    private Rigidbody rb;
    private BoxCollider box;
    private ulong _casterClientId;

    private static readonly Collider[] sweepBuffer = new Collider[64];

    public void SetCaster(ulong clientId) => _casterClientId = clientId;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();

        if (IsServer)
        {
            Invoke(nameof(DespawnSelf), lifetime);
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned) return;

        float step = speed * Time.fixedDeltaTime;
        ClearBombsAlong(step);
        rb.MovePosition(rb.position + transform.forward * step);
    }

    // Trigger events alone missed moving bombs (Galleon shots): the wave is thin (0.2 m) and the shot flies towards it
    // fast. Sweep the volume the wave covers this step instead, so any bomb it passes over is removed.
    private void ClearBombsAlong(float step)
    {
        if (box == null) return;

        Vector3 halfExtents = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
        halfExtents = new Vector3(Mathf.Abs(halfExtents.x), Mathf.Abs(halfExtents.y), Mathf.Abs(halfExtents.z) + step * 0.5f);
        Vector3 center = transform.TransformPoint(box.center) + transform.forward * (step * 0.5f);

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, sweepBuffer, transform.rotation, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (sweepBuffer[i].TryGetComponent(out Bomb bomb))
                bomb.ServerDestroy();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.TryGetComponent(out Bomb bomb))
        {
            bomb.ServerDestroy();
        }
        else if (other.TryGetComponent(out PlayerMovement movement))
        {
            if (movement.OwnerClientId != _casterClientId)
            {
                movement.ServerApplyKnockback(transform.forward, pushForce);
            }
        }
    }

    private void DespawnSelf()
    {
        if (IsServer && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}
