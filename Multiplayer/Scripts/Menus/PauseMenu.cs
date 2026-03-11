using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private SettingsMenuUI settingsMenu;

    private bool isPaused;
    public static bool IsPaused { get; private set; }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) Resume();
            else Pause();
        }
    }

    public void Pause()
    {
        pausePanel.SetActive(true);
        //Time.timeScale = 0f;
        isPaused = true;
        IsPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        pausePanel.SetActive(false);
       // Time.timeScale = 1f;
        isPaused = false;
        IsPaused = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OpenSettings()
    {
        settingsMenu.Open();
    }

    public void BackToMainMenu()
    {
        Resume(); // optional: hide UI / unlock cursor states you want

        StartCoroutine(LeaveSessionAndLoadMenu());
    }



    private System.Collections.IEnumerator LeaveSessionAndLoadMenu()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            HostSingleton.Instance.GameManager.ShutDown();
        }

        if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
        {
            ClientSingleton.Instance.GameManager.Disconnect();
        }

        // Let NGO process shutdown/despawns
        yield return null;
        yield return null;

        /// Cleanup leftover NetworkObjects ONLY in the active scene (don't kill DontDestroyOnLoad stuff)
        var activeScene = SceneManager.GetActiveScene();

        Unity.Netcode.NetworkObject[] netObjects =
#if UNITY_2023_1_OR_NEWER
    FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
    Resources.FindObjectsOfTypeAll<Unity.Netcode.NetworkObject>();
#endif

        foreach (var no in netObjects)
        {
            if (no == null) continue;

            // Only destroy objects that belong to the ACTIVE scene
            if (no.gameObject.scene != activeScene) continue;

            // Extra safety: never destroy the NetworkManager object if it has NetworkObject
            if (no.GetComponent<Unity.Netcode.NetworkManager>() != null) continue;

            Destroy(no.gameObject);
        }



        yield return null;

        SceneManager.LoadScene("MainMenu");
    }


    public void ExitGame()
    {
        //.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
