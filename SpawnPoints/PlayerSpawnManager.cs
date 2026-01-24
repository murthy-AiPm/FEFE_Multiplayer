using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages player spawn points and ensures no two players spawn at the same location.
/// Singleton pattern for easy access from NetworkServer.
/// </summary>
public class PlayerSpawnManager : MonoBehaviour
{
    private static PlayerSpawnManager instance;
    public static PlayerSpawnManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<PlayerSpawnManager>();
                if (instance == null)
                {
                    Debug.LogError("No PlayerSpawnManager found in scene!");
                }
            }
            return instance;
        }
    }

    [Header("Spawn Points")]
    [Tooltip("Automatically finds all SpawnPoint components in scene if empty")]
    [SerializeField] private List<SpawnPoint> spawnPoints = new List<SpawnPoint>();

    [Header("Spawn Settings")]
    [SerializeField] private bool randomizeSpawnOrder = true;
    [SerializeField] private bool recycleSpawnPoints = true; // Allow reusing points after players disconnect

    private Dictionary<ulong, SpawnPoint> clientIdToSpawnPoint = new Dictionary<ulong, SpawnPoint>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        // Auto-find spawn points if not manually assigned
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            spawnPoints = FindObjectsOfType<SpawnPoint>().ToList();
            Debug.Log($"Auto-found {spawnPoints.Count} spawn points in scene");
        }

        if (spawnPoints.Count == 0)
        {
            Debug.LogError("No spawn points found! Please add SpawnPoint components to your scene.");
        }
    }

    /// <summary>
    /// Gets the next available spawn point and marks it as occupied
    /// </summary>
    public bool TryGetSpawnPoint(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        SpawnPoint availablePoint = GetAvailableSpawnPoint();

        if (availablePoint != null)
        {
            availablePoint.SetOccupied(true);
            clientIdToSpawnPoint[clientId] = availablePoint;

            position = availablePoint.Position;
            rotation = availablePoint.Rotation;
            
            Debug.Log($"Assigned spawn point to client {clientId} at {position}");
            return true;
        }

        Debug.LogWarning($"No available spawn points for client {clientId}!");
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    /// <summary>
    /// Releases a spawn point when a player disconnects
    /// </summary>
    public void ReleaseSpawnPoint(ulong clientId)
    {
        if (clientIdToSpawnPoint.TryGetValue(clientId, out SpawnPoint spawnPoint))
        {
            if (recycleSpawnPoints && spawnPoint != null)
            {
                spawnPoint.SetOccupied(false);
                Debug.Log($"Released spawn point for client {clientId}");
            }
            clientIdToSpawnPoint.Remove(clientId);
        }
    }

    private SpawnPoint GetAvailableSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        List<SpawnPoint> availablePoints = spawnPoints.Where(sp => sp != null && !sp.IsOccupied).ToList();

        if (availablePoints.Count == 0)
        {
            Debug.LogWarning("All spawn points are occupied!");
            return null;
        }

        if (randomizeSpawnOrder)
        {
            int randomIndex = Random.Range(0, availablePoints.Count);
            return availablePoints[randomIndex];
        }
        else
        {
            return availablePoints[0];
        }
    }

    /// <summary>
    /// Validates all spawn points and removes null references
    /// </summary>
    [ContextMenu("Validate Spawn Points")]
    public void ValidateSpawnPoints()
    {
        if (spawnPoints != null)
        {
            spawnPoints.RemoveAll(sp => sp == null);
            Debug.Log($"Validated spawn points. {spawnPoints.Count} valid points remaining.");
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
