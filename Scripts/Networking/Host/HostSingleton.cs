using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class HostSingleton : MonoBehaviour
{
    private static HostSingleton instance;

    public HostGameManager GameManager { get; private set; }

    public static HostSingleton Instance
    {
        get
        {
            if (instance != null) { return instance; }

            instance = FindFirstObjectByType<HostSingleton>();

            if (instance == null)
            {
                Debug.LogError("No HostSingleton in the scene!");
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
    //private void Start()
    //{
    //    DontDestroyOnLoad(gameObject);
    //}

    public void CreateHost()
    {
        GameManager = new HostGameManager();
    }

    private void OnDestroy()
    {
        GameManager?.Dispose();
    }

    public static bool TryGet(out HostSingleton singleton)
    {
        singleton = instance != null ? instance : FindObjectOfType<HostSingleton>();
        return singleton != null;
    }

}
