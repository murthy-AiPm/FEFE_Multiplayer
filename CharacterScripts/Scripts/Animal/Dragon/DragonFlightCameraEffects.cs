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

    [Header("Thrust Gate")]
    [Tooltip("Scales all noise/FOV/pulse air effects, including Base Amplitude, to zero when positive thrust is below Min Thrust For Air Effects.")]
    [SerializeField] private bool gateAirEffectsByThrust = true;
    [Tooltip("Positive FlightThrust value where air effects begin. 0.1 = 10% thrust.")]
    [Range(0f, 1f)]
    [SerializeField] private float minThrustForAirEffects = 0.1f;
    [Tooltip("Blend range above Min Thrust For Air Effects. 0 = hard cutoff, 0.1 = fade in over the next 10% thrust.")]
    [Range(0f, 1f)]
    [SerializeField] private float thrustGateBlendRange = 0.1f;
    [Tooltip("When below the thrust gate, disable Cinemachine noise components entirely instead of only setting gains to 0.")]
    [SerializeField] private bool disableNoiseBelowThrust = true;
    [Tooltip("Also scales Dutch lean to zero below the thrust gate. Leave off if you like lean at low thrust.")]
    [SerializeField] private bool gateLeanByThrust;

    [Header("Acceleration Pulse")]
    [SerializeField] private bool enableAccelerationPulse = false;
    [Tooltip("Speed increase in m/s^2 required before the air-pressure pulse starts.")]
    [SerializeField] private float accelerationThreshold = 8f;
    [Tooltip("Speed increase in m/s^2 that maps to full pulse strength.")]
    [SerializeField] private float fullPulseAcceleration = 28f;
    [SerializeField] private float pulseAmplitudeAdd = 0.5f;
    [SerializeField] private float pulseFrequencyAdd = 0.9f;
    [SerializeField] private float pulseFovKick = 2f;
    [SerializeField] private float pulseDecaySpeed = 3f;

    [Header("FOV Output")]
    [SerializeField] private bool driveFov = true;
    [SerializeField] private float speedFovKick = 6f;
    [SerializeField] private float lowAltitudeFovKick = 2f;

    [Header("Camera Lean")]
    [SerializeField] private bool driveDutchLean = true;
    [Tooltip("Camera roll degrees at full yaw input. Negative values invert the lean.")]
    [SerializeField] private float yawLeanDegrees = -4f;
    [Tooltip("Camera roll degrees at full roll input. Negative values invert the lean.")]
    [SerializeField] private float rollLeanDegrees = -6f;
    [SerializeField] private float leanSmoothing = 7f;

    [Header("Debug Readout")]
    [SerializeField] private float debugSpeed;
    [SerializeField] private float debugAltitude = -1f;
    [SerializeField] private float debugSpeedT;
    [SerializeField] private float debugLowAltitudeT;
    [SerializeField] private float debugThrustGateT;
    [SerializeField] private float debugPulseT;
    [SerializeField] private float debugAmplitude;
    [SerializeField] private float debugFrequency;
    [SerializeField] private float debugDutch;

    private float[] _baseFovs;
    private float[] _baseDutch;
    private bool[] _baseNoiseEnabled;
    private float _currentAmplitude;
    private float _currentFrequency;
    private float _currentFovKick;
    private float _currentDutch;
    private float _lastSpeed;
    private float _pulseT;

    private void Awake()
    {
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        if (cameras == null || cameras.Length == 0)
            cameras = GetComponentsInChildren<CinemachineCamera>(true);

        if (noiseComponents == null || noiseComponents.Length == 0)
            noiseComponents = GetComponentsInChildren<CinemachineBasicMultiChannelPerlin>(true);

        CacheBaseFovs();
        CacheBaseNoiseState();
        _currentAmplitude = baseAmplitude;
        _currentFrequency = baseFrequency;
        _currentFovKick = 0f;
        _currentDutch = 0f;
        ApplyEffects(baseAmplitude, baseFrequency, 0f, 0f, true);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            ApplyEffects(baseAmplitude, baseFrequency, 0f, 0f, true);
    }

    private void OnDisable()
    {
        ApplyEffects(baseAmplitude, baseFrequency, 0f, 0f, true);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        bool inFlight = flightController != null && flightController.IsFlightMode;
        float speedT = applyOnlyInFlight && !inFlight ? 0f : GetSpeedT(inFlight);
        float lowAltitudeT = applyOnlyInFlight && !inFlight ? 0f : GetLowAltitudeT();
        float thrustGateT = applyOnlyInFlight && !inFlight ? 0f : GetThrustGateT();
        float airGateT = gateAirEffectsByThrust ? thrustGateT : 1f;
        bool airNoiseEnabled = !gateAirEffectsByThrust || airGateT > 0f;
        float leanGateT = gateLeanByThrust ? thrustGateT : 1f;
        float pulseT = (applyOnlyInFlight && !inFlight ? 0f : UpdateAccelerationPulse(inFlight)) * airGateT;
        float targetDutch = (applyOnlyInFlight && !inFlight ? 0f : GetTargetDutch()) * leanGateT;

        float ungatedAmplitude = baseAmplitude
                               + speedT * speedAmplitudeAdd
                               + lowAltitudeT * lowAltitudeAmplitudeAdd
                               + pulseT * pulseAmplitudeAdd;
        float ungatedFrequency = baseFrequency
                               + speedT * speedFrequencyAdd
                               + lowAltitudeT * lowAltitudeFrequencyAdd
                               + pulseT * pulseFrequencyAdd;
        float targetAmplitude = ungatedAmplitude * airGateT;
        float targetFrequency = ungatedFrequency * airGateT;
        float targetFovKick = driveFov
            ? (speedT * speedFovKick + lowAltitudeT * lowAltitudeFovKick) * airGateT + pulseT * pulseFovKick
            : 0f;

        if (gateAirEffectsByThrust && airGateT <= 0f)
        {
            _currentAmplitude = targetAmplitude;
            _currentFrequency = targetFrequency;
            _currentFovKick = targetFovKick;
        }
        else
        {
            float follow = 1f - Mathf.Exp(-Mathf.Max(0f, smoothing) * Time.deltaTime);
            _currentAmplitude = Mathf.Lerp(_currentAmplitude, targetAmplitude, follow);
            _currentFrequency = Mathf.Lerp(_currentFrequency, targetFrequency, follow);
            _currentFovKick = Mathf.Lerp(_currentFovKick, targetFovKick, follow);
        }

        float leanFollow = 1f - Mathf.Exp(-Mathf.Max(0f, leanSmoothing) * Time.deltaTime);
        _currentDutch = Mathf.Lerp(_currentDutch, targetDutch, leanFollow);

        ApplyEffects(_currentAmplitude, _currentFrequency, _currentFovKick, _currentDutch, airNoiseEnabled);

        debugSpeedT = speedT;
        debugLowAltitudeT = lowAltitudeT;
        debugThrustGateT = thrustGateT;
        debugPulseT = pulseT;
        debugAmplitude = _currentAmplitude;
        debugFrequency = _currentFrequency;
        debugDutch = _currentDutch;
    }

    private float GetSpeedT(bool inFlight)
    {
        debugSpeed = flightController != null && inFlight
            ? flightController.Velocity.magnitude
            : 0f;

        float normalized = Mathf.InverseLerp(minSpeed, maxSpeed, debugSpeed);
        return Mathf.Clamp01(speedCurve.Evaluate(normalized));
    }

    private float UpdateAccelerationPulse(bool inFlight)
    {
        if (!enableAccelerationPulse)
        {
            _lastSpeed = debugSpeed;
            _pulseT = 0f;
            return 0f;
        }

        if (!inFlight)
        {
            _lastSpeed = debugSpeed;
            _pulseT = Mathf.MoveTowards(_pulseT, 0f, pulseDecaySpeed * Time.deltaTime);
            return _pulseT;
        }

        float acceleration = (debugSpeed - _lastSpeed) / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastSpeed = debugSpeed;

        if (acceleration > accelerationThreshold)
        {
            float pulse = Mathf.InverseLerp(accelerationThreshold, fullPulseAcceleration, acceleration);
            _pulseT = Mathf.Max(_pulseT, Mathf.Clamp01(pulse));
        }

        _pulseT = Mathf.MoveTowards(_pulseT, 0f, pulseDecaySpeed * Time.deltaTime);
        return _pulseT;
    }

    private float GetThrustGateT()
    {
        if (flightController == null) return 0f;

        float positiveThrust = Mathf.Clamp01(flightController.FlightThrust);
        if (thrustGateBlendRange <= 0.0001f)
            return positiveThrust >= minThrustForAirEffects ? 1f : 0f;

        return Mathf.InverseLerp(
            minThrustForAirEffects,
            Mathf.Clamp01(minThrustForAirEffects + thrustGateBlendRange),
            positiveThrust);
    }

    private float GetTargetDutch()
    {
        if (!driveDutchLean || flightController == null) return 0f;

        float yawLean = Mathf.Clamp(flightController.FlightYaw, -1f, 1f) * yawLeanDegrees;
        float rollLean = Mathf.Clamp(flightController.FlightRoll, -1f, 1f) * rollLeanDegrees;
        return yawLean + rollLean;
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
            _baseDutch = System.Array.Empty<float>();
            return;
        }

        _baseFovs = new float[cameras.Length];
        _baseDutch = new float[cameras.Length];
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null)
            {
                _baseFovs[i] = 0f;
                _baseDutch[i] = 0f;
                continue;
            }

            _baseFovs[i] = cameras[i].Lens.FieldOfView;
            _baseDutch[i] = cameras[i].Lens.Dutch;
        }
    }

    private void CacheBaseNoiseState()
    {
        if (noiseComponents == null)
        {
            _baseNoiseEnabled = System.Array.Empty<bool>();
            return;
        }

        _baseNoiseEnabled = new bool[noiseComponents.Length];
        for (int i = 0; i < noiseComponents.Length; i++)
            _baseNoiseEnabled[i] = noiseComponents[i] != null && noiseComponents[i].enabled;
    }

    private void ApplyEffects(float amplitude, float frequency, float fovKick, float dutch, bool airNoiseEnabled)
    {
        if (noiseComponents != null)
        {
            for (int i = 0; i < noiseComponents.Length; i++)
            {
                if (noiseComponents[i] == null) continue;

                bool baseEnabled = _baseNoiseEnabled == null || i >= _baseNoiseEnabled.Length || _baseNoiseEnabled[i];
                bool enabledNow = baseEnabled && (!disableNoiseBelowThrust || airNoiseEnabled);
                noiseComponents[i].enabled = enabledNow;

                if (!enabledNow)
                {
                    noiseComponents[i].AmplitudeGain = 0f;
                    noiseComponents[i].FrequencyGain = 0f;
                    continue;
                }

                noiseComponents[i].AmplitudeGain = amplitude;
                noiseComponents[i].FrequencyGain = frequency;
            }
        }

        if (cameras == null || _baseFovs == null || _baseDutch == null) return;

        int count = Mathf.Min(Mathf.Min(cameras.Length, _baseFovs.Length), _baseDutch.Length);
        for (int i = 0; i < count; i++)
        {
            if (cameras[i] == null) continue;
            if (driveFov)
                cameras[i].Lens.FieldOfView = _baseFovs[i] + fovKick;
            if (driveDutchLean)
                cameras[i].Lens.Dutch = _baseDutch[i] + dutch;
        }
    }
}
