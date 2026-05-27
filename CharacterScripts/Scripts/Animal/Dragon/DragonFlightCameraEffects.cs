using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Owner-only camera feel for dragon flight. Drives Cinemachine noise gains from
/// speed and low-altitude turbulence without modifying the NoiseSettings asset.
/// </summary>
public class DragonFlightCameraEffects : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private CinemachineCamera[] cameras;
    [SerializeField] private CinemachineBasicMultiChannelPerlin[] noiseComponents;
    [Tooltip("Origin for altitude raycasts. If unset, uses this transform.")]
    [SerializeField] private Transform altitudeRayOrigin;

    [Header("Speed Input")]
    [SerializeField] private bool applyOnlyInFlight = true;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 40f;
    [Tooltip("Maps normalized speed (0 at Min Speed, 1 at Max Speed) to effect strength.")]
    [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Low Altitude Input")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float minAltitude = 3f;
    [SerializeField] private float maxAltitude = 50f;
    [Tooltip("Maps closeness to ground (0 at Max Altitude, 1 at Min Altitude) to turbulence strength.")]
    [SerializeField] private AnimationCurve lowAltitudeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool showAltitudeDebug;

    [Header("Noise Output")]
    [SerializeField] private float baseAmplitude = 0f;
    [SerializeField] private float speedAmplitudeAdd = 0.8f;
    [SerializeField] private float lowAltitudeAmplitudeAdd = 0.6f;
    [SerializeField] private float baseFrequency = 0.4f;
    [SerializeField] private float speedFrequencyAdd = 1.0f;
    [SerializeField] private float lowAltitudeFrequencyAdd = 0.8f;
    [SerializeField] private float smoothing = 5f;

    [Header("FOV Output")]
    [SerializeField] private bool driveFov = true;
    [SerializeField] private float speedFovKick = 6f;
    [SerializeField] private float lowAltitudeFovKick = 2f;

    [Header("Debug Readout")]
    [SerializeField] private float debugSpeed;
    [SerializeField] private float debugAltitude = -1f;
    [SerializeField] private float debugSpeedT;
    [SerializeField] private float debugLowAltitudeT;
    [SerializeField] private float debugAmplitude;
    [SerializeField] private float debugFrequency;

    private float[] _baseFovs;
    private float _currentAmplitude;
    private float _currentFrequency;
    private float _currentFovKick;

    private void Awake()
    {
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        if (cameras == null || cameras.Length == 0)
            cameras = GetComponentsInChildren<CinemachineCamera>(true);

        if (noiseComponents == null || noiseComponents.Length == 0)
            noiseComponents = GetComponentsInChildren<CinemachineBasicMultiChannelPerlin>(true);

        CacheBaseFovs();
        _currentAmplitude = baseAmplitude;
        _currentFrequency = baseFrequency;
        _currentFovKick = 0f;
        ApplyEffects(baseAmplitude, baseFrequency, 0f);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            ApplyEffects(baseAmplitude, baseFrequency, 0f);
    }

    private void OnDisable()
    {
        ApplyEffects(baseAmplitude, baseFrequency, 0f);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        bool inFlight = flightController != null && flightController.IsFlightMode;
        float speedT = applyOnlyInFlight && !inFlight ? 0f : GetSpeedT(inFlight);
        float lowAltitudeT = applyOnlyInFlight && !inFlight ? 0f : GetLowAltitudeT();

        float targetAmplitude = baseAmplitude
                              + speedT * speedAmplitudeAdd
                              + lowAltitudeT * lowAltitudeAmplitudeAdd;
        float targetFrequency = baseFrequency
                              + speedT * speedFrequencyAdd
                              + lowAltitudeT * lowAltitudeFrequencyAdd;
        float targetFovKick = driveFov
            ? speedT * speedFovKick + lowAltitudeT * lowAltitudeFovKick
            : 0f;

        float follow = 1f - Mathf.Exp(-Mathf.Max(0f, smoothing) * Time.deltaTime);
        _currentAmplitude = Mathf.Lerp(_currentAmplitude, targetAmplitude, follow);
        _currentFrequency = Mathf.Lerp(_currentFrequency, targetFrequency, follow);
        _currentFovKick = Mathf.Lerp(_currentFovKick, targetFovKick, follow);

        ApplyEffects(_currentAmplitude, _currentFrequency, _currentFovKick);

        debugSpeedT = speedT;
        debugLowAltitudeT = lowAltitudeT;
        debugAmplitude = _currentAmplitude;
        debugFrequency = _currentFrequency;
    }

    private float GetSpeedT(bool inFlight)
    {
        debugSpeed = flightController != null && inFlight
            ? flightController.Velocity.magnitude
            : 0f;

        float normalized = Mathf.InverseLerp(minSpeed, maxSpeed, debugSpeed);
        return Mathf.Clamp01(speedCurve.Evaluate(normalized));
    }

    private float GetLowAltitudeT()
    {
        debugAltitude = -1f;

        Transform origin = altitudeRayOrigin != null ? altitudeRayOrigin : transform;
        float rayDistance = Mathf.Max(maxAltitude, minAltitude);

        if (!Physics.Raycast(origin.position, Vector3.down, out RaycastHit hit, rayDistance, groundMask))
        {
            if (showAltitudeDebug)
                Debug.DrawRay(origin.position, Vector3.down * rayDistance, Color.cyan);
            return 0f;
        }

        debugAltitude = hit.distance;
        if (showAltitudeDebug)
            Debug.DrawLine(origin.position, hit.point, Color.cyan);

        float closeness = 1f - Mathf.InverseLerp(minAltitude, maxAltitude, hit.distance);
        return Mathf.Clamp01(lowAltitudeCurve.Evaluate(Mathf.Clamp01(closeness)));
    }

    private void CacheBaseFovs()
    {
        if (cameras == null)
        {
            _baseFovs = System.Array.Empty<float>();
            return;
        }

        _baseFovs = new float[cameras.Length];
        for (int i = 0; i < cameras.Length; i++)
            _baseFovs[i] = cameras[i] != null ? cameras[i].Lens.FieldOfView : 0f;
    }

    private void ApplyEffects(float amplitude, float frequency, float fovKick)
    {
        if (noiseComponents != null)
        {
            for (int i = 0; i < noiseComponents.Length; i++)
            {
                if (noiseComponents[i] == null) continue;
                noiseComponents[i].AmplitudeGain = amplitude;
                noiseComponents[i].FrequencyGain = frequency;
            }
        }

        if (!driveFov || cameras == null || _baseFovs == null) return;

        int count = Mathf.Min(cameras.Length, _baseFovs.Length);
        for (int i = 0; i < count; i++)
        {
            if (cameras[i] == null) continue;
            cameras[i].Lens.FieldOfView = _baseFovs[i] + fovKick;
        }
    }
}
