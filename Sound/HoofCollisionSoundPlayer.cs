using UnityEngine;

// ─────────────────────────────────────────────────────────
// HoofCollisionSoundPlayer.cs
//
// Attach to each hoof GameObject (which has a Sphere Collider).
// Calls HorseSoundPlayer.HorseFootStep() when the hoof touches
// the ground, but only when the horse is moving.
//
// Requires:
//   - SphereCollider on this GameObject
//   - HorseSoundPlayer on the horse root
//   - MountableEntity on the horse root (for IsMoving check)
// ─────────────────────────────────────────────────────────

public class HoofCollisionSoundPlayer : MonoBehaviour
{
    [Header("Ground Layers")]
    [Tooltip("Layers considered ground. Set to your terrain/ground layer.")]
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Cooldown")]
    [Tooltip("Minimum time between sounds per hoof (prevents rapid double-fire on rough terrain)")]
    [SerializeField] private float cooldown = 0.1f;

    // ─── References (found at runtime) ───
    private HorseSoundPlayer _horseSoundPlayer;
    private MountableEntity _mountableEntity;
    private HorseSoundPlayer.FootstepMode _requiredMode = HorseSoundPlayer.FootstepMode.HoofCollision;

    // ─── State ───
    private float _lastSoundTime;

    private void Awake()
    {
        _horseSoundPlayer = GetComponentInParent<HorseSoundPlayer>();
        _mountableEntity = GetComponentInParent<MountableEntity>();

        //if (_horseSoundPlayer == null)
        //    Debug.LogWarning($"[HoofCollisionSoundPlayer] No HorseSoundPlayer found in parent of {gameObject.name}");
    }

    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log($"[HoofCollision] {gameObject.name} triggered by {other.gameObject.name} layer={other.gameObject.layer}");

        // Check layer
        if ((groundLayers.value & (1 << other.gameObject.layer)) == 0) return;

        // Check cooldown
        if (Time.time - _lastSoundTime < cooldown) return;

        // Check mode
        if (_horseSoundPlayer != null && _horseSoundPlayer.GetFootstepMode() != HorseSoundPlayer.FootstepMode.HoofCollision) return;

        // Check horse is moving
        if (_mountableEntity != null && !_mountableEntity.IsMoving) return;

        // Play
        if (_horseSoundPlayer != null)
            _horseSoundPlayer.HorseFootStep();

        _lastSoundTime = Time.time;
    }
}
