using UnityEngine;
using Unity.Netcode;

public class DrakkarWall : NetworkBehaviour
{
    public float speed = 10f;
    public float lifetime = 3f;
    public float pushForce = 25f;

    private Rigidbody rb;
    private ulong _casterClientId;

    public void SetCaster(ulong clientId) => _casterClientId = clientId;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (IsServer)
        {
            Invoke(nameof(DespawnSelf), lifetime);
        }
    }

    private void FixedUpdate()
    {
        if (IsServer)
        {
            rb.MovePosition(rb.position + transform.forward * speed * Time.fixedDeltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Bomb"))
        {
            if (other.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
            {
                netObj.Despawn();
            }
        }
        else if (other.TryGetComponent(out PlayerMovement movement))
        {
            if (movement.OwnerClientId != _casterClientId)
            {
                movement.ApplyKnockbackClientRpc(transform.forward, pushForce);
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
