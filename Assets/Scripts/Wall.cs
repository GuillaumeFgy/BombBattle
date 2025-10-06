using UnityEngine;
using Unity.Netcode;

public class Wall : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (other.TryGetComponent(out PlayerDeathHandler deathHandler))
            {
                deathHandler.HandleDeathServerRpc();
            }
        }
        else if (other.CompareTag("Bomb"))
        {
            if (other.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
            {
                netObj.Despawn();
            }
            Destroy(other.gameObject);
        }
    }
}
