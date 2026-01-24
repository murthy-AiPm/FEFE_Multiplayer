using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class LobbiesList : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform lobbyItemParent;
    [SerializeField] private LobbyItem lobbyItemPrefab;

    private bool isJoining;
    private bool isRefreshing;

    private ApplicationController app;

    private void Awake()
    {
        app = FindFirstObjectByType<ApplicationController>();
    }

    private void OnEnable()
    {
        RefreshList();
    }

    // =============================
    // LOBBY LIST
    // =============================
    public async void RefreshList()
    {
        if (isRefreshing) return;
        isRefreshing = true;

        try
        {
            var options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(
                        field: QueryFilter.FieldOptions.AvailableSlots,
                        op: QueryFilter.OpOptions.GT,
                        value: "0"),
                    new QueryFilter(
                        field: QueryFilter.FieldOptions.IsLocked,
                        op: QueryFilter.OpOptions.EQ,
                        value: "0")
                }
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);

            foreach (Transform child in lobbyItemParent)
                Destroy(child.gameObject);

            foreach (Lobby lobby in response.Results)
            {
                LobbyItem item = Instantiate(lobbyItemPrefab, lobbyItemParent);
                item.Initialise(this, lobby);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Lobby query failed: {e}");
        }
        finally
        {
            isRefreshing = false;
        }
    }

    // =============================
    // JOIN LOBBY
    // =============================
    public async Task JoinAsync(Lobby lobby)
    {
        if (isJoining) return;
        isJoining = true;

        try
        {
            // Ensure network singletons exist
            if (app != null)
                app.EnsureNetworkSingletons();
            else
                Debug.LogWarning("ApplicationController not found (did you start from Bootstrap scene?)");

            // Silent lookup (do NOT use ClientSingleton.Instance here)
            ClientSingleton client = FindObjectOfType<ClientSingleton>();
            if (client == null)
            {
                Debug.LogError("ClientSingleton missing. Cannot join lobby.");
                return;
            }

            // Ensure client GameManager exists
            if (client.GameManager == null)
                await client.CreateClient();

            // Validate lobby data
            if (lobby.Data == null)
            {
                Debug.LogError("Lobby.Data is null — host did not publish JoinCode.");
                return;
            }

            if (!lobby.Data.TryGetValue("JoinCode", out var joinCodeObj) ||
                string.IsNullOrWhiteSpace(joinCodeObj.Value))
            {
                Debug.LogError("Lobby is missing JoinCode. Available keys:");
                foreach (var kv in lobby.Data)
                    Debug.Log($"  {kv.Key} = {kv.Value.Value}");

                return;
            }

            string joinCode = joinCodeObj.Value;
            Debug.Log($"Joining lobby '{lobby.Name}' with JoinCode: {joinCode}");

            // 🚀 ACTUAL CONNECTION
            await client.GameManager.StartClientAsync(joinCode);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Lobby join failed: {e}");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            isJoining = false;
        }
    }
}
