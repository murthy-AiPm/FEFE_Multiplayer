using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeField;
    [SerializeField] private LoadingScreen loadingScreen;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
