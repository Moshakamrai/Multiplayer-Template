using Mirror;
using Steamworks;
using UnityEngine;

public class SteamLobby : MonoBehaviour
{
    private NetworkManager networkManager;
    
    // Callback for when a lobby is created
    protected Callback<LobbyCreated_t> lobbyCreated;
    // Callback for when a game join request (Invite) is received
    protected Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequested;
    // Callback for when a lobby is entered
    protected Callback<LobbyEnter_t> lobbyEntered;

    private const string HostAddressKey = "HostAddress";

    void Start()
    {
        networkManager = GetComponent<NetworkManager>();

        // DEBUG LOGS
        if (SteamManager.Initialized) 
        {
            Debug.Log("Steam IS active! User: " + SteamFriends.GetPersonaName());
        }
        else
        {
            Debug.LogError("Steam is NOT active. Check steam_appid.txt and ensure Steam is open.");
            return;
        }

        // Subscribe to Steam events
        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
    }

    // 1. HOST: Call this when you click "Host Game" button
    public void HostSteamLobby()
    {
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, networkManager.maxConnections);
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            return;
        }

        networkManager.StartHost();

        // Set the Steam Lobby data so others know who to connect to
        SteamMatchmaking.SetLobbyData(new CSteamID(callback.m_ulSteamIDLobby), HostAddressKey, SteamUser.GetSteamID().ToString());
    }

    // 2. CLIENT: Runs when you accept a Steam Invite
    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t callback)
    {
        if (NetworkServer.active) { return; } // If I am host, don't join myself

        // Get the host's Steam ID from the lobby data
        string hostAddress = SteamMatchmaking.GetLobbyData(new CSteamID(callback.m_ulSteamIDLobby), HostAddressKey);

        // Tell Mirror to connect to that Steam ID
        networkManager.networkAddress = hostAddress;
        networkManager.StartClient();
    }
}