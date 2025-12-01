using UnityEngine;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine.SceneManagement;
using System;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Netcode;
using Unity.Services.Relay;

public class ClientGameManager 
{
    private const string MenuSceneName = "Menu";
    private JoinAllocation allocation;
    private const string GameSceneName = "Game";
    public async Task<bool> InitAsync()
    {
        await UnityServices.InitializeAsync();

        AuthState authState =  await AuthenticationWrapper.DoAuth();

        if (authState == AuthState.Authenticated)
        {
            return true;
        }

        return false;
    }


    public async Task StartClientAsync(string joinCode)
    {
        try
        {
            allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch (Exception e)
        {
            Debug.Log(e);
            return;
        }


        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
        transport.SetRelayServerData(relayServerData);

        NetworkManager.Singleton.StartClient();

        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);

    }

    public void GoToMenu()
    {
        SceneManager.LoadScene(MenuSceneName);
    }

}
