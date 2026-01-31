using UnityEngine;
using Unity.Netcode;

public class QuitGameButton : MonoBehaviour
{
    public void Quit()
    {
        // restore cursor (nice UX)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // If Netcode exists, shut it down safely
        var nm = NetworkManager.Singleton;

        if (nm != null)
        {
            // Host cleanup (also deletes lobby via HostGameManager.Dispose)
            if (nm.IsHost)
            {
                if (HostSingleton.Instance != null && HostSingleton.Instance.GameManager != null)
                {
                    HostSingleton.Instance.GameManager.Dispose();
                }
            }

            // Client/Host shutdown (idempotent)
            if (nm.IsListening || nm.IsConnectedClient)
            {
                nm.Shutdown();
            }
        }

        // Destroy your singletons so next run is clean
        if (HostSingleton.Instance != null) Destroy(HostSingleton.Instance.gameObject);
        if (ClientSingleton.Instance != null) Destroy(ClientSingleton.Instance.gameObject);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
