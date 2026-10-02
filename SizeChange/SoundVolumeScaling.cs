using System;

namespace SizeChange;

internal static class SoundVolumeScaling
{
    public static float ForSize(bool enabled, float baseVolume, float settledSize, float gainPerScale, float maximum)
    {
        float volume = float.IsFinite(baseVolume) ? Math.Clamp(baseVolume, 0f, 20f) : 0f;
        if (!enabled || volume == 0f || !float.IsFinite(settledSize)) return volume;
        float gain = float.IsFinite(gainPerScale) ? Math.Clamp(gainPerScale, 0f, 20f) : 0.25f;
        float limit = float.IsFinite(maximum) ? Math.Clamp(maximum, volume, 20f) : 20f;
        return Math.Min(limit, volume * (1f + Math.Max(0f, settledSize - 1f) * gain));
    }
}
