using UnityEngine;
using Unity.Netcode;

public class CharacterPrefabRegistry : MonoBehaviour
{
    public static CharacterPrefabRegistry Instance { get; private set; }

    [SerializeField] private NetworkObject[] characterPrefabs; // size = 5

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public NetworkObject GetPrefab(int id)
    {
        id = Mathf.Clamp(id, 0, characterPrefabs.Length - 1);
        return characterPrefabs[id];
    }
}
