using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class ApplicationController : MonoBehaviour
{
    [SerializeField] private ClientSingleton clientPrefab;
    [SerializeField] private HostSingleton hostPrefab;

    private async void Start()
    {
        DontDestroyOnLoad(gameObject);

       await LaunchInMode(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
    }
    public void EnsureNetworkSingletons()
    {
        // Host
        if (!HostSingleton.TryGet(out var host))
        {
            host = Instantiate(hostPrefab);
            host.CreateHost();
        }
        else if (host.GameManager == null)
        {
            host.CreateHost();
        }

        // Client (silent check)
        if (!ClientSingleton.TryGet(out var client))
        {
            client = Instantiate(clientPrefab);
            _ = client.CreateClient(); // or await somewhere if needed
        }
    }


    private async Task LaunchInMode(bool isDedicatedServer)
    {
        if (isDedicatedServer)
        {

        }
        else
        {

            HostSingleton hostSingleton = Instantiate(hostPrefab);
            hostSingleton.CreateHost();

            ClientSingleton clientSingleton =  Instantiate(clientPrefab);
            bool authenticated = await clientSingleton.CreateClient();


            if (authenticated)
            {
                clientSingleton.GameManager.GoToMenu();
            }
        }
    }

}