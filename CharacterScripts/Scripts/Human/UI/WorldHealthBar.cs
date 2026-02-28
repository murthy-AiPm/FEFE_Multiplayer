using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space health bar that floats above an entity (enemy, other player).
/// Billboards toward the camera. Shows health from VitalManager.
/// 
/// Setup:
///   1. Add this to any entity with VitalManager (bear, other players)
///   2. Script auto-creates a world-space Canvas with health bar
///   3. For players: hides on the local owner (they see PlayerHUD instead)
///   4. For enemies: always visible when player is nearby
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private NetworkObject networkObject;

    [Header("Position")]
    [Tooltip("Height above the entity's pivot point")]
    [SerializeField] private float heightOffset = 2.5f;

    [Header("Appearance")]
    [SerializeField] private float barWidth = 1.2f;
    [SerializeField] private float barHeight = 0.12f;
    [SerializeField] private Color healthColor = new Color(0.8f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color bgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f);
    [SerializeField] private Color borderColor = new Color(0f, 0f, 0f, 0.9f);

    [Header("Visibility")]
    [Tooltip("Max distance to show the health bar")]
    [SerializeField] private float visibleDistance = 20f;
    [Tooltip("Hide when health is full")]
    [SerializeField] private bool hideWhenFull = false;
    [Tooltip("Seconds to keep showing after taking damage")]
    [SerializeField] private float showAfterDamageTime = 5f;

    [Header("Smoothing")]
    [SerializeField] private float fillSpeed = 5f;

    // Runtime
    private Canvas _canvas;
    private Image _fillImage;
    private GameObject _barRoot;
    private Camera _mainCam;
    private float _displayedHealth = 1f;
    private float _lastDamageTime = -100f;
    private float _lastHealth;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponent<VitalManager>();
        if (networkObject == null) networkObject = GetComponent<NetworkObject>();
    }

    private void Start()
    {
        _mainCam = Camera.main;

        // Hide on the local player (they use PlayerHUD)
        if (networkObject != null && networkObject.IsSpawned && networkObject.IsOwner)
        {
            enabled = false;
            return;
        }

        CreateWorldSpaceBar();

        // Cache initial health
        var health = vitalManager?.GetVital("health");
        _lastHealth = health != null ? health.Current : 0f;
    }

    private void LateUpdate()
    {
        if (_barRoot == null || vitalManager == null) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        // Get health
        var health = vitalManager.GetVital("health");
        if (health == null) return;

        float healthPct = health.Current / health.Max;

        // Detect damage
        if (health.Current < _lastHealth)
            _lastDamageTime = Time.time;
        _lastHealth = health.Current;

        // Visibility checks
        bool shouldShow = true;

        // Distance check
        float dist = Vector3.Distance(_mainCam.transform.position, transform.position);
        if (dist > visibleDistance)
            shouldShow = false;

        // Hide when full (if enabled)
        if (hideWhenFull && healthPct >= 0.999f)
        {
            if (Time.time - _lastDamageTime > showAfterDamageTime)
                shouldShow = false;
        }

        // Dead — hide after a moment
        if (healthPct <= 0f)
        {
            if (Time.time - _lastDamageTime > 2f)
                shouldShow = false;
        }

        _barRoot.SetActive(shouldShow);

        if (!shouldShow) return;

        // Position above entity
        _barRoot.transform.position = transform.position + Vector3.up * heightOffset;

        // Billboard — face camera
        _barRoot.transform.rotation = Quaternion.LookRotation(
            _barRoot.transform.position - _mainCam.transform.position);

        // Smooth fill
        _displayedHealth = Mathf.Lerp(_displayedHealth, healthPct, fillSpeed * Time.deltaTime);
        if (Mathf.Abs(_displayedHealth - healthPct) < 0.001f)
            _displayedHealth = healthPct;

        if (_fillImage != null)
            _fillImage.fillAmount = _displayedHealth;

        // Scale based on distance (smaller when far)
        float scale = Mathf.Clamp(1f - (dist / visibleDistance) * 0.5f, 0.5f, 1f);
        _barRoot.transform.localScale = Vector3.one * scale;
    }

    private void CreateWorldSpaceBar()
    {
        // Root object
        _barRoot = new GameObject("WorldHealthBar");
        _barRoot.transform.SetParent(null); // World space, not parented

        // Canvas
        _canvas = _barRoot.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 5;

        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth, barHeight);
        canvasRect.localScale = Vector3.one * 0.01f; // World-space scale

        // Border
        GameObject borderObj = new GameObject("Border");
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.SetParent(canvasRect, false);
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-2f, -2f);
        borderRect.offsetMax = new Vector2(2f, 2f);
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.color = borderColor;

        // Background
        GameObject bgObj = new GameObject("Background");
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.SetParent(canvasRect, false);
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = bgColor;

        // Fill
        GameObject fillObj = new GameObject("Fill");
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.SetParent(canvasRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        _fillImage = fillObj.AddComponent<Image>();
        _fillImage.color = healthColor;
        _fillImage.type = Image.Type.Filled;
        _fillImage.fillMethod = Image.FillMethod.Horizontal;
        _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        _fillImage.fillAmount = 1f;
    }

    private void OnDestroy()
    {
        if (_barRoot != null)
            Destroy(_barRoot);
    }
}
