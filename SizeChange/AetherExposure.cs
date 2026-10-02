using System;

namespace SizeChange;

internal static class AetherExposure
{
    public static float HitRatio(float distance, float range, float peakPercent)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(range) || !float.IsFinite(peakPercent) ||
            range <= 0f || peakPercent <= 0f || distance >= range) return 0f;
        float proximity = 1f - Math.Clamp(distance / range, 0f, 1f);
        float strength = proximity * proximity * (3f - 2f * proximity);
        return Math.Clamp(peakPercent, 0f, 100f) / 100f * strength;
    }
}
