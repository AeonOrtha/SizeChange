using System;

namespace SizeChange;

// Earned size and temporary pulse are independent. Only the baseline is fed
// back into its next lerp, so a surge cannot consume or compound slow growth.
internal struct GrowthVisualScale
{
    private bool initialized;
    private GrowthOvershootPulse pulse;
    public float BaseScale { get; private set; }
    public float PulseOffset { get; private set; }
    public bool IsPulsing => pulse.IsActive;

    public void StartPulse(float extraScale, float riseTime, float settleTime,
        float riseCurve, float returnCurve, float accumulatorDelay, float frameSeconds)
    {
        // Retriggers start at the currently visible offset, never at zero.
        // Do not stack old amplitudes into the next pulse or the earned size.
        pulse.Start(PulseOffset, Math.Max(PulseOffset, extraScale), riseTime,
            settleTime, riseCurve, returnCurve, accumulatorDelay, frameSeconds);
    }

    public void CancelPulse()
    {
        // Switching the effect off resumes normal interpolation from the
        // visible size, rather than abruptly removing a live pulse.
        if (initialized) BaseScale += PulseOffset;
        PulseOffset = 0f;
        pulse = default;
    }

    public float Update(float previousVisibleScale, float targetScale, float lerpAmount,
        float maximumScale, float minimumScale, float frameSeconds)
    {
        if (!initialized)
        {
            BaseScale = previousVisibleScale;
            initialized = true;
        }
        // Identical smoothing to overshoot-disabled growth, including shrink.
        // It continues during rise, settle and the interval between pulses.
        BaseScale = Math.Max(minimumScale,
            Math.Min(float.Lerp(BaseScale, targetScale, lerpAmount), maximumScale));
        PulseOffset = pulse.IsActive ? pulse.Update(frameSeconds, 0f) : 0f;
        // The temporary pulse may exceed the cap; its baseline never does.
        return BaseScale + PulseOffset;
    }
}
