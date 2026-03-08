using UnityEngine;

// ─────────────────────────────────────────────────────────
// SurfaceTag.cs — Identifies surface material on non-terrain objects
//
// Attach to any collider (floors, walls, props, buildings)
// so the sound system knows what material it is.
// Used for footsteps on non-terrain surfaces and arrow impacts.
// ─────────────────────────────────────────────────────────

public class SurfaceTag : MonoBehaviour
{
    [Tooltip("Surface type name. Must match an entry in SoundDatabase " +
             "(e.g. 'Stone', 'Wood', 'Metal', 'Dirt', 'Grass')")]
    [SerializeField] private string surfaceType = "Stone";

    [Tooltip("Material type for impact sounds. Must match an entry in " +
             "the SoundDatabase impact matrix (e.g. 'Stone', 'Wood', 'Metal', 'Flesh')")]
    [SerializeField] private string impactMaterial = "Stone";

    public string SurfaceType => surfaceType;
    public string ImpactMaterial => impactMaterial;
}
