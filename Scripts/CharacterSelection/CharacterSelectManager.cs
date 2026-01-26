using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Simplified character selection manager. 
/// Stores selections in static dictionary that persists between scenes.
/// No NetworkObject required - just a regular MonoBehaviour.
/// </summary>
public class CharacterSelectManager : MonoBehaviour
{
    public static CharacterSelectManager Instance { get; private set; }

    // Static storage that persists between scenes - this is the source of truth for spawning
    private static Dictionary<ulong, int> persistedSelections = new Dictionary<ulong, int>();
    private static Dictionary<ulong, string> persistedNames = new Dictionary<ulong, string>();

    [SerializeField] private CharacterDatabase characterDatabase;

    public event Action OnPlayerSelectionsChanged;

    public CharacterDatabase Database => characterDatabase;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Register local player automatically
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;
            string localName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Player");

            if (!persistedNames.ContainsKey(localId))
            {
                persistedNames[localId] = localName;
                persistedSelections[localId] = -1; // Not selected yet
            }

            OnPlayerSelectionsChanged?.Invoke();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Static method to get character index - works even after scene transition
    /// </summary>
    public static int GetPersistedCharacterIndex(ulong clientId)
    {
        if (persistedSelections.TryGetValue(clientId, out int index))
        {
            Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} = {index}");
            return Mathf.Max(0, index); // Return 0 if -1 (not selected)
        }
        Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} not found, returning 0");
        return 0;
    }

    /// <summary>
    /// Clear all selections (call when returning to menu)
    /// </summary>
    public static void ClearAllSelections()
    {
        persistedSelections.Clear();
        persistedNames.Clear();
    }

    /// <summary>
    /// Call this from UI to select a character
    /// </summary>
    public void SelectCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        ulong clientId = NetworkManager.Singleton.LocalClientId;

        persistedSelections[clientId] = characterIndex;
        Debug.Log($"[CharacterSelectManager] SelectCharacter: Client {clientId} selected character {characterIndex}");

        OnPlayerSelectionsChanged?.Invoke();
    }

    /// <summary>
    /// Set ready state
    /// </summary>
    public void SetReady(bool ready)
    {
        Debug.Log($"[CharacterSelectManager] SetReady: {ready}");
        OnPlayerSelectionsChanged?.Invoke();
    }

    /// <summary>
    /// Get all current selections for UI display
    /// </summary>
    public List<PlayerSelectionDisplay> GetAllSelections()
    {
        var list = new List<PlayerSelectionDisplay>();

        foreach (var kvp in persistedSelections)
        {
            string name = persistedNames.TryGetValue(kvp.Key, out string n) ? n : $"Player {kvp.Key}";
            list.Add(new PlayerSelectionDisplay
            {
                ClientId = kvp.Key,
                PlayerName = name,
                CharacterIndex = kvp.Value,
                IsReady = false
            });
        }

        return list;
    }

    /// <summary>
    /// Get local player's current selection
    /// </summary>
    public int GetLocalSelection()
    {
        if (NetworkManager.Singleton == null) return -1;

        ulong clientId = NetworkManager.Singleton.LocalClientId;
        return persistedSelections.TryGetValue(clientId, out int index) ? index : -1;
    }
}

/// <summary>
/// Simple display data for UI
/// </summary>
public struct PlayerSelectionDisplay
{
    public ulong ClientId;
    public string PlayerName;
    public int CharacterIndex;
    public bool IsReady;
}