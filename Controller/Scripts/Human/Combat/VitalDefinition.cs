using UnityEngine;

/// <summary>
/// Definition for any player vital: health, stamina, hunger, thirst, etc.
/// Create one asset per vital type and assign to VitalManager.
/// </summary>
[CreateAssetMenu(fileName = "NewVital", menuName = "Game/Combat/Vital Definition")]
public class VitalDefinition : ScriptableObject
{
    [Header("Identity")]
    public string vitalName = "Health";
    public string vitalID = "health"; // unique key for lookup

    [Header("Values")]
    public float maxValue = 100f;
    public float startValue = 100f; // value on spawn (defaults to max if <= 0)

    [Header("Regeneration")]
    public bool regenEnabled = true;
    public float regenRate = 1f;           // per second
    public float regenDelay = 3f;          // seconds after last consumption/damage before regen starts
    public bool regenOnlyWhenGrounded;     // e.g. stamina only regens on ground

    [Header("Depletion Behavior")]
    public bool clampAtZero = true;        // prevent going negative
    public bool killOnDepleted = false;    // trigger death when this vital hits zero (health)

    public float GetStartValue() => startValue > 0 ? startValue : maxValue;
}
