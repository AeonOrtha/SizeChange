using System;

namespace SizeChange;

[Serializable]
public sealed class DigestionSettings
{
    public bool Enabled { get; set; }
    public float StoredPercent { get; set; } = 50f;
    public float GrowthPerSecond { get; set; } = 0.05f;
    public float MaximumReserve { get; set; } = 5f;
    public bool BoneEnabled { get; set; }
    public string BoneName { get; set; } = string.Empty;
    public float BoneMaximumAddition { get; set; } = 0.4f;

    internal float BoneAddition(double reserve) => Enabled && BoneEnabled &&
        double.IsFinite(reserve) && float.IsFinite(MaximumReserve) && MaximumReserve > 0f
        ? (float)Math.Clamp(reserve / MaximumReserve, 0.0, 1.0) * BoneMaximumAddition : 0f;

    public void Validate()
    {
        BoneName = BoneName?.Trim() ?? string.Empty;
        if (BoneName.Equals("n_root", StringComparison.OrdinalIgnoreCase)) BoneName = string.Empty;
        BoneMaximumAddition = float.IsFinite(BoneMaximumAddition) ? Math.Clamp(BoneMaximumAddition, 0f, 5f) : 0.4f;
        StoredPercent = float.IsFinite(StoredPercent) ? Math.Clamp(StoredPercent, 0f, 100f) : 50f;
        GrowthPerSecond = float.IsFinite(GrowthPerSecond) ? Math.Clamp(GrowthPerSecond, 0.001f, 100f) : 0.05f;
        MaximumReserve = float.IsFinite(MaximumReserve) ? Math.Clamp(MaximumReserve, 0.001f, 10000f) : 5f;
    }

    public static DigestionSettings CopyOf(DigestionSettings settings) => (DigestionSettings)settings.MemberwiseClone();
}

// Growth is valued once, when absorbed. Damage travels with it only for BPM
// feedback; digestion must not run the gain formula again at a larger size.
internal readonly record struct EarnedGrowth(double Growth, double DamageRatio);

internal struct DigestionReserve
{
    public double Growth { get; private set; }
    private double damageRatio;
    private double elapsed;
    public bool HasGrowth => Growth > 0;

    public EarnedGrowth Absorb(float growth, float damage, DigestionSettings settings)
    {
        if (!float.IsFinite(growth) || growth <= 0f) return default;
        double validDamage = float.IsFinite(damage) ? Math.Max(0f, damage) : 0;
        if (!settings.Enabled) return new(growth, validDamage);
        double stored = Math.Min(growth * settings.StoredPercent / 100.0,
            Math.Max(0.0, settings.MaximumReserve - Growth));
        double storedDamage = validDamage * (stored / growth);
        Growth += stored;
        damageRatio += storedDamage;
        // A full reserve passes overflow straight through. Reducing capacity
        // does not destroy an existing balance or trigger a sudden release.
        return new(growth - stored, validDamage - storedDamage);
    }

    public EarnedGrowth Advance(bool sourcesActive, bool paused, float seconds, DigestionSettings settings)
    {
        if (!settings.Enabled) { this = default; return default; }
        if (sourcesActive || paused || !HasGrowth) { elapsed = 0; return default; }
        elapsed += float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0;
        if (elapsed < 1.0) return default;
        double ticks = Math.Floor(elapsed);
        elapsed -= ticks;
        double release = Math.Min(Growth, settings.GrowthPerSecond * ticks);
        double releasedDamage = damageRatio * (release / Growth);
        Growth -= release;
        damageRatio -= releasedDamage;
        if (!HasGrowth) this = default;
        return new(release, releasedDamage);
    }
}
