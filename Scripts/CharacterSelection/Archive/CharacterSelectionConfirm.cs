using UnityEngine;

public class CharacterSelectConfirm : MonoBehaviour
{
    public void PickCharacter(int id)
    {
        PlayerPrefs.SetInt("SelectedCharacter", id);
        PlayerPrefs.Save();
    }

public void Confirm()
{
    Debug.Log("CONFIRM CLICKED");

    var app = FindFirstObjectByType<ApplicationController>();
    if (app == null)
    {
        Debug.LogError("ApplicationController NOT found. Start the game from Bootstrap scene, not from CharacterSelect.");
        return;
    }

    app.EnsureNetworkSingletons();

    int mode = PlayerPrefs.GetInt("PendingMode", 0);
    Debug.Log($"MODE = {mode}");

    if (mode == 1)
    {
        if (HostSingleton.Instance == null || HostSingleton.Instance.GameManager == null)
        {
            Debug.LogError("HostSingleton/GameManager missing even after EnsureNetworkSingletons.");
            return;
        }

        Debug.Log("Starting HOST...");
        _ = HostSingleton.Instance.GameManager.StartHostAsync();
    }
    else if (mode == 2)
    {
        if (ClientSingleton.Instance == null || ClientSingleton.Instance.GameManager == null)
        {
            Debug.LogError("ClientSingleton/GameManager missing even after EnsureNetworkSingletons.");
            return;
        }

        string code = PlayerPrefs.GetString("PendingJoinCode", "");
        Debug.Log($"Starting CLIENT... joinCode={code}");
        _ = ClientSingleton.Instance.GameManager.StartClientAsync(code);
    }
    else
    {
        Debug.LogError("PendingMode not set. You must come here via Menu Host/Client buttons.");
    }
}

}
