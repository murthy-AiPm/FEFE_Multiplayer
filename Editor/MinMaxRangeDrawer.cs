using UnityEngine;
using UnityEditor;

// ─────────────────────────────────────────────────────────
// MinMaxRangeDrawer.cs — Editor drawer for MinMaxRangeAttribute
// Attribute itself lives in Sound/MinMaxRangeAttribute.cs (runtime)
// ─────────────────────────────────────────────────────────

[CustomPropertyDrawer(typeof(MinMaxRangeAttribute))]
public class MinMaxRangeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Vector2)
        {
            EditorGUI.LabelField(position, label.text, "Use MinMaxRange with Vector2.");
            return;
        }

        var attr = (MinMaxRangeAttribute)attribute;
        Vector2 range = property.vector2Value;
        float minVal = range.x;
        float maxVal = range.y;

        float fieldWidth = 40f;
        float padding = 4f;

        // Label
        Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
        EditorGUI.LabelField(labelRect, label);

        // Min field
        Rect minFieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, fieldWidth, position.height);
        minVal = EditorGUI.FloatField(minFieldRect, minVal);

        // Slider
        float sliderStart = minFieldRect.xMax + padding;
        float sliderEnd = position.xMax - fieldWidth - padding;
        Rect sliderRect = new Rect(sliderStart, position.y, sliderEnd - sliderStart, position.height);
        EditorGUI.MinMaxSlider(sliderRect, ref minVal, ref maxVal, attr.Min, attr.Max);

        // Max field
        Rect maxFieldRect = new Rect(sliderEnd + padding, position.y, fieldWidth, position.height);
        maxVal = EditorGUI.FloatField(maxFieldRect, maxVal);

        // Clamp
        minVal = Mathf.Clamp(minVal, attr.Min, maxVal);
        maxVal = Mathf.Clamp(maxVal, minVal, attr.Max);

        property.vector2Value = new Vector2(minVal, maxVal);
    }
}
