using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ScoreBoardUI : MonoBehaviour
{
    [SerializeField] GameObject playerEntryPrefab;

    public static ScoreBoardUI Instance { get; private set; }

    private Dictionary<ulong, PlayerEntry> playerEntries = new Dictionary<ulong, PlayerEntry>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void UpdateScoreboard(List<PlayerStats> playerList)
    {
        foreach (var player in playerList)
        {
            string playerName = "Unknown";
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(player.playerId, out var client)
                && client.PlayerObject != null)
            {
                if (client.PlayerObject.TryGetComponent(out PlayerName playerNameComponent))
                {
                    playerName = playerNameComponent.GetPlayerName();

                    // Only subscribe once to prevent duplicate handlers
                    playerNameComponent.OnNameChanged -= (newName) => UpdatePlayerEntry(player.playerId, newName, player.score);
                    playerNameComponent.OnNameChanged += (newName) => UpdatePlayerEntry(player.playerId, newName, player.score);
                }
            }

            // Do not overwrite with "Unknown" if we already have the entry
            if (playerName != "Unknown")
            {
                UpdatePlayerEntry(player.playerId, playerName, player.score);
            }
        }
    }


    private void UpdatePlayerEntry(ulong playerId, string playerName, int score)
    {
        if (!playerEntries.TryGetValue(playerId, out var entry))
        {
            GameObject playerEntryObject = Instantiate(playerEntryPrefab, transform);
            entry = playerEntryObject.GetComponent<PlayerEntry>();
            playerEntries[playerId] = entry;
        }

        entry.SetPlayerEntry(playerName, score);
    }
}