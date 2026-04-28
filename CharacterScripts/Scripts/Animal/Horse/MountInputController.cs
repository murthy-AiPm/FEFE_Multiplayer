using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Legacy stub kept on existing horse prefabs for backwards compatibility.
/// Input gating now lives in HorseGroundController.IsInputSuppressed(),
/// which consults MountableEntity directly — there is no per-frame flag
/// to toggle here anymore. The controller stays enabled at all times so
/// in-air physics (jump, fake gravity) and decay-to-idle keep ticking
/// after a dismount.
/// </summary>
public class MountInputController : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("Should horse have AI movement when not mounted? (Not implemented yet)")]
    [SerializeField] private bool useAIWhenUnmounted = false;
}