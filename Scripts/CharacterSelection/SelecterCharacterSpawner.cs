using Unity.Netcode;
using UnityEngine;

public class SelectedCharacterSpawner : NetworkBehaviour
{
    [Header("Character Prefabs (NetworkObject)")]
    [SerializeField] private NetworkObject[] characterPrefabs;

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform[] spawnPoints;

    private CharacterSelectManager selectManager;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // find the persistent select manager (it survived scene load)
        selectManager = FindFirstObjectByType<CharacterSelectManager>();

        SpawnForAllClients();
    }

    private void SpawnForAllClients()
    {
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnOne(clientId);
        }
    }

    private void SpawnOne(ulong clientId)
    {
        int charId = 0;
        if (selectManager != null && selectManager.TryGetSelection(clientId, out int picked))
            charId = Mathf.Clamp(picked, 0, characterPrefabs.Length - 1);

        Transform sp = GetSpawnPointFor(clientId);
        var prefab = characterPrefabs[charId];

        var obj = Instantiate(prefab, sp.position, sp.rotation);
        obj.SpawnAsPlayerObject(clientId, true);
    }

    private Transform GetSpawnPointFor(ulong clientId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return this.transform;

        int idx = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[idx];
    }
}
