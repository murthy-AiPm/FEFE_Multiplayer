using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private GameObject[] characterPrefabs;

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("No NetworkManager in this scene yet. Put NetworkManager in Bootstrap/DontDestroy.");
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        int characterId = PlayerPrefs.GetInt("SelectedCharacter", 0);
        characterId = Mathf.Clamp(characterId, 0, characterPrefabs.Length - 1);

        GameObject player = Instantiate(
            characterPrefabs[characterId],
            Vector3.zero,
            Quaternion.identity
        );

        player.GetComponent<NetworkObject>()
              .SpawnAsPlayerObject(clientId, true);
    }
}
