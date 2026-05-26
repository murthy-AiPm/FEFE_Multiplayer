using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Owner-only camera feel for dragon flight. Scales Cinemachine noise and FOV from
/// flight speed so fast dives/flaps feel like pushing through air.
/// </summary>
public class DragonFlightCameraEffects : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private CinemachineCamera[] cameras;
    [SerializeField] private CinemachineBasicMultiChannelPerlin[] noiseComponents;

    [Header("Speed Range")]
    [SerializeField] private float minEffectSpeed = 5f;
    [SerializeField] private float maxEffectSpeed = 35f;
    [SerializeField] private float smoothing = 5f;

    [Header("Noise")]
    [SerializeField] private float minNoiseAmplitude = 0f;
    [SerializeField] private float maxNoiseAmplitude = 1.2f;
    [SerializeField] private float minNoiseFrequency = 0.4f;
    [SerializeField] private float maxNoiseFrequency = 1.8f;

    [Header("FOV")]
    [SerializeField] private bool driveFov = true;
    [SerializeField] private float maxFovKick = 8f;

    private float[] _baseFovs;
    private float _effectT;

    private void Awake()
    {
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        if (cameras == null || cameras.Length == 0)
            cameras = GetComponentsInChildren<CinemachineCamera>(true);

        if (noiseComponents == null || noiseComponents.Length == 0)
            noiseComponents = GetComponentsInChildren<CinemachineBasicMultiChannelPerlin>(true);

        CacheBaseFovs();
        ApplyEffects(0f);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            ApplyEffects(0f);
    }

    private void OnDisable()
    {
        ApplyEffects(0f);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        float speed = flightController != null && flightController.IsFlightMode
            ? flightController.Velocity.magnitude
            : 0f;
        float targetT = Mathf.InverseLerp(minEffectSpeed, maxEffectSpeed, speed);
        float follow = 1f - Mathf.Exp(-Mathf.Max(0f, smoothing) * Time.deltaTime);
        _effectT = Mathf.Lerp(_effectT, targetT, follow);

        ApplyEffects(_effectT);
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

    private void ApplyEffects(float t)
    {
        t = Mathf.Clamp01(t);

        if (noiseComponents != null)
        {
            float amplitude = Mathf.Lerp(minNoiseAmplitude, maxNoiseAmplitude, t);
            float frequency = Mathf.Lerp(minNoiseFrequency, maxNoiseFrequency, t);
            for (int i = 0; i < noiseComponents.Length; i++)
            {
                if (noiseComponents[i] == null) continue;
                noiseComponents[i].AmplitudeGain = amplitude;
                noiseComponents[i].FrequencyGain = frequency;
            }
        }

        if (!driveFov || cameras == null || _baseFovs == null) return;

        float fovOffset = maxFovKick * t;
        int count = Mathf.Min(cameras.Length, _baseFovs.Length);
        for (int i = 0; i < count; i++)
        {
            if (cameras[i] == null) continue;
            cameras[i].Lens.FieldOfView = _baseFovs[i] + fovOffset;
        }
    }
}
