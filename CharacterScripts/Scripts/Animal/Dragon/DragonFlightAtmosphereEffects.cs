using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owner-only high-altitude atmosphere effects for dragon flight. Drives a local
/// post-process Volume and wind-gush ParticleSystems from altitude and speed.
/// </summary>
public class DragonFlightAtmosphereEffects : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [Tooltip("Optional local/global Volume with lens distortion, chromatic aberration, vignette, etc. Script drives weight only.")]
    [SerializeField] private Volume atmosphereVolume;
    [Tooltip("Optional URP profile used when Auto Create Atmosphere Volume is enabled and no Volume is assigned.")]
    [SerializeField] private VolumeProfile autoAtmosphereProfile;
    [SerializeField] private bool autoCreateAtmosphereVolume = true;
    [SerializeField] private bool autoAtmosphereVolumeIsGlobal = true;
    [SerializeField] private float autoAtmosphereVolumePriority = 10f;
    [Tooltip("Wind streak / gust particle systems near the camera or dragon. Script drives emission and simulation speed.")]
    [SerializeField] private ParticleSystem[] windGushParticles;
    [Tooltip("Origin for altitude raycasts. If unset, uses this transform.")]
    [SerializeField] private Transform altitudeRayOrigin;

    [Header("Altitude Input")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("Altitude where high-atmosphere effects begin.")]
    [SerializeField] private float highAltitudeStart = 60f;
    [Tooltip("Altitude where high-atmosphere effects reach full strength.")]
    [SerializeField] private float highAltitudeFull = 180f;
    [Tooltip("Maps normalized high altitude (0 at Start, 1 at Full) to effect strength.")]
    [SerializeField] private AnimationCurve altitudeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool showAltitudeDebug;

    [Header("Speed Input")]
    [SerializeField] private float minEffectSpeed = 8f;
    [SerializeField] private float maxEffectSpeed = 45f;
    [Tooltip("Maps normalized speed (0 at Min, 1 at Max) to effect strength.")]
    [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Effect Mix")]
    [Tooltip("If true, effect strength requires both altitude and speed. If false, altitude alone can show atmosphere.")]
    [SerializeField] private bool multiplyAltitudeBySpeed = true;
    [Tooltip("How quickly Volume weight and particle intensity follow target strength.")]
    [SerializeField] private float smoothing = 4f;

    [Header("Debug Preview")]
    [Tooltip("For Inspector tuning. Forces the effect on even if altitude/speed/owner checks would keep it off.")]
    [SerializeField] private bool forceEffectForTuning;
    [Range(0f, 1f)]
    [SerializeField] private float forcedEffectStrength = 1f;

    [Header("Volume Output")]
    [SerializeField] private bool driveVolumeWeight = true;
    [SerializeField] private float maxVolumeWeight = 1f;

    [Header("Wind Gush VFX Output")]
    [SerializeField] private bool driveWindParticles = true;
    [Tooltip("Creates a simple local wind-streak ParticleSystem if none are assigned.")]
    [SerializeField] private bool autoCreateWindParticles = true;
    [SerializeField] private string autoWindParticleName = "Auto_WindGushParticles";
    [Tooltip("Optional parent for auto-created wind particles. If unset, the script prefers Camera.main, then falls back to this transform.")]
    [SerializeField] private Transform autoWindParticleParent;
    [SerializeField] private bool preferMainCameraForAutoWindParticles = true;
    [SerializeField] private Vector3 autoWindParticleLocalPosition = new Vector3(0f, 0f, 2f);
    [SerializeField] private float autoWindParticleConeAngle = 10f;
    [SerializeField] private float autoWindParticleConeRadius = 1.8f;
    [SerializeField] private Vector2 autoWindParticleLifetime = new Vector2(0.35f, 0.8f);
    [SerializeField] private Vector2 autoWindParticleStartSpeed = new Vector2(8f, 22f);
    [SerializeField] private Vector2 autoWindParticleStartSize = new Vector2(0.03f, 0.1f);
    [SerializeField] private Color autoWindParticleColor = new Color(0.75f, 0.9f, 1f, 0.45f);
    [SerializeField] private int autoWindParticleMaxParticles = 400;
    [SerializeField] private float autoWindParticleLengthScale = 5f;
    [SerializeField] private float autoWindParticleVelocityScale = 0.6f;
    [Tooltip("Emission rate at zero effect strength.")]
    [SerializeField] private float minEmissionRate = 0f;
    [Tooltip("Emission rate at full effect strength.")]
    [SerializeField] private float maxEmissionRate = 180f;
    [Tooltip("Particle simulation speed at zero effect strength.")]
    [SerializeField] private float minSimulationSpeed = 0.5f;
    [Tooltip("Particle simulation speed at full effect strength.")]
    [SerializeField] private float maxSimulationSpeed = 2.5f;
    [Tooltip("Stop particle systems when effect strength reaches zero.")]
    [SerializeField] private bool stopParticlesWhenInactive = true;

    [Header("Debug Readout")]
    [SerializeField] private float debugAltitude = -1f;
    [SerializeField] private float debugSpeed;
    [SerializeField] private float debugAltitudeT;
    [SerializeField] private float debugSpeedT;
    [SerializeField] private float debugEffectT;
    [SerializeField] private bool debugHasAtmosphereVolume;
    [SerializeField] private bool debugHasAtmosphereProfile;
    [SerializeField] private int debugWindParticleCount;
    [SerializeField] private float debugVolumeWeight;
    [SerializeField] private float debugEmissionRate;
    [SerializeField] private float debugSimulationSpeed;

    private float _currentEffectT;
    private float _baseVolumeWeight;
    private ParticleSystem _autoCreatedWindParticles;

    private void Awake()
    {
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        EnsureAtmosphereVolume();

        if (atmosphereVolume != null)
            _baseVolumeWeight = atmosphereVolume.weight;

        EnsureWindParticles();
        UpdateDebugSetupState();
        ApplyEffects(0f);
    }

    private void OnValidate()
    {
        forcedEffectStrength = Mathf.Clamp01(forcedEffectStrength);
        highAltitudeFull = Mathf.Max(highAltitudeStart + 0.01f, highAltitudeFull);
        maxEffectSpeed = Mathf.Max(minEffectSpeed + 0.01f, maxEffectSpeed);
        autoWindParticleMaxParticles = Mathf.Max(1, autoWindParticleMaxParticles);
        UpdateDebugSetupState();
    }

    private void Start()
    {
        if (flightController != null && !flightController.IsOwner)
            ApplyEffects(0f);
    }

    private void OnDisable()
    {
        ApplyEffects(0f);
    }

    private void LateUpdate()
    {
        if (!forceEffectForTuning && (flightController == null || !flightController.IsOwner)) return;

        TryAttachAutoWindParticlesToMainCamera();

        bool inFlight = flightController != null && flightController.IsFlightMode;
        float targetT = forceEffectForTuning ? forcedEffectStrength : inFlight ? GetTargetEffectT() : 0f;
        float follow = 1f - Mathf.Exp(-Mathf.Max(0f, smoothing) * Time.deltaTime);
        _currentEffectT = Mathf.Lerp(_currentEffectT, targetT, follow);

        ApplyEffects(_currentEffectT);
        debugEffectT = _currentEffectT;
    }

    private float GetTargetEffectT()
    {
        debugAltitudeT = GetAltitudeT();
        debugSpeedT = GetSpeedT();

        float effectT = multiplyAltitudeBySpeed
            ? debugAltitudeT * debugSpeedT
            : Mathf.Max(debugAltitudeT, debugSpeedT);

        return Mathf.Clamp01(effectT);
    }

    private float GetAltitudeT()
    {
        debugAltitude = -1f;

        Transform origin = altitudeRayOrigin != null ? altitudeRayOrigin : transform;
        float rayDistance = Mathf.Max(highAltitudeFull, highAltitudeStart);

        if (!Physics.Raycast(origin.position, Vector3.down, out RaycastHit hit, rayDistance, groundMask))
        {
            if (showAltitudeDebug)
                Debug.DrawRay(origin.position, Vector3.down * rayDistance, Color.magenta);
            debugAltitude = rayDistance;
            return 1f;
        }

        debugAltitude = hit.distance;
        if (showAltitudeDebug)
            Debug.DrawLine(origin.position, hit.point, Color.magenta);

        float normalized = Mathf.InverseLerp(highAltitudeStart, highAltitudeFull, hit.distance);
        return Mathf.Clamp01(altitudeCurve.Evaluate(normalized));
    }

    private float GetSpeedT()
    {
        debugSpeed = flightController != null ? flightController.Velocity.magnitude : 0f;
        float normalized = Mathf.InverseLerp(minEffectSpeed, maxEffectSpeed, debugSpeed);
        return Mathf.Clamp01(speedCurve.Evaluate(normalized));
    }

    private void ApplyEffects(float effectT)
    {
        effectT = Mathf.Clamp01(effectT);

        if (driveVolumeWeight && atmosphereVolume != null)
        {
            atmosphereVolume.weight = Mathf.Lerp(_baseVolumeWeight, maxVolumeWeight, effectT);
            debugVolumeWeight = atmosphereVolume.weight;
        }
        else
        {
            debugVolumeWeight = 0f;
        }

        if (!driveWindParticles || windGushParticles == null)
        {
            debugEmissionRate = 0f;
            debugSimulationSpeed = 0f;
            return;
        }

        float emissionRate = Mathf.Lerp(minEmissionRate, maxEmissionRate, effectT);
        float simulationSpeed = Mathf.Lerp(minSimulationSpeed, maxSimulationSpeed, effectT);
        debugEmissionRate = emissionRate;
        debugSimulationSpeed = simulationSpeed;

        for (int i = 0; i < windGushParticles.Length; i++)
        {
            ParticleSystem ps = windGushParticles[i];
            if (ps == null) continue;

            var emission = ps.emission;
            emission.rateOverTime = emissionRate;

            var main = ps.main;
            main.simulationSpeed = simulationSpeed;

            if (effectT > 0.001f)
            {
                if (!ps.isPlaying)
                    ps.Play(true);
            }
            else if (stopParticlesWhenInactive && ps.isPlaying)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void EnsureWindParticles()
    {
        if (!driveWindParticles || !autoCreateWindParticles || HasAssignedWindParticles())
            return;

        Transform parent = GetAutoWindParticleParent();
        GameObject windObject = new GameObject(autoWindParticleName);
        windObject.transform.SetParent(parent, false);
        windObject.transform.localPosition = autoWindParticleLocalPosition;
        windObject.transform.localRotation = Quaternion.identity;
        windObject.transform.localScale = Vector3.one;

        ParticleSystem ps = windObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(autoWindParticleLifetime.x, autoWindParticleLifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(autoWindParticleStartSpeed.x, autoWindParticleStartSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(autoWindParticleStartSize.x, autoWindParticleStartSize.y);
        main.startColor = autoWindParticleColor;
        main.maxParticles = Mathf.Max(1, autoWindParticleMaxParticles);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = Mathf.Max(0f, autoWindParticleConeAngle);
        shape.radius = Mathf.Max(0f, autoWindParticleConeRadius);

        ParticleSystemRenderer particleRenderer = ps.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = Mathf.Max(0f, autoWindParticleLengthScale);
        particleRenderer.velocityScale = Mathf.Max(0f, autoWindParticleVelocityScale);

        windGushParticles = new[] { ps };
        _autoCreatedWindParticles = ps;
        UpdateDebugSetupState();
    }

    private bool HasAssignedWindParticles()
    {
        if (windGushParticles == null)
            return false;

        for (int i = 0; i < windGushParticles.Length; i++)
        {
            if (windGushParticles[i] != null)
                return true;
        }

        return false;
    }

    private void EnsureAtmosphereVolume()
    {
        if (atmosphereVolume != null || !autoCreateAtmosphereVolume || autoAtmosphereProfile == null)
            return;

        GameObject volumeObject = new GameObject("Auto_DragonAtmosphereVolume");
        volumeObject.transform.SetParent(transform, false);
        volumeObject.transform.localPosition = Vector3.zero;
        volumeObject.transform.localRotation = Quaternion.identity;
        volumeObject.transform.localScale = Vector3.one;

        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = autoAtmosphereVolumeIsGlobal;
        volume.priority = autoAtmosphereVolumePriority;
        volume.weight = 0f;
        volume.sharedProfile = autoAtmosphereProfile;

        atmosphereVolume = volume;
        UpdateDebugSetupState();
    }

    private void UpdateDebugSetupState()
    {
        debugHasAtmosphereVolume = atmosphereVolume != null;
        debugHasAtmosphereProfile = atmosphereVolume != null && atmosphereVolume.sharedProfile != null;
        debugWindParticleCount = 0;

        if (windGushParticles == null)
            return;

        for (int i = 0; i < windGushParticles.Length; i++)
        {
            if (windGushParticles[i] != null)
                debugWindParticleCount++;
        }
    }

    private Transform GetAutoWindParticleParent()
    {
        if (autoWindParticleParent != null)
            return autoWindParticleParent;

        if (preferMainCameraForAutoWindParticles && Camera.main != null)
            return Camera.main.transform;

        return transform;
    }

    private void TryAttachAutoWindParticlesToMainCamera()
    {
        if (_autoCreatedWindParticles == null || autoWindParticleParent != null || !preferMainCameraForAutoWindParticles)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null || _autoCreatedWindParticles.transform.parent == mainCamera.transform)
            return;

        _autoCreatedWindParticles.transform.SetParent(mainCamera.transform, false);
        _autoCreatedWindParticles.transform.localPosition = autoWindParticleLocalPosition;
        _autoCreatedWindParticles.transform.localRotation = Quaternion.identity;
        _autoCreatedWindParticles.transform.localScale = Vector3.one;
    }
}
