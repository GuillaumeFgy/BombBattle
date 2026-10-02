using TMPro;
using Unity.Multiplayer.Widgets;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits next to the Multiplayer Widgets "Create Session" widget so the host only has to press Create.
/// The widget reads the session name from a child TMP_InputField and only enables its button once that
/// field has text, so this script provides an invisible field (or hides an existing one) and fills it
/// with a random name.
/// </summary>
[DefaultExecutionOrder(-1000)] // Awake must run before CreateSession.OnEnable looks up the input field.
public class AutoSessionName : MonoBehaviour
{
    static readonly string[] Adjectives = { "Salty", "Stormy", "Rusty", "Jolly", "Sneaky", "Mighty", "Crimson", "Rowdy" };
    static readonly string[] Nouns = { "Kraken", "Parrot", "Cannon", "Anchor", "Barrel", "Gull", "Plank", "Reef" };

    TMP_InputField inputField;

    void Awake()
    {
        inputField = GetComponentInChildren<TMP_InputField>(true);
        if (inputField != null) return;

        // No name box in the UI: create a hidden one. The component stays disabled so it never
        // renders, takes input or occupies layout space; the widget only reads its text.
        var go = new GameObject("Hidden Session Name", typeof(RectTransform), typeof(LayoutElement));
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        inputField = go.AddComponent<TMP_InputField>();
        inputField.enabled = false;
        go.SetActive(true); // Must be active for the widget's GetComponentInChildren to find it.
    }

    void Start()
    {
        // A name box placed in the scene: the widget has cached it in OnEnable, so hiding it is safe.
        if (inputField.enabled) inputField.gameObject.SetActive(false);
    }

    void Update()
    {
        // The widget only enables its Create button from the input's onValueChanged listener,
        // which it registers once services are initialized, so wait before setting the text.
        if (!WidgetServiceInitialization.IsInitialized) return;

        inputField.text = $"{Adjectives[Random.Range(0, Adjectives.Length)]} {Nouns[Random.Range(0, Nouns.Length)]} {Random.Range(100, 1000)}";
        enabled = false;
    }
}
