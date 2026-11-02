using System;

namespace SizeChange;

internal static class GrowthReleaseTiming
{
    internal static (float Minimum, float Maximum) Bounds(float delay, float variation)
        => Bounds(delay, variation, variation);

    internal static (float Minimum, float Maximum) ForSettings(GrowthSettings settings) =>
        Bounds(settings.AccumulatorDelaySeconds, settings.EarlierVariation, settings.LaterVariation);

    internal static (float Minimum, float Maximum) Bounds(float delay, float earlier, float later)
    {
        delay = float.IsFinite(delay) ? Math.Clamp(delay, 0, 60) : 0;
        earlier = float.IsFinite(earlier) ? Math.Clamp(earlier, 0, 60) : 0;
        later = float.IsFinite(later) ? Math.Clamp(later, 0, 60) : 0;
        if (delay <= 0 || (earlier <= 0 && later <= 0)) return (delay, delay);
        // Uniform within the valid range, rather than clamping random negatives
        // into a pile-up of instant releases. Zero nominal delay stays immediate.
        return (Math.Max(.05f, delay - earlier), Math.Max(.05f, Math.Min(60, delay + later)));
    }

    internal static float ForQueue(float remaining, float delay, float variation,
        bool immediate, Func<double> random)
        => ForQueue(remaining, delay, variation, variation, immediate, random);

    internal static float ForQueue(float remaining, float delay, float earlier, float later,
        bool immediate, Func<double> random)
    {
        if (immediate) return 0;
        var range = Bounds(delay, earlier, later);
        if (range.Maximum <= 0) return 0;
        // Existing windows retain their deadline, regardless of more hits.
        if (remaining > 0) return remaining;
        if (range.Minimum == range.Maximum) return range.Minimum;
        return float.Lerp(range.Minimum, range.Maximum, (float)Math.Clamp(random(), 0, 1));
    }
}
