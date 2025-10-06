using UnityEngine;
using Unity.Netcode;

public class CaravelTeleporter : NetworkBehaviour
{
    [SerializeField] private float bombClearRadius = 5f;
    [SerializeField] private float clearInterval = 0.5f;

    private float timer;

    void Update()
    {
        if (!IsServer) return;

        timer += Time.deltaTime;
        if (timer >= clearInterval)
        {
            timer = 0f;
            ClearNearbyBombs();
        }
    }

    private void ClearNearbyBombs()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, bombClearRadius);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Bomb"))
            {
                if (hit.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                {
                    netObj.Despawn();
                }

                Destroy(hit.gameObject);
            }
        }
    }
}
