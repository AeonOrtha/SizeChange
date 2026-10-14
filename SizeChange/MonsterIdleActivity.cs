using System;

namespace SizeChange;

// Eligibility only: never spends growth, releases the accumulator or changes
// its deadline. A completed pulse refreshes the grace period too.
internal struct MonsterIdleActivity
{
    private bool hasActivity;
    private long lastActivity;
    public void Pulse(long now) { hasActivity = true; lastActivity = now; }
    public bool Update(bool accumulating, long now, float intervalSeconds)
    {
        if (accumulating) Pulse(now);
        if (!hasActivity || now < lastActivity) return false;
        return now - lastActivity < 2d * Math.Max(0.05f, intervalSeconds) * 1000d;
    }
    internal static float Interval(float accumulatorDelay, float riseSeconds, float settleSeconds)
    {
        float seconds = accumulatorDelay > 0 ? accumulatorDelay : riseSeconds + settleSeconds;
        return float.IsFinite(seconds) ? Math.Max(0.05f, seconds) : 0.05f;
    }
}
