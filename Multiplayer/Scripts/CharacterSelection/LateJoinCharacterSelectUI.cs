using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Character selection overlay.
/// Place this in the GAME scene - it shows as an overlay when a client has no player object,
/// and can also be reused for "Change Character".
/// </summary>
public class LateJoinCharacterSelectUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private CharacterSpawnHandler spawnHandler; // optional (kept for compatibility)
    [SerializeField] private CharacterSelectManager selectManager;

    [Header("UI Panel")]
    [SerializeField] private GameObject selectionPanel;

    [Header("Character Buttons")]
    [SerializeField] private Transform characterButtonContainer;
    [SerializeField] private GameObject characterButtonPrefab;

    [Header("UI Elements")]
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Colors")]
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color selectedColor = Color.yellow;
    [SerializeField] private Color takenColor = Color.gray;

    private List<Button> characterButtons = new List<Button>();
    private List<Image> buttonImages = new List<Image>();

    // When duplicates are allowed, this remains empty and everything is selectable.
    private List<int> takenCharacters = new List<int>();

    private int selectedCharacterIndex = -1;

    private void Start()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (selectManager == null)
            selectManager = CharacterSelectManager.Instance;

        StartCoroutine(CheckIfNeedsSelection());
        StartCoroutine(WatchForSpawn());
    }

    private System.Collections.IEnumerator WatchForSpawn()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.25f);

            if (NetworkManager.Singleton == null) continue;

            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out var client))
            {
                if (client.PlayerObject != null)
                {
                    OnSpawnSuccess();
                    yield break;
                }
            }
        }
    }

    private System.Collections.IEnumerator CheckIfNeedsSelection()
    {
        // Wait for Netcode to initialize
        yield return new WaitForSeconds(0.5f);

        if (NetworkManager.Singleton == null)
            yield break;

        // If local client already has a player object, no need
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out var client))
        {
            if (client.PlayerObject != null)
                yield break;
        }

        ShowSelectionUI();
    }

    /// <summary>
    /// Call this from Pause Menu -> Change Character
    /// </summary>
    public void OpenForCharacterChange()
    {
        ShowSelectionUI();
    }

    private void ShowSelectionUI()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CreateCharacterButtons();

        if (selectManager == null)
            selectManager = CharacterSelectManager.Instance;

        if (selectManager != null)
            selectManager.OnSelectionsChanged += RefreshFromNetwork;

        RefreshFromNetwork();

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(OnJoinClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            joinButton.interactable = false;
        }

        if (statusText != null)
            statusText.text = "Select your character";

        if (selectionPanel != null)
            selectionPanel.SetActive(true);
    }

    private void RefreshFromNetwork()
    {
        if (selectManager == null) selectManager = CharacterSelectManager.Instance;

        takenCharacters.Clear();

        // If unique enforcement is on, compute taken list from synced selections
        if (selectManager != null && selectManager.EnforceUniqueCharacters)
        {
            var all = selectManager.GetAllSelections();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].CharacterIndex >= 0)
                    takenCharacters.Add(all[i].CharacterIndex);
            }
        }

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
            Destroy(child.gameObject);

        characterButtons.Clear();
        buttonImages.Clear();

        for (int i = 0; i < characterDatabase.CharacterCount; i++)
        {
            var data = characterDatabase.GetCharacter(i);
            var buttonObj = Instantiate(characterButtonPrefab, characterButtonContainer);

            var button = buttonObj.GetComponent<Button>();
            var image = buttonObj.GetComponent<Image>();
            var text = buttonObj.GetComponentInChildren<TMP_Text>();
            var iconImage = buttonObj.transform.Find("IconImage")?.GetComponent<Image>();

            if (text != null && data != null)
                text.text = data.characterName;

            if (iconImage != null && data != null)
            {
                if (data.icon != null)
                {
                    iconImage.sprite = data.icon;
                    iconImage.color = Color.white;
                }
                else
                {
                    iconImage.sprite = null;
                    iconImage.color = data.characterColor;
                }
            }

            int index = i;
            if (button != null)
            {
                button.onClick.AddListener(() => SelectCharacter(index));
                characterButtons.Add(button);
            }

            buttonImages.Add(image);
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            bool isTaken = takenCharacters.Contains(i);
            bool isSelected = (i == selectedCharacterIndex);

            if (characterButtons[i] != null)
                characterButtons[i].interactable = !isTaken;

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
        if (takenCharacters.Contains(index))
            return;

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

        if (spawnHandler == null) spawnHandler = FindObjectOfType<CharacterSpawnHandler>();

        if (spawnHandler != null)
        {
            spawnHandler.RequestLateJoinSpawn(selectedCharacterIndex);
        }
        else
        {
            Debug.LogError("[LateJoinCharacterSelectUI] No CharacterSpawnHandler found!");
        }
    }

    public void OnSpawnSuccess()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (selectManager != null)
            selectManager.OnSelectionsChanged -= RefreshFromNetwork;

        var pauseMenu = FindObjectOfType<PauseMenu>();
        if (pauseMenu != null) pauseMenu.OnCharacterSelectClosed();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDestroy()
    {
        if (joinButton != null)
            joinButton.onClick.RemoveListener(OnJoinClicked);

        if (selectManager != null)
            selectManager.OnSelectionsChanged -= RefreshFromNetwork;
    }
    // Compatibility: CharacterSpawnHandler may still call this
    public void SetTakenCharacters(int[] taken)
    {
        if (takenCharacters == null) takenCharacters = new List<int>();
        takenCharacters.Clear();

        if (taken != null)
            takenCharacters.AddRange(taken);

        UpdateButtonStates();
    }

    // Also accept List<int> (handy elsewhere)
    public void SetTakenCharacters(List<int> taken)
    {
        if (takenCharacters == null) takenCharacters = new List<int>();
        takenCharacters.Clear();

        if (taken != null)
            takenCharacters.AddRange(taken);

        UpdateButtonStates();
    }

    // Compatibility: CharacterSpawnHandler may still call this
    public void OnSpawnFailed(string reason = "Spawn failed")
    {
        if (statusText != null)
            statusText.text = reason;

        if (joinButton != null)
            joinButton.interactable = true;

        // Keep selection panel open so user can try again
        if (selectionPanel != null)
            selectionPanel.SetActive(true);
    }

}
