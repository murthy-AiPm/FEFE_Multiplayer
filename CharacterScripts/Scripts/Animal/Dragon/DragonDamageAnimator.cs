using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bridge between DamageReceiver's animation events and the dragon's Mecanim Animator.
/// Place on the dragon prefab alongside DamageReceiver.
///
/// Hit reaction:
///   Computes hit direction in dragon-local space, sets HitFB / HitLR floats
///   and GotHit bool on the Animator. The Animator Controller has a transition
///   from locomotion → hit blend tree conditioned on GotHit == true.
///   The blend tree uses HitFB (front/back) and HitLR (left/right) to pick
///   the directional hit animation.
///
/// Death:
///   1D blend tree using DeathLR only (left/right). Sets DeathLR float and IsDead bool.
///   Front/back hits map to nearest side (front-left → left, etc.).
///
/// Animator parameter setup:
///   Bool:  GotHit, IsDead
///   Float: HitFB, HitLR, DeathLR
/// </summary>
public class DragonDamageAnimator : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private Animator animator;

    [Header("Hit Reaction")]
    [Tooltip("Seconds before GotHit resets to false. Set this SHORTER than the hit anim length " +
             "so the Animator entry transition fires, but the exit transition uses Has Exit Time to " +
             "wait for the clip to finish. HitFB/HitLR values are NOT zeroed — they persist until the next hit.")]
    [SerializeField] private float hitResetDelay = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = false;
    [Tooltip("Enable keypad testing: Hit = 8/2/4/6 (FB/LR), Death = 7(-1)/1(-0.5)/3(+0.5)/9(+1). REMOVE BEFORE SHIPPING.")]
    [SerializeField] private bool debugKeypadTesting = false;

    // Animator param hashes
    private static readonly int Hash_GotHit = Animator.StringToHash("GotHit");
    private static readonly int Hash_HitFB  = Animator.StringToHash("HitFB");
    private static readonly int Hash_HitLR  = Animator.StringToHash("HitLR");
    private static readonly int Hash_IsDead = Animator.StringToHash("IsDead");
    private static readonly int Hash_DeathLR = Animator.StringToHash("DeathLR");

    private bool _isDead = false;
    private float _hitResetTimer = -1f;

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInParent<DamageReceiver>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (damageReceiver != null)
        {
            damageReceiver.OnPlayHitAnimation += HandleHitAnimation;
            damageReceiver.OnPlayDeathAnimation += HandleDeathAnimation;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (damageReceiver != null)
        {
            damageReceiver.OnPlayHitAnimation -= HandleHitAnimation;
            damageReceiver.OnPlayDeathAnimation -= HandleDeathAnimation;
        }

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // Auto-reset GotHit after a short delay so the Animator transition can fire.
        // HitFB/HitLR stay at their values — the exit transition uses Has Exit Time
        // to wait for the clip to finish before returning to locomotion.
        if (_hitResetTimer > 0f)
        {
            _hitResetTimer -= Time.deltaTime;
            if (_hitResetTimer <= 0f)
            {
                animator.SetBool(Hash_GotHit, false);
            }
        }

        // ── DEBUG KEYPAD TESTING — REMOVE BEFORE SHIPPING ──
        if (debugKeypadTesting && IsOwner && animator != null)
        {
            // Hit: Numpad 8=front, 2=back, 4=left, 6=right
            if (Input.GetKeyDown(KeyCode.Keypad8))
                DebugTriggerHit(1f, 0f);   // front
            if (Input.GetKeyDown(KeyCode.Keypad2))
                DebugTriggerHit(-1f, 0f);  // back
            if (Input.GetKeyDown(KeyCode.Keypad4))
                DebugTriggerHit(0f, -1f);  // left
            if (Input.GetKeyDown(KeyCode.Keypad6))
                DebugTriggerHit(0f, 1f);   // right

            // Death: Numpad 7=-1, 1=-0.5, 3=+0.5, 9=+1
            if (Input.GetKeyDown(KeyCode.Keypad7))
                DebugTriggerDeath(-1f);
            if (Input.GetKeyDown(KeyCode.Keypad1))
                DebugTriggerDeath(-0.5f);
            if (Input.GetKeyDown(KeyCode.Keypad3))
                DebugTriggerDeath(0.5f);
            if (Input.GetKeyDown(KeyCode.Keypad9))
                DebugTriggerDeath(1f);

            // Reset death: Numpad 5
            if (Input.GetKeyDown(KeyCode.Keypad5))
                ResetDeathState();
        }
    }

    // ── DEBUG HELPERS — REMOVE BEFORE SHIPPING ──

    private void DebugTriggerHit(float fb, float lr)
    {
        if (_isDead) return;
        animator.SetFloat(Hash_HitFB, fb);
        animator.SetFloat(Hash_HitLR, lr);
        animator.SetBool(Hash_GotHit, true);
        _hitResetTimer = hitResetDelay;
        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] DEBUG Hit FB={fb:F1} LR={lr:F1}");
    }

    private void DebugTriggerDeath(float lr)
    {
        if (_isDead) return;
        _isDead = true;
        animator.SetFloat(Hash_DeathLR, lr);
        animator.SetBool(Hash_IsDead, true);
        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);
        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] DEBUG Death LR={lr:F1}");
    }

    // ─── Hit Reaction ───

    private void HandleHitAnimation(Vector3 attackerWorldPos)
    {
        if (_isDead || animator == null) return;

        ComputeDirection(attackerWorldPos, out float fb, out float lr);

        animator.SetFloat(Hash_HitFB, fb);
        animator.SetFloat(Hash_HitLR, lr);
        animator.SetBool(Hash_GotHit, true);

        _hitResetTimer = hitResetDelay;

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] Hit from FB={fb:F2} LR={lr:F2}");
    }

    // ─── Death ───

    private void HandleDeathAnimation(Vector3 attackerWorldPos)
    {
        if (_isDead || animator == null) return;
        _isDead = true;

        float deathLR = ComputeDeathLR(attackerWorldPos);

        animator.SetFloat(Hash_DeathLR, deathLR);
        animator.SetBool(Hash_IsDead, true);

        // Cancel any pending hit reset
        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] Death LR={deathLR:F2}");
    }

    // ─── Respawn (call from RespawnController or equivalent) ───

    /// <summary>
    /// Resets death state so the dragon can animate again after respawn.
    /// </summary>
    public void ResetDeathState()
    {
        _isDead = false;

        if (animator != null)
        {
            animator.SetBool(Hash_IsDead, false);
            animator.SetFloat(Hash_DeathLR, 0f);
            animator.SetBool(Hash_GotHit, false);
            animator.SetFloat(Hash_HitFB, 0f);
            animator.SetFloat(Hash_HitLR, 0f);
        }
    }

    // ─── Direction Computation ───

    /// <summary>
    /// Converts attacker world position into dragon-local front/back and left/right values.
    /// FB:  +1 = hit from front, -1 = hit from back
    /// LR:  +1 = hit from right, -1 = hit from left
    /// Used by hit reaction (2D blend tree).
    /// </summary>
    private void ComputeDirection(Vector3 attackerWorldPos, out float fb, out float lr)
    {
        Vector3 toAttacker = (attackerWorldPos - transform.position);
        toAttacker.y = 0f;

        if (toAttacker.sqrMagnitude < 0.001f)
        {
            fb = 1f;
            lr = 0f;
            return;
        }

        Vector3 localDir = transform.InverseTransformDirection(toAttacker.normalized);

        fb = localDir.z;
        lr = localDir.x;

        // Normalize to dominant axis for cleaner blend tree selection
        float absFB = Mathf.Abs(fb);
        float absLR = Mathf.Abs(lr);

        if (absFB > absLR)
        {
            fb = Mathf.Sign(fb);
            lr = 0f;
        }
        else
        {
            fb = 0f;
            lr = Mathf.Sign(lr);
        }
    }

    /// <summary>
    /// Computes death direction as left/right only (1D blend tree).
    /// Returns raw localDir.x clamped to -1..1 so the blend tree can
    /// interpolate across all clip positions (-1, -0.5, 0.5, 1).
    /// If attacker is dead ahead or behind, picks a random side.
    /// </summary>
    private float ComputeDeathLR(Vector3 attackerWorldPos)
    {
        Vector3 toAttacker = (attackerWorldPos - transform.position);
        toAttacker.y = 0f;

        if (toAttacker.sqrMagnitude < 0.001f)
            return Random.value > 0.5f ? 1f : -1f;

        Vector3 localDir = transform.InverseTransformDirection(toAttacker.normalized);

        // If attacker is almost exactly in front or behind, pick a random side
        if (Mathf.Abs(localDir.x) < 0.1f)
            return Random.value > 0.5f ? 0.5f : -0.5f;

        return Mathf.Clamp(localDir.x, -1f, 1f);
    }
}
