using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeField;
    [SerializeField] private LoadingScreen loadingScreen;
    [SerializeField] private Button hostButton;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private async void Start()
    {
        // Hide host button until version check completes
        if (hostButton != null)
            hostButton.gameObject.SetActive(false);

        // Wait a frame to ensure ApplicationController has finished auth init
        await System.Threading.Tasks.Task.Yield();

        // Wait until VersionChecker exists (it may be on a DontDestroyOnLoad object)
        float timeout = 10f;
        float elapsed = 0f;
        while (VersionChecker.Instance == null && elapsed < timeout)
        {
            await System.Threading.Tasks.Task.Delay(100);
            elapsed += 0.1f;
        }

        if (VersionChecker.Instance != null)
        {
            Debug.Log("[MainMenu] VersionChecker found, running check...");
            bool allowed = await VersionChecker.Instance.CheckAsync();
            Debug.Log($"[MainMenu] VersionChecker result: {allowed}");
            if (hostButton != null)
                hostButton.gameObject.SetActive(allowed);
            else
                Debug.LogWarning("[MainMenu] hostButton is null - not wired in Inspector!");
        }
        else
        {
            Debug.LogWarning("[MainMenu] VersionChecker.Instance is null after timeout - host button stays hidden.");
        }
    }

    public async void StartHost()
    {
        if (loadingScreen != null)
            loadingScreen.Show("Starting host...");

        // Optional: if we were previously a client, clean that up
        if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
            ClientSingleton.Instance.GameManager.Dispose();

        await HostSingleton.Instance.GameManager.StartHostAsync();

        // If StartHostAsync failed (returned early), hide the loading screen
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (loadingScreen != null)
                loadingScreen.Hide();
        }
    }

    public async void StartClient()
    {
        if (loadingScreen != null)
            loadingScreen.Show("Joining lobby...");

        await ClientSingleton.Instance.GameManager.StartClientAsync(joinCodeField.text);

        // If connection failed, hide so the player can try again
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (loadingScreen != null)
                loadingScreen.Hide();
        }
    }
}
