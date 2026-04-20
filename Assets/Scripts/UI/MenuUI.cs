using System.Linq;
using Mirror;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MenuUI : MonoBehaviour
    {
        private Button _hostButton;
        private Button _joinButton;
        private Button _quitButton;
        private Button _beatMapperButton;
        private Button _autoBeatMapperButton;
        private TextField _nameTextField;
        private TextField _addressTextField;
        private Label _steamStatusLabel;

        [Header("Networking Mode")]
        [Tooltip("Uncheck this to test locally without Steam")]
        public bool UseSteam = false;

        public SteamLobby steamLobby;

        [Header("Offline / Solo Transport")]
        [Tooltip("Assign a KcpTransport component here for offline / LAN play. " +
                 "If left empty the script will auto-discover one on the NetworkManager.")]
        public Transport offlineTransport;

        [Header("Background")]
        [Tooltip("Assign the RawImage component that sits on the background Canvas.")]
        public UnityEngine.UI.RawImage backgroundRawImage;
        [Tooltip("Drag the background Texture2D here.")]
        public Texture2D backgroundImage;

        private void Start()
        {
            if (backgroundRawImage != null && backgroundImage != null)
                backgroundRawImage.texture = backgroundImage;

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            _hostButton       = root.Q<Button>("HostButton");
            _joinButton       = root.Q<Button>("JoinButton");
            _quitButton       = root.Q<Button>("QuitButton");
            _beatMapperButton     = root.Q<Button>("BeatMapperButton");
            _autoBeatMapperButton = root.Q<Button>("AutoBeatMapperButton");
            _nameTextField    = root.Q<TextField>("NameTextField");
            _addressTextField = root.Q<TextField>("AddressTextField");
            _steamStatusLabel = root.Q<Label>("SteamStatusLabel");

            // Auto-detect Steam — override the Inspector toggle so it's never
            // wrong when the game is launched without Steam running.
            UseSteam = UseSteam && SteamManager.Initialized;
            ApplySteamStatus();

            if (UseSteam && steamLobby != null)
                _hostButton.clicked += steamLobby.HostSteamLobby;
            else
                _hostButton.clicked += HostGame;

            _joinButton.clicked += JoinGame;

            _quitButton.clicked += Application.Quit;
            _beatMapperButton.clicked += LoadBeatMapper;
            if (_autoBeatMapperButton != null) _autoBeatMapperButton.clicked += LoadAutoBeatMapper;

            _nameTextField.value = GameManager.PlayerName;
            _addressTextField.value = NetworkManager.singleton.networkAddress;

            _nameTextField.RegisterValueChangedCallback(NameChanged);
            _addressTextField.RegisterValueChangedCallback(AddressChanged);
        }

        private void OnDestroy()
        {
            try
            {
                if (_hostButton != null)
                {
                    if (UseSteam && steamLobby != null) _hostButton.clicked -= steamLobby.HostSteamLobby;
                    else _hostButton.clicked -= HostGame;
                }

                if (_joinButton != null)
                    _joinButton.clicked -= JoinGame;

                if (_quitButton != null)
                    _quitButton.clicked -= Application.Quit;

                if (_beatMapperButton != null)
                    _beatMapperButton.clicked -= LoadBeatMapper;

                if (_autoBeatMapperButton != null) _autoBeatMapperButton.clicked -= LoadAutoBeatMapper;

                _nameTextField?.UnregisterValueChangedCallback(NameChanged);
                _addressTextField?.UnregisterValueChangedCallback(AddressChanged);
            }
            catch { }
        }

        // ── Transport swap ────────────────────────────────────────────────
        // When Steam is offline the active transport is likely FizzySteamworks
        // which crashes even on StartHost. Swap to KcpTransport first.
        // The transport MUST live on the persistent NetworkManager GameObject;
        // if it's on the Menu UI object it will be destroyed on scene load.
        private void SwapToOfflineTransport()
        {
            if (SteamManager.Initialized) return;

            GameObject nmGO = NetworkManager.singleton.gameObject;

            // 1. Prefer a non-steam transport already on the persistent NetworkManager.
            Transport target = nmGO.GetComponents<Transport>()
                .FirstOrDefault(t =>
                {
                    string n = t.GetType().Name.ToLowerInvariant();
                    return !n.Contains("steam") && !n.Contains("fizzy");
                });

            // 2. Fall back to the Inspector-assigned field.
            if (target == null)
                target = offlineTransport;

            if (target == null)
            {
                Debug.LogError("[MenuUI] No offline transport found. " +
                    "Add a KcpTransport component to the GameManager GameObject.");
                return;
            }

            // 3. If the chosen transport is NOT on the persistent NetworkManager, copy
            //    its type onto the NetworkManager so it survives scene loads.
            if (target.gameObject != nmGO)
            {
                Transport existing = nmGO.GetComponent(target.GetType()) as Transport;
                if (existing == null)
                    existing = nmGO.AddComponent(target.GetType()) as Transport;
                target = existing;
                Debug.Log($"[MenuUI] Added {target.GetType().Name} to persistent NetworkManager for offline play.");
            }

            Transport.active = target;
            NetworkManager.singleton.transport = target;
            Debug.Log($"[MenuUI] Transport set to {target.GetType().Name} for offline play.");
        }

        private void HostGame()
        {
            SwapToOfflineTransport();
            NetworkManager.singleton.StartHost();
        }

        private void JoinGame()
        {
            SwapToOfflineTransport();
            NetworkManager.singleton.StartClient();
        }

        // ── Status label ──────────────────────────────────────────────────
        private void ApplySteamStatus()
        {
            if (_steamStatusLabel != null)
            {
                if (SteamManager.Initialized)
                {
                    _steamStatusLabel.text = "● Steam Online";
                    _steamStatusLabel.style.color = new StyleColor(new Color(0.2f, 0.8f, 0.3f));
                }
                else
                {
                    _steamStatusLabel.text = "● Steam Offline — Solo / LAN only";
                    _steamStatusLabel.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
                }
            }

            if (!SteamManager.Initialized && _joinButton != null)
                _joinButton.text = "Join (LAN)";
        }

        private void LoadBeatMapper() => SceneManager.LoadScene("TrackEditor");
        private void LoadAutoBeatMapper() => SceneManager.LoadScene("RhythmTest");

        // ── Value callbacks ───────────────────────────────────────────────
        private void NameChanged(ChangeEvent<string> evt)
        {
            if (string.IsNullOrWhiteSpace(evt.newValue)) {
                GameManager.SetPlayerName(GameManager.DefaultPlayerName); return;
            }
            string playerName = new(evt.newValue.Trim().ToCharArray().Where(x => !char.IsWhiteSpace(x)).ToArray());
            if (playerName != evt.newValue) _nameTextField.value = playerName;
            GameManager.SetPlayerName(playerName);
            PlayerPrefs.SetString(nameof(GameManager.PlayerName), playerName);
        }

        private void AddressChanged(ChangeEvent<string> evt)
        {
            if (string.IsNullOrWhiteSpace(evt.newValue)) {
                NetworkManager.singleton.networkAddress = GameManager.DefaultAddress; return;
            }
            string networkAddress = new(evt.newValue.Trim().ToCharArray().Where(x => !char.IsWhiteSpace(x)).ToArray());
            if (networkAddress != evt.newValue) _addressTextField.value = networkAddress;
            NetworkManager.singleton.networkAddress = networkAddress;
            PlayerPrefs.SetString(nameof(NetworkManager.singleton.networkAddress), networkAddress);
        }
    }
}
