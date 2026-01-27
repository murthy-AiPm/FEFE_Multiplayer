using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class CharacterSelectUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterDatabase characterDatabase;

    [Header("Character Buttons")]
    [SerializeField] private Transform characterButtonContainer;
    [SerializeField] private CharacterSelectButton characterButtonPrefab;

    [Header("Player List")]
    [SerializeField] private Transform playerCardContainer;
    [SerializeField] private PlayerCard playerCardPrefab;

    [Header("UI Elements")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startGameButton;
    [SerializeField] private TMP_Text joinCodeText;

    private List<CharacterSelectButton> characterButtons = new List<CharacterSelectButton>();
    private Dictionary<ulong, PlayerCard> playerCards = new Dictionary<ulong, PlayerCard>();
    private int selectedCharacterIndex = -1;
    private bool isReady = false;

    private CharacterSelectManager selectManager;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CreateCharacterButtons();
        SetupButtons();
        DisplayJoinCode();

        // Find the manager (spawned NetworkObject)
        StartCoroutine(WaitForManager());

        Debug.Log($"[CharacterSelectUI] Start - NetworkManager exists: {NetworkManager.Singleton != null}");
        Debug.Log($"[CharacterSelectUI] Start - IsHost: {NetworkManager.Singleton?.IsHost}");
        Debug.Log($"[CharacterSelectUI] Start - CharacterSelectManager.Instance: {CharacterSelectManager.Instance != null}");
    }

    private System.Collections.IEnumerator WaitForManager()
    {
        while (CharacterSelectManager.Instance == null)
        {
            yield return null;
        }

        selectManager = CharacterSelectManager.Instance;
        selectManager.OnSelectionsChanged += RefreshUI;
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (selectManager != null)
            selectManager.OnSelectionsChanged -= RefreshUI;

        if (readyButton != null)
            readyButton.onClick.RemoveListener(OnReadyClicked);
        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
    }

    private void CreateCharacterButtons()
    {
        if (characterDatabase == null || characterButtonPrefab == null) return;

        foreach (Transform child in characterButtonContainer)
        {
            Destroy(child.gameObject);
        }
        characterButtons.Clear();

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
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            startGameButton.gameObject.SetActive(isHost);
        }

        UpdateButtons();
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
        if (selectManager == null) return;

        // Check if character is available
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (selectManager.IsCharacterTaken(index, localId))
        {
            Debug.Log($"Character {index} is already taken!");
            return;
        }

        // Send selection to server
        selectManager.TrySelectCharacter(index);
        selectedCharacterIndex = index;

        UpdateButtons();
    }

    private void OnReadyClicked()
    {
        if (selectManager == null) return;

        var localSel = selectManager.GetLocalSelection();
        if (localSel == null || localSel.Value.CharacterIndex < 0)
        {
            Debug.Log("Please select a character first!");
            return;
        }

        isReady = !isReady;
        selectManager.SetReady(isReady);
        UpdateButtons();
    }

    private void OnStartGameClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        var localSel = selectManager?.GetLocalSelection();
        if (localSel == null || localSel.Value.CharacterIndex < 0)
        {
            Debug.Log("Please select a character first!");
            return;
        }

        Debug.Log("[CharacterSelectUI] Host starting game...");
        NetworkManager.Singleton.SceneManager.LoadScene("Game", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void RefreshUI()
    {
        if (selectManager == null) return;

        RefreshCharacterButtons();
        RefreshPlayerCards();
        UpdateButtons();
    }

    private void RefreshCharacterButtons()
    {
        if (selectManager == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        var localSel = selectManager.GetLocalSelection();
        int localCharIndex = localSel?.CharacterIndex ?? -1;

        for (int i = 0; i < characterButtons.Count; i++)
        {
            bool isTaken = selectManager.IsCharacterTaken(i, localId);
            bool isSelected = (i == localCharIndex);

            characterButtons[i].SetState(isSelected, isTaken);
        }

        selectedCharacterIndex = localCharIndex;
    }

    private void RefreshPlayerCards()
    {
        if (selectManager == null || playerCardPrefab == null || playerCardContainer == null) return;

        var allSelections = selectManager.GetAllSelections();
        var activeIds = new HashSet<ulong>();

        foreach (var sel in allSelections)
        {
            activeIds.Add(sel.ClientId);

            if (!playerCards.TryGetValue(sel.ClientId, out var card))
            {
                card = Instantiate(playerCardPrefab, playerCardContainer);
                bool isLocal = NetworkManager.Singleton.LocalClientId == sel.ClientId;
                card.Initialize(sel.PlayerName.ToString(), isLocal);
                playerCards[sel.ClientId] = card;
            }

            if (sel.CharacterIndex >= 0 && characterDatabase != null)
            {
                var charData = characterDatabase.GetCharacter(sel.CharacterIndex);
                card.SetCharacter(charData);
            }
            else
            {
                card.SetCharacter(null);
            }

            card.SetReady(sel.IsReady);
        }

        // Remove disconnected players
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

    private void UpdateButtons()
    {
        bool hasSelection = selectedCharacterIndex >= 0;
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        // Ready button - only for clients
        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(!isHost);
            readyButton.interactable = hasSelection;
        }

        if (readyButtonText != null)
            readyButtonText.text = isReady ? "Cancel Ready" : "Ready";

        // Start button - only for host
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = hasSelection;
        }
    }
}