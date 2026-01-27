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

    private List<Button> characterButtons = new List<Button>();
    private List<Image> buttonImages = new List<Image>();
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

        // No player object - show character selection
        Debug.Log("[LateJoinCharacterSelectUI] Client needs to select character - showing UI");
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

            if (image != null && data != null)
                image.color = data.characterColor;

            int index = i; // Capture for closure
            if (button != null)
            {
                button.onClick.AddListener(() => SelectCharacter(index));
                characterButtons.Add(button);
            }

            if (image != null)
                buttonImages.Add(image);
        }
    }

    private void SelectCharacter(int index)
    {
        // Reset all buttons
        for (int i = 0; i < buttonImages.Count; i++)
        {
            var data = characterDatabase.GetCharacter(i);
            if (buttonImages[i] != null && data != null)
            {
                buttonImages[i].color = data.characterColor;
            }
        }

        // Highlight selected
        selectedCharacterIndex = index;
        if (index >= 0 && index < buttonImages.Count && buttonImages[index] != null)
        {
            buttonImages[index].color = Color.yellow;
        }

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

    private void OnDestroy()
    {
        if (joinButton != null)
            joinButton.onClick.RemoveListener(OnJoinClicked);
    }
}
