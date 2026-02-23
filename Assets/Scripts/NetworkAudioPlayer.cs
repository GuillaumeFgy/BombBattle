using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network relay for audio. Place this component once in the scene on a
/// NetworkObject (e.g. the same GameObject as GameManager, or a dedicated
/// empty NetworkObject).
///
/// Any NetworkBehaviour can call the public ServerRpc methods from any client;
/// the server then broadcasts the sound to every connected client so spatial
/// audio is heard at the correct world position by everyone.
///
/// Usage examples:
///   // Spatial — position matters (player abilities, explosions)
///   NetworkAudioPlayer.Instance.PlaySpatialSFXServerRpc(SFXId.BombExplosion, transform.position);
///
///   // Non-spatial — identical on all clients (round start beep, etc.)
///   NetworkAudioPlayer.Instance.PlaySFXServerRpc(SFXId.RoundStart);
/// </summary>
public class NetworkAudioPlayer : NetworkBehaviour
{
    public static NetworkAudioPlayer Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (Instance == this) Instance = null;
    }

    // ── Spatial SFX ──────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast a spatial (3-D positional) SFX to all clients.
    /// Can be called by any client — ownership is not required.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PlaySpatialSFXServerRpc(SFXId id, Vector3 position)
    {
        PlaySpatialSFXClientRpc(id, position);
    }

    [ClientRpc]
    private void PlaySpatialSFXClientRpc(SFXId id, Vector3 position)
    {
        AudioManager.Instance?.PlaySpatialSFX(id, position);
    }

    // ── Non-spatial SFX ──────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast a 2-D (non-spatial) SFX to all clients.
    /// Can be called by any client — ownership is not required.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PlaySFXServerRpc(SFXId id)
    {
        PlaySFXClientRpc(id);
    }

    [ClientRpc]
    private void PlaySFXClientRpc(SFXId id)
    {
        AudioManager.Instance?.PlaySFX(id);
    }
}
