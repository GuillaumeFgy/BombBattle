using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static PlayerClass;

public class UIManager : MonoBehaviour
{
    [SerializeField] public TMP_InputField nameInputField;
    [SerializeField] private TextMeshProUGUI gameInfoText;
    [SerializeField] Button hostButton;
    [SerializeField] Button joinButton;
    [SerializeField] GameObject menu;

    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TextMeshProUGUI lobbyStatusText;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerListEntryPrefab;

    private HashSet<string> lobbyNames = new HashSet<string>();

    private PlayerClass playerClass;
    [SerializeField] private RawImage[] shipImages;        // Assign your 4 RawImages in the Inspector
    [SerializeField] private RectTransform selectorImage;  // Assign the selector image here
    [SerializeField] private Button[] shipButtons;         // Assign corresponding buttons

    [SerializeField] private TextMeshProUGUI abilityDescription;
    [SerializeField] private Texture galleonIcon;
    [SerializeField] private Texture caravelIcon;
    [SerializeField] private Texture drakkarIcon;
    [SerializeField] private Texture sloopIcon;
    private AbilityUI abilityUI;


    private void Start()
    {
        hostButton.onClick.AddListener(Host);
        joinButton.onClick.AddListener(Join);

        StartCoroutine(WaitForLocalPlayerClass());

        for (int i = 0; i < shipButtons.Length; i++)
        {
            int index = i;
            shipButtons[i].onClick.AddListener(() => OnShipSelected(index));
        }
        abilityUI = AbilityUI.Instance;
    }

    private IEnumerator WaitForLocalPlayerClass()
    {
        while (NetworkManager.Singleton.LocalClient?.PlayerObject == null ||
               !NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out playerClass))
        {
            yield return null;
        }

        Debug.Log("PlayerClass successfully assigned.");
    }


    void Host()
    {
        menu.SetActive(false);
        lobbyPanel.SetActive(true);
        gameInfoText.gameObject.SetActive(false);
    }

    void Join()
    {
        menu.SetActive(false);
        lobbyPanel.SetActive(true);
        lobbyStatusText.text = "Waiting for host...";
        gameInfoText.gameObject.SetActive(false);
    }

    public void AddPlayerToLobbyList(string playerName)
    {
        if (lobbyNames.Contains(playerName)) return;

        lobbyNames.Add(playerName);
        GameObject entry = Instantiate(playerListEntryPrefab, playerListContainer);
        entry.GetComponentInChildren<TextMeshProUGUI>().text = playerName;
    }

    public void HideLobby()
    {
        lobbyPanel.SetActive(false);
        gameInfoText.gameObject.SetActive(true);
    }

    public void ShowLobby() 
    {
        lobbyPanel.SetActive(true);
        gameInfoText.gameObject.SetActive(false);
    }


    void AssignPlayerClass()
    {
        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer != null && localPlayer.TryGetComponent(out PlayerClass pc))
        {
            playerClass = pc;
        }
        else
        {
            Debug.LogWarning("Local player's PlayerClass component not found.");
        }
    }

    void OnShipSelected(int index)
    {
        if (playerClass == null)
        {
            Debug.LogWarning("PlayerClass not assigned.");
            return;
        }

        MoveSelectorTo(shipImages[index].rectTransform);

        switch (index)
        {
            case 0:
                playerClass.SetShipServerRpc(ShipType.Galleon);
                abilityDescription.text = "The Galleon can Launch a heavy bomb straight ahead that explodes on impact.";
                abilityUI.SetAbilityIcon(1, galleonIcon);
                break;
            case 1:
                playerClass.SetShipServerRpc(ShipType.Caravel);
                abilityDescription.text = "The Caravel can place a teleport marker and warps back to it with a second press.";
                abilityUI.SetAbilityIcon(1, caravelIcon);
                break;
            case 2:
                playerClass.SetShipServerRpc(ShipType.Drakkar);
                abilityDescription.text = "The Drakkar can summon a water wall straight ahead, destroying the bombs in its path.";
                abilityUI.SetAbilityIcon(1, drakkarIcon);
                break;
            case 3:
                playerClass.SetShipServerRpc(ShipType.Sloop);
                abilityDescription.text = "The Sloop can enter a rapid-fire mode and slows down enemy boats, trapping them in your bombs.";
                abilityUI.SetAbilityIcon(1, sloopIcon);
                break;
        }


    }


    void MoveSelectorTo(RectTransform target)
    {
        selectorImage.position = target.position;
    }

}
