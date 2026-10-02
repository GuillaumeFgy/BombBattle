using Unity.Netcode;
using UnityEngine;

// Browser builds only: a hidden tab stops running the game, and when that tab is the host the match
// freezes for every player. Warns the host when hosting starts, and again after it detects a freeze.
// Created automatically at startup in WebGL builds; nothing to place in a scene.
public class WebHostTabWarning : MonoBehaviour
{
    const float IntroDuration = 15f;     // seconds the warning stays up after hosting starts
    const float FreezeDuration = 8f;     // seconds the "match froze" message stays up
    const float FreezeThreshold = 3f;    // a frame gap longer than this means the tab was hidden (above scene-load hitches)

    bool wasHost;
    float showUntil;
    float lastFrameTime;
    string message;
    GUIStyle style;

#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject(nameof(WebHostTabWarning));
        DontDestroyOnLoad(go);
        go.AddComponent<WebHostTabWarning>();
    }
#endif

    void Update()
    {
        float now = Time.realtimeSinceStartup;
        float gap = now - lastFrameTime;
        lastFrameTime = now;

        var nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsHost;

        if (isHost && !wasHost)
            Show("You are hosting: keep this tab visible, or the match freezes for everyone.", IntroDuration);
        else if (isHost && gap > FreezeThreshold)
            Show($"The match froze for everyone for {gap:0} s because this tab was hidden. Keep it visible while hosting.", FreezeDuration);

        if (!isHost)
            showUntil = 0f;
        wasHost = isHost;
    }

    void Show(string text, float duration)
    {
        message = text;
        showUntil = Time.realtimeSinceStartup + duration;
    }

    void OnGUI()
    {
        if (Time.realtimeSinceStartup > showUntil)
            return;

        style ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.85f, 0.3f) }
        };

        float width = Mathf.Min(640f, Screen.width - 32f);
        GUI.Box(new Rect((Screen.width - width) / 2f, 16f, width, 56f), message, style);
    }
}
