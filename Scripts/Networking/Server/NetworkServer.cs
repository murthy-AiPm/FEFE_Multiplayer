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
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log($"[NetworkServer] Client {request.ClientNetworkId} requesting to join");

        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        UserData userData = JsonUtility.FromJson<UserData>(payload);

        clientIdToAuth[request.ClientNetworkId] = userData.userAuthId;
        authIdToUserData[userData.userAuthId] = userData;

        response.Approved = true;
        response.CreatePlayerObject = false;

        Debug.Log($"[NetworkServer] Client {request.ClientNetworkId} approved, spawning player");

        if (response.Approved)
        {
            SpawnPlayerAtSpawnPoint(request.ClientNetworkId);
        }
    }

    private void SpawnPlayerAtSpawnPoint(ulong clientId)
    {
        Debug.Log($"[NetworkServer] SpawnPlayerAtSpawnPoint for client {clientId}");

        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (PlayerSpawnManager.Instance != null)
        {
            if (PlayerSpawnManager.Instance.TryGetSpawnPoint(clientId, out spawnPosition, out spawnRotation))
            {
                Debug.Log($"[NetworkServer] Got spawn point at {spawnPosition}");
            }
            else
            {
                Debug.LogError($"[NetworkServer] Failed to get spawn point for client {clientId}");
            }
        }
        else
        {
            Debug.LogError("[NetworkServer] PlayerSpawnManager is NULL!");
        }

        if (playerPrefabReference != null)
        {
            Debug.Log($"[NetworkServer] Instantiating player at {spawnPosition}");

            GameObject playerInstance = UnityEngine.Object.Instantiate(
                playerPrefabReference,
                spawnPosition,
                spawnRotation
            );

            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                Debug.Log($"[NetworkServer] Spawning NetworkObject for client {clientId}");
                networkObject.SpawnAsPlayerObject(clientId, true);
                Debug.Log($"[NetworkServer] SUCCESS: Player spawned for client {clientId}");
            }
            else
            {
                Debug.LogError("[NetworkServer] NetworkObject component missing!");
                UnityEngine.Object.Destroy(playerInstance);
            }
        }
        else
        {
            Debug.LogError("[NetworkServer] playerPrefabReference is NULL!");
        }
    }

    private void OnNetworkReady()
    {
        networkManager.OnClientDisconnectCallback += OnClientDisconnect;

        if (networkManager.IsHost)
        {
            networkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
        }
    }

    private void OnSceneLoadCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
        SpawnPlayerAtSpawnPoint(networkManager.LocalClientId);
    }

    private void OnClientDisconnect(ulong clientId)
    {
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