using System;

namespace SizeChange;

// Per actor, independent of the current window's remaining time. Emptying a
// window must not reset the ramp, otherwise every release starts from base.
internal struct GrowthAccumulatorPacing
{
    private float additionalDelay;

    internal static float ValidSeconds(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 60) : 0;

    internal static float Maximum(float baseDelay, float maximum) =>
        Math.Max(ValidSeconds(baseDelay), float.IsFinite(maximum) ? Math.Clamp(maximum, 0, 60) : 5);

    internal float Delay(float baseDelay, float increase, float maximum)
    {
        float start = ValidSeconds(baseDelay);
        return ValidSeconds(increase) <= 0 ? start : Math.Min(Maximum(start, maximum), start + additionalDelay);
    }

    internal void OnGrowthPulse(float baseDelay, float increase, float maximum)
    {
        float start = ValidSeconds(baseDelay);
        float step = ValidSeconds(increase);
        additionalDelay = step <= 0 ? 0 : Math.Min(Maximum(start, maximum) - start,
            Delay(start, step, maximum) - start + step);
    }

    internal void Reset() => this = default;
}
