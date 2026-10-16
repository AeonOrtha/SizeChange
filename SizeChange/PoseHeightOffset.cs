using System;

namespace SizeChange;

// Select one flat offset. Growth-dependent height is added by the existing path.
internal static class PoseHeightOffset
{
    internal static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, -100f, 100f) : 0f;
    internal static float Select(float normal, bool allowed, bool sitting, bool sitEnabled,
        float sitHeight, float? emoteHeight) => !allowed ? normal :
        emoteHeight.HasValue ? Clamp(emoteHeight.Value) :
        sitting && sitEnabled ? Clamp(sitHeight) : normal;
}
