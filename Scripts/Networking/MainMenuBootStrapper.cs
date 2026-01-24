using UnityEngine;

public class MainMenuBootstrapper : MonoBehaviour
{
    [SerializeField] private ClientSingleton clientPrefab;
    [SerializeField] private HostSingleton hostPrefab;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        if (ClientSingleton.Instance == null)
            Instantiate(clientPrefab);

        if (HostSingleton.Instance == null)
            Instantiate(hostPrefab);
    }
}
