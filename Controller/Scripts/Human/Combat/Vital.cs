using System;
using UnityEngine;

/// <summary>
/// Runtime instance of a single vital. Managed by VitalManager.
/// NOT a MonoBehaviour — it's a plain C# object owned by VitalManager.
/// </summary>
[Serializable]
public class Vital
{
    public VitalDefinition Definition { get; private set; }

    // Current state
    public float Current { get; private set; }
    public float Max { get; private set; }
    public float Normalized => Max > 0 ? Current / Max : 0f;
    public bool IsDepleted => Current <= 0f;

    // Regen tracking
    private float _regenCooldownTimer;
    private bool _regenPaused;

    // Events (fired by VitalManager on server, replicated via NetworkVariables)
    public event Action<float, float> OnValueChanged;    // (newValue, oldValue)
    public event Action OnDepleted;
    public event Action OnMaxChanged;

    public Vital(VitalDefinition definition)
    {
        Definition = definition;
        Max = definition.maxValue;
        Current = definition.GetStartValue();
        _regenCooldownTimer = 0f;
    }

    /// <summary>
    /// Apply damage or consumption. Positive amount = reduce vital.
    /// Returns actual amount consumed.
    /// </summary>
    public float Consume(float amount)
    {
        if (amount <= 0) return 0f;

        float old = Current;
        Current -= amount;

        if (Definition.clampAtZero && Current < 0f)
            Current = 0f;

        // Reset regen cooldown
        _regenCooldownTimer = Definition.regenDelay;

        if (Current != old)
            OnValueChanged?.Invoke(Current, old);

        if (Current <= 0f && old > 0f)
            OnDepleted?.Invoke();

        return old - Current; // actual consumed
    }

    /// <summary>
    /// Restore vital. Positive amount = increase vital.
    /// Returns actual amount restored.
    /// </summary>
    public float Restore(float amount)
    {
        if (amount <= 0) return 0f;

        float old = Current;
        Current = Mathf.Min(Current + amount, Max);

        if (Current != old)
            OnValueChanged?.Invoke(Current, old);

        return Current - old;
    }

    /// <summary>
    /// Force-set the current value (used for network sync).
    /// </summary>
    public void SetCurrent(float value)
    {
        float old = Current;
        Current = Mathf.Clamp(value, 0f, Max);

        if (Current != old)
            OnValueChanged?.Invoke(Current, old);

        if (Current <= 0f && old > 0f)
            OnDepleted?.Invoke();
    }

    /// <summary>
    /// Set max value (e.g. from buffs/equipment).
    /// </summary>
    public void SetMax(float newMax)
    {
        Max = Mathf.Max(0f, newMax);
        if (Current > Max)
            Current = Max;
        OnMaxChanged?.Invoke();
    }

    /// <summary>
    /// Pause/resume regeneration externally (e.g. sprinting pauses stamina regen).
    /// </summary>
    public void SetRegenPaused(bool paused)
    {
        _regenPaused = paused;
        if (paused)
            _regenCooldownTimer = Definition.regenDelay;
    }

    /// <summary>
    /// Tick regen. Call from VitalManager.Update() on the server.
    /// </summary>
    public void TickRegen(float deltaTime, bool isGrounded)
    {
        if (!Definition.regenEnabled || _regenPaused) return;
        if (Definition.regenOnlyWhenGrounded && !isGrounded) return;
        if (Current >= Max) return;

        if (_regenCooldownTimer > 0f)
        {
            _regenCooldownTimer -= deltaTime;
            return;
        }

        Restore(Definition.regenRate * deltaTime);
    }
}
