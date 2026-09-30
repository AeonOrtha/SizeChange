using System;

namespace SizeChange;

// Runtime-only activation. The timer advances once per profile per frame,
// so adding more tracked actors does not make fake hits arrive faster.
internal sealed class GrowthPreview
{
    private bool enabled;
    private float remaining;
    public float HitRatio { get; private set; }
    public float HealthRatio { get; private set; } = 1f;
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            remaining = 0;
            HitRatio = 0;
            HealthRatio = 1f;
        }
    }

    public void Advance(float seconds, bool allowed, float hitPercent, float interval)
    {
        HitRatio = 0;
        if (!allowed) Enabled = false;
        if (!Enabled) return;
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0f;
        interval = float.IsFinite(interval) ? Math.Clamp(interval, 0.1f, 10f) : 1f;
        remaining -= seconds;
        if (remaining > 0) return;
        // Do not replay a backlog after a stalled frame.
        remaining = interval;
        HitRatio = float.IsFinite(hitPercent) ? Math.Clamp(hitPercent, 0f, 100f) / 100f : 0f;
        HealthRatio = Math.Max(0f, HealthRatio - HitRatio);
    }
}
