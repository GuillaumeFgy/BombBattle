using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class OptionsMenu : MonoBehaviour
{
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject optionsPanel;

    [SerializeField] private Button resumeButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button leaveSessionButton;
    [SerializeField] private Button quitButton;

    private void Start()
    {
        resumeButton.onClick.AddListener(CloseMenu);
        optionsButton.onClick.AddListener(OpenOptionsPanel);
        backButton.onClick.AddListener(CloseOptionsPanel);
        leaveSessionButton.onClick.AddListener(LeaveSession);
        quitButton.onClick.AddListener(QuitGame);

        menuPanel.SetActive(false);
        optionsPanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (optionsPanel.activeSelf)
                CloseOptionsPanel();
            else
                ToggleMenu();
        }
    }

    private void ToggleMenu()
    {
        bool opening = !menuPanel.activeSelf;
        menuPanel.SetActive(opening);

        if (opening)
            RefreshLeaveSessionButton();
    }

    private void CloseMenu()
    {
        optionsPanel.SetActive(false);
        menuPanel.SetActive(false);
    }

    private void OpenOptionsPanel()
    {
        optionsPanel.SetActive(true);
    }

    private void CloseOptionsPanel()
    {
        optionsPanel.SetActive(false);
    }

    private void RefreshLeaveSessionButton()
    {
        bool inSession = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost);
        leaveSessionButton.gameObject.SetActive(inSession);
    }

    private async void LeaveSession()
    {
        leaveSessionButton.interactable = false;

        List<ISession> sessions = MultiplayerService.Instance.Sessions.Values.ToList();
        foreach (ISession session in sessions)
        {
            if (session.State == SessionState.Deleted) continue;

            try
            {
                // If we're the host and other players are in the session, migrate host first
                if (session.IsHost && session.Players.Count > 1)
                {
                    IReadOnlyPlayer newHost = session.Players
                        .FirstOrDefault(p => p.Id != session.CurrentPlayer.Id);

                    if (newHost != null)
                    {
                        try
                        {
                            IHostSession hostSession = session.AsHost();
                            hostSession.Host = newHost.Id;
                            await hostSession.SavePropertiesAsync();
                        }
                        catch (SessionException e) { Debug.LogWarning($"Host migration: {e.Message}"); }
                    }
                }

                await session.LeaveAsync();
            }
            catch (SessionException e) { Debug.LogWarning($"LeaveAsync: {e.Message}"); }
        }

        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
