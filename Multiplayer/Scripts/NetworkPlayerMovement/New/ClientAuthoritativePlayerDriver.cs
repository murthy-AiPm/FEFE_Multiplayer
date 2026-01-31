using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Client-authoritative player driver:
/// - Owner runs local input + character controller scripts.
/// - Non-owners are "puppets": disable CharacterController + input/movement scripts,
///   and let ClientNetworkTransform + ClientNetworkAnimator drive them.
/// </summary>
[DisallowMultipleComponent]
public class ClientAuthoritativePlayerDriver : NetworkBehaviour
{
    [Header("Root references (optional, auto-found if null)")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private InputController inputController;

    [Header("Movement scripts (optional, auto-found if empty)")]
    [Tooltip("Put HumanoidController / DragonController / ThirdPersonController etc here if you want explicit control.")]
    [SerializeField] private MonoBehaviour[] movementScriptsToToggle;

    [Header("Physics / movement components to toggle")]
    [SerializeField] private CharacterController characterController;

    [Header("Camera (optional)")]
    [Tooltip("If your Cinemachine rig is on the player, disable it for non-owners.")]
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private void Reset()
    {
        AutoWire();
    }

    private void Awake()
    {
        AutoWire();
    }

    private void AutoWire()
    {
        if (!playerController) playerController = GetComponentInChildren<PlayerController>(true);
        if (!inputController) inputController = GetComponentInChildren<InputController>(true);
        if (!characterController) characterController = GetComponentInChildren<CharacterController>(true);

        // If user didn't assign movement scripts explicitly, try to grab common ones.
        if (movementScriptsToToggle == null || movementScriptsToToggle.Length == 0)
        {
            // You can add more types here if you have them (DragonController, etc.)
            var humanoid = GetComponentInChildren<HumanoidController>(true);
            var tps = GetComponentInChildren<ThirdPersonController>(true);

            if (humanoid != null && tps != null && humanoid != (MonoBehaviour)tps)
                movementScriptsToToggle = new MonoBehaviour[] { humanoid, tps };
            else if (humanoid != null)
                movementScriptsToToggle = new MonoBehaviour[] { humanoid };
            else if (tps != null)
                movementScriptsToToggle = new MonoBehaviour[] { tps };
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Owner drives itself. Everyone else becomes a puppet.
        ApplyOwnershipMode(IsOwner);

        // If ownership can change in your game, you'd also handle OnGainedOwnership/OnLostOwnership.
    }

    private void ApplyOwnershipMode(bool isOwner)
    {
        // Input only on owner
        if (inputController) inputController.enabled = isOwner; // uses InputSnapshot etc :contentReference[oaicite:2]{index=2}

        // Your PlayerController is mostly a hub, keep it enabled on owner only (optional)
        if (playerController) playerController.enabled = isOwner; // :contentReference[oaicite:3]{index=3}

        // Movement scripts only on owner (this prevents non-owner CharacterController fighting net updates)
        if (movementScriptsToToggle != null)
        {
            foreach (var s in movementScriptsToToggle)
            {
                if (!s) continue;
                s.enabled = isOwner; // ThirdPersonController/HumanoidController etc :contentReference[oaicite:4]{index=4} :contentReference[oaicite:5]{index=5}
            }
        }

        // CharacterController ON only for owner
        if (characterController) characterController.enabled = isOwner;

        // Cameras / Cinemachine rigs / audio listeners etc should be owner-only
        if (ownerOnlyObjects != null)
        {
            foreach (var go in ownerOnlyObjects)
            {
                if (!go) continue;
                go.SetActive(isOwner);
            }
        }
    }
}
