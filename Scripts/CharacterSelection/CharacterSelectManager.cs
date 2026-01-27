using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Networked character selection manager.
/// - Syncs selections across all clients
/// - Prevents duplicate character selection
/// - Tracks ready state
/// </summary>
public class CharacterSelectManager : NetworkBehaviour
{
    public static CharacterSelectManager Instance { get; private set; }

    [SerializeField] private CharacterDatabase characterDatabase;

    // Network synced list of all player selections
    private NetworkList<CharacterSelection> selections;

    // Static storage for spawning (persists between scenes on server)
    private static Dictionary<ulong, int> serverSelections = new Dictionary<ulong, int>();

    public event Action OnSelectionsChanged;

    public CharacterDatabase Database => characterDatabase;

    private void Awake()
    {
        selections = new NetworkList<CharacterSelection>();
    }

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        selections.OnListChanged += HandleSelectionsChanged;

        if (IsServer)
        {
            // Clear previous selections when scene loads
            serverSelections.Clear();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Register existing connected clients
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayer(clientId);
            }
        }

        OnSelectionsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        selections.OnListChanged -= HandleSelectionsChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this)
            Instance = null;
    }

    private void HandleSelectionsChanged(NetworkListEvent<CharacterSelection> changeEvent)
    {
        OnSelectionsChanged?.Invoke();
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
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId) return;
        }

        string playerName = GetPlayerName(clientId);

        selections.Add(new CharacterSelection
        {
            ClientId = clientId,
            CharacterIndex = -1, // Not selected
            IsReady = false,
            PlayerName = playerName
        });

        Debug.Log($"[CharacterSelectManager] Added player: {playerName} (ID: {clientId})");
    }

    private void RemovePlayer(ulong clientId)
    {
        for (int i = selections.Count - 1; i >= 0; i--)
        {
            if (selections[i].ClientId == clientId)
            {
                selections.RemoveAt(i);
                serverSelections.Remove(clientId);
                Debug.Log($"[CharacterSelectManager] Removed player {clientId}");
                break;
            }
        }
    }

    private string GetPlayerName(ulong clientId)
    {
        if (HostSingleton.Instance?.GameManager?.networkServer != null)
        {
            var userData = HostSingleton.Instance.GameManager.networkServer.GetUserDataByClientID(clientId);
            if (userData != null)
                return userData.userName;
        }
        return $"Player {clientId}";
    }

    #region Selection Logic

    /// <summary>
    /// Check if a character is already taken by another player
    /// </summary>
    public bool IsCharacterTaken(int characterIndex, ulong excludeClientId = ulong.MaxValue)
    {
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].CharacterIndex == characterIndex &&
                selections[i].ClientId != excludeClientId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get list of available character indices
    /// </summary>
    public List<int> GetAvailableCharacters(ulong forClientId)
    {
        var available = new List<int>();
        for (int i = 0; i < characterDatabase.CharacterCount; i++)
        {
            if (!IsCharacterTaken(i, forClientId))
            {
                available.Add(i);
            }
        }
        return available;
    }

    /// <summary>
    /// Try to select a character (called by clients)
    /// </summary>
    public void TrySelectCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        // Check locally first to give instant feedback
        if (IsCharacterTaken(characterIndex, localClientId))
        {
            Debug.Log($"[CharacterSelectManager] Character {characterIndex} is already taken!");
            return;
        }

        // Send to server
        SelectCharacterServerRpc(characterIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SelectCharacterServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Validate character isn't taken
        if (IsCharacterTaken(characterIndex, clientId))
        {
            Debug.Log($"[CharacterSelectManager] Server rejected: Character {characterIndex} already taken");
            // Notify client of rejection
            RejectSelectionClientRpc(characterIndex, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
            return;
        }

        // Update selection
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId)
            {
                var sel = selections[i];
                sel.CharacterIndex = characterIndex;
                selections[i] = sel;

                // Store for spawning
                serverSelections[clientId] = characterIndex;

                Debug.Log($"[CharacterSelectManager] Server accepted: Client {clientId} selected character {characterIndex}");
                break;
            }
        }
    }

    [ClientRpc]
    private void RejectSelectionClientRpc(int characterIndex, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[CharacterSelectManager] Selection rejected: Character {characterIndex} is taken");
        OnSelectionsChanged?.Invoke(); // Refresh UI
    }

    #endregion

    #region Ready Logic

    public void SetReady(bool ready)
    {
        SetReadyServerRpc(ready);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(bool ready, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId)
            {
                var sel = selections[i];

                // Can only be ready if character is selected
                if (ready && sel.CharacterIndex < 0)
                {
                    Debug.Log($"[CharacterSelectManager] Client {clientId} cannot ready without selecting character");
                    return;
                }

                sel.IsReady = ready;
                selections[i] = sel;
                Debug.Log($"[CharacterSelectManager] Client {clientId} ready: {ready}");
                break;
            }
        }
    }

    #endregion

    #region Getters

    public CharacterSelection? GetLocalSelection()
    {
        if (NetworkManager.Singleton == null) return null;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == localId)
                return selections[i];
        }
        return null;
    }

    public CharacterSelection? GetSelection(ulong clientId)
    {
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId)
                return selections[i];
        }
        return null;
    }

    public List<CharacterSelection> GetAllSelections()
    {
        var list = new List<CharacterSelection>();
        for (int i = 0; i < selections.Count; i++)
        {
            list.Add(selections[i]);
        }
        return list;
    }

    /// <summary>
    /// Static method for spawning - gets selection from server storage
    /// </summary>
    public static int GetPersistedCharacterIndex(ulong clientId)
    {
        if (serverSelections.TryGetValue(clientId, out int index))
        {
            Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} = {index}");
            return Mathf.Max(0, index);
        }
        Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} not found, returning 0");
        return 0;
    }

    /// <summary>
    /// Called by server to store selection for spawning
    /// </summary>
    public static void SetServerCharacterSelection(ulong clientId, int characterIndex)
    {
        serverSelections[clientId] = characterIndex;
        Debug.Log($"[CharacterSelectManager] SetServerCharacterSelection: Client {clientId} = {characterIndex}");
    }

    #endregion
}

/// <summary>
/// Network serializable struct for player selections
/// </summary>
public struct CharacterSelection : INetworkSerializable, IEquatable<CharacterSelection>
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

    public bool Equals(CharacterSelection other)
    {
        return ClientId == other.ClientId &&
               CharacterIndex == other.CharacterIndex &&
               IsReady == other.IsReady &&
               PlayerName.Equals(other.PlayerName);
    }
}