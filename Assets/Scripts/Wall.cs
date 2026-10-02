using UnityEngine;
using Unity.Netcode;

public class Wall : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        if (other.CompareTag("Player"))
        {
            if (!other.TryGetComponent(out PlayerDeathHandler deathHandler)) return;

            if (nm.IsServer)
                deathHandler.ServerKill();
            else if (deathHandler.IsOwner)
                deathHandler.ReportOwnWallHitRpc(); // our own ship hit the wall on our screen: report it right away
        }
        else if (other.CompareTag("Bomb") && nm.IsServer && other.TryGetComponent(out Bomb bomb))
        {
            bomb.ServerDestroy();
        }
    }
}
