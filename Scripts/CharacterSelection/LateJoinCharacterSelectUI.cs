using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Character selection overlay for late-joining clients.
/// Place this in the GAME scene - it shows as an overlay when a client joins without a character selection.
/// </summary>
public class LateJoinCharacterSelectUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private CharacterSpawnHandler spawnHandler;

    [Header("UI Panel")]
    [SerializeField] private GameObject selectionPanel;

    [Header("Character Buttons")]
    [SerializeField] private Transform characterButtonContainer;
    [SerializeField] private GameObject characterButtonPrefab; // Simple button prefab

    [Header("UI Elements")]
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Colors")]
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color selectedColor = Color.yellow;
    [SerializeField] private Color takenColor = Color.gray;

    private List<Button> characterButtons = new List<Button>();
    private List<Image> buttonImages = new List<Image>();
    private List<int> takenCharacters = new List<int>();
    private int selectedCharacterIndex = -1;

    private void Start()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        // Wait a moment for network to initialize, then check if we need to show UI
        StartCoroutine(CheckIfNeedsSelection());
    }

    private System.Collections.IEnumerator CheckIfNeedsSelection()
    {
        // Wait for NetworkManager
        yield return new WaitForSeconds(0.5f);

        if (NetworkManager.Singleton == null)
        {
            Debug.Log("[LateJoinCharacterSelectUI] No NetworkManager");
            yield break;
        }

        // Only for clients (not host)
        if (NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[LateJoinCharacterSelectUI] We are host, no need for late join UI");
            yield break;
        }

        // Check if local client has a player object
        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                Debug.Log("[LateJoinCharacterSelectUI] Client already has player object");
                yield break;
            }
        }

        // No player object - request taken characters from server then show UI
        Debug.Log("[LateJoinCharacterSelectUI] Client needs to select character - requesting taken list");

        if (CharacterSpawnHandler.Instance != null)
        {
            CharacterSpawnHandler.Instance.RequestTakenCharacters();
        }

        // Wait a moment for response
        yield return new WaitForSeconds(0.3f);

        ShowSelectionUI();
    }

    private void ShowSelectionUI()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CreateCharacterButtons();

        if (joinButton != null)
        {
            joinButton.onClick.AddListener(OnJoinClicked);
            joinButton.interactable = false;
        }

        if (statusText != null)
            statusText.text = "Select your character to join";

        if (selectionPanel != null)
            selectionPanel.SetActive(true);
    }

    /// <summary>
    /// Called by CharacterSpawnHandler when server sends taken character list
    /// </summary>
    public void SetTakenCharacters(int[] taken)
    {
        takenCharacters.Clear();
        takenCharacters.AddRange(taken);
        Debug.Log($"[LateJoinCharacterSelectUI] Received taken characters: {string.Join(", ", taken)}");

        // Update button states if UI is already showing
        UpdateButtonStates();
    }

    private void CreateCharacterButtons()
    {
        if (characterDatabase == null || characterButtonPrefab == null || characterButtonContainer == null)
        {
            Debug.LogError("[LateJoinCharacterSelectUI] Missing references!");
            return;
        }

        // Clear existing
        foreach (Transform child in characterButtonContainer)
        {
            Destroy(child.gameObject);
        }
        characterButtons.Clear();
        buttonImages.Clear();

        // Create buttons
        for (int i = 0; i < characterDatabase.CharacterCount; i++)
        {
            var data = characterDatabase.GetCharacter(i);
            var buttonObj = Instantiate(characterButtonPrefab, characterButtonContainer);

            var button = buttonObj.GetComponent<Button>();
            var image = buttonObj.GetComponent<Image>();
            var text = buttonObj.GetComponentInChildren<TMP_Text>();

            if (text != null && data != null)
                text.text = data.characterName;

            int index = i; // Capture for closure
            if (button != null)
            {
                button.onClick.AddListener(() => SelectCharacter(index));
                characterButtons.Add(button);
            }

            if (image != null)
                buttonImages.Add(image);
            else
                buttonImages.Add(null);
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            bool isTaken = takenCharacters.Contains(i);
            bool isSelected = (i == selectedCharacterIndex);

            // Update button interactability
            if (characterButtons[i] != null)
                characterButtons[i].interactable = !isTaken;

            // Update button color
            if (buttonImages[i] != null)
            {
                if (isSelected)
                    buttonImages[i].color = selectedColor;
                else if (isTaken)
                    buttonImages[i].color = takenColor;
                else
                    buttonImages[i].color = availableColor;
            }
        }
    }

    private void SelectCharacter(int index)
    {
        // Don't allow selecting taken characters
        if (takenCharacters.Contains(index))
        {
            Debug.Log($"[LateJoinCharacterSelectUI] Character {index} is taken!");
            return;
        }

        selectedCharacterIndex = index;
        UpdateButtonStates();

        if (joinButton != null)
            joinButton.interactable = true;

        if (statusText != null)
        {
            var charData = characterDatabase.GetCharacter(index);
            statusText.text = $"Selected: {charData?.characterName ?? "Unknown"}";
        }
    }

    private void OnJoinClicked()
    {
        if (selectedCharacterIndex < 0) return;

        // Double-check it's not taken
        if (takenCharacters.Contains(selectedCharacterIndex))
        {
            if (statusText != null)
                statusText.text = "That character was just taken! Select another.";
            selectedCharacterIndex = -1;
            UpdateButtonStates();
            return;
        }

        if (joinButton != null)
            joinButton.interactable = false;

        if (statusText != null)
            statusText.text = "Joining...";

        // Send request to server via CharacterSpawnHandler
        if (spawnHandler != null)
        {
            spawnHandler.RequestLateJoinSpawn(selectedCharacterIndex);
        }
        else if (CharacterSpawnHandler.Instance != null)
        {
            CharacterSpawnHandler.Instance.RequestLateJoinSpawn(selectedCharacterIndex);
        }
        else
        {
            Debug.LogError("[LateJoinCharacterSelectUI] No CharacterSpawnHandler found!");
        }
    }

    /// <summary>
    /// Called when spawn is successful - hide the UI
    /// </summary>
    public void OnSpawnSuccess()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Called when spawn fails (e.g., character was taken)
    /// </summary>
    public void OnSpawnFailed(string reason)
    {
        if (statusText != null)
            statusText.text = reason;

        if (joinButton != null)
            joinButton.interactable = false;

        selectedCharacterIndex = -1;

        // Refresh taken list
        if (CharacterSpawnHandler.Instance != null)
        {
            CharacterSpawnHandler.Instance.RequestTakenCharacters();
        }
    }

    private void OnDestroy()
    {
        if (joinButton != null)
            joinButton.onClick.RemoveListener(OnJoinClicked);
    }
}