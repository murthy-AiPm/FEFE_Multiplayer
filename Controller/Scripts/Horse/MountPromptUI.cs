using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple UI that shows "Hold E to Mount" prompt when player is near a horse.
/// Shows progress bar while holding E.
/// </summary>
public class MountPromptUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MountController mountController;
    
    [Header("UI Elements")]
    [SerializeField] private GameObject promptPanel;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Image progressBar;

    [Header("Settings")]
    [SerializeField] private string mountPromptText = "Hold E to Mount";
    [SerializeField] private string dismountPromptText = "Press E to Dismount";

    private void Awake()
    {
        if (mountController == null)
            mountController = GetComponentInParent<MountController>();

        if (promptPanel != null)
            promptPanel.SetActive(false);
    }

    private void Update()
    {
        if (mountController == null || promptPanel == null) return;

        // Show prompt if near a mount and not already mounted
        bool showPrompt = false;
        string text = "";

        if (mountController.IsMounted)
        {
            // Show dismount prompt
            showPrompt = true;
            text = dismountPromptText;
            
            if (progressBar != null)
                progressBar.fillAmount = 0f;
        }
        else if (mountController.GetNearbyMount() != null)
        {
            // Show mount prompt
            showPrompt = true;
            text = mountPromptText;

            // Show hold progress
            if (progressBar != null)
            {
                float progress = mountController.GetMountHoldProgress();
                progressBar.fillAmount = progress;
            }
        }

        promptPanel.SetActive(showPrompt);

        if (showPrompt && promptText != null)
        {
            promptText.text = text;
        }
    }
}
