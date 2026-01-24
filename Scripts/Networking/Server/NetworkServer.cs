using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkServer : IDisposable
{
    private NetworkManager networkManager;
    private GameObject playerPrefabReference; // Store the prefab

    private Dictionary<ulong, string> clientIdToAuth = new Dictionary<ulong, string>();
    private Dictionary<string, UserData> authIdToUserData = new Dictionary<string, UserData>();

    public NetworkServer(NetworkManager networkManager)
    {
        this.networkManager = networkManager;

        // Store the player prefab reference BEFORE we disable auto-spawning
        playerPrefabReference = networkManager.NetworkConfig.PlayerPrefab;

        // Disable automatic player spawning - we'll handle it manually
        networkManager.NetworkConfig.PlayerPrefab = null;

        networkManager.ConnectionApprovalCallback += ApprovalCheck;
        networkManager.OnServerStarted += OnNetworkReady;

        Debug.Log("[NetworkServer] NetworkServer created, automatic spawning disabled");
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log($"[NetworkServer] ApprovalCheck called for client {request.ClientNetworkId}");

        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        UserData userData = JsonUtility.FromJson<UserData>(payload);

        clientIdToAuth[request.ClientNetworkId] = userData.userAuthId;
        authIdToUserData[userData.userAuthId] = userData;

        response.Approved = true;
        response.CreatePlayerObject = false; // We'll create it manually with correct position

        Debug.Log($"[NetworkServer] Approval granted. CreatePlayerObject = false. Will spawn manually.");

        // Spawn player with custom position immediately
        if (response.Approved)
        {
            SpawnPlayerAtSpawnPoint(request.ClientNetworkId);
        }
    }

    private void SpawnPlayerAtSpawnPoint(ulong clientId)
    {
        Debug.Log($"[NetworkServer] SpawnPlayerAtSpawnPoint called for client {clientId}");

        // Get spawn position from PlayerSpawnManager
        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (PlayerSpawnManager.Instance != null)
        {
            Debug.Log($"[NetworkServer] PlayerSpawnManager found!");
            if (PlayerSpawnManager.Instance.TryGetSpawnPoint(clientId, out spawnPosition, out spawnRotation))
            {
                Debug.Log($"[NetworkServer] Got spawn point at {spawnPosition}");
            }
            else
            {
                Debug.LogWarning($"[NetworkServer] No spawn point available, using default");
            }
        }
        else
        {
            Debug.LogError("[NetworkServer] PlayerSpawnManager not found in scene!");
        }

        // Use our stored prefab reference instead of NetworkConfig.PlayerPrefab (which is now null)
        Debug.Log($"[NetworkServer] Player prefab: {(playerPrefabReference != null ? playerPrefabReference.name : "NULL")}");

        if (playerPrefabReference != null)
        {
            // Instantiate the player at the spawn point
            GameObject playerInstance = UnityEngine.Object.Instantiate(
                playerPrefabReference,
                spawnPosition,
                spawnRotation
            );

            Debug.Log($"[NetworkServer] Instantiated player '{playerInstance.name}' at {playerInstance.transform.position}");

            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                // Spawn as player object for this client
                networkObject.SpawnAsPlayerObject(clientId, true);
                Debug.Log($"[NetworkServer] ✓ SUCCESS: Spawned player for client {clientId} at {spawnPosition}");
            }
            else
            {
                Debug.LogError("[NetworkServer] Player prefab missing NetworkObject component!");
                UnityEngine.Object.Destroy(playerInstance);
            }
        }
        else
        {
            Debug.LogError("[NetworkServer] No player prefab reference stored!");
        }
    }

    private void OnNetworkReady()
    {
        Debug.Log("[NetworkServer] OnNetworkReady called - Server started");
        networkManager.OnClientDisconnectCallback += OnClientDisconnect;

        // Handle spawning the host player (clientId 0)
        // The host doesn't go through ApprovalCheck, so we handle it here
        if (networkManager.IsHost)
        {
            Debug.Log("[NetworkServer] Is Host - will spawn host player after scene loads");
            networkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
        }
    }

    private void OnSceneLoadCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        Debug.Log($"[NetworkServer] Scene '{sceneName}' loaded, spawning host player");

        // Unsubscribe to avoid multiple calls
        networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;

        // Spawn the host player at a spawn point
        SpawnPlayerAtSpawnPoint(networkManager.LocalClientId);
    }

    private void OnClientDisconnect(ulong clientId)
    {
        Debug.Log($"[NetworkServer] Client {clientId} disconnected");

        // Release the spawn point so it can be reused
        if (PlayerSpawnManager.Instance != null)
        {
            PlayerSpawnManager.Instance.ReleaseSpawnPoint(clientId);
        }

        if (clientIdToAuth.TryGetValue(clientId, out string authId))
        {
            clientIdToAuth.Remove(clientId);
            authIdToUserData.Remove(authId);
        }
    }

    public void Dispose()
    {
        if (networkManager == null) { return; }

        networkManager.ConnectionApprovalCallback -= ApprovalCheck;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        networkManager.OnServerStarted -= OnNetworkReady;

        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
        }

        if (networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }
}