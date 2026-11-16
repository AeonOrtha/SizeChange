using System;

namespace SizeChange;

// Apply capped and bypass contributions separately. Existing bypass size is
// retained, but never becomes headroom for a different, capped contribution.
internal static class GrowthLimitPolicy
{
    internal static float Apply(float current, float requested, float bypass, bool limited, float limit)
    {
        if (!float.IsFinite(current) || current < 1) current = 1;
        if (!float.IsFinite(requested) || requested <= 0) return current;
        double allowed = float.IsFinite(bypass) ? Math.Clamp((double)bypass, 0, requested) : 0;
        double ordinary = requested - allowed;
        if (limited)
        {
            double ceiling = float.IsFinite(limit) ? Math.Max(1, limit) : 1;
            ordinary = Math.Min(ordinary, Math.Max(0, ceiling - current));
        }
        return (float)Math.Min(float.MaxValue, current + ordinary + allowed);
    }
}

// Capture permissions when a hit enters the accumulator. Later option changes
// do not reclassify queued hits; ending preview retains only proximity entries.
internal struct PendingGrowthPermissions
{
    private float bypass, proximityBypass;
    internal void Queue(float amount, bool proximity, bool ignoreLimit)
    {
        if (!ignoreLimit || !float.IsFinite(amount) || amount <= 0) return;
        bypass = (float)Math.Min(float.MaxValue, (double)bypass + amount);
        if (proximity) proximityBypass = (float)Math.Min(float.MaxValue, (double)proximityBypass + amount);
    }
    internal void KeepProximity() => bypass = proximityBypass;
    internal float Take()
    {
        float value = bypass;
        this = default;
        return value;
    }
}
