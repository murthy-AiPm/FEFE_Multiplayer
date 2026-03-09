using UnityEngine;

/// <summary>
/// Marks a Vector2 field to be drawn as a min/max range slider in the Inspector.
/// x = min value, y = max value.
///
/// Usage:
///   [MinMaxRange(0f, 1f)]
///   public Vector2 volume = new Vector2(0.8f, 1f);
/// </summary>
public class MinMaxRangeAttribute : PropertyAttribute
{
    public float Min;
    public float Max;

    public MinMaxRangeAttribute(float min, float max)
    {
        Min = min;
        Max = max;
    }
}
