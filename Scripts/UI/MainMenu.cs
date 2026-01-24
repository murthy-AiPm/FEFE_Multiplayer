using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeField;

    private ApplicationController app; // your DontDestroy controller

    private void Awake()
    {
        // Works in Unity 6+. If you’re on older, use FindObjectOfType<ApplicationController>()
        app = FindFirstObjectByType<ApplicationController>();
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Hook your UI button to this (NOT directly to an async method)
    public void StartHost() => _ = StartHostAsync();

    // Hook your UI button to this
    public void StartClient() => _ = StartClientAsync();

    private async Task StartHostAsync()
    {
        try
        {
            // Ensure your singletons exist (ApplicationController should instantiate prefabs if missing)
            if (app != null)
                app.EnsureNetworkSingletons(); // <-- implement this in ApplicationController
            else
                Debug.LogWarning("ApplicationController not found. Did you start from the Bootstrap scene?");

            // If still missing, bail safely
            var host = HostSingleton.Instance;
            if (host == null)
            {
                Debug.LogError("HostSingleton is missing. Cannot start host.");
                return;
            }

            // If the singleton exists but GameManager got disposed, recreate it
            if (host.GameManager == null)
                host.CreateHost();

            // Optional: if we were previously a client, shut that down safely
            var client = ClientSingleton.Instance;
            if (client != null && client.GameManager != null)
            {
                client.GameManager.Dispose();
            }

            await host.GameManager.StartHostAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private async Task StartClientAsync()
    {
        try
        {
            if (app != null)
                app.EnsureNetworkSingletons();
            else
                Debug.LogWarning("ApplicationController not found. Did you start from the Bootstrap scene?");

            var client = ClientSingleton.Instance;
            if (client == null)
            {
                Debug.LogError("ClientSingleton is missing. Cannot start client.");
                return;
            }

            if (client.GameManager == null)
            {
                // if your ClientSingleton needs CreateClient() to build the GameManager
                await client.CreateClient();
            }

            await client.GameManager.StartClientAsync(joinCodeField.text);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
