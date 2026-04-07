using UnityEngine;

/// <summary>
/// Controls random idle variants via a Direct Blend Tree.
/// Smoothly lerps weights between variants to avoid snappy transitions.
///
/// Animator setup:
///   - Create a Direct Blend Tree inside your Idle state
///   - Add 5 child motions: base idle + 4 variants
///   - Create 5 float parameters: Idle0, Idle1, Idle2, Idle3, Idle4
///   - Map each child motion to its corresponding parameter
///   - Set Idle0 = 1, rest = 0 as defaults
///   - Variant clips should NOT be looping
/// </summary>
public class RandomIdleAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Settings")]
    [Tooltip("Number of idle variants (not counting base idle).")]
    [SerializeField] private int variantCount = 4;

    [Tooltip("Min time on Idle0 before picking a variant")]
    [SerializeField] private float minInterval = 8f;

    [Tooltip("Max time on Idle0 before picking a variant")]
    [SerializeField] private float maxInterval = 20f;

    [Tooltip("Duration of each variant clip in seconds. Set to match your actual clip lengths.")]
    [SerializeField] private float[] variantDurations = new float[] { 2f, 2f, 2f, 2f };

    [Tooltip("How fast to blend between idle variants (higher = faster crossfade)")]
    [SerializeField] private float blendSpeed = 3f;

    [Tooltip("Parameter name prefix. Parameters should be Idle0, Idle1, Idle2, etc.")]
    [SerializeField] private string paramPrefix = "Idle";

    // ─── References ───
    private MountableEntity _mountable;

    // ─── State ───
    private int[] _paramHashes;
    private bool[] _paramExists;
    private float[] _currentWeights;
    private float[] _targetWeights;
    private int _activeIndex = 0;
    private float _timer;
    private bool _playingVariant;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _mountable = GetComponent<MountableEntity>();

        int total = variantCount + 1;
        _paramHashes = new int[total];
        _paramExists = new bool[total];
        _currentWeights = new float[total];
        _targetWeights = new float[total];

        for (int i = 0; i < total; i++)
        {
            _paramHashes[i] = Animator.StringToHash(paramPrefix + i);
            _paramExists[i] = HasParameter(_paramHashes[i]);
        }

        SetTargetIdle(0);
        // Initialize weights immediately (no lerp on start)
        for (int i = 0; i < total; i++)
            _currentWeights[i] = _targetWeights[i];

        ApplyWeights();
        ResetTimer();
    }

    private void Update()
    {
        if (animator == null) return;

        bool isMoving = _mountable != null && _mountable.IsMoving;

        if (isMoving)
        {
            if (_activeIndex != 0)
                SetTargetIdle(0);

            _playingVariant = false;
            ResetTimer();
        }
        else if (_playingVariant)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                SetTargetIdle(0);
                _playingVariant = false;
                ResetTimer();
            }
        }
        else
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                int next = Random.Range(1, variantCount + 1);
                float duration = (next - 1 < variantDurations.Length) ? variantDurations[next - 1] : 2f;
                SetTargetIdle(next);
                _playingVariant = true;
                _timer = duration;
            }
        }

        // Lerp current weights toward target weights, keeping sum = 1 at all times
        bool changed = false;
        for (int i = 0; i < _currentWeights.Length; i++)
        {
            if (i == _activeIndex) continue; // handle active last
            float newWeight = Mathf.MoveTowards(_currentWeights[i], _targetWeights[i], blendSpeed * Time.deltaTime);
            if (newWeight != _currentWeights[i])
            {
                _currentWeights[i] = newWeight;
                changed = true;
            }
        }

        // Active index always gets whatever is left so total stays at 1
        float otherSum = 0f;
        for (int i = 0; i < _currentWeights.Length; i++)
            if (i != _activeIndex) otherSum += _currentWeights[i];

        float activeWeight = Mathf.Clamp01(1f - otherSum);
        if (activeWeight != _currentWeights[_activeIndex])
        {
            _currentWeights[_activeIndex] = activeWeight;
            changed = true;
        }

        if (changed)
            ApplyWeights();
    }

    private void SetTargetIdle(int index)
    {
        _activeIndex = index;
        for (int i = 0; i < _targetWeights.Length; i++)
            _targetWeights[i] = i == index ? 1f : 0f;
    }

    private void ApplyWeights()
    {
        for (int i = 0; i < _paramHashes.Length; i++)
        {
            if (_paramExists[i])
                animator.SetFloat(_paramHashes[i], _currentWeights[i]);
        }
    }

    private bool HasParameter(int hash)
    {
        foreach (var p in animator.parameters)
        {
            if (p.nameHash == hash)
                return true;
        }
        return false;
    }

    private void ResetTimer()
    {
        _timer = Random.Range(minInterval, maxInterval);
    }
}
