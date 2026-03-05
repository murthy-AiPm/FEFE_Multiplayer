using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Temporary debug script to validate bow aim raycast position.
/// Attach to the player root (same object as CombatController).
/// Shows a red sphere in the scene view + a small debug sphere in the game view at the aim point.
/// Remove or disable once crosshair UI is implemented.
/// </summary>
public class BowAimDebug : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CombatController combatController;

    [Header("Raycast Settings")]
    [SerializeField] private float maxRange = 200f;
    [SerializeField] private LayerMask raycastMask = ~0; // everything by default

    [Header("Debug Visuals")]
    [SerializeField] private float sphereRadius = 0.15f;
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private Color missColor = Color.yellow;

    // Runtime
    private Vector3 _aimPoint;
    private bool _hasHit;
    private GameObject _debugSphere; // visible in game view

    private void Awake()
    {
        if (combatController == null)
            combatController = GetComponent<CombatController>();

        // Create a small sphere primitive visible in game view
        _debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _debugSphere.name = "[BowAimDebug] AimPoint";
        _debugSphere.transform.localScale = Vector3.one * sphereRadius * 2f;
        Destroy(_debugSphere.GetComponent<Collider>()); // no physics interference

        var rend = _debugSphere.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            rend.material.color = hitColor;
        }

        _debugSphere.SetActive(false);
    }

    private void LateUpdate()
    {
        // Only run for the local owner
        var netBehaviour = GetComponent<NetworkBehaviour>();
        if (netBehaviour != null && !netBehaviour.IsOwner) 
        {
            _debugSphere.SetActive(false);
            return;
        }

        bool isAiming = combatController != null &&
            (combatController.IsBowAiming || combatController.IsBowDrawing);

        if (!isAiming)
        {
            _debugSphere.SetActive(false);
            return;
        }

        _debugSphere.SetActive(true);

        // Raycast from camera forward — same direction arrow fires
        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, raycastMask))
        {
            _aimPoint = hit.point;
            _hasHit = true;
        }
        else
        {
            // Nothing hit — project to max range
            _aimPoint = ray.origin + ray.direction * maxRange;
            _hasHit = false;
        }

        // Move debug sphere to aim point
        _debugSphere.transform.position = _aimPoint;

        // Color: red = hit something, yellow = missed (open air)
        var rend = _debugSphere.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = _hasHit ? hitColor : missColor;

        // Also log screen position so you know where to place the UI crosshair
        Vector3 screenPos = cam.WorldToScreenPoint(_aimPoint);
        Debug.Log($"[BowAimDebug] AimPoint: {_aimPoint} | ScreenPos: {screenPos} | Hit: {(_hasHit ? hit.collider.name : "none")}");
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = _hasHit ? hitColor : missColor;
        Gizmos.DrawWireSphere(_aimPoint, sphereRadius);

        // Draw the ray line from camera
        if (Camera.main != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Camera.main.transform.position, _aimPoint);
        }
    }

    private void OnDestroy()
    {
        if (_debugSphere != null)
            Destroy(_debugSphere);
    }
}
