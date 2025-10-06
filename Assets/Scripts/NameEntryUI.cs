using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class NameEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private string nextSceneName = "GameScene";

    public void OnOkClicked()
    {
        string trimmedName = nameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            Debug.LogWarning("Player name is empty!");
            return;
        }

        PlayerPrefs.SetString("PlayerName", trimmedName);
        SceneManager.LoadScene(nextSceneName);
    }
}
