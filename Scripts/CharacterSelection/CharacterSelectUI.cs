using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectUI : MonoBehaviour
{
    [SerializeField] private CharacterSelectManager selectManager;
    [SerializeField] private Button[] characterButtons; // index == characterId
    [SerializeField] private Button continueButton;     // host only (optional)

    private void Start()
    {
        if (continueButton != null)
            continueButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);

        selectManager.Selections.OnListChanged += _ => RefreshButtons();
        RefreshButtons();
    }

    private void OnDestroy()
    {
        if (selectManager != null)
            selectManager.Selections.OnListChanged -= _ => RefreshButtons();
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            bool taken = selectManager.IsTaken(i);
            characterButtons[i].interactable = !taken;
        }
    }

    public void PickCharacter(int characterId)
    {
        selectManager.RequestSelectServerRpc(characterId);
    }

    // Hook this to Host "Continue" button
    public void HostContinueToGame()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        // optional: ensure everyone picked something before continuing
        // (simple check: selections count == connected clients count)
        if (selectManager.Selections.Count < NetworkManager.Singleton.ConnectedClientsIds.Count)
            return;

        NetworkManager.Singleton.SceneManager.LoadScene("Game", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
