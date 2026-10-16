using System;

namespace SizeChange;

// Track the latest unmodified game request separately from our applied value.
// Framework refreshes must never multiply a previously modified speed again.
internal struct EmoteSlotSpeed
{
    private float baseline, applied;
    private bool owned, blocked;
    internal bool Apply(ref float current, float multiplier)
    {
        if (blocked || !float.IsFinite(current) || current <= 0f) return false;
        if (owned && current != applied) { owned = false; blocked = true; return false; }
        if (!owned) baseline = current;
        float target = baseline * multiplier;
        if (!float.IsFinite(target) || target <= 0f) return false;
        owned = true; applied = target;
        bool changed = current != target;
        current = target;
        return changed;
    }
    internal float FromGame(float requested, float multiplier)
    {
        float target = requested * multiplier;
        if (!float.IsFinite(requested) || requested < 0f || !float.IsFinite(target) || target < 0f)
            return requested;
        baseline = requested; applied = target; owned = true; blocked = false;
        return target;
    }
    internal bool Release(ref float current)
    {
        bool restore = owned && current == applied && current != baseline;
        if (restore) current = baseline;
        this = default;
        return restore;
    }
}
