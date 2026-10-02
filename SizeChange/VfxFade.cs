using System;

namespace SizeChange;

internal struct VfxFade
{
    public float Level;
    public float Opacity => Level * Level * (3f - 2f * Level);
    public float Advance(bool visible, float seconds, float fadeIn, float fadeOut)
    {
        float duration = visible ? fadeIn : fadeOut;
        if (!float.IsFinite(duration) || duration <= 0f) Level = visible ? 1f : 0f;
        else if (float.IsFinite(seconds) && seconds > 0f)
            Level = Math.Clamp(Level + (visible ? 1f : -1f) * seconds / duration, 0f, 1f);
        return Opacity;
    }
}
