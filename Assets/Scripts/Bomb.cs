using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Bomb : NetworkBehaviour
{
    private ulong creatorId = ulong.MaxValue;
    private bool ignoreCreator = false;

    [SerializeField] private Renderer bombRenderer;
    [SerializeField] private GameObject deathEffectPrefab;

    private NetworkVariable<Vector4> syncedColorVec = new(Vector4.one, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public void SetColor(Color color)
    {
        if (IsServer)
        {
            SetBombColorClientRpc(color); // all clients apply directly
        }
    }

    private void ApplyColor(Color color)
    {
        if (bombRenderer != null)
        {
            bombRenderer.material.color = color;
        }
    }

    public void SetCreator(ulong id, bool shouldIgnore)
    {
        creatorId = id;
        ignoreCreator = shouldIgnore;
    }



    private void Start()
    {
        StartCoroutine(EnableTriggerAfterDelay());
    }

    private IEnumerator EnableTriggerAfterDelay()
    {
        Collider col = GetComponent<Collider>();
        col.enabled = false;
        yield return new WaitForSeconds(0.2f);
        col.enabled = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player") &&
            other.TryGetComponent(out NetworkObject netObj))
        {
            if (ignoreCreator && netObj.OwnerClientId == creatorId)
            {
                Debug.Log("Galleon bomb ignored its creator");
                return;
            }

            if (other.TryGetComponent(out PlayerDeathHandler deathHandler))
            {
                // Spawn death effect before applying death logic
                SpawnDeathEffect(other.transform.position);

                deathHandler.HandleDeathServerRpc();
            }

            DestroyBombRpc();
        }
    }

    private void SpawnDeathEffect(Vector3 position)
    {
        if (deathEffectPrefab == null) return;

        GameObject effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
        var netObj = effect.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            StartCoroutine(DespawnAfterDelay(netObj, 2f));
        }
    }

    private IEnumerator DespawnAfterDelay(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (netObj.IsSpawned)
            netObj.Despawn(true);
    }




    [Rpc(SendTo.Server)]
    public void DestroyBombRpc() 
    {
        GetComponent<NetworkObject>().Despawn(true);
        Destroy(gameObject);
    }

    [ClientRpc]
    public void SetBombColorClientRpc(Color color)
    {
        ApplyColor(color);
    }
}
