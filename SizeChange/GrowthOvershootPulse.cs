using System;

namespace SizeChange;

// Visual-only pulse. It never changes earned growth or the accumulator clock.
internal struct GrowthOvershootPulse
{
    public bool IsActive { get; private set; }
    private bool rising;
    private float startScale;
    private float overshootScale;
    private float peakScale;
    private float elapsed;
    private float riseSeconds;
    private float settleSeconds;

    public void Start(float visibleScale, float extraScale, float riseTime,
        float settleTime, float accumulatorDelay, float frameSeconds)
    {
        startScale = visibleScale;
        overshootScale = extraScale;
        riseSeconds = riseTime;
        settleSeconds = settleTime;
        if (accumulatorDelay > 0f)
        {
            // Fit the animation to the clock, never the clock to the animation.
            // Reserve two updates for phase rounding and the settled frame.
            float budget = Math.Max(frameSeconds, accumulatorDelay - 2f * frameSeconds);
            float fit = Math.Min(1f, budget / (riseSeconds + settleSeconds));
            riseSeconds *= fit;
            settleSeconds *= fit;
        }
        elapsed = 0f;
        rising = true;
        IsActive = true;
    }

    public float Update(float frameSeconds, float intendedScale)
    {
        elapsed += Math.Max(0f, frameSeconds);
        if (rising)
        {
            peakScale = intendedScale + overshootScale;
            float t = riseSeconds <= 0f ? 1f : Math.Clamp(elapsed / riseSeconds, 0f, 1f);
            float visibleScale = float.Lerp(startScale, peakScale, t);
            if (t >= 1f)
            {
                // Render the actual peak for this update before returning.
                rising = false;
                elapsed = 0f;
            }
            return visibleScale;
        }

        float settleT = settleSeconds <= 0f ? 1f : Math.Clamp(elapsed / settleSeconds, 0f, 1f);
        float result = float.Lerp(peakScale, intendedScale, settleT);
        if (settleT >= 1f)
            IsActive = false;
        return result;
    }
}
