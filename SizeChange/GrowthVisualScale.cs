using System;
using System.Collections.Generic;

namespace SizeChange;

// Earned size and temporary pulse are independent. Only the baseline is fed
// back into its next lerp, so a surge cannot consume or compound slow growth.
internal struct GrowthVisualScale
{
    private bool initialized;
    private struct Envelope(GrowthOvershootPulse pulse, bool chain)
    {
        public GrowthOvershootPulse Pulse = pulse;
        public bool Chain = chain;
    }
    private List<Envelope>? pulses;
    public float BaseScale { get; private set; }
    public float PulseOffset { get; private set; }
    public float NormalPulseOffset { get; private set; }
    public float ChainPulseOffset { get; private set; }
    public bool IsPulsing => pulses is { Count: > 0 };
    public bool IsChainPulsing => pulses?.Exists(p => p.Chain && p.Pulse.IsActive) == true;
    internal int ActivePulseCount => pulses?.Count ?? 0;

    public void StartPulse(float extraScale, float durationSeconds, float risePercent = 30, bool chain = false)
    {
        if (!float.IsFinite(extraScale) || extraScale <= 0) return;
        pulses ??= new(4);
        // Same-frame contributions with identical timing share an envelope.
        if (pulses.Count > 0 && pulses[^1].Chain == chain && pulses[^1].Pulse.CanCombine(durationSeconds, risePercent))
        {
            var combined = pulses[^1];
            combined.Pulse.Combine(extraScale);
            pulses[^1] = combined;
            return;
        }
        var next = new GrowthOvershootPulse();
        next.Start(extraScale, durationSeconds, risePercent);
        pulses.Add(new(next, chain));
    }

    public void CancelPulse()
    {
        // Switching the effect off resumes normal interpolation from the
        // visible size, rather than abruptly removing a live pulse.
        if (initialized) BaseScale += PulseOffset;
        PulseOffset = 0f;
        NormalPulseOffset = ChainPulseOffset = 0f;
        pulses?.Clear();
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
        double normalOffset = 0, chainOffset = 0;
        if (pulses != null)
        {
            // Compact in one pass. Expired pulses leave no history; storage is
            // reused and no objects are allocated for each frame or envelope.
            int live = 0;
            for (int i = 0; i < pulses.Count; i++)
            {
                var pulse = pulses[i];
                double offset = pulse.Pulse.Update(frameSeconds);
                if (pulse.Chain) chainOffset += offset; else normalOffset += offset;
                if (pulse.Pulse.IsActive) pulses[live++] = pulse;
            }
            if (live < pulses.Count) pulses.RemoveRange(live, pulses.Count - live);
        }
        NormalPulseOffset = (float)Math.Min(float.MaxValue, normalOffset);
        ChainPulseOffset = (float)Math.Min(float.MaxValue, chainOffset);
        PulseOffset = (float)Math.Min(float.MaxValue, normalOffset + chainOffset);
        // The temporary pulse may exceed the cap; its baseline never does.
        return BaseScale + PulseOffset;
    }
}
