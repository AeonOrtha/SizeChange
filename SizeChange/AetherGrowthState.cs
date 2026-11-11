using System;

namespace SizeChange;

internal struct AetherGrowthState
{
    public const float HitRatio = 0.004f;
    public float RemainingSeconds;
    private float drainRemainingSeconds;
    public float PendingGrowth;
    public float PendingDamageRatio;
    public float Bonus;
    public bool InRange;

    public int Advance(int sources, float seconds)
    {
        return Advance(sources, 0, seconds, 1f).Crystals ? sources : 0;
    }

    public (bool Crystals, bool Drain) Advance(int crystals, int drains, float seconds, float crystalInterval)
    {
        InRange = crystals > 0 || drains > 0;
        return (Tick(crystals, ref RemainingSeconds, seconds, crystalInterval),
            Tick(drains, ref drainRemainingSeconds, seconds, 1f));
    }

    private static bool Tick(int sources, ref float remaining, float seconds, float interval)
    {
        if (sources <= 0) { remaining = 0; return false; }
        interval = float.IsFinite(interval) ? Math.Clamp(interval, .05f, 60f) : 1f;
        remaining = Math.Min(remaining, interval);
        remaining -= float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0;
        if (remaining > 0) return false;
        // At most one hit per source per update; never replay a stalled backlog.
        remaining = interval;
        return true;
    }

    public float Cap(float regularLimit) => regularLimit + Bonus;
    public void Release()
    {
        Bonus += PendingGrowth;
        PendingGrowth = PendingDamageRatio = 0f;
    }
    public void Decay(float amount) => Bonus = Math.Max(0f, Bonus - amount);
}
