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

    private Coroutine heartbeatCoroutine;

    public NetworkServer networkServer { get; private set; }
    public string JoinCode => joinCode;

    private const int MaxConnections = 20;
    private const string GameSceneName = "Game";

    public async Task StartHostAsync()
    {
        // Guard: if you destroyed NetworkManager elsewhere, fail loudly instead of NRE
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[HostGameManager] NetworkManager.Singleton is null. " +
                           "Option A requires NetworkManager to exist (don�t destroy it on shutdown).");
            return;
        }

        try
        {
            allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Join Code: {joinCode}");
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }

        // Relay transport setup
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
        transport.SetRelayServerData(relayServerData);

        // Create lobby
        try
        {
            CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        "JoinCode",
                        new DataObject(DataObject.VisibilityOptions.Public, joinCode)
                    }
                }
            };

            string playerName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Unknown");
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(
                $"{playerName}'s Lobby", MaxConnections, lobbyOptions);

            lobbyId = lobby.Id;

            // Start heartbeat (store handle so we can stop it on shutdown)
            StopHeartbeat();
            heartbeatCoroutine = HostSingleton.Instance.StartCoroutine(HeartbeatLobby(15));
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            return;
        }

        // Server setup
        networkServer = new NetworkServer(NetworkManager.Singleton);

        // Connection payload
        UserData userData = new UserData
        {
            userName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Missing Name"),
            userAuthId = AuthenticationService.Instance.PlayerId,
            characterId = 0
        };

        string payload = JsonUtility.ToJson(userData);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);

        // Add host data for approval dictionary
        networkServer.AddHostData(userData);

        // Start host first (this initializes SceneManager)
        NetworkManager.Singleton.StartHost();

        // IMPORTANT: prevent duplicate handler if hosting multiple times
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnGameSceneLoaded;
        }

        // Load scene
        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
    }

    private void OnGameSceneLoaded(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName == GameSceneName && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;

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

    private IEnumerator HeartbeatLobby(float waitTimeSeconds)
    {
        var wait = new WaitForSecondsRealtime(waitTimeSeconds);

        while (!string.IsNullOrEmpty(lobbyId))
        {
            // Fire-and-forget heartbeat; keep loop alive even if a call fails
            try { LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
            catch { /* ignore */ }

            yield return wait;
        }
    }

    private void StopHeartbeat()
    {
        if (heartbeatCoroutine != null && HostSingleton.Instance != null)
        {
            HostSingleton.Instance.StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }
    }

    public void Dispose()
    {
        // Nothing special currently (shutdown is explicit)
    }

    public async void ShutDown()
    {
        // Stop heartbeat first (prevents errors / spam after leaving lobby)
        StopHeartbeat();

        // Unsubscribe scene handler (prevents stacking across sessions)
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;

        // Dispose server
        try { networkServer?.Dispose(); }
        catch (Exception e) { Debug.LogWarning(e.Message); }
        networkServer = null;

        // Shutdown netcode (DO NOT destroy NetworkManager in Option A)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        // Clear approval callback safety
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.ConnectionApprovalCallback = null;

        // Best effort delete lobby
        if (!string.IsNullOrEmpty(lobbyId))
        {
            try { await LobbyService.Instance.DeleteLobbyAsync(lobbyId); }
            catch (Exception e) { Debug.LogWarning($"DeleteLobby failed: {e.Message}"); }
        }

        lobbyId = null;
        joinCode = null;
        allocation = default;
    }
}