using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class CharacterSelectUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private CharacterSelectManager selectManager;

    [Header("Character Buttons")]
    [SerializeField] private Transform characterButtonContainer;
    [SerializeField] private CharacterSelectButton characterButtonPrefab;

    [Header("Player List")]
    [SerializeField] private Transform playerCardContainer;
    [SerializeField] private PlayerCard playerCardPrefab;

    [Header("UI Elements")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startGameButton; // Host only
    [SerializeField] private TMP_Text joinCodeText;

    private List<CharacterSelectButton> characterButtons = new List<CharacterSelectButton>();
    private Dictionary<ulong, PlayerCard> playerCards = new Dictionary<ulong, PlayerCard>();
    private int selectedCharacterIndex = -1;
    private bool isReady = false;

    private void Start()
    {
        // Ensure cursor is visible
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CreateCharacterButtons();
        SetupButtons();
        DisplayJoinCode();
        RefreshPlayerList();
    }

    private void OnEnable()
    {
        if (selectManager != null)
            selectManager.OnPlayerSelectionsChanged += RefreshPlayerList;
    }

    private void OnDisable()
    {
        if (selectManager != null)
            selectManager.OnPlayerSelectionsChanged -= RefreshPlayerList;
    }

    private void CreateCharacterButtons()
    {
        if (characterDatabase == null || characterButtonPrefab == null) return;

        // Clear existing
        foreach (Transform child in characterButtonContainer)
        {
            Destroy(child.gameObject);
        }
        characterButtons.Clear();

        // Create button for each character
        for (int i = 0; i < characterDatabase.CharacterCount; i++)
        {
            var data = characterDatabase.GetCharacter(i);
            var button = Instantiate(characterButtonPrefab, characterButtonContainer);
            button.Initialize(this, i, data);
            characterButtons.Add(button);
        }
    }

    private void SetupButtons()
    {
        if (readyButton != null)
            readyButton.onClick.AddListener(OnReadyClicked);

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(OnStartGameClicked);
            // Only host can start game
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            startGameButton.gameObject.SetActive(isHost);
        }

        UpdateReadyButton();
    }

    private void DisplayJoinCode()
    {
        if (joinCodeText == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (isHost)
        {
            var hostInstance = FindObjectOfType<HostSingleton>();
            if (hostInstance != null && hostInstance.GameManager != null)
            {
                string code = hostInstance.GameManager.JoinCode;
                if (!string.IsNullOrEmpty(code))
                {
                    joinCodeText.text = $"Join Code: {code}";
                    joinCodeText.gameObject.SetActive(true);
                    return;
                }
            }
            joinCodeText.text = "Join Code: (check console)";
            joinCodeText.gameObject.SetActive(true);
        }
        else
        {
            joinCodeText.gameObject.SetActive(false);
        }
    }

    public void SelectCharacter(int index)
    {
        // Update local visual
        if (selectedCharacterIndex >= 0 && selectedCharacterIndex < characterButtons.Count)
            characterButtons[selectedCharacterIndex].SetSelected(false);

        selectedCharacterIndex = index;

        if (selectedCharacterIndex >= 0 && selectedCharacterIndex < characterButtons.Count)
            characterButtons[selectedCharacterIndex].SetSelected(true);

        // Store the selection locally
        if (selectManager != null)
        {
            selectManager.SelectCharacter(index);
        }

        // Sync to server (for clients)
        if (CharacterSelectionSync.Instance != null)
        {
            CharacterSelectionSync.Instance.SyncSelection(index);
        }

        UpdateReadyButton();
    }

    private void OnReadyClicked()
    {
        if (selectedCharacterIndex < 0)
        {
            Debug.Log("Please select a character first!");
            return;
        }

        isReady = !isReady;

        if (selectManager != null)
            selectManager.SetReady(isReady);

        UpdateReadyButton();
    }

    private void UpdateReadyButton()
    {
        if (readyButton != null)
            readyButton.interactable = selectedCharacterIndex >= 0;

        if (readyButtonText != null)
            readyButtonText.text = isReady ? "Cancel Ready" : "Ready";
    }

    private void OnStartGameClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        if (selectedCharacterIndex < 0)
        {
            Debug.Log("Please select a character first!");
            return;
        }

        // Make sure host's selection is stored on server side
        ulong hostId = NetworkManager.Singleton.LocalClientId;
        CharacterSelectManager.SetServerCharacterSelection(hostId, selectedCharacterIndex);
        Debug.Log($"[CharacterSelectUI] Host starting game with character {selectedCharacterIndex}");

        // Host starts the game - load Game scene
        NetworkManager.Singleton.SceneManager.LoadScene(
            "Game",
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void RefreshPlayerList()
    {
        if (selectManager == null || playerCardPrefab == null || playerCardContainer == null) return;

        var selections = selectManager.GetAllSelections();
        var activeIds = new HashSet<ulong>();

        foreach (var selection in selections)
        {
            activeIds.Add(selection.ClientId);

            if (!playerCards.TryGetValue(selection.ClientId, out var card))
            {
                card = Instantiate(playerCardPrefab, playerCardContainer);
                bool isLocal = NetworkManager.Singleton != null &&
                               NetworkManager.Singleton.LocalClientId == selection.ClientId;
                card.Initialize(selection.PlayerName, isLocal);
                playerCards[selection.ClientId] = card;
            }

            // Update card
            if (selection.CharacterIndex >= 0 && characterDatabase != null)
            {
                var charData = characterDatabase.GetCharacter(selection.CharacterIndex);
                card.SetCharacter(charData);
            }
            else
            {
                card.SetCharacter(null);
            }

            card.SetReady(selection.IsReady);
        }

        // Remove cards for disconnected players
        var toRemove = new List<ulong>();
        foreach (var kvp in playerCards)
        {
            if (!activeIds.Contains(kvp.Key))
            {
                Destroy(kvp.Value.gameObject);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
        {
            playerCards.Remove(id);
        }
    }

    private void OnDestroy()
    {
        if (readyButton != null)
            readyButton.onClick.RemoveListener(OnReadyClicked);
        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
    }
}