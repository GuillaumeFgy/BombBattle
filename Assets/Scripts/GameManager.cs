using TMPro;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    private bool gameActive = false;
    private bool lobbyActive = true;

    public Transform[] spawnPoints;
    [SerializeField] TextMeshProUGUI gameInfoText;
    [SerializeField] Button startButton;

    private List<ulong> connectedPlayers = new List<ulong>();
    private const int MaxPlayers = 8;

    private bool roundResetting = false;

    private readonly List<Color> availableColors = new()
    {
        Color.red, Color.blue, Color.green, Color.yellow,
        Color.orange, Color.magenta, Color.brown, Color.turquoise
    };

    private Dictionary<ulong, int> playerColorIndices = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerJoined;
        startButton.onClick.AddListener(StartGame);
    }
    private void OnPlayerJoined(ulong clientId)
    {
        if (!IsServer) return;

        if (!connectedPlayers.Contains(clientId) && connectedPlayers.Count < MaxPlayers)
        {
            connectedPlayers.Add(clientId);

            int colorIndex = connectedPlayers.Count - 1;
            playerColorIndices[clientId] = colorIndex;
            StartCoroutine(WaitForPlayerName(clientId, colorIndex));

            if (clientId == NetworkManager.Singleton.LocalClientId && IsHost)
            {
                startButton.gameObject.SetActive(true);
            }
        }
        else if (connectedPlayers.Count >= MaxPlayers)
        {
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }


    private IEnumerator WaitForPlayerName(ulong clientId, int colorIndex)
    {
        while (true)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
                client.PlayerObject != null &&
                client.PlayerObject.TryGetComponent(out PlayerClass pc))
            {
                pc.SetPlayerColor(colorIndex);
                break;
            }

            yield return null;
        }

        UpdateLobbyTextRpc($"Player {connectedPlayers.Count}/8 joined...");
        ScoreBoardUI.Instance.UpdateScoreboard(ScoreboardManager.Instance.GetPlayerList());
    }

    public Color GetColorByIndex(int index)
    {
        return availableColors[Mathf.Clamp(index, 0, availableColors.Count - 1)];
    }

    void StartGame()
    {
        if (!IsServer) return;
        if (IsHost && !gameActive && lobbyActive)
        {
            if (connectedPlayers.Count > 0)
            {
                ResetGameState();
                lobbyActive = false;
                StartCoroutine(CountdownAndStartGame());
            }
        }
    }


    private IEnumerator CountdownAndStartGame()
    {
        PlayerMovement.AllowMovement = false;
        for (int count = 3; count > 0; count--)
        {
            UpdateLobbyTextRpc($"Game starting in {count}...");
            yield return new WaitForSeconds(1f);
        }

        UpdateLobbyTextRpc("");

        for (int i = 0; i < connectedPlayers.Count && i < spawnPoints.Length; i++)
        {
            ulong clientId = connectedPlayers[i];
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                var obj = client.PlayerObject;
                var spawn = GetSpawnPoint(i);
                obj.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            }
        }

        for (int i = 0; i < connectedPlayers.Count && i < spawnPoints.Length; i++)
        {
            ulong clientId = connectedPlayers[i];
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                var obj = client.PlayerObject;
                var spawn = GetSpawnPoint(i);
                obj.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

                if (obj.TryGetComponent(out PlayerDeathHandler deathHandler))
                {
                    deathHandler.TeleportToPositionClientRpc(spawn.position, spawn.rotation);
                    deathHandler.ResetPlayerClientRpc();
                }

                var camController = obj.GetComponentInChildren<PlayerCameraController>();
                if (camController != null)
                {
                    camController.ResetCameraClientRpc();
                }
            }
        }


        foreach (ulong clientId in connectedPlayers)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                var player = client.PlayerObject;
                if (player != null && player.TryGetComponent(out PlayerClass pc))
                {
                    while (pc.GetColorIndex() == -1)
                        yield return null;
                }
            }
        }

        HideLobbyRpc();
        SetGameStateRpc(true, false);
        EnablePlayerMovementRpc();
    }


    private void EndGame()
    {
        SetGameStateRpc(false, true); 
        Bomb[] bombs = FindObjectsByType<Bomb>(FindObjectsSortMode.None);
        foreach (Bomb bomb in bombs)
        {
            bomb.DestroyBombRpc();
        }

        UpdateWinnerRpc();
        StartCoroutine(ReturnToLobby());
    }

    [Rpc(SendTo.ClientsAndHost)]
    void UpdateWinnerRpc()
    {
        string winnerText = ScoreboardManager.Instance.GetWinnerName();
        gameInfoText.text = winnerText;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void UpdateLobbyTextRpc(string message)
    {
        gameInfoText.text = message;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void HideLobbyRpc()
    {
        UIManager ui = FindFirstObjectByType<UIManager>();
        if (ui != null) ui.HideLobby();
    }

    public Transform GetSpawnPoint(int index)
    {
        if (index < 0 || index >= spawnPoints.Length)
        {
            Debug.LogWarning("Invalid spawn index");
            return null;
        }
        return spawnPoints[index];
    }

    public bool IsGameActive()
    {
        return gameActive;
    }

    public void CheckEndCondition()
    {
        if (!IsServer || roundResetting) return;

        int aliveCount = 0;
        ulong lastAlivePlayerId = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj != null &&
                playerObj.TryGetComponent(out PlayerDeathHandler deathHandler) &&
                deathHandler.isAlive.Value)
            {
                aliveCount++;
                lastAlivePlayerId = client.ClientId;
            }
        }

        if (aliveCount == 1)
        {
            roundResetting = true;
            ScoreboardManager.Instance.IncreasePlayerScoreRpc(lastAlivePlayerId, 1);
            StartCoroutine(HandleRoundEnd(lastAlivePlayerId));
        }
        else if (aliveCount == 0)
        {
            // No one survived — restart the round without awarding points
            roundResetting = true;
            StartCoroutine(HandleRoundEnd(ulong.MaxValue)); // Pass invalid ID to indicate no winner
        }
    }



    public IEnumerator DelayedCheckEndCondition()
    {
        yield return new WaitForSeconds(0.2f); // Give time for other deaths to register
        CheckEndCondition();
    }


    private IEnumerator HandleRoundEnd(ulong winnerId)
    {
        yield return new WaitForSeconds(2f);

        if (winnerId != ulong.MaxValue)
        {
            int score = ScoreboardManager.Instance.GetScore(winnerId);
            if (score >= 5)
            {
                EndGame();
                yield break;
            }
        }

        RestartRound();
    }


    void RestartRound()
    {
        Bomb[] bombs = FindObjectsByType<Bomb>(FindObjectsSortMode.None);
        foreach (Bomb bomb in bombs)
        {
            bomb.DestroyBombRpc();
        }

        roundResetting = false; // Allow future rounds to reset
        StartCoroutine(CountdownAndResetPlayers());
    }


    private IEnumerator CountdownAndResetPlayers()
    {
        DisablePlayerMovementRpc(); 
        gameInfoText.text = "Next round in...";

        // MOVE PLAYERS TO SPAWN POINTS AND RESET FIRST
        for (int i = 0; i < connectedPlayers.Count && i < spawnPoints.Length; i++)
        {
            ulong clientId = connectedPlayers[i];
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                var obj = client.PlayerObject;
                if (obj == null) continue;

                // Get spawn point and reposition on server
                var spawn = GetSpawnPoint(i);
                obj.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

                
                if (obj.TryGetComponent(out PlayerDeathHandler deathHandler))
                {
                    deathHandler.TeleportToPositionClientRpc(spawn.position, spawn.rotation);
                    deathHandler.ResetPlayerClientRpc();
                }

                var camController = obj.GetComponentInChildren<PlayerCameraController>();
                if (camController != null)
                {
                    camController.ResetCameraClientRpc();
                }
            }
        }

        // COUNTDOWN
        for (int count = 3; count > 0; count--)
        {
            UpdateLobbyTextRpc($"Next round in {count}...");
            yield return new WaitForSeconds(1f);
        }

        UpdateLobbyTextRpc(""); // Clear message

        SetGameStateRpc(true, false);
        EnablePlayerMovementRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void DisablePlayerMovementRpc()
    {
        PlayerMovement.AllowMovement = false;
    }




    [Rpc(SendTo.ClientsAndHost)]
    void EnablePlayerMovementRpc()
    {
        PlayerMovement.AllowMovement = true;
    }

    private IEnumerator ReturnToLobby()
    {
        yield return new WaitForSeconds(5f); // Let players read winner message

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = client.PlayerObject;
            if (playerObj != null &&
                playerObj.TryGetComponent(out PlayerDeathHandler deathHandler))
            {
                deathHandler.ResetPlayerClientRpc();
            }
        }

        SetGameStateRpc(false, true);
        DisablePlayerMovementRpc(); // Prevent movement in lobby
        ShowLobbyRpc();
        ScoreboardManager.Instance.ResetAllScoresServerRpc();
    }
    private void ResetGameState()
    {
        roundResetting = false;
        gameActive = false;
        lobbyActive = true;

        // Reset scoreboard
        ScoreboardManager.Instance.ResetAllScoresServerRpc();

        // Destroy all Caravel teleporters
        var teleporters = GameObject.FindObjectsOfType<CaravelTeleporter>();
        foreach (var tele in teleporters)
        {
            if (tele.IsServer && tele.NetworkObject.IsSpawned)
            {
                tele.NetworkObject.Despawn();
                Destroy(tele.gameObject);
            }
        }

        // Clear teleporter references on all players
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var obj = client.PlayerObject;
            if (obj == null) continue;

            if (obj.TryGetComponent(out PlayerClass pc))
            {
                pc.ClearTeleporterClientRpc();
            }

            var spawn = GetSpawnPoint(connectedPlayers.IndexOf(client.ClientId));
            if (spawn != null)
            {
                obj.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

                if (obj.TryGetComponent(out PlayerDeathHandler deathHandler))
                {
                    deathHandler.TeleportToPositionClientRpc(spawn.position, spawn.rotation);
                    deathHandler.ResetPlayerClientRpc();
                }

                var camController = obj.GetComponentInChildren<PlayerCameraController>();
                if (camController != null)
                {
                    camController.ResetCameraClientRpc();
                }
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SetGameStateRpc(bool isGameActive, bool isLobbyActive)
    {
        gameActive = isGameActive;
        lobbyActive = isLobbyActive;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShowLobbyRpc()
    {
        UIManager ui = FindFirstObjectByType<UIManager>();
        if (ui != null) ui.ShowLobby();
    }

}
