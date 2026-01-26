using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Unity.Netcode;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private TMP_InputField joinCodeField;

 public void GoToMainMenu()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            HostSingleton.Instance.GameManager.ShutDown();
        }

        ClientSingleton.Instance.GameManager.Disconnect();
    }
}
