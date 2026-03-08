using UnityEngine;

// ─────────────────────────────────────────────────────────
// MaterialTag.cs — Identifies weapon/projectile material
//
// Attach to weapon prefabs and projectiles (arrows, ballista bolts).
// Used by CombatSoundPlayer to look up the correct impact sound
// when this weapon/projectile hits a target.
// ─────────────────────────────────────────────────────────

public class MaterialTag : MonoBehaviour
{
    [Tooltip("Material type of this weapon/projectile. " +
             "Must match an attackerMaterial in SoundDatabase's impact matrix " +
             "(e.g. 'Metal', 'Wood', 'Stone')")]
    [SerializeField] private string materialType = "Metal";

    public string MaterialType => materialType;
}
