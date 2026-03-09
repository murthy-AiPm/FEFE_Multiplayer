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
    [SerializeField] private string bowLoadSound = "Bow_Load";
    [SerializeField] private string bowDrawSound = "Bow_Draw";
    [SerializeField] private string bowReleaseSound = "Bow_Release";
    [SerializeField] private string arrowWhooshSound = "Arrow_Whoosh";
    [SerializeField] private string blockSound = "Block_Clang";
    [SerializeField] private string dodgeSound = "Dodge_Whoosh";
    [SerializeField] private string deathSound = "Player_Death";
    [SerializeField] private string respawnSound = "Player_Respawn";

    private CombatController.CombatState _prevState = CombatController.CombatState.None;
    private HitboxController _activeHitbox;
    private MaterialTag _activeWeaponMaterialTag;
    private WeaponData _activeWeaponData;
    private float _lastImpactTime;
    private const float IMPACT_DEDUP_WINDOW = 0.1f; // ignore duplicate impact within 100ms

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
            }

            if (weaponManager != null)
            {
                weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
                weaponManager.OnWeaponHolstered += HandleWeaponHolstered;

                var existingHitbox = GetComponentInChildren<HitboxController>();
                if (existingHitbox != null)
                    BindHitbox(existingHitbox);
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
        _activeWeaponData = weaponData;
        UnbindHitbox();
        if (weaponData == null || weaponData.weaponPrefab == null) return;
        var hitbox = GetComponentInChildren<HitboxController>();
        if (hitbox == null) return;
        BindHitbox(hitbox);
    }

    private void HandleWeaponHolstered(WeaponData weaponData)
    {
        _activeWeaponData = null;
        UnbindHitbox();
    }

    private void BindHitbox(HitboxController hitbox)
    {
        _activeHitbox = hitbox;
        _activeWeaponMaterialTag = hitbox.GetComponent<MaterialTag>();
        if (_activeWeaponMaterialTag == null)
            _activeWeaponMaterialTag = hitbox.GetComponentInChildren<MaterialTag>();
        if (_activeWeaponMaterialTag == null)
            _activeWeaponMaterialTag = hitbox.GetComponentInParent<MaterialTag>();

        Debug.Log($"[CombatSoundPlayer] BindHitbox: hitbox={hitbox.gameObject.name}, MaterialTag={(_activeWeaponMaterialTag != null ? _activeWeaponMaterialTag.MaterialType : "NOT FOUND")}");

        _activeHitbox.OnHitboxEnabled += HandleSwingStarted;
        _activeHitbox.OnHitDetected += HandleHitDetected;
    }

    private void UnbindHitbox()
    {
        if (_activeHitbox == null) return;
        _activeHitbox.OnHitboxEnabled -= HandleSwingStarted;
        _activeHitbox.OnHitDetected -= HandleHitDetected;
        _activeHitbox = null;
        _activeWeaponMaterialTag = null;
    }

    // ═══════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════

    private void HandleSwingStarted()
    {
        if (ProximitySoundManager.Instance == null) return;

        bool isBow = _activeWeaponData != null && _activeWeaponData.weaponType == WeaponType.Bow;
        if (isBow)
            ProximitySoundManager.Instance.PlaySound(bowLoadSound, transform.position);
        else
            ProximitySoundManager.Instance.PlaySound(swordSwingSound, transform.position);
    }

    private void HandleDamageReceived(float damage, Vector3 hitPoint)
    {
        if (ProximitySoundManager.Instance == null) return;
        // Deduplicate: OnHitLocal and NotifyHitClientRpc both fire OnDamageReceived
        // within milliseconds of each other — only play the first one
        if (Time.time - _lastImpactTime < IMPACT_DEDUP_WINDOW) return;
        _lastImpactTime = Time.time;
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
            ProximitySoundManager.Instance.PlaySound(bowDrawSound, transform.position);

        if (_prevState == CombatController.CombatState.BowAiming &&
            newState == CombatController.CombatState.None)
            ProximitySoundManager.Instance.PlaySound(bowReleaseSound, transform.position);

        _prevState = newState;
    }

    private void HandleDodge()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(dodgeSound, transform.position);
    }

    private void HandleHitDetected(HitInfo hitInfo)
    {
        if (ProximitySoundManager.Instance == null)
        {
            Debug.LogWarning("[CombatSoundPlayer] HandleHitDetected: ProximitySoundManager is NULL");
            return;
        }

        string attackerMat = _activeWeaponMaterialTag != null
            ? _activeWeaponMaterialTag.MaterialType
            : defaultWeaponMaterial;

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
            if (surfaceTag != null)
                targetMat = surfaceTag.ImpactMaterial;
            else
            {
                var matTag = hitInfo.hitCollider.GetComponentInParent<MaterialTag>();
                if (matTag != null) targetMat = matTag.MaterialType;
            }
        }

        Debug.Log($"[CombatSoundPlayer] HandleHitDetected: attacker={attackerMat}, target={targetMat}, hitCollider={hitInfo.hitCollider?.gameObject.name}, hitPoint={hitInfo.hitPoint}");

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
