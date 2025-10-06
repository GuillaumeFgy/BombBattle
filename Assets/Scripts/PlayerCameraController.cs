using Unity.Netcode;
using UnityEngine;

public class PlayerCameraController : NetworkBehaviour
{
    private Camera playerCamera;
    private AudioListener audioListener;

    private Vector3 initialCameraLocalPosition;
    private Quaternion initialCameraLocalRotation;

    private void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>(true); // include inactive
        audioListener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;

        if (playerCamera != null)
        {
            initialCameraLocalPosition = playerCamera.transform.localPosition;
            initialCameraLocalRotation = playerCamera.transform.localRotation;

            playerCamera.enabled = false;
            if (audioListener != null)
                audioListener.enabled = false;
        }
    }


    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            if (playerCamera != null)
                playerCamera.enabled = true;

            if (audioListener != null)
                audioListener.enabled = true;
        }
    }

    [ClientRpc]
    public void ResetCameraClientRpc()
    {
        if (!IsOwner) return;
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cam.enabled = true;
            cam.transform.localPosition = initialCameraLocalPosition;
            cam.transform.localRotation = initialCameraLocalRotation;
        }

        if (audioListener != null)
            audioListener.enabled = true;
    }


}
