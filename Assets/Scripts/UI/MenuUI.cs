using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MenuUI : MonoBehaviour
    {
        private Button _hostButton;
        private Button _joinButton;
        private Button _quitButton;
        private TextField _nameTextField;
        private TextField _addressTextField;

        // NEW: Reference to the SteamLobby script
        public SteamLobby steamLobby; 

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            
            _hostButton = root.Q<Button>("HostButton");
            _joinButton = root.Q<Button>("JoinButton");
            _quitButton = root.Q<Button>("QuitButton");
            _nameTextField = root.Q<TextField>("NameTextField");
            _addressTextField = root.Q<TextField>("AddressTextField");

            // CRITICAL CHANGE: Call SteamLobby instead of NetworkManager directly
            if (steamLobby != null)
            {
                _hostButton.clicked += steamLobby.HostSteamLobby;
            }
            else
            {
                Debug.LogError("SteamLobby script is missing from MenuUI inspector!");
                // Fallback (Will likely fail for Steam, but works for local)
                _hostButton.clicked += NetworkManager.singleton.StartHost;
            }

            // Note: Join button is usually not needed for Steam (you join via Friend List), 
            // but we keep it for direct IP testing if needed.
            _joinButton.clicked += NetworkManager.singleton.StartClient;
            
            _quitButton.clicked += Application.Quit;

            _nameTextField.value = GameManager.PlayerName;
            _addressTextField.value = NetworkManager.singleton.networkAddress;

            _nameTextField.RegisterValueChangedCallback(NameChanged);
            _addressTextField.RegisterValueChangedCallback(AddressChanged);
        }

        private void OnDestroy()
        {
            try
            {
                if (_hostButton != null && steamLobby != null) 
                    _hostButton.clicked -= steamLobby.HostSteamLobby;
                    
                if (_joinButton != null) 
                    _joinButton.clicked -= NetworkManager.singleton.StartClient;

                if (_quitButton != null) 
                    _quitButton.clicked -= Application.Quit;

                _nameTextField?.UnregisterValueChangedCallback(NameChanged);
                _addressTextField?.UnregisterValueChangedCallback(AddressChanged);
            }
            catch { }
        }

        // ... (Keep the rest of your NameChanged / AddressChanged logic exactly the same) ...
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