using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Network manager for character selection. Syncs player selections across all clients.
/// </summary>
public class CharacterSelectManager : NetworkBehaviour
{
    public static CharacterSelectManager Instance { get; private set; }

    [SerializeField] private CharacterDatabase characterDatabase;

    // Stores each player's selection: key = clientId, value = characterIndex (-1 = not selected)
    private NetworkList<PlayerSelectionData> playerSelections;

    public event Action OnPlayerSelectionsChanged;

    public CharacterDatabase Database => characterDatabase;

    private void Awake()
    {
        Instance = this;
        playerSelections = new NetworkList<PlayerSelectionData>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Add existing clients (including host)
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayer(clientId);
            }
        }

        playerSelections.OnListChanged += OnSelectionsListChanged;

        // Initial refresh
        OnPlayerSelectionsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        playerSelections.OnListChanged -= OnSelectionsListChanged;
    }

    private void OnDestroy()
    {
        Instance = null;
    }

    private void OnClientConnected(ulong clientId)
    {
        AddPlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        RemovePlayer(clientId);
    }

    private void AddPlayer(ulong clientId)
    {
        // Check if already exists
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].ClientId == clientId) return;
        }

        playerSelections.Add(new PlayerSelectionData
        {
            ClientId = clientId,
            CharacterIndex = -1,
            IsReady = false,
            PlayerName = GetPlayerName(clientId)
        });
    }

    private void RemovePlayer(ulong clientId)
    {
        for (int i = playerSelections.Count - 1; i >= 0; i--)
        {
            if (playerSelections[i].ClientId == clientId)
            {
                playerSelections.RemoveAt(i);
                break;
            }
        }
    }

    private FixedString32Bytes GetPlayerName(ulong clientId)
    {
        // Try to get from NetworkServer if available (host)
        if (HostSingleton.Instance?.GameManager?.networkServer != null)
        {
            var userData = HostSingleton.Instance.GameManager.networkServer.GetUserDataByClientID(clientId);
            if (userData != null)
                return userData.userName;
        }
        return $"Player {clientId}";
    }

    private void OnSelectionsListChanged(NetworkListEvent<PlayerSelectionData> changeEvent)
    {
        OnPlayerSelectionsChanged?.Invoke();
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectCharacterServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].ClientId == clientId)
            {
                var data = playerSelections[i];
                data.CharacterIndex = characterIndex;
                playerSelections[i] = data;
                break;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetReadyServerRpc(bool ready, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].ClientId == clientId)
            {
                var data = playerSelections[i];
                data.IsReady = ready;
                playerSelections[i] = data;
                break;
            }
        }
    }

    public PlayerSelectionData? GetLocalPlayerSelection()
    {
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].ClientId == localId)
                return playerSelections[i];
        }
        return null;
    }

    public List<PlayerSelectionData> GetAllSelections()
    {
        var list = new List<PlayerSelectionData>();
        for (int i = 0; i < playerSelections.Count; i++)
        {
            list.Add(playerSelections[i]);
        }
        return list;
    }

    public int GetPlayerCharacterIndex(ulong clientId)
    {
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].ClientId == clientId)
                return playerSelections[i].CharacterIndex;
        }
        return 0; // Default to first character
    }
}

/// <summary>
/// Data structure for player selection, must be unmanaged for NetworkList
/// </summary>
public struct PlayerSelectionData : INetworkSerializable, IEquatable<PlayerSelectionData>
{
    public ulong ClientId;
    public int CharacterIndex;
    public bool IsReady;
    public FixedString32Bytes PlayerName;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref CharacterIndex);
        serializer.SerializeValue(ref IsReady);
        serializer.SerializeValue(ref PlayerName);
    }

    public bool Equals(PlayerSelectionData other)
    {
        return ClientId == other.ClientId &&
               CharacterIndex == other.CharacterIndex &&
               IsReady == other.IsReady &&
               PlayerName.Equals(other.PlayerName);
    }
}