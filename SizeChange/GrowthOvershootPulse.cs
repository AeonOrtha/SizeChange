using System;

namespace SizeChange;

// One finite additive envelope. Timing is independent of release cadence.
internal struct GrowthOvershootPulse
{
    // Multiplier approaches 1 as releases grow. The resulting amplitude stays
    // monotonic: a larger release must never produce a smaller overshoot.
    public static float BoostSmallRelease(float growth, float maximumBoost, float rangePercent)
    {
        if (growth <= 0f) return 0f;
        float range = Math.Clamp(rangePercent, 0.01f, 100f) / 100f;
        float boost = Math.Clamp(maximumBoost, 1f, 100f);
        return growth * (1f + (boost - 1f) * range / (range + growth));
    }

    private double elapsed;
    private double duration;
    private double amplitude;
    private double riseFraction;
    public bool IsActive => duration > 0 && elapsed < duration;
    internal bool CanCombine(float seconds, float risePercent) => elapsed == 0 && duration == seconds && riseFraction == RiseFraction(risePercent);
    private static double RiseFraction(float percent) => float.IsFinite(percent) ? Math.Clamp(percent, 1f, 99f) / 100d : .3;
    internal void Combine(float extraScale) => amplitude += extraScale;

    public void Start(float extraScale, float seconds, float risePercent = 30)
    {
        elapsed = 0;
        duration = float.IsFinite(seconds) && seconds > 0 ? Math.Clamp(seconds, .1f, 20f) : .7f;
        amplitude = float.IsFinite(extraScale) ? Math.Max(0, extraScale) : 0;
        riseFraction = RiseFraction(risePercent);
    }

    public double Update(float frameSeconds)
    {
        if (!IsActive) return 0;
        if (float.IsFinite(frameSeconds)) elapsed += Math.Max(0, frameSeconds);
        if (!IsActive) return 0;
        double progress = elapsed / duration;
        // A rounded rise and gentle return using the remaining time. Both have
        // zero velocity and acceleration at their endpoints, including peak.
        double value = progress < riseFraction
            ? Ease(progress / riseFraction)
            : 1 - Ease((progress - riseFraction) / (1 - riseFraction));
        return amplitude * value;
    }

    private static double Ease(double t) => Math.Clamp(t * t * t * (t * (6 * t - 15) + 10), 0, 1);
}
