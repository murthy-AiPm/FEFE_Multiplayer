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

        // 5. Load Game scene (everyone will use the in-game character select overlay)
        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);

        // 6. Spawn CharacterSelectManager after scene loads
        NetworkManager.Singleton.SceneManager.OnLoadComplete += OnGameSceneLoaded;
    }

    private void OnGameSceneLoaded(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName == GameSceneName && NetworkManager.Singleton.IsServer)
        {
            // Unsubscribe to avoid multiple calls
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;

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
    /// Optional: call this if you still want a "Start Game" button somewhere.
    /// If you are already in the Game scene, this just locks the lobby.
    /// </summary>
    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        LockLobby();
    }

    private async void LockLobby()
    {
        if (string.IsNullOrEmpty(lobbyId)) return;

        try
        {
            var options = new UpdateLobbyOptions { IsLocked = true };
            await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
            Debug.Log("[HostGameManager] Lobby locked");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostGameManager] Failed to lock lobby: {e}");
        }
    }

    private IEnumerator HearbeatLobby(float waitTimeSeconds)
    {
        var wait = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return wait;
        }
    }

    public void Dispose()
    {
        // nothing special here currently
    }

    public async void ShutDown()
    {
        // 1) Dispose server FIRST (important: this should clear ConnectionApprovalCallback)
        try
        {
            networkServer?.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostGameManager] networkServer.Dispose failed: {e.Message}");
        }
        finally
        {
            networkServer = null;
        }

        // 2) Shutdown Netcode
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 3) Extra safety: clear approval callback if anything left it set
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }

        // 4) Best-effort: delete lobby
        if (!string.IsNullOrEmpty(lobbyId))
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HostGameManager] DeleteLobby failed: {e.Message}");
            }
        }

        // 5) Clear local state
        lobbyId = null;
        joinCode = null;
    }


}
