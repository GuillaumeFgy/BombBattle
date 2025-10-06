//#define DISABLE_VIVOX // <- Optional flag if you want to toggle easily

using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
#if UNITY_SERVICES_VIVOX
using Unity.Services.Vivox;
#endif

public class MultiplayerWidgetsInitializer : MonoBehaviour
{
    private async void Start()
    {
        string playerName = PlayerPrefs.GetString("PlayerName", "Player");

        var options = new InitializationOptions()
            .SetOption("displayName", playerName);

        await UnityServices.InitializeAsync(options);

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await AuthenticationService.Instance.UpdatePlayerNameAsync(playerName);
            PlayerPrefs.SetString("PlayerName", playerName); // Ensure saved
        }

#if UNITY_SERVICES_VIVOX && !DISABLE_VIVOX
        await VivoxService.Instance.InitializeAsync();
        Debug.Log("[WidgetsInit] Vivox initialized");
#endif

        Unity.Multiplayer.Widgets.WidgetServiceInitialization.ServicesInitialized();
        Debug.Log($"[WidgetsInit] Services ready for {playerName}");
    }
}
