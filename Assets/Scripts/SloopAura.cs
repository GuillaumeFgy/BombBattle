using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SloopAura : NetworkBehaviour
{
    [SerializeField] private float boostMultiplier = 1.5f;
    [SerializeField] private float effectDuration = 3f;

    private Transform sloopTransform;
    private NetworkVariable<NetworkObjectReference> sloopRef = new();

    // Players currently inside the aura (server-side)
    private HashSet<PlayerMovement> _playersInAura = new();

    public void Initialize(NetworkObject sloopNetObj)
    {
        if (IsServer)
            sloopRef.Value = sloopNetObj;
    }

    public override void OnNetworkSpawn()
    {
        TryResolveSloopTransform();
    }

    private void TryResolveSloopTransform()
    {
        if (sloopRef.Value.TryGet(out var sloopNetObj))
            sloopTransform = sloopNetObj.transform;
        else
            Debug.LogWarning($"[Aura] Failed to resolve Sloop ref on client {NetworkManager.Singleton.LocalClientId}");
    }

    private void FixedUpdate()
    {
        if (sloopTransform == null)
        {
            TryResolveSloopTransform();
            return;
        }
        transform.position = sloopTransform.position;
        transform.rotation = sloopTransform.rotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!IsOtherPlayer(other, out PlayerMovement pm)) return;

        _playersInAura.Add(pm);
        // Apply boost and cancel any pending restore timer on the owner client
        pm.StartSloopSpeedBoostClientRpc(boostMultiplier);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (other.TryGetComponent(out PlayerMovement pm) && _playersInAura.Remove(pm))
        {
            // Start the post-aura countdown on the owner client
            pm.BeginSloopSpeedRestoreClientRpc(effectDuration);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            // Players still inside when the aura ends get the same post-aura countdown
            foreach (var pm in _playersInAura)
            {
                if (pm != null)
                    pm.BeginSloopSpeedRestoreClientRpc(effectDuration);
            }
            _playersInAura.Clear();
        }
        base.OnNetworkDespawn();
    }

    private bool IsOtherPlayer(Collider other, out PlayerMovement pm)
    {
        pm = null;
        if (!other.CompareTag("Player")) return false;
        if (!other.TryGetComponent(out NetworkObject netObj)) return false;
        if (!sloopRef.Value.TryGet(out var sloopNetObj)) return false;
        if (netObj.OwnerClientId == sloopNetObj.OwnerClientId) return false;
        return other.TryGetComponent(out pm);
    }
}
