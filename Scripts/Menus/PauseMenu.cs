using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private SettingsMenuUI settingsMenu;

    private bool isPaused;

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
        Time.timeScale = 0f;
        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        pausePanel.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OpenSettings()
    {
        settingsMenu.Open();
    }

    public void BackToMainMenu()
    {
        Time.timeScale = 1f;

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsHost)
            {
                if (HostSingleton.Instance?.GameManager != null)
                    HostSingleton.Instance.GameManager.Dispose();
            }

            if (nm.IsListening || nm.IsConnectedClient)
                nm.Shutdown();
        }

        if (HostSingleton.Instance != null) Destroy(HostSingleton.Instance.gameObject);
        if (ClientSingleton.Instance != null) Destroy(ClientSingleton.Instance.gameObject);

        SceneManager.LoadScene("Menu");
    }

    public void ExitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
