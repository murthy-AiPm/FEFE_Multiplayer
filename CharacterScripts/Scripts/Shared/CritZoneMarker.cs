using UnityEngine;

/// <summary>
/// Place on any collider that should count as a critical hit zone.
/// Projectiles check for this component on the hit collider to decide
/// whether the impact triggers a hit reaction animation.
/// Works on any character — dragon, human, NPC.
/// </summary>
public class CritZoneMarker : MonoBehaviour
{
    [Tooltip("Optional damage multiplier for this zone. 1.0 = normal crit damage. " +
             "Not used yet — placeholder for future per-zone scaling.")]
    [SerializeField] private float damageMultiplier = 1f;

    [Tooltip("Optional label for this zone (e.g. 'head', 'wing'). " +
             "Not used yet — placeholder for future per-zone logic.")]
    [SerializeField] private string zoneName = "";

    /// <summary>Damage multiplier for this crit zone. Default 1.0.</summary>
    public float DamageMultiplier => damageMultiplier;

    /// <summary>Optional zone label for per-zone logic (empty = unnamed).</summary>
    public string ZoneName => zoneName;
}
