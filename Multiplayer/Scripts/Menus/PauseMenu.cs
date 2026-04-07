using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private SettingsMenuUI settingsMenu;
    [SerializeField] private LateJoinCharacterSelectUI characterSelectUI;

    private bool isPaused;
    public static bool IsPaused { get; private set; }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            var deathScreen = FindObjectOfType<DeathScreen>();
            bool deathScreenOpen = deathScreen != null && deathScreen.IsOpen;

            if (deathScreenOpen)
            {
                // Close death screen and return to pause panel
                deathScreen.Hide();
                pausePanel.SetActive(true);
            }
            else if (characterSelectUI != null && characterSelectUI.IsOpen)
            {
                if (characterSelectUI.IsCharacterChange) characterSelectUI.OnBackClicked();
            }
            else if (settingsMenu.IsOpen) OnSettingsClosed();
            else if (isPaused) Resume();
            else Pause();
        }
    }

    public void Pause()
    {
        pausePanel.SetActive(true);
        isPaused = true;
        IsPaused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        pausePanel.SetActive(false);
        settingsMenu.Close();
        isPaused = false;
        IsPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OpenSettings()
    {
        pausePanel.SetActive(false);
        settingsMenu.Open();
        isPaused = true;
        IsPaused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void OpenRespawn()
    {
        // Disallow respawn while mounted
        var mountController = FindObjectOfType<MountController>();
        if (mountController != null && mountController.IsOwner &&
            (mountController.IsMounted || mountController.IsTransitioning))
        {
            Debug.Log("[PauseMenu] Cannot respawn while mounted.");
            return;
        }

        // Disallow respawn while operating a ballista
        var ballistaOperator = FindObjectOfType<BallistaOperator>();
        if (ballistaOperator != null && ballistaOperator.IsOwner && ballistaOperator.IsOperating)
        {
            Debug.Log("[PauseMenu] Cannot respawn while operating ballista.");
            return;
        }

        // Disallow respawn while dragon is in flight
        var flightController = FindObjectOfType<DragonFlightController>();
        if (flightController != null && flightController.IsOwner && flightController.IsFlightMode)
        {
            Debug.Log("[PauseMenu] Cannot respawn while in flight.");
            return;
        }

        pausePanel.SetActive(false);
        isPaused = true;
        IsPaused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        var deathScreen = FindObjectOfType<DeathScreen>();
        if (deathScreen != null)
            deathScreen.Show(localPlayer);
    }

    public void OnRespawnScreenClosed()
    {
        pausePanel.SetActive(false);
        isPaused = false;
        IsPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ChangeCharacter()
    {
        pausePanel.SetActive(false);
        isPaused = true;
        IsPaused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        var charSelectUI = FindObjectOfType<LateJoinCharacterSelectUI>();
        if (charSelectUI != null) charSelectUI.OpenForCharacterChange();
    }

    public void OnCharacterSelectClosed()
    {
        pausePanel.SetActive(false);
        isPaused = false;
        IsPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnSettingsClosed()
    {
        settingsMenu.Close();
        pausePanel.SetActive(true);
    }

    public void BackToMainMenu()
    {
        Resume();
        StartCoroutine(LeaveSessionAndLoadMenu());
    }

    private System.Collections.IEnumerator LeaveSessionAndLoadMenu()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            HostSingleton.Instance.GameManager.ShutDown();

        if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
            ClientSingleton.Instance.GameManager.Disconnect();

        yield return null;
        yield return null;

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
            if (no.gameObject.scene != activeScene) continue;
            if (no.GetComponent<Unity.Netcode.NetworkManager>() != null) continue;
            Destroy(no.gameObject);
        }

        yield return null;

        SceneManager.LoadScene("MainMenu");
    }

    public void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
