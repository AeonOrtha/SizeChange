using System;

namespace SizeChange;

internal static class GrowthReleaseTiming
{
    internal static (float Minimum, float Maximum) Bounds(float delay, float variation)
        => Bounds(delay, variation, variation);

    internal static (float Minimum, float Maximum) ForSettings(GrowthSettings settings) =>
        ForSettings(settings, settings.AccumulatorDelaySeconds);

    internal static (float Minimum, float Maximum) ForSettings(GrowthSettings settings, float delay)
    {
        var range = Bounds(delay, settings.EarlierVariation, settings.LaterVariation);
        if (GrowthAccumulatorPacing.ValidSeconds(settings.AccumulatorDelayIncreaseSeconds) <= 0) return range;
        float cap = GrowthAccumulatorPacing.Maximum(settings.AccumulatorDelaySeconds, settings.AccumulatorMaximumDelaySeconds);
        // Variation can shorten or lengthen a ramped window, but cannot bypass
        // its configured maximum. Clamp the range before drawing randomness.
        return (Math.Min(range.Minimum, cap), Math.Min(range.Maximum, cap));
    }

    internal static float ForQueue(float remaining, GrowthSettings settings, float delay,
        bool immediate, Func<double> random) => Select(remaining, ForSettings(settings, delay), immediate, random);

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
        return Select(remaining, Bounds(delay, earlier, later), immediate, random);
    }

    private static float Select(float remaining, (float Minimum, float Maximum) range,
        bool immediate, Func<double> random)
    {
        if (immediate) return 0;
        if (range.Maximum <= 0) return 0;
        // Existing windows retain their deadline, regardless of more hits.
        if (remaining > 0) return remaining;
        if (range.Minimum == range.Maximum) return range.Minimum;
        return float.Lerp(range.Minimum, range.Maximum, (float)Math.Clamp(random(), 0, 1));
    }
}
