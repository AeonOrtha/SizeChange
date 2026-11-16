using System;

namespace SizeChange;

// Select one flat offset. Growth-dependent height is added by the existing path.
internal static class PoseHeightOffset
{
    // Absolute result from the selected flat offset, never last frame's output.
    internal static float WithGrowth(float flatOffset, float visibleMultiplier, float perScale) =>
        flatOffset + Math.Max(0f, visibleMultiplier - 1f) * perScale;
    internal static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, -100f, 100f) : 0f;
    internal static float Select(float normal, bool allowed, bool sitting, bool sitEnabled,
        float sitHeight, float? emoteHeight) => !allowed ? normal :
        emoteHeight.HasValue ? Clamp(emoteHeight.Value) :
        sitting && sitEnabled ? Clamp(sitHeight) : normal;
}
