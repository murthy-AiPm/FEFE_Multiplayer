using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space health bar that floats above an entity (enemy, other player).
/// Billboards toward the camera. Shows health from VitalManager.
/// 
/// Setup:
///   1. Create a World Space Canvas on the prefab, assign it to canvasRoot
///   2. Create a Fill Image (Filled, Horizontal) inside the Canvas, assign to fillImage
///   3. Add this script to the entity root (same object as VitalManager)
/// </summary>
public class WorldHealthBar : NetworkBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private NetworkObject networkObject;
        [SerializeField] private bool isPlayer = false; // ← add this, default false for NPCs

    [Header("Canvas Setup (assign in inspector)")]
    [SerializeField] private GameObject canvasRoot;   // the world-space Canvas GameObject
    [SerializeField] private Image fillImage;          // the fill Image inside the canvas

    [Header("Position")]
    [SerializeField] private float heightOffset = 2.5f;

    [Header("Visibility")]
    [SerializeField] private float visibleDistance = 20f;
    [SerializeField] private bool hideWhenFull = false;
    [SerializeField] private float showAfterDamageTime = 5f;

    [Header("Smoothing")]
    [SerializeField] private float fillSpeed = 5f;

    // Runtime
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


    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only hide for the local player (they use PlayerHUD)
        if (isPlayer && IsOwner)
        {
            if (canvasRoot != null) canvasRoot.SetActive(false);
            enabled = false;
            return;
        }

        if (canvasRoot != null) canvasRoot.SetActive(false);

        var health = vitalManager?.GetVital("health");
        _lastHealth = health != null ? health.Current : 0f;
        _displayedHealth = 1f;
    }
    private void LateUpdate()
    {
        if (canvasRoot == null || fillImage == null || vitalManager == null) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        var health = vitalManager.GetVital("health");
        if (health == null) return;

        float healthPct = health.Max > 0f ? health.Current / health.Max : 0f;

        // Detect damage
        if (health.Current < _lastHealth)
            _lastDamageTime = Time.time;
        _lastHealth = health.Current;

        // ── Visibility ──
        bool shouldShow = true;

        float dist = Vector3.Distance(_mainCam.transform.position, transform.position);
        if (dist > visibleDistance)
            shouldShow = false;

        if (hideWhenFull && healthPct >= 0.999f)
        {
            if (Time.time - _lastDamageTime > showAfterDamageTime)
                shouldShow = false;
        }

        if (healthPct <= 0f && Time.time - _lastDamageTime > 2f)
            shouldShow = false;

        canvasRoot.SetActive(shouldShow);
        if (!shouldShow) return;

        // ── Position & billboard ──
        canvasRoot.transform.position = transform.position + Vector3.up * heightOffset;
        canvasRoot.transform.rotation = Quaternion.LookRotation(
            canvasRoot.transform.position - _mainCam.transform.position);

        // ── Smooth fill ──
        _displayedHealth = Mathf.Lerp(_displayedHealth, healthPct, fillSpeed * Time.deltaTime);
        if (Mathf.Abs(_displayedHealth - healthPct) < 0.001f)
            _displayedHealth = healthPct;

        fillImage.fillAmount = _displayedHealth;
    }

    private void OnDestroy()
    {
        // Nothing to destroy — canvas lives on the prefab
    }
}