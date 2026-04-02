using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-only static coordinator. Assigns evenly-spaced orbit slots around targets
/// so multiple enemies surround rather than overlap.
///
/// Usage:
///   Register   — call when an enemy picks a target (returns assigned slot index, or -1 if full)
///   Unregister — call when an enemy dies or disengages
///   GetSlotPosition — call every frame in UpdateChase to get the world-space destination
///   FindLeastContestedTarget — call on first target selection to distribute enemies across clients
/// </summary>
public static class EnemySurroundCoordinator
{
    private class SlotEntry
    {
        public Transform enemy;
        public int       slotIndex;
    }

    // target → list of assigned slots
    private static readonly Dictionary<Transform, List<SlotEntry>> _registry =
        new Dictionary<Transform, List<SlotEntry>>();

    // ─── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Register an enemy against a target. Returns true if a slot was assigned.
    /// Call this once when Chase begins; it will not re-assign if already registered.
    /// </summary>
    public static bool Register(Transform target, Transform enemy, int maxSlots)
    {
        if (target == null || enemy == null) return false;

        if (!_registry.TryGetValue(target, out var slots))
        {
            slots = new List<SlotEntry>();
            _registry[target] = slots;
        }

        // Already registered for this target
        if (slots.Exists(e => e.enemy == enemy)) return true;

        // Full
        if (slots.Count >= maxSlots) return false;

        // Find nearest free slot index
        int assigned = FindFreeSlotIndex(slots, maxSlots, enemy.position, target, maxSlots);
        slots.Add(new SlotEntry { enemy = enemy, slotIndex = assigned });
        return true;
    }

    /// <summary>
    /// Release an enemy's slot. Call on death, disengage, or target switch.
    /// </summary>
    public static void Unregister(Transform target, Transform enemy)
    {
        if (target == null || enemy == null) return;
        if (!_registry.TryGetValue(target, out var slots)) return;

        slots.RemoveAll(e => e.enemy == enemy);

        if (slots.Count == 0)
            _registry.Remove(target);
    }

    /// <summary>
    /// Unregister an enemy from ALL targets (e.g. on death when target is unknown).
    /// </summary>
    public static void UnregisterAll(Transform enemy)
    {
        if (enemy == null) return;

        var toClean = new List<Transform>();
        foreach (var kvp in _registry)
        {
            kvp.Value.RemoveAll(e => e.enemy == enemy);
            if (kvp.Value.Count == 0)
                toClean.Add(kvp.Key);
        }
        foreach (var t in toClean)
            _registry.Remove(t);
    }

    /// <summary>
    /// Returns the world-space position this enemy should path toward.
    /// Returns target.position as fallback if not registered.
    /// </summary>
    public static Vector3 GetSlotPosition(Transform target, Transform enemy, float orbitRadius, int maxSlots)
    {
        if (target == null) return Vector3.zero;

        if (_registry.TryGetValue(target, out var slots))
        {
            var entry = slots.Find(e => e.enemy == enemy);
            if (entry != null)
            {
                float angle = entry.slotIndex * (360f / maxSlots) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * orbitRadius;
                return target.position + offset;
            }
        }

        // Not registered — fall back to target position directly
        return target.position;
    }

    /// <summary>
    /// How many enemies are currently registered against this target.
    /// </summary>
    public static int GetAttackerCount(Transform target)
    {
        if (target == null) return 0;
        return _registry.TryGetValue(target, out var slots) ? slots.Count : 0;
    }

    /// <summary>
    /// From a list of candidate targets, returns the one with the fewest registered
    /// attackers that still has a free slot. Returns null if all are full.
    /// Pass in the full candidate list (e.g. all living detected players).
    /// </summary>
    public static Transform FindLeastContestedTarget(List<Transform> candidates, int maxSlots)
    {
        Transform best      = null;
        int       bestCount = int.MaxValue;

        foreach (var t in candidates)
        {
            if (t == null) continue;
            int count = GetAttackerCount(t);
            if (count < maxSlots && count < bestCount)
            {
                bestCount = count;
                best      = t;
            }
        }

        return best;
    }

    // ─── Internal ─────────────────────────────────────────────────

    private static int FindFreeSlotIndex(
        List<SlotEntry> occupied, int maxSlots,
        Vector3 enemyPos, Transform target, int totalSlots)
    {
        // Collect taken indices
        var taken = new HashSet<int>();
        foreach (var e in occupied) taken.Add(e.slotIndex);

        // Pick the free slot whose world position is closest to the enemy
        int   best     = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < maxSlots; i++)
        {
            if (taken.Contains(i)) continue;

            float angle  = i * (360f / totalSlots) * Mathf.Deg2Rad;
            Vector3 pos  = target.position +
                           new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));   // radius=1, scaled later
            float dist   = Vector3.SqrMagnitude(enemyPos - pos);

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = i;
            }
        }

        return best >= 0 ? best : 0; // fallback to 0 if something went wrong
    }
}
