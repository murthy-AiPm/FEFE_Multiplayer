using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HostGameManager : IDisposable
{
    private Allocation allocation;
    private string joinCode;
    private string lobbyId;

    public NetworkServer networkServer { get; private set; }
    public string JoinCode => joinCode; // Expose for UI

    private const int MaxConnections = 20;
    private const string CharacterSelectSceneName = "CharacterSelect";
    private const string GameSceneName = "Game";

    [System.NonSerialized]
    public GameObject characterSelectManagerPrefab; // Assign via code or make this a field on HostSingleton

    public async Task StartHostAsync()
    {
        try
        {
            allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }

        try
        {
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Join Code: {joinCode}");
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
        transport.SetRelayServerData(relayServerData);

        try
        {
            CreateLobbyOptions lobbyOptions = new CreateLobbyOptions();
            lobbyOptions.IsPrivate = false;
            lobbyOptions.Data = new Dictionary<string, DataObject>()
            {
                {
                    "JoinCode", new DataObject(
                        visibility: DataObject.VisibilityOptions.Public,
                        value: joinCode
                    )
                }
            };
            string playerName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Unknown");
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(
                $"{playerName}'s Lobby", MaxConnections, lobbyOptions);

            lobbyId = lobby.Id;

            HostSingleton.Instance.StartCoroutine(HearbeatLobby(15));
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            return;
        }

        // 1. Create NetworkServer FIRST (so approval callback is registered)
        networkServer = new NetworkServer(NetworkManager.Singleton);

        // 2. Set up connection data
        UserData userData = new UserData
        {
            userName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Missing Name"),
            userAuthId = AuthenticationService.Instance.PlayerId,
            characterId = 0 // Will be updated during character selection
        };
        string payload = JsonUtility.ToJson(userData);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = payloadBytes;

        // 3. MANUALLY add the host's user data to the server dictionary
        networkServer.AddHostData(userData);

        // 4. NOW start the host
        NetworkManager.Singleton.StartHost();

        // 5. Load Character Select scene
        NetworkManager.Singleton.SceneManager.LoadScene(CharacterSelectSceneName, LoadSceneMode.Single);

        // 6. Spawn CharacterSelectManager after scene loads
        NetworkManager.Singleton.SceneManager.OnLoadComplete += OnCharacterSelectSceneLoaded;
    }

    private void OnCharacterSelectSceneLoaded(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName == CharacterSelectSceneName && NetworkManager.Singleton.IsServer)
        {
            // Unsubscribe to avoid multiple calls
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnCharacterSelectSceneLoaded;

            // Find and spawn the CharacterSelectManager if it exists in scene
            var manager = GameObject.FindObjectOfType<CharacterSelectManager>();
            if (manager != null)
            {
                var networkObject = manager.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.IsSpawned)
                {
                    networkObject.Spawn();
                    Debug.Log("[HostGameManager] Spawned CharacterSelectManager");
                }
            }
            else
            {
                Debug.LogError("[HostGameManager] CharacterSelectManager not found in scene!");
            }
        }
    }

    /// <summary>
    /// Call this from CharacterSelectUI when host clicks Start Game
    /// </summary>
    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        // Lock the lobby so no new players can join mid-game
        LockLobby();

        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
    }

    private async void LockLobby()
    {
        if (string.IsNullOrEmpty(lobbyId)) return;

        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions
            {
                IsLocked = true
            });
            Debug.Log("[HostGameManager] Lobby locked - no new players can join");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"[HostGameManager] Failed to lock lobby: {e}");
        }
    }

    private IEnumerator HearbeatLobby(float waitTimeSeconds)
    {
        WaitForSecondsRealtime delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return delay;
        }
    }

    public async void ShutDown()
    {
        // Safely stop the heartbeat coroutine
        if (HostSingleton.Instance != null)
        {
            HostSingleton.Instance.StopCoroutine(nameof(HearbeatLobby));
        }

        if (!string.IsNullOrEmpty(lobbyId))
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            }
            catch (LobbyServiceException e)
            {
                Debug.Log(e);
            }

            lobbyId = string.Empty;
        }

        if (networkServer != null)
        {
            networkServer.OnClientLeft -= HandleClientLeft;
            networkServer.Dispose();
            networkServer = null;
        }
    }

    private async void HandleClientLeft(string authId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, authId);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public void Dispose()
    {
        ShutDown();
    }
}