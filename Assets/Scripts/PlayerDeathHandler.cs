using Unity.Netcode;
using UnityEngine;

public class PlayerDeathHandler : NetworkBehaviour
{
    [SerializeField] private GameObject[] shipModels;
    [SerializeField] private MonoBehaviour[] componentsToDisable; // assign in inspector: e.g. PlayerMovement
    [SerializeField] private Transform spectatorCameraPosition;   // optional: assign a transform for top-down view

    public NetworkVariable<bool> isAlive = new NetworkVariable<bool>(true);

    [ServerRpc(RequireOwnership = false)]
    public void HandleDeathServerRpc(ServerRpcParams rpcParams = default)
    {
        isAlive.Value = false; 
        HideModelAndDisableClientRpc();
        GameManager.Instance.StartCoroutine(GameManager.Instance.DelayedCheckEndCondition());
    }


    [ClientRpc]
    private void HideModelAndDisableClientRpc()
    {
        foreach (var model in shipModels)
        {
            if (model != null)
                model.SetActive(false);
        }

        // 2. Disable movement & abilities
        foreach (var comp in componentsToDisable)
        {
            if (comp != null)
                comp.enabled = false;
        }

        // 3. Disable all colliders
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        // 4. Disable physics
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
        }

        // 5. Camera switch
        if (IsOwner)
        {
            SetSpectatorCamera();
        }
    }

    private void SetSpectatorCamera()
    {
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cam.enabled = true;

            if (spectatorCameraPosition != null)
            {
                cam.transform.position = spectatorCameraPosition.position;
                cam.transform.rotation = spectatorCameraPosition.rotation;
            }
            else
            {
                cam.transform.position = new Vector3(0, 100, 0); // fallback overhead
                cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }
    }

    [ClientRpc]
    public void ResetPlayerClientRpc()
    {
        foreach (var model in shipModels)
        {
            if (model != null)
                model.SetActive(false); // deactivate all
        }

        if (TryGetComponent(out PlayerClass playerClass))
        {
            playerClass.ApplyCurrentModel();
        }

        foreach (var comp in componentsToDisable)
            if (comp != null) comp.enabled = true;

        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = true;

        foreach (var rb in GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = false;

        if (IsServer)
        {
            isAlive.Value = true;
        }
    }

    [ClientRpc]
    public void TeleportToPositionClientRpc(Vector3 position, Quaternion rotation)
    {
        if (!IsOwner) return;
        transform.SetPositionAndRotation(position, rotation);
    }



}
