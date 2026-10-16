using System;

namespace SizeChange;

// Own only the value we wrote. A competing speed writer wins until this slot
// changes animation or the emote/configuration changes.
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
    internal bool Release(ref float current)
    {
        bool restore = owned && current == applied && current != baseline;
        if (restore) current = baseline;
        this = default;
        return restore;
    }
}
