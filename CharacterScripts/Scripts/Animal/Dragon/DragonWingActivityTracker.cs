using UnityEngine;

/// <summary>
/// Tracks dragon wing-bone angular velocity and exposes an "are the wings actually
/// flapping" signal. Pure local computation from the animator-driven bone — runs on
/// every peer (server, owner, remote clients) without any IsOwner gating, so
/// server-authoritative consumers like DragonStaminaController can read it directly.
///
/// The signal differs from input-side proxies (FlightThrust, IsFlightMode) because a
/// dragon diving with wings tucked still has high thrust but no flap motion. Anything
/// that should fire only on real wing work (stamina drain, flap audio gate) should
/// read this rather than thrust.
///
/// Originally lived inside DragonSoundPlayer; extracted so combat/vital code doesn't
/// have to depend on the sound class.
/// </summary>
public class DragonWingActivityTracker : MonoBehaviour
{
    [Header("Wing Bone")]
    [Tooltip("Wing bone to track. Place near the shoulder where flap rotation is largest.")]
    [SerializeField] private Transform wingBone;

    [Header("Tuning")]
    [Tooltip("Minimum smoothed wing angular velocity (deg/sec) to count as 'flapping'. 80 works for the default dragon rig (glide peaks at ~30, flap bottoms at ~160).")]
    [SerializeField] private float flapThreshold = 80f;
    [Tooltip("Smoothing factor for wing motion. Higher = more responsive to sudden changes, lower = smoother average.")]
    [SerializeField] private float smoothing = 8f;

    private Quaternion _lastWingRotation;
    private float _wingActivity; // smoothed deg/sec

    /// <summary>Smoothed wing angular velocity in degrees/sec.</summary>
    public float WingActivity => _wingActivity;

    /// <summary>
    /// True when smoothed wing activity exceeds the flap threshold. Returns true when
    /// no wing bone is wired so consumers fall back to pre-tracker behavior (no gate /
    /// drain as if flapping) rather than silently breaking when the Inspector ref is
    /// missing.
    /// </summary>
    public bool IsFlapping => wingBone == null || _wingActivity > flapThreshold;

    /// <summary>The configured flap threshold (deg/sec).</summary>
    public float FlapThreshold => flapThreshold;

    private void Awake()
    {
        if (wingBone != null) _lastWingRotation = wingBone.localRotation;
    }

    // LateUpdate so we sample the wing bone AFTER the animator has written to it.
    private void LateUpdate()
    {
        if (wingBone == null) return;

        Quaternion current = wingBone.localRotation;
        float deltaAngle = Quaternion.Angle(_lastWingRotation, current);
        float angularVelocity = deltaAngle / Mathf.Max(Time.deltaTime, 0.0001f);
        _wingActivity = Mathf.Lerp(_wingActivity, angularVelocity, smoothing * Time.deltaTime);
        _lastWingRotation = current;
    }
}
