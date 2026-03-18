using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeathScreen : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private GameObject spawnButtonPrefab;

    [Header("Settings")]
    [SerializeField] private string titleMessage = "You Died";

    private NetworkObject _ownerNetObj;

    public bool IsOpen => panel != null && panel.activeSelf;

    public void Show(NetworkObject ownerNetObj)
    {
        _ownerNetObj = ownerNetObj;

        if (panel != null)
            panel.SetActive(true);

        if (titleText != null)
            titleText.text = titleMessage;

        PopulateSpawnButtons();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        if (panel != null)
            panel.SetActive(false);

        // Reset pause state so the game doesn't stay locked after respawn
        var pauseMenu = FindObjectOfType<PauseMenu>();
        if (pauseMenu != null)
            pauseMenu.Resume();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void PopulateSpawnButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        var points = SpawnPoint.GetAllSpawnPoints();

        if (points == null || points.Count == 0)
        {
            CreateButton("Random Spawn", 0);
            return;
        }

        for (int i = 0; i < points.Count; i++)
        {
            string label = string.IsNullOrEmpty(points[i].name)
                ? $"Spawn Point {i + 1}"
                : points[i].name;
            CreateButton(label, i);
        }
    }

    private void CreateButton(string label, int spawnIndex)
    {
        if (spawnButtonPrefab == null || buttonContainer == null) return;

        GameObject btn = Instantiate(spawnButtonPrefab, buttonContainer);

        var tmp = btn.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
            tmp.text = label;

        var button = btn.GetComponent<Button>();
        if (button != null)
        {
            int index = spawnIndex;
            button.onClick.AddListener(() => OnSpawnPointSelected(index));
        }
    }

    private void OnSpawnPointSelected(int spawnPointIndex)
    {
        if (_ownerNetObj == null) return;

        var dmgReceiver = _ownerNetObj.GetComponent<DamageReceiver>();
        if (dmgReceiver != null)
            dmgReceiver.RequestRespawnServerRpc(spawnPointIndex);
    }
}
