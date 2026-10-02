using System;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Unity.Multiplayer.Widgets
{
    internal class ShowJoinCode : WidgetBehaviour, ISessionLifecycleEvents, ISessionProvider
    {
        const string k_NoCode = "–";

        public ISession Session { get; set; }

        [SerializeField]
        TMP_Text m_Text;
        [SerializeField]
        Button m_CopyCodeButton;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void WidgetsArmClipboardCopy(string text);
#endif

        void Start()
        {
            if(m_Text == null)
                m_Text = GetComponentInChildren<TMP_Text>();
            if(m_CopyCodeButton == null)
                m_CopyCodeButton = GetComponentInChildren<Button>();

            m_CopyCodeButton.onClick.AddListener(CopySessionCodeToClipboard);

#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser only lets a page write the clipboard during a user gesture: arm the copy on press, the
            // browser performs it on release (see WidgetsClipboard.jslib).
            m_CopyCodeButton.gameObject.AddComponent<PointerDownRelay>().PointerDown = ArmBrowserCopy;
#endif
        }

        public override void OnServicesInitialized()
        {
            m_CopyCodeButton.interactable = false;
        }

        public void OnSessionJoined()
        {
            m_Text.text = Session?.Code ?? k_NoCode;
            m_CopyCodeButton.interactable = true;
        }

        public void OnSessionLeft()
        {
            m_Text.text = k_NoCode;
            m_CopyCodeButton.interactable = false;
        }

        bool TryGetCode(out string code)
        {
            code = m_Text.text;
            return Session?.Code != null && !string.IsNullOrEmpty(code);
        }

        void CopySessionCodeToClipboard()
        {
            // Deselect the button when clicked.
            EventSystem.current.SetSelectedGameObject(null);

            if (!TryGetCode(out var code))
            {
                return;
            }

            // Copy the text to the clipboard. On WebGL this doesn't reach the browser's clipboard: the copy is armed
            // on pointer down instead (ArmBrowserCopy).
            GUIUtility.systemCopyBuffer = code;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        void ArmBrowserCopy()
        {
            if (m_CopyCodeButton.interactable && TryGetCode(out var code))
                WidgetsArmClipboardCopy(code);
        }

        class PointerDownRelay : MonoBehaviour, IPointerDownHandler
        {
            public Action PointerDown;

            public void OnPointerDown(PointerEventData eventData) => PointerDown?.Invoke();
        }
#endif
    }
}
