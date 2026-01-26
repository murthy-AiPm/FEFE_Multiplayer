using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectButton : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private GameObject selectedIndicator;
    [SerializeField] private Button button;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = Color.yellow;

    private CharacterSelectUI selectUI;
    private int characterIndex;

    public void Initialize(CharacterSelectUI ui, int index, CharacterData data)
    {
        selectUI = ui;
        characterIndex = index;

        if (data != null)
        {
            if (iconImage != null && data.icon != null)
                iconImage.sprite = data.icon;
            
            if (iconImage != null && data.icon == null)
                iconImage.color = data.characterColor; // Use color if no icon
            
            if (nameText != null)
                nameText.text = data.characterName;
        }

        if (button != null)
            button.onClick.AddListener(OnClicked);

        SetSelected(false);
    }

    private void OnClicked()
    {
        selectUI?.SelectCharacter(characterIndex);
    }

    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);

        if (backgroundImage != null)
            backgroundImage.color = selected ? selectedColor : normalColor;
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClicked);
    }
}
