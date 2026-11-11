using System;

namespace SizeChange;

// Per actor, independent of the current window's remaining time. Emptying a
// window must not reset the ramp, otherwise every release starts from base.
internal struct GrowthAccumulatorPacing
{
    private double additionalDelay;
    private double secondsWithoutPulse;

    internal static float ValidSeconds(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 60) : 0;

    internal static float Maximum(float baseDelay, float maximum) =>
        Math.Max(ValidSeconds(baseDelay), float.IsFinite(maximum) ? Math.Clamp(maximum, 0, 60) : 5);

    internal float Delay(float baseDelay, float increase, float maximum)
    {
        float start = ValidSeconds(baseDelay);
        return ValidSeconds(increase) <= 0 ? start : (float)Math.Min(Maximum(start, maximum), start + additionalDelay);
    }

    internal void OnGrowthPulse(float baseDelay, float increase, float maximum)
    {
        secondsWithoutPulse = 0;
        float start = ValidSeconds(baseDelay);
        float step = ValidSeconds(increase);
        additionalDelay = step <= 0 ? 0 : Math.Min(Maximum(start, maximum) - start,
            Delay(start, step, maximum) - start + step);
    }

    internal void AdvanceRecovery(float seconds, float baseDelay, float increase, float maximum,
        float recoveryDelay, float recoveryPerSecond)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        if (ValidSeconds(increase) <= 0) { Reset(); return; }
        double wait = float.IsFinite(recoveryDelay) ? Math.Clamp(recoveryDelay, 0, 3600) : 5;
        double rate = float.IsFinite(recoveryPerSecond) ? Math.Clamp(recoveryPerSecond, 0, 60) : 0.5;
        double eligibleSeconds = Math.Clamp(secondsWithoutPulse + seconds - wait, 0, seconds);
        // Saturate the clock, not the recovery: after the largest supported
        // wait, every subsequent frame can still reduce the remaining delay.
        secondsWithoutPulse = Math.Min(3601, secondsWithoutPulse + seconds);
        additionalDelay = Math.Min(additionalDelay, Maximum(baseDelay, maximum) - ValidSeconds(baseDelay));
        additionalDelay = Math.Max(0, additionalDelay - rate * eligibleSeconds);
    }

    internal void Reset() => this = default;
}
