using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private TMP_Text statusText;

    private bool _subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (loadingPanel != null)
            loadingPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        UnsubscribeFromSceneEvents();
    }

    public void Show(string message = "Loading...")
    {
        if (statusText != null)
            statusText.text = message;

        if (loadingPanel != null)
            loadingPanel.SetActive(true);

        StartCoroutine(SubscribeWhenReady());
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    public void Hide()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(false);

        UnsubscribeFromSceneEvents();
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
            yield return null;

        if (!_subscribed)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            _subscribed = true;
        }
    }

    private void UnsubscribeFromSceneEvents()
    {
        if (!_subscribed) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;

        _subscribed = false;
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Hide();
    }
}
