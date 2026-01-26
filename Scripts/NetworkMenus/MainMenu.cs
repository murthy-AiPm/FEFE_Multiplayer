using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeField;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    public async void StartHost()
    {
        // Optional: if we were previously a client, clean that up
        if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
        {
            ClientSingleton.Instance.GameManager.Dispose(); // this disposes NetworkClient
        }

        await HostSingleton.Instance.GameManager.StartHostAsync();
    }


    public async void StartClient()
    {
  
            await ClientSingleton.Instance.GameManager.StartClientAsync(joinCodeField.text);
    }
}
