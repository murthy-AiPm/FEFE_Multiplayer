using UnityEngine;
using Unity.Netcode;

public class CombatSoundPlayer : NetworkBehaviour
{
    [Header("References (auto-found if null)")]
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private AnimationEventRelay animEventRelay;

    [Header("Default Materials")]
    [SerializeField] private string defaultWeaponMaterial = "Metal";
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

    private CombatController.CombatState _prevState = CombatController.CombatState.None;
    private HitboxController _activeHitbox;

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInChildren<DamageReceiver>();
        if (combatController == null) combatController = GetComponentInChildren<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInChildren<WeaponManager>();
        if (animEventRelay == null) animEventRelay = GetComponentInChildren<AnimationEventRelay>();

        Debug.Log($"[CombatSoundPlayer] Awake — weaponManager={weaponManager}, combatController={combatController}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[CombatSoundPlayer] OnNetworkSpawn — IsOwner={IsOwner}");

        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived += HandleDamageReceived;
            damageReceiver.OnDamageBlocked += HandleDamageBlocked;
            damageReceiver.OnDeath += HandleDeath;
        }

        if (IsOwner)
        {
            if (combatController != null)
            {
                combatController.OnStateChanged += HandleStateChanged;
                combatController.OnDodgeStarted += HandleDodge;
                Debug.Log("[CombatSoundPlayer] Subscribed to CombatController events");
            }
            else
            {
                Debug.LogWarning("[CombatSoundPlayer] CombatController is NULL — state change sounds won't work");
            }

            if (weaponManager != null)
            {
                weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
                weaponManager.OnWeaponHolstered += HandleWeaponHolstered;
                Debug.Log("[CombatSoundPlayer] Subscribed to WeaponManager events");

                // If a weapon is already equipped when we spawn (e.g. host), bind immediately
                var existingHitbox = GetComponentInChildren<HitboxController>();
                if (existingHitbox != null)
                {
                    Debug.Log($"[CombatSoundPlayer] Found existing HitboxController on spawn: {existingHitbox.gameObject.name}");
                    BindHitbox(existingHitbox);
                }
                else
                {
                    Debug.Log("[CombatSoundPlayer] No HitboxController found on spawn (weapon not yet in hand — OK)");
                }
            }
            else
            {
                Debug.LogWarning("[CombatSoundPlayer] WeaponManager is NULL — swing sounds won't work");
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

        if (weaponManager != null)
        {
            weaponManager.OnWeaponEquipped -= HandleWeaponEquipped;
            weaponManager.OnWeaponHolstered -= HandleWeaponHolstered;
        }

        UnbindHitbox();
        base.OnNetworkDespawn();
    }

    // ═══════════════════════════════════════════════════════
    //  HITBOX BINDING
    // ═══════════════════════════════════════════════════════

    private void HandleWeaponEquipped(WeaponData weaponData)
    {
        Debug.Log($"[CombatSoundPlayer] OnWeaponEquipped fired — weapon={weaponData?.name}");
        UnbindHitbox();

        if (weaponData == null || weaponData.weaponPrefab == null)
        {
            Debug.Log("[CombatSoundPlayer] weaponData or prefab is null, skipping hitbox bind");
            return;
        }

        // Weapon instance is now parented under this character — find the HitboxController
        var hitbox = GetComponentInChildren<HitboxController>();
        if (hitbox == null)
        {
            Debug.LogWarning($"[CombatSoundPlayer] No HitboxController found in children after equipping {weaponData.name}. Check weapon prefab has HitboxController.");
            return;
        }

        BindHitbox(hitbox);
    }

    private void HandleWeaponHolstered(WeaponData weaponData)
    {
        Debug.Log($"[CombatSoundPlayer] OnWeaponHolstered fired — weapon={weaponData?.name}");
        UnbindHitbox();
    }

    private void BindHitbox(HitboxController hitbox)
    {
        _activeHitbox = hitbox;
        _activeHitbox.OnHitboxEnabled += HandleSwingStarted;
        _activeHitbox.OnHitDetected += HandleHitDetected;
        Debug.Log($"[CombatSoundPlayer] Bound to HitboxController on {hitbox.gameObject.name}");
    }

    private void UnbindHitbox()
    {
        if (_activeHitbox == null) return;
        _activeHitbox.OnHitboxEnabled -= HandleSwingStarted;
        _activeHitbox.OnHitDetected -= HandleHitDetected;
        Debug.Log($"[CombatSoundPlayer] Unbound HitboxController");
        _activeHitbox = null;
    }

    // ═══════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════

    private void HandleSwingStarted()
    {
        Debug.Log($"[CombatSoundPlayer] HandleSwingStarted — playing '{swordSwingSound}' at {transform.position}");
        if (ProximitySoundManager.Instance == null)
        {
            Debug.LogWarning("[CombatSoundPlayer] ProximitySoundManager.Instance is NULL");
            return;
        }
        ProximitySoundManager.Instance.PlaySound(swordSwingSound, transform.position);
    }

    private void HandleDamageReceived(float damage, Vector3 hitPoint)
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlayImpact(defaultWeaponMaterial, bodyMaterial, hitPoint);
    }

    private void HandleDamageBlocked(float blockedDamage, Vector3 hitPoint)
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(blockSound, hitPoint);
    }

    private void HandleDeath()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(deathSound, transform.position);
    }

    private void HandleStateChanged(CombatController.CombatState newState)
    {
        if (ProximitySoundManager.Instance == null) return;

        if (newState == CombatController.CombatState.BowDrawing &&
            _prevState != CombatController.CombatState.BowDrawing)
        {
            ProximitySoundManager.Instance.PlaySound(bowDrawSound, transform.position);
        }

        if (_prevState == CombatController.CombatState.BowAiming &&
            newState == CombatController.CombatState.None)
        {
            ProximitySoundManager.Instance.PlaySound(bowReleaseSound, transform.position);
        }

        _prevState = newState;
    }

    private void HandleDodge()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(dodgeSound, transform.position);
    }

    private void HandleHitDetected(HitInfo hitInfo)
    {
        if (ProximitySoundManager.Instance == null) return;

        string attackerMat = defaultWeaponMaterial;
        if (_activeHitbox != null)
        {
            var matTag = _activeHitbox.GetComponentInParent<MaterialTag>();
            if (matTag != null) attackerMat = matTag.MaterialType;
        }

        if (attackerMat == defaultWeaponMaterial && hitInfo.weaponData != null)
        {
            attackerMat = hitInfo.weaponData.weaponType switch
            {
                WeaponType.Bow => "Wood",
                _ => "Metal"
            };
        }

        string targetMat = "Flesh";
        if (hitInfo.hitCollider != null)
        {
            var surfaceTag = hitInfo.hitCollider.GetComponentInParent<SurfaceTag>();
            if (surfaceTag != null) targetMat = surfaceTag.ImpactMaterial;

            var matTag = hitInfo.hitCollider.GetComponentInParent<MaterialTag>();
            if (matTag != null) targetMat = matTag.MaterialType;
        }

        ProximitySoundManager.Instance.PlayImpact(attackerMat, targetMat, hitInfo.hitPoint);
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════

    public void PlaySwingSound()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(swordSwingSound, transform.position);
    }

    public void PlayCombatSound(string soundName)
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(soundName, transform.position);
    }

    public void PlayRespawnSound()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(respawnSound, transform.position);
    }
}
