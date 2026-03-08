using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// CombatSoundPlayer.cs — Context-sensitive combat sounds
//
// Attach to player prefab root. Subscribes to existing combat
// events (DamageReceiver, CombatController, AnimationEventRelay)
// and plays the appropriate networked sounds.
//
// Impact sounds use the material matrix:
//   weapon material + target material → specific clip set
// ─────────────────────────────────────────────────────────

public class CombatSoundPlayer : NetworkBehaviour
{
    [Header("References (auto-found if null)")]
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private AnimationEventRelay animEventRelay;
    [SerializeField] private HitboxController hitboxController;

    [Header("Default Materials")]
    [Tooltip("Default weapon material if no MaterialTag on weapon")]
    [SerializeField] private string defaultWeaponMaterial = "Metal";

    [Tooltip("Material type of this character's body (for incoming hits)")]
    [SerializeField] private string bodyMaterial = "Flesh";

    [Header("Sound Names (must match SoundDatabase entries)")]
    [SerializeField] private string swordSwingSound = "Sword_Swing";
    [SerializeField] private string bowDrawSound = "Bow_Draw";
    [SerializeField] private string bowReleaseSound = "Bow_Release";
    [SerializeField] private string arrowWhooshSound = "Arrow_Whoosh";
    [SerializeField] private string blockSound = "Block_Clang";
    [SerializeField] private string dodgeSound = "Dodge_Whoosh";
    [SerializeField] private string deathSound = "Player_Death";
    [SerializeField] private string respawnSound = "Player_Respawn";

    // Track combat state for sound triggers
    private CombatController.CombatState _prevState = CombatController.CombatState.None;

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInChildren<DamageReceiver>();
        if (combatController == null) combatController = GetComponentInChildren<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInChildren<WeaponManager>();
        if (animEventRelay == null) animEventRelay = GetComponentInChildren<AnimationEventRelay>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to damage events (these fire on all clients via ClientRpc)
        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived += HandleDamageReceived;
            damageReceiver.OnDamageBlocked += HandleDamageBlocked;
            damageReceiver.OnDeath += HandleDeath;
        }

        // Owner-only: combat state changes and attack events
        if (IsOwner)
        {
            if (combatController != null)
            {
                combatController.OnStateChanged += HandleStateChanged;
                combatController.OnDodgeStarted += HandleDodge;
            }

            // Hitbox enable = swing started (for swing whoosh sound)
            if (hitboxController != null)
            {
                hitboxController.OnHitDetected += HandleHitDetected;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived -= HandleDamageReceived;
            damageReceiver.OnDamageBlocked -= HandleDamageBlocked;
            damageReceiver.OnDeath -= HandleDeath;
        }

        if (combatController != null)
        {
            combatController.OnStateChanged -= HandleStateChanged;
            combatController.OnDodgeStarted -= HandleDodge;
        }

        if (hitboxController != null)
        {
            hitboxController.OnHitDetected -= HandleHitDetected;
        }

        base.OnNetworkDespawn();
    }

    // ═══════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Called on ALL clients when this character takes damage (via ClientRpc in DamageReceiver).
    /// Plays impact sound based on weapon + body material.
    /// </summary>
    private void HandleDamageReceived(float damage, Vector3 hitPoint)
    {
        if (ProximitySoundManager.Instance == null) return;

        // Play flesh impact at hit point
        // We use the default weapon material since we don't know the attacker's weapon here
        // TODO: Pass attacker material through DamageReceiver for more accurate sounds
        ProximitySoundManager.Instance.PlayImpact(defaultWeaponMaterial, bodyMaterial, hitPoint);
    }

    /// <summary>
    /// Called on ALL clients when this character blocks an attack.
    /// </summary>
    private void HandleDamageBlocked(float blockedDamage, Vector3 hitPoint)
    {
        if (ProximitySoundManager.Instance == null) return;

        // Play block sound (metal on metal clang)
        ProximitySoundManager.Instance.PlaySound(blockSound, hitPoint);
    }

    /// <summary>
    /// Called when this character dies.
    /// </summary>
    private void HandleDeath()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(deathSound, transform.position);
    }

    /// <summary>
    /// Owner only — combat state changed. Used to trigger bow draw/release sounds.
    /// </summary>
    private void HandleStateChanged(CombatController.CombatState newState)
    {
        if (ProximitySoundManager.Instance == null) return;

        // Bow draw started
        if (newState == CombatController.CombatState.BowDrawing &&
            _prevState != CombatController.CombatState.BowDrawing)
        {
            ProximitySoundManager.Instance.PlaySound(bowDrawSound, transform.position);
        }

        // Bow released (was aiming, now going back to None — arrow was fired)
        if (_prevState == CombatController.CombatState.BowAiming &&
            newState == CombatController.CombatState.None)
        {
            ProximitySoundManager.Instance.PlaySound(bowReleaseSound, transform.position);
        }

        _prevState = newState;
    }

    /// <summary>
    /// Owner only — dodge started.
    /// </summary>
    private void HandleDodge()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(dodgeSound, transform.position);
    }

    /// <summary>
    /// Owner only — hitbox detected a hit on something.
    /// This is where we play the IMPACT sound with correct materials.
    /// </summary>
    private void HandleHitDetected(HitInfo hitInfo)
    {
        if (ProximitySoundManager.Instance == null) return;

        // Determine attacker material from weapon
        string attackerMat = defaultWeaponMaterial;

        // Try to find MaterialTag on the hitbox's weapon GameObject
        if (hitboxController != null)
        {
            var matTag = hitboxController.GetComponentInParent<MaterialTag>();
            if (matTag != null) attackerMat = matTag.MaterialType;
        }

        // Fallback: infer material from weapon type
        if (attackerMat == defaultWeaponMaterial && hitInfo.weaponData != null)
        {
            attackerMat = hitInfo.weaponData.weaponType switch
            {
                WeaponType.Bow => "Wood",
                _ => "Metal"
            };
        }

        // Determine target material
        string targetMat = "Flesh"; // default for living things
        if (hitInfo.hitCollider != null)
        {
            // Check for SurfaceTag on hit object
            var surfaceTag = hitInfo.hitCollider.GetComponentInParent<SurfaceTag>();
            if (surfaceTag != null)
                targetMat = surfaceTag.ImpactMaterial;

            // Check for MaterialTag on hit object
            var matTag = hitInfo.hitCollider.GetComponentInParent<MaterialTag>();
            if (matTag != null)
                targetMat = matTag.MaterialType;
        }

        ProximitySoundManager.Instance.PlayImpact(attackerMat, targetMat, hitInfo.hitPoint);
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API — For triggering sounds from external scripts
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Play the swing/whoosh sound. Call from animation event or hitbox enable.
    /// </summary>
    public void PlaySwingSound()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(swordSwingSound, transform.position);
    }

    /// <summary>
    /// Play a named sound at this character's position (convenience method).
    /// </summary>
    public void PlayCombatSound(string soundName)
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(soundName, transform.position);
    }

    /// <summary>
    /// Play respawn sound. Call from DamageReceiver respawn flow.
    /// </summary>
    public void PlayRespawnSound()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(respawnSound, transform.position);
    }
}
