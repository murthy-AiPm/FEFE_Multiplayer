using TMPro;
using Unity.Collections;
using UnityEngine;

public class PlayerNameDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text playerNameText;

    private HumanoidPlayer player;
    private Canvas canvas;

    private void Start()
    {
        player = GetComponentInParent<HumanoidPlayer>();
        canvas = GetComponentInParent<Canvas>();

        if (player == null)
        {
            Debug.LogError("[PlayerNameDisplay] Could not find HumanoidPlayer in parent!");
            return;
        }

        if (playerNameText == null)
        {
            Debug.LogError("[PlayerNameDisplay] playerNameText reference is NULL!");
            return;
        }

        // Assign the main camera to the world space canvas
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
        {
            canvas.worldCamera = Camera.main;
        }

        UpdateNameDisplay(player.PlayerName.Value);
        player.PlayerName.OnValueChanged += OnNameChanged;
    }

    private void LateUpdate()
    {
        // Billboard - make the canvas face the camera
        if (Camera.main != null && canvas != null)
        {
            canvas.transform.forward = Camera.main.transform.forward;
        }

        // If camera wasn't ready at Start, try again
        if (canvas != null && canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }
    }

    private void OnNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName)
    {
        Debug.Log($"[PlayerNameDisplay] Name changed: '{oldName}' -> '{newName}'");
        UpdateNameDisplay(newName);
    }

    private void UpdateNameDisplay(FixedString32Bytes name)
    {
        string nameStr = name.ToString();
        playerNameText.text = string.IsNullOrEmpty(nameStr) ? "..." : nameStr;
    }

    private void OnDestroy()
    {
        if (player != null)
        {
            player.PlayerName.OnValueChanged -= OnNameChanged;
        }
    }
}