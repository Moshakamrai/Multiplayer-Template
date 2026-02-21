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

        [Header("Networking Mode")]
        [Tooltip("Uncheck this to test locally without Steam")]
        public bool UseSteam = false; 
        
        public SteamLobby steamLobby; 

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            
            _hostButton = root.Q<Button>("HostButton");
            _joinButton = root.Q<Button>("JoinButton");
            _quitButton = root.Q<Button>("QuitButton");
            _nameTextField = root.Q<TextField>("NameTextField");
            _addressTextField = root.Q<TextField>("AddressTextField");

            // --- LOCAL VS STEAM TOGGLE ---
            if (UseSteam && steamLobby != null)
            {
                _hostButton.clicked += steamLobby.HostSteamLobby;
            }
            else
            {
                // Local Hosting bypasses Steam entirely
                _hostButton.clicked += NetworkManager.singleton.StartHost;
            }

            // Join button for Local Testing (Requires 'localhost' in address field)
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
                if (_hostButton != null)
                {
                    if (UseSteam && steamLobby != null) _hostButton.clicked -= steamLobby.HostSteamLobby;
                    else _hostButton.clicked -= NetworkManager.singleton.StartHost;
                }
                    
                if (_joinButton != null) 
                    _joinButton.clicked -= NetworkManager.singleton.StartClient;

                if (_quitButton != null) 
                    _quitButton.clicked -= Application.Quit;

                _nameTextField?.UnregisterValueChangedCallback(NameChanged);
                _addressTextField?.UnregisterValueChangedCallback(AddressChanged);
            }
            catch { }
        }

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