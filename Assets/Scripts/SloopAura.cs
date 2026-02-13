using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SloopAura : NetworkBehaviour
{
    private Transform sloopTransform;

    private NetworkVariable<NetworkObjectReference> sloopRef = new();

    private Dictionary<PlayerMovement, float> affectedPlayers = new();

    public void Initialize(NetworkObject sloopNetObj)
    {
        if (IsServer)
        {
            sloopRef.Value = sloopNetObj;
        }
    }

    public override void OnNetworkSpawn()
    {
        TryResolveSloopTransform();
    }

    private void TryResolveSloopTransform()
    {
        if (sloopRef.Value.TryGet(out var sloopNetObj))
        {
            sloopTransform = sloopNetObj.transform;
        }
        else
        {
            Debug.LogWarning($"[Aura] Failed to resolve Sloop NetworkObjectReference on client {NetworkManager.Singleton.LocalClientId}");
        }
    }

    private void FixedUpdate()
    {
        if (sloopTransform == null)
        {
            TryResolveSloopTransform(); // Try again if not yet resolved
            return;
        }

        transform.position = sloopTransform.position;
        transform.rotation = sloopTransform.rotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player") &&
            other.TryGetComponent(out NetworkObject netObj) &&
            sloopRef.Value.TryGet(out var sloopNetObj) &&
            netObj.OwnerClientId != sloopNetObj.OwnerClientId)
        {
            if (other.TryGetComponent(out PlayerMovement targetMovement) &&
                !affectedPlayers.ContainsKey(targetMovement))
            {
                affectedPlayers[targetMovement] = targetMovement.GetMoveSpeed();
                targetMovement.SetMoveSpeed(targetMovement.GetMoveSpeed() * 0.5f);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.TryGetComponent(out PlayerMovement targetMovement))
        {
            RestoreSpeed(targetMovement);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            RestoreAllSpeeds();
        }
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        if (IsServer)
        {
            RestoreAllSpeeds();
        }
    }

    private void RestoreSpeed(PlayerMovement player)
    {
        if (affectedPlayers.TryGetValue(player, out float originalSpeed))
        {
            player.SetMoveSpeed(originalSpeed);
            affectedPlayers.Remove(player);
        }
    }

    private void RestoreAllSpeeds()
    {
        foreach (var kvp in affectedPlayers)
        {
            if (kvp.Key != null)
            {
                kvp.Key.SetMoveSpeed(kvp.Value);
            }
        }
        affectedPlayers.Clear();
    }
}
