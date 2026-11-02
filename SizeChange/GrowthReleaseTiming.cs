using System;

namespace SizeChange;

internal static class GrowthReleaseTiming
{
    internal static (float Minimum, float Maximum) Bounds(float delay, float variation)
    {
        delay = float.IsFinite(delay) ? Math.Clamp(delay, 0, 60) : 0;
        variation = float.IsFinite(variation) ? Math.Clamp(variation, 0, 60) : 0;
        if (delay <= 0 || variation <= 0) return (delay, delay);
        // Uniform within the valid range, rather than clamping random negatives
        // into a pile-up of instant releases. Zero nominal delay stays immediate.
        return (Math.Max(.05f, delay - variation), Math.Max(.05f, Math.Min(60, delay + variation)));
    }

    internal static float ForQueue(float remaining, float delay, float variation,
        bool immediate, Func<double> random)
    {
        if (immediate) return 0;
        var range = Bounds(delay, variation);
        if (range.Maximum <= 0) return 0;
        // Existing windows retain their deadline, regardless of more hits.
        if (remaining > 0) return remaining;
        if (range.Minimum == range.Maximum) return range.Minimum;
        return float.Lerp(range.Minimum, range.Maximum, (float)Math.Clamp(random(), 0, 1));
    }
}
