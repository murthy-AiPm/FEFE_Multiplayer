using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Shared respawn controller — works on any player prefab (human, dragon, etc.).
/// Place on the root of the player prefab alongside the NetworkObject.
/// Handles corpse hide timer, respawn teleport, and re-enabling controllers.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class RespawnController : NetworkBehaviour
{
    [Header("Respawn")]
    [SerializeField] private float corpseVisibleTime = 3f;

    private Coroutine _hideCorpseCoroutine;

    // ─── Corpse Timer (called by DamageReceiver on death) ───

    /// <summary>
    /// Call from the server after death to start the corpse-hide countdown.
    /// </summary>
    public void StartCorpseTimer()
    {
        if (!IsServer) return;

        if (_hideCorpseCoroutine != null)
            StopCoroutine(_hideCorpseCoroutine);

        _hideCorpseCoroutine = StartCoroutine(HideCorpseAfterDelay());
    }

    private System.Collections.IEnumerator HideCorpseAfterDelay()
    {
        yield return new WaitForSeconds(corpseVisibleTime);
        HideCorpseClientRpc();
        _hideCorpseCoroutine = null;
    }

    [ClientRpc]
    private void HideCorpseClientRpc()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
    }

    // ─── Respawn ───

    [ServerRpc(RequireOwnership = false)]
    public void RequestRespawnServerRpc(int spawnPointIndex)
    {
        if (_hideCorpseCoroutine != null)
        {
            StopCoroutine(_hideCorpseCoroutine);
            _hideCorpseCoroutine = null;
        }

        var points = SpawnPoint.GetAllSpawnPoints();

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (points != null && spawnPointIndex >= 0 && spawnPointIndex < points.Count)
        {
            spawnPos = points[spawnPointIndex].transform.position;
            spawnRot = points[spawnPointIndex].transform.rotation;
        }
        else
        {
            spawnPos = SpawnPoint.GetRandomSpawnPos();
            spawnRot = Quaternion.identity;
        }

        // Reset vitals if present (human has VitalManager, dragon may not yet)
        var vitalManager = GetComponent<VitalManager>();
        if (vitalManager != null)
            vitalManager.ResetAllVitals();

        NotifyRespawnClientRpc(spawnPos, spawnRot);
    }

    [ClientRpc]
    private void NotifyRespawnClientRpc(Vector3 spawnPos, Quaternion spawnRot)
    {
        // Show renderers
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = true;

        // Force dismount if mounted before teleporting (human only)
        if (IsOwner)
        {
            var mountController = GetComponentInChildren<MountController>();
            if (mountController != null && (mountController.IsMounted || mountController.IsTransitioning))
                mountController.ForceDismount();
        }

        // Cache vcam FOV before any state changes that might reset it
        CinemachineCamera vcam = GetComponentInChildren<CinemachineCamera>(true);
        float cachedFOV = vcam != null ? vcam.Lens.FieldOfView : 0f;

        // --- Teleport ---
        // Disable physics body before moving
        var cc = GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        var rb = GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.position = spawnPos;
        transform.rotation = spawnRot;

        if (cc != null) cc.enabled = IsOwner;
        if (rb != null) rb.isKinematic = false;

        // --- Re-enable controllers (owner only) ---
        if (IsOwner)
        {
            // Human controllers
            var input = GetComponentInChildren<InputController>();
            if (input != null) input.enabled = true;

            var tps = GetComponentInChildren<ThirdPersonController>();
            if (tps != null) tps.enabled = true;

            var combat = GetComponentInChildren<CombatController>();
            if (combat != null)
            {
                combat.enabled = true;
                combat.ResetState();
            }

            // Dragon controllers
            var animalGround = GetComponentInChildren<AnimalGroundController>();
            if (animalGround != null) animalGround.enabled = true;

            // Close death screen
            var deathScreen = FindObjectOfType<DeathScreen>();
            if (deathScreen != null)
                deathScreen.Hide();
        }

        // Play respawn animation (human)
        var animancerDriver = GetComponentInChildren<RuleAnimancerDriver>();
        if (animancerDriver != null)
            animancerDriver.PlayRespawn();

        // Restore FOV in case anything reset it
        if (vcam != null && cachedFOV > 0f)
        {
            var lens = vcam.Lens;
            lens.FieldOfView = cachedFOV;
            vcam.Lens = lens;
        }
    }
}
