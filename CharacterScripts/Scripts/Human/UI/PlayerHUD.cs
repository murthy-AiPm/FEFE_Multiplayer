using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player HUD displaying health and stamina bars.
/// Attach to a Canvas (Screen Space - Overlay) child of the player prefab.
/// Only visible on the local owner.
/// 
/// Setup:
///   1. Create Canvas on player prefab (Screen Space - Overlay)
///   2. Add this script to the Canvas
///   3. Leave references null — script auto-creates the UI
///   4. Or assign your own bars in the inspector for custom styling
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private NetworkObject networkObject;

    [Header("UI References (auto-created if empty)")]
    [SerializeField] private Image healthFill;
    [SerializeField] private Image healthBackground;
    [SerializeField] private Image staminaFill;
    [SerializeField] private Image staminaBackground;

    [Header("Layout")]
    [SerializeField] private float barWidth = 300f;
    [SerializeField] private float barHeight = 20f;
    [SerializeField] private float staminaBarHeight = 12f;
    [SerializeField] private float padding = 20f;
    [SerializeField] private float spacing = 6f;
    [SerializeField] private AnchorPosition anchorPosition = AnchorPosition.BottomCenter;

    [Header("Colors")]
    [SerializeField] private Color healthColor = new Color(0.8f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color healthBgColor = new Color(0.2f, 0.05f, 0.05f, 0.8f);
    [SerializeField] private Color staminaColor = new Color(0.2f, 0.7f, 0.3f, 1f);
    [SerializeField] private Color staminaBgColor = new Color(0.05f, 0.2f, 0.08f, 0.8f);
    [SerializeField] private Color borderColor = new Color(0f, 0f, 0f, 0.9f);

    [Header("Smoothing")]
    [SerializeField] private float fillSpeed = 5f;

    public enum AnchorPosition
    {
        BottomCenter,
        BottomLeft,
        TopLeft
    }

    private Canvas _canvas;
    private float _displayedHealth = 1f;
    private float _displayedStamina = 1f;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponentInParent<VitalManager>();
        if (networkObject == null) networkObject = GetComponentInParent<NetworkObject>();
        _canvas = GetComponent<Canvas>();
    }

    private void Start()
    {
        // Only show HUD for local player
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
        {
            gameObject.SetActive(false);
            return;
        }

        // Auto-create UI if not assigned
        if (healthFill == null || staminaFill == null)
        {
            CreateUI();
        }
    }

    private void Update()
    {
        if (vitalManager == null) return;

        // Get current vitals
        var health = vitalManager.GetVital("health");
        var stamina = vitalManager.GetVital("stamina");

        float healthPct = health != null ? health.Current / health.Max : 1f;
        float staminaPct = stamina != null ? stamina.Current / stamina.Max : 1f;

        // Smooth fill
        _displayedHealth = Mathf.Lerp(_displayedHealth, healthPct, fillSpeed * Time.deltaTime);
        _displayedStamina = Mathf.Lerp(_displayedStamina, staminaPct, fillSpeed * Time.deltaTime);

        // Snap when very close
        if (Mathf.Abs(_displayedHealth - healthPct) < 0.001f) _displayedHealth = healthPct;
        if (Mathf.Abs(_displayedStamina - staminaPct) < 0.001f) _displayedStamina = staminaPct;

        // Update fill
        if (healthFill != null) healthFill.fillAmount = _displayedHealth;
        if (staminaFill != null) staminaFill.fillAmount = _displayedStamina;
    }

    private void CreateUI()
    {
        // Ensure we have a Canvas
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            gameObject.AddComponent<GraphicRaycaster>();
        }

        // Container
        GameObject container = new GameObject("VitalBars");
        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.SetParent(transform, false);

        // Position based on anchor setting
        switch (anchorPosition)
        {
            case AnchorPosition.BottomCenter:
                containerRect.anchorMin = new Vector2(0.5f, 0f);
                containerRect.anchorMax = new Vector2(0.5f, 0f);
                containerRect.pivot = new Vector2(0.5f, 0f);
                containerRect.anchoredPosition = new Vector2(0f, padding);
                break;
            case AnchorPosition.BottomLeft:
                containerRect.anchorMin = new Vector2(0f, 0f);
                containerRect.anchorMax = new Vector2(0f, 0f);
                containerRect.pivot = new Vector2(0f, 0f);
                containerRect.anchoredPosition = new Vector2(padding, padding);
                break;
            case AnchorPosition.TopLeft:
                containerRect.anchorMin = new Vector2(0f, 1f);
                containerRect.anchorMax = new Vector2(0f, 1f);
                containerRect.pivot = new Vector2(0f, 1f);
                containerRect.anchoredPosition = new Vector2(padding, -padding);
                break;
        }

        float totalHeight = barHeight + spacing + staminaBarHeight;
        containerRect.sizeDelta = new Vector2(barWidth, totalHeight);

        // Health bar (top)
        float healthY = staminaBarHeight + spacing;
        CreateBar(containerRect, "HealthBar", barWidth, barHeight, healthY,
            healthColor, healthBgColor, out healthFill, out healthBackground);

        // Stamina bar (bottom, slightly smaller)
        CreateBar(containerRect, "StaminaBar", barWidth, staminaBarHeight, 0f,
            staminaColor, staminaBgColor, out staminaFill, out staminaBackground);
    }

    private void CreateBar(RectTransform parent, string name, float width, float height, float yOffset,
        Color fillColor, Color bgColor, out Image fill, out Image bg)
    {
        // Border
        GameObject borderObj = new GameObject(name + "_Border");
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.SetParent(parent, false);
        borderRect.anchorMin = new Vector2(0f, 0f);
        borderRect.anchorMax = new Vector2(0f, 0f);
        borderRect.pivot = new Vector2(0f, 0f);
        borderRect.anchoredPosition = new Vector2(-1f, yOffset - 1f);
        borderRect.sizeDelta = new Vector2(width + 2f, height + 2f);
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.color = borderColor;


        // Background
        GameObject bgObj = new GameObject(name + "_BG");
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.SetParent(parent, false);
        bgRect.anchorMin = new Vector2(0f, 0f);
        bgRect.anchorMax = new Vector2(0f, 0f);
        bgRect.pivot = new Vector2(0f, 0f);
        bgRect.anchoredPosition = new Vector2(0f, yOffset);
        bgRect.sizeDelta = new Vector2(width, height);
        bg = bgObj.AddComponent<Image>();
        bg.color = bgColor;

        // Fill
        GameObject fillObj = new GameObject(name + "_Fill");
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.SetParent(parent, false);
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 0f);
        fillRect.pivot = new Vector2(0f, 0f);
        fillRect.anchoredPosition = new Vector2(0f, yOffset);
        fillRect.sizeDelta = new Vector2(width, height);
        fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
    }

    //private static Sprite _whiteSprite;
    //private static Sprite WhiteSprite
    //{
    //    get
    //    {
    //        if (_whiteSprite != null) return _whiteSprite;
    //        var tex = Texture2D.whiteTexture;
    //        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    //        return _whiteSprite;
    //    }
    //}
}
