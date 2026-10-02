using System;

namespace SizeChange;

internal struct AetherGrowthState
{
    public const float HitRatio = 0.004f;
    public float RemainingSeconds;
    public float PendingGrowth;
    public float PendingDamageRatio;
    public float Bonus;
    public bool InRange;

    public int Advance(int sources, float seconds)
    {
        InRange = sources > 0;
        if (!InRange) { RemainingSeconds = 0; return 0; }
        RemainingSeconds -= float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0;
        if (RemainingSeconds > 0) return 0;
        RemainingSeconds = 1f;
        return sources;
    }

    public float Cap(float regularLimit) => regularLimit + Bonus;
    public void Release()
    {
        Bonus += PendingGrowth;
        PendingGrowth = PendingDamageRatio = 0f;
    }
    public void Decay(float amount) => Bonus = Math.Max(0f, Bonus - amount);
}
