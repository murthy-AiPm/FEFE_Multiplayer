using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkServer : IDisposable
{
    private NetworkManager networkManager;

    public Action<string> OnClientLeft;

    private Dictionary<ulong, string> clientIdToAuth = new Dictionary<ulong, string>();
    private Dictionary<string, UserData> authIdToUserData = new Dictionary<string, UserData>();

    public NetworkServer(NetworkManager networkManager)
    {
        this.networkManager = networkManager;

        networkManager.ConnectionApprovalCallback += ApprovalCheck;
        networkManager.OnServerStarted += OnNetworkReady;
    }

    // Manually register the host's data
    public void AddHostData(UserData userData)
    {
        // Host is always clientId 0
        clientIdToAuth[0] = userData.userAuthId;
        authIdToUserData[userData.userAuthId] = userData;

        // Store host's character selection
        CharacterSelectManager.SetServerCharacterSelection(0, userData.characterId);

        Debug.Log($"[NetworkServer] Added host data: {userData.userName} (AuthId: {userData.userAuthId}, Character: {userData.characterId})");
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        UserData userData = JsonUtility.FromJson<UserData>(payload);

        clientIdToAuth[request.ClientNetworkId] = userData.userAuthId;
        authIdToUserData[userData.userAuthId] = userData;

        // Store character selection for spawning
        CharacterSelectManager.SetServerCharacterSelection(request.ClientNetworkId, userData.characterId);

        Debug.Log($"[NetworkServer] ApprovalCheck - ClientId: {request.ClientNetworkId}, Name: {userData.userName}, Character: {userData.characterId}");

        response.Approved = true;
        response.CreatePlayerObject = false; // We handle spawning manually via CharacterSpawnHandler
    }

    private void OnNetworkReady()
    {
        networkManager.OnClientDisconnectCallback += OnClientDisconnect;
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (clientIdToAuth.TryGetValue(clientId, out string authId))
        {
            clientIdToAuth.Remove(clientId);
            authIdToUserData.Remove(authId);
            OnClientLeft?.Invoke(authId);
        }
    }

    public UserData GetUserDataByClientID(ulong clientId)
    {
        Debug.Log($"[NetworkServer] GetUserDataByClientID called for clientId: {clientId}");
        Debug.Log($"[NetworkServer] clientIdToAuth contains: {string.Join(", ", clientIdToAuth.Keys)}");

        if (clientIdToAuth.TryGetValue(clientId, out string authID))
        {
            if (authIdToUserData.TryGetValue(authID, out UserData data))
            {
                return data;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (networkManager == null) { return; }

        networkManager.ConnectionApprovalCallback -= ApprovalCheck;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        networkManager.OnServerStarted -= OnNetworkReady;

        if (networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }
}