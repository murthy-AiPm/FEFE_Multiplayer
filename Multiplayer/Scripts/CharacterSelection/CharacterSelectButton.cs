using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectButton : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private GameObject takenIndicator; // Shows X or "Taken" overlay
    [SerializeField] private Button button;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = Color.yellow;
    [SerializeField] private Color takenColor = Color.gray;

    private CharacterSelectUI selectUI;
    private int characterIndex;

    public void Initialize(CharacterSelectUI ui, int index, CharacterData data)
    {
        selectUI = ui;
        characterIndex = index;

        if (data != null)
        {
            if (iconImage != null)
            {
                if (data.icon != null)
                    iconImage.sprite = data.icon;
                else
                    iconImage.color = data.characterColor;
            }

            if (nameText != null)
                nameText.text = data.characterName;
        }

        if (button != null)
            button.onClick.AddListener(OnClicked);

        SetState(false, false);
    }

    private void OnClicked()
    {
        selectUI?.SelectCharacter(characterIndex);
    }

    /// <summary>
    /// Update button visual state
    /// </summary>
    /// <param name="isSelected">Is this character selected by local player</param>
    /// <param name="isTaken">Is this character taken by another player</param>
    public void SetState(bool isSelected, bool isTaken)
    {
        // Selected indicator
        if (selectedIndicator != null)
            selectedIndicator.SetActive(isSelected);

        // Taken indicator
        if (takenIndicator != null)
            takenIndicator.SetActive(isTaken && !isSelected);

        // Background color
        if (backgroundImage != null)
        {
            if (isSelected)
                backgroundImage.color = selectedColor;
            else if (isTaken)
                backgroundImage.color = takenColor;
            else
                backgroundImage.color = normalColor;
        }

        // Button interactable
        if (button != null)
            button.interactable = !isTaken || isSelected;

        // Dim icon if taken
        if (iconImage != null)
        {
            Color c = iconImage.color;
            c.a = (isTaken && !isSelected) ? 0.5f : 1f;
            iconImage.color = c;
        }
    }

    // Legacy method for compatibility
    public void SetSelected(bool selected)
    {
        SetState(selected, false);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClicked);
    }
}