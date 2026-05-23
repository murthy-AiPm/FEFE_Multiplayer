using UnityEngine;

/// <summary>
/// Humanoid-specific water mode helper built on AnimalSwimSystem's water
/// trigger, surface raycast, and hysteresis.
/// </summary>
public class HumanoidSwimSystem : AnimalSwimSystem
{
    [Header("Humanoid Swim Mode")]
    [Tooltip("Surface mode is used while submersion is at or above this depth.")]
    [SerializeField] private float surfaceModeMaxDepth = 1.2f;

    public bool IsSwimming => IsInWater;
    public bool IsUnderwater => IsInWater && SubmersionDepth > surfaceModeMaxDepth;
    public bool IsSurfaceSwimming => IsInWater && !IsUnderwater;
}
