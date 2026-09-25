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
    private float riseCurve;
    private float returnCurve;

    public void Start(float visibleScale, float extraScale, float riseTime,
        float settleTime, float riseCurveBias, float returnCurveBias,
        float accumulatorDelay, float frameSeconds)
    {
        startScale = visibleScale;
        overshootScale = extraScale;
        riseSeconds = riseTime;
        settleSeconds = settleTime;
        riseCurve = Math.Clamp(riseCurveBias, -2f, 2f);
        returnCurve = Math.Clamp(returnCurveBias, -2f, 2f);
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
            float visibleScale = float.Lerp(startScale, peakScale, EasePulse(t, riseCurve));
            if (t >= 1f)
            {
                // Render the actual peak for this update before returning.
                rising = false;
                elapsed = 0f;
            }
            return visibleScale;
        }

        float settleT = settleSeconds <= 0f ? 1f : Math.Clamp(elapsed / settleSeconds, 0f, 1f);
        float result = float.Lerp(peakScale, intendedScale, EasePulse(settleT, returnCurve));
        if (settleT >= 1f)
            IsActive = false;
        return result;
    }

    // Quintic easing gives zero velocity and acceleration at both ends.
    // The rise rounds into the peak and the return rounds into the settled
    // size, instead of reversing a constant-speed lerp abruptly.
    private static float EasePulse(float t, float curveBias)
    {
        // Negative bias puts more movement early in the phase; positive bias
        // delays it. Warping before easing preserves the rounded endpoints for
        // every setting, including the shared rise/return peak.
        float bias = MathF.Pow(2f, curveBias);
        t /= bias + (1f - bias) * t;
        return Math.Clamp(t * t * t * (t * (6f * t - 15f) + 10f), 0f, 1f);
    }
}
