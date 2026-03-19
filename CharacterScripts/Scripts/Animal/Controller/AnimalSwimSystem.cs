using UnityEngine;

/// <summary>
/// Detects water contact for any animal using:
/// - Trigger volume (OnTriggerEnter/Exit) for broad water body detection
/// - Downward raycast against the Water layer for precise surface Y
///
/// Public state consumed by swim controllers (e.g. DragonSwimController).
/// Flight exclusion: call SetFlightActive(true) to pause detection when airborne.
/// </summary>//
public class AnimalSwimSystem : MonoBehaviour
{
    [Header("Water Detection")]
    [SerializeField] private string waterTag = "Water";
    [SerializeField] private LayerMask waterLayer;
    [Tooltip("Raycast origin for surface detection (body centre transform)")]
    [SerializeField] private Transform bodyCentre;
    [Tooltip("How far above the dragon to start the surface raycast")]
    [SerializeField] private float surfaceRaycastUpOffset = 2f;
    [Tooltip("Total raycast distance downward to find water surface")]
    [SerializeField] private float surfaceRaycastDistance = 6f;
    [Tooltip("Body must be this far below surface to enter swimming")]
    [SerializeField] private float waterEntryDepthThreshold = 0.3f;
    [Tooltip("Body must be this far ABOVE surface before swimming exits (negative = above surface)")]
    [SerializeField] private float waterExitDepthThreshold  = -0.5f;
    [Tooltip("Dragon is near surface when within this distance above/below")]
    [SerializeField] private float nearSurfaceThreshold = 1.5f;

    [Header("Debug (Read Only)")]
    [SerializeField] private bool _debugInVolume;
    [SerializeField] private bool _debugIsInWater;
    [SerializeField] private bool _debugIsNearSurface;
    [SerializeField] private float _debugSubmersionDepth;
    [SerializeField] private float _debugWaterSurfaceY;

    // Internal state
    private bool  _inWaterVolume;
    private bool  _flightActive;
    private bool  _isInWaterState; // latched with hysteresis
    private float _waterSurfaceY;
    private float _submersionDepth;

    // ── Public state ──────────────────────────────────────────────
    /// <summary>True when sufficiently submerged. Uses hysteresis to prevent flickering.</summary>
    public bool IsInWater      => _isInWaterState;
    /// <summary>True when body centre is within nearSurfaceThreshold of the water surface.</summary>
    public bool IsNearSurface  => _inWaterVolume && Mathf.Abs(_submersionDepth) <= nearSurfaceThreshold;
    /// <summary>World Y of the detected water surface. Valid only when InWaterVolume.</summary>
    public float WaterSurfaceY => _waterSurfaceY;
    /// <summary>
    /// How far below the water surface the body centre is.
    /// Positive = submerged, negative = above surface.
    /// </summary>
    public float SubmersionDepth => _submersionDepth;
    /// <summary>Raw trigger volume state, independent of flight exclusion.</summary>
    public bool InWaterVolume  => _inWaterVolume;

    // ═════════════════════════════════════════════════════════════
    private void Awake()
    {
        if (bodyCentre == null)
            bodyCentre = transform;
    }

    private void Update()
    {
        if (!_inWaterVolume || _flightActive)
        {
            _submersionDepth = 0f;
            UpdateDebug();
            return;
        }

        UpdateSurface();
        UpdateIsInWaterState();
        UpdateDebug();
    }

    // ── Hysteresis ────────────────────────────────────────────────
    private void UpdateIsInWaterState()
    {
        if (_flightActive || !_inWaterVolume)
        {
            _isInWaterState = false;
            return;
        }

        if (!_isInWaterState)
        {
            // Not yet swimming — enter when sufficiently submerged
            if (_submersionDepth >= waterEntryDepthThreshold)
                _isInWaterState = true;
        }
        else
        {
            // Already swimming — only exit when clearly above surface
            if (_submersionDepth < waterExitDepthThreshold)
                _isInWaterState = false;
        }
    }

    // ── Water surface raycast ─────────────────────────────────────
    private void UpdateSurface()
    {
        // Cast downward from above to find water surface Y
        Vector3 rayStart = bodyCentre.position + Vector3.up * surfaceRaycastUpOffset;
        float   rayDist  = surfaceRaycastDistance + surfaceRaycastUpOffset;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDist,
            waterLayer, QueryTriggerInteraction.Collide))
        {
            _waterSurfaceY   = hit.point.y;
        }
        // If raycast misses (dragon is deep underwater), keep last known surface Y.
        // Never reset submersion when inside the water volume.
        _submersionDepth = _waterSurfaceY - bodyCentre.position.y;
    }

    // ── Trigger detection ─────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
            _inWaterVolume = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            _inWaterVolume   = false;
            _isInWaterState  = false;
            _submersionDepth = 0f;
            _waterSurfaceY   = 0f;
        }
    }

    // ── Flight exclusion ──────────────────────────────────────────
    /// <summary>
    /// Call with true when the animal enters any flight mode.
    /// Pauses water detection so flying above water doesn't trigger swimming.
    /// </summary>
    public void SetFlightActive(bool active) => _flightActive = active;

    // ── Debug mirrors ─────────────────────────────────────────────
    private void UpdateDebug()
    {
        _debugInVolume      = _inWaterVolume;
        _debugIsInWater     = IsInWater;
        _debugIsNearSurface = IsNearSurface;
        _debugSubmersionDepth = _submersionDepth;
        _debugWaterSurfaceY   = _waterSurfaceY;
    }

    private void OnDrawGizmosSelected()
    {
        if (bodyCentre == null) return;

        // Show surface raycast
        Vector3 rayStart = bodyCentre.position + Vector3.up * surfaceRaycastUpOffset;
        Gizmos.color = _inWaterVolume ? Color.cyan : Color.grey;
        Gizmos.DrawLine(rayStart, rayStart + Vector3.down * (surfaceRaycastDistance + surfaceRaycastUpOffset));
        Gizmos.DrawSphere(new Vector3(bodyCentre.position.x, _waterSurfaceY, bodyCentre.position.z), 0.15f);
    }
}
