using System;
using Unity.Collections;
using Unity.Multiplayer.Center.NetcodeForGameObjectsExample.DistributedAuthority;
using Unity.Netcode;
using UnityEngine;

public class PlayerName : NetworkBehaviour
{

    public NetworkVariable<FixedString32Bytes> networkPlayerName = new NetworkVariable<FixedString32Bytes>("Unknown",
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public event Action<string> OnNameChanged;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            string inputName = PlayerPrefs.GetString("PlayerName", Guid.NewGuid().ToString()); // fallback to something unique
            if (string.IsNullOrWhiteSpace(inputName) || inputName == "Unknown")
                inputName = "Player_" + OwnerClientId;

            networkPlayerName.Value = new FixedString32Bytes(inputName);
        }

        networkPlayerName.OnValueChanged += NetworkPlayerName_OnValueChanged;
        OnNameChanged?.Invoke(networkPlayerName.Value.ToString());
    }


    private void NetworkPlayerName_OnValueChanged(FixedString32Bytes previousValue, FixedString32Bytes newValue) 
    {
        // playerName.text = newValue;
        OnNameChanged?.Invoke(newValue.Value);
    }

    public string GetPlayerName() 
    {
        return networkPlayerName.Value.ToString();
    }
}
