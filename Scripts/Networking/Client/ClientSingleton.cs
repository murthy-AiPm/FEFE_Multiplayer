using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class ClientSingleton : MonoBehaviour
{
    private static ClientSingleton instance;
    public static bool TryGet(out ClientSingleton singleton)
    {
        singleton = instance != null ? instance : FindObjectOfType<ClientSingleton>();
        return singleton != null;
    }

    public ClientGameManager GameManager { get; private set; }

    public static ClientSingleton Instance
    {
        get
        {
            if (instance != null) { return instance; }

            instance = FindFirstObjectByType<ClientSingleton>();

            if (instance == null)
            {
                Debug.LogError("No ClientSingleton in the scene!");
                return null;
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<bool> CreateClient()
    {
        GameManager = new ClientGameManager();

        return await GameManager.InitAsync();
    }

    private void OnDestroy()
    {
        GameManager?.Dispose();
    }
}
