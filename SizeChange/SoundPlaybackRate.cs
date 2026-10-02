using System;

namespace SizeChange;

internal static class SoundPlaybackRate
{
    public static float ForSize(bool enabled, float settledScale, float dropPerScale, float minimum)
    {
        if (!enabled || !float.IsFinite(settledScale)) return 1f;
        float drop = float.IsFinite(dropPerScale) ? Math.Clamp(dropPerScale, 0f, 1f) : 0.1f;
        float floor = float.IsFinite(minimum) ? Math.Clamp(minimum, 0.1f, 1f) : 0.5f;
        return Math.Clamp(1f - Math.Max(0f, settledScale - 1f) * drop, floor, 1f);
    }
}
