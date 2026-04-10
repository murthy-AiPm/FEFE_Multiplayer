using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Provides LAN (direct IP) hosting and joining, bypassing Unity Relay and Lobby services.
/// All gameplay code (NetworkVariables, RPCs, spawning) works identically to Relay mode.
///
/// Setup:
///   1. Add to MainMenu scene (or wherever MainMenu lives)
///   2. Wire up UI buttons for LAN Host / LAN Join + an IP input field
///   3. Or use keyboard shortcuts: F5 = LAN Host, F6 = LAN Join (localhost)
///
/// Builds using this also work with Relay — this is an alternative connection path only.
/// The transport, NetworkManager, and all game systems are the same.
/// </summary>
public class LANManager : MonoBehaviour
{
    [Header("LAN Settings")]
    [Tooltip("Port for LAN connections.")]
    [SerializeField] private ushort port = 7777;
    [Tooltip("IP address for LAN join. Ignored when hosting (host listens on 0.0.0.0).")]
    [SerializeField] private string joinIP = "127.0.0.1";

    [Header("UI (Optional)")]
    [Tooltip("Optional IP input field for joining. If assigned, overrides joinIP.")]
    [SerializeField] private TMPro.TMP_InputField ipInputField;
    [SerializeField] private LoadingScreen loadingScreen;

    [Header("Keyboard Shortcuts")]
    [SerializeField] private KeyCode lanHostKey = KeyCode.F5;
    [SerializeField] private KeyCode lanJoinKey = KeyCode.F6;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = true;

    private NetworkServer networkServer;
    private const string GameSceneName = "Game";

    private void Update()
    {
        if (Input.GetKeyDown(lanHostKey))
            StartLANHost();

        if (Input.GetKeyDown(lanJoinKey))
            StartLANClient();
    }

    /// <summary>Start as LAN host. Skips Relay, Lobby, and version check entirely.</summary>
    public void StartLANHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LAN] NetworkManager.Singleton is null.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[LAN] NetworkManager is already listening. Shut down first.");
            return;
        }

        if (loadingScreen != null)
            loadingScreen.Show("Starting LAN host...");

        // Configure transport for direct connection
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");

        if (debugLogging)
            Debug.Log($"[LAN] Hosting on port {port} | Local IP: {GetLocalIPAddress()}");

        // Connection payload (same as Relay path)
        UserData userData = new UserData
        {
            userName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "LAN Host"),
            userAuthId = "lan-host-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            characterId = 0
        };

        string payload = JsonUtility.ToJson(userData);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);

        // Server setup
        networkServer = new NetworkServer(NetworkManager.Singleton);
        networkServer.AddHostData(userData);

        // Start host
        NetworkManager.Singleton.StartHost();

        // Scene loading (same as HostGameManager)
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnGameSceneLoaded;
        }

        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);

        if (debugLogging)
            Debug.Log($"[LAN] Host started. Clients can join at {GetLocalIPAddress()}:{port}");
    }

    /// <summary>Join a LAN host by IP. Skips Relay entirely.</summary>
    public void StartLANClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LAN] NetworkManager.Singleton is null.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[LAN] NetworkManager is already listening. Shut down first.");
            return;
        }

        // Use IP from input field if available, otherwise use serialized joinIP
        string targetIP = joinIP;
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
            targetIP = ipInputField.text.Trim();

        if (loadingScreen != null)
            loadingScreen.Show($"Joining {targetIP}...");

        // Configure transport for direct connection
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(targetIP, port);

        if (debugLogging)
            Debug.Log($"[LAN] Joining {targetIP}:{port}");

        // Connection payload (same as Relay path)
        UserData userData = new UserData
        {
            userName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "LAN Client"),
            userAuthId = "lan-client-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            characterId = 0
        };

        string payload = JsonUtility.ToJson(userData);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);

        NetworkManager.Singleton.StartClient();
    }

    private void OnGameSceneLoaded(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName == GameSceneName && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;

            var manager = UnityEngine.Object.FindObjectOfType<CharacterSelectManager>();
            if (manager != null)
            {
                var networkObject = manager.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.IsSpawned)
                {
                    networkObject.Spawn();
                    if (debugLogging)
                        Debug.Log("[LAN] Spawned CharacterSelectManager");
                }
            }
            else
            {
                Debug.LogError("[LAN] CharacterSelectManager not found in scene!");
            }
        }
    }

    /// <summary>Shut down LAN session and return to menu.</summary>
    public void ShutDown()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }

        try { networkServer?.Dispose(); }
        catch (Exception e) { Debug.LogWarning(e.Message); }
        networkServer = null;
    }

    /// <summary>Gets the local network IP address for display purposes.</summary>
    private string GetLocalIPAddress()
    {
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                // Doesn't actually connect — just determines which local adapter would be used
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "unknown";
            }
        }
        catch
        {
            return "unknown";
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnGameSceneLoaded;
    }
}
