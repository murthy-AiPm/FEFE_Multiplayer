using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Randomizes a target material's color on spawn.
/// Assign the Renderer whose material you want colored in the Inspector.
/// </summary>
public class MaterialColorRandomizer : NetworkBehaviour
{
    [Header("Target")]
    [Tooltip("The Renderer containing the material to colorize. Assign in Inspector.")]
    public Renderer targetRenderer;

    [Tooltip("Material index on the Renderer (0 = first material).")]
    public int materialIndex = 0;

    [Header("Color Options")]
    [Tooltip("If true, picks from the curated palette below. If false, fully random HSV.")]
    public bool usePalette = false;

    [Tooltip("Optional curated color list. Only used when usePalette is true.")]
    public Color[] palette = new Color[]
    {
        Color.red, Color.blue, Color.green, Color.yellow,
        Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), Color.white
    };

    [Header("Random HSV Range (used when usePalette is false)")]
    [Range(0f, 1f)] public float saturationMin = 0.6f;
    [Range(0f, 1f)] public float saturationMax = 1f;
    [Range(0f, 1f)] public float valueMin = 0.7f;
    [Range(0f, 1f)] public float valueMax = 1f;

    // Synced color so late-joining clients see the correct color
    private NetworkVariable<Color> _syncedColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _syncedColor.Value = PickColor();
        }

        _syncedColor.OnValueChanged += OnColorChanged;
        ApplyColor(_syncedColor.Value);
    }

    public override void OnNetworkDespawn()
    {
        _syncedColor.OnValueChanged -= OnColorChanged;
    }

    private void OnColorChanged(Color previous, Color current)
    {
        ApplyColor(current);
    }

    private void ApplyColor(Color color)
    {
        if (targetRenderer == null)
        {
            Debug.LogWarning($"[MaterialColorRandomizer] No targetRenderer assigned on {gameObject.name}.");
            return;
        }

        // Use a material property block to avoid creating new Material instances
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(block, materialIndex);
        block.SetColor("_BaseColor", color);   // URP / HDRP
        block.SetColor("_Color", color);        // Built-in RP fallback
        targetRenderer.SetPropertyBlock(block, materialIndex);
    }

    private Color PickColor()
    {
        if (usePalette && palette != null && palette.Length > 0)
        {
            return palette[Random.Range(0, palette.Length)];
        }

        float h = Random.value;
        float s = Random.Range(saturationMin, saturationMax);
        float v = Random.Range(valueMin, valueMax);
        return Color.HSVToRGB(h, s, v);
    }
}
