using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NameSelector : MonoBehaviour
{
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private Button connectButton;
    [SerializeField] private int minNameLength = 1;
    [SerializeField] private int maxNameLength = 12;

    public const string PlayerNameKey = "PlayerName";

    public static string GetUniquePlayerNameKey()
    {
#if UNITY_EDITOR
        // Use different key for virtual players vs main editor
        if (UnityEditor.MPE.ProcessService.level == UnityEditor.MPE.ProcessLevel.Secondary)
        {
            return PlayerNameKey + "_VirtualPlayer";
        }
#endif
        return PlayerNameKey;
    }

    private void Start()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
            return;
        }

        nameField.text = PlayerPrefs.GetString(GetUniquePlayerNameKey(), string.Empty);
        HandleNameChanged();
    }

    public void HandleNameChanged()
    {
        connectButton.interactable =
            nameField.text.Length >= minNameLength &&
            nameField.text.Length <= maxNameLength;
    }

    public void Connect()
    {
        PlayerPrefs.SetString(GetUniquePlayerNameKey(), nameField.text);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }
}