using System;

namespace SizeChange;

internal static class GrowthScalingMath
{
    public static bool IsOutOfCombat(bool inCombat, bool previewing) =>
        !inCombat && !previewing;

    // Settled multiplier only: pending growth and the rendered pulse are excluded.
    public static float CalculateGain(float healthLostRatio, float damageMultiplier,
        float settledMultiplier, bool proportional) =>
        healthLostRatio * damageMultiplier *
        (proportional ? Math.Max(1f, settledMultiplier) : 1f);
}
