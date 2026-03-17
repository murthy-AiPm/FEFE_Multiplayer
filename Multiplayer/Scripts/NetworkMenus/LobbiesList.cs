using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class LobbiesList : MonoBehaviour
{
    [SerializeField] private Transform lobbyItemParent;
    [SerializeField] private LobbyItem lobbyItemPrefab;
    private bool isJoining;
    private bool isRefreshing;

    private void OnEnable()
    {
        RefreshList();
    }

    public async void RefreshList()
    {
        if (isRefreshing) { return; }

        isRefreshing = true;

        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions();
            options.Count = 25;

            options.Filters = new List<QueryFilter>()
            {
                new QueryFilter(
                    field: QueryFilter.FieldOptions.AvailableSlots,
                    op: QueryFilter.OpOptions.GT,
                    value: "0"),
                new QueryFilter(
                    field: QueryFilter.FieldOptions.IsLocked,
                    op: QueryFilter.OpOptions.EQ,
                    value: "0")
            };

            QueryResponse lobbies = await LobbyService.Instance.QueryLobbiesAsync(options);

            foreach (Transform child in lobbyItemParent)
            {
                Destroy(child.gameObject);
            }

            foreach (Lobby lobby in lobbies.Results)
            {
                LobbyItem lobbyItem = Instantiate(lobbyItemPrefab, lobbyItemParent);
                lobbyItem.Initialise(this, lobby);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }

        isRefreshing = false;
    }

    public async void JoinAsync(Lobby lobby)
    {
        if (isJoining) { return; }

        isJoining = true;

        LoadingScreen.Instance?.Show("Joining lobby...");

        try
        {
            Lobby joiningLobby;

            // Check if this player is already a member of this lobby (e.g. from a
            // previous Play-mode session that didn't cleanly leave). If so, reconnect
            // instead of joining fresh — avoids the 409 Conflict from UGS.
            List<string> joinedLobbyIds = await LobbyService.Instance.GetJoinedLobbiesAsync();

            if (joinedLobbyIds.Contains(lobby.Id))
            {
                joiningLobby = await LobbyService.Instance.ReconnectToLobbyAsync(lobby.Id);
            }
            else
            {
                joiningLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            }

            string joinCode = joiningLobby.Data["JoinCode"].Value;

            await ClientSingleton.Instance.GameManager.StartClientAsync(joinCode);

            // If connection failed, hide so player can try another lobby
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                LoadingScreen.Instance?.Hide();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            LoadingScreen.Instance?.Hide();
        }

        isJoining = false;
    }
}
