using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerCard : MonoBehaviour
{
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Image characterIcon;
    [SerializeField] private TMP_Text characterNameText;
    [SerializeField] private GameObject readyIndicator;
    [SerializeField] private Image backgroundImage;

    [Header("Colors")]
    [SerializeField] private Color localPlayerColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private Color otherPlayerColor = Color.white;

    public void Initialize(string playerName, bool isLocalPlayer)
    {
        if (playerNameText != null)
            playerNameText.text = playerName;

        if (backgroundImage != null)
            backgroundImage.color = isLocalPlayer ? localPlayerColor : otherPlayerColor;

        SetCharacter(null);
        SetReady(false);
    }

    public void SetCharacter(CharacterData data)
    {
        if (data != null)
        {
            if (characterIcon != null)
            {
                characterIcon.gameObject.SetActive(true);
                if (data.icon != null)
                    characterIcon.sprite = data.icon;
                else
                    characterIcon.color = data.characterColor;
            }

            if (characterNameText != null)
                characterNameText.text = data.characterName;
        }
        else
        {
            if (characterIcon != null)
                characterIcon.gameObject.SetActive(false);

            if (characterNameText != null)
                characterNameText.text = "Selecting...";
        }
    }

    public void SetReady(bool ready)
    {
        if (readyIndicator != null)
            readyIndicator.SetActive(ready);
    }
}