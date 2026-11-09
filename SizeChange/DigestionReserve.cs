using System;

namespace SizeChange;

[Serializable]
public sealed class DigestionSettings
{
    public bool Enabled { get; set; }
    public float StoredPercent { get; set; } = 50f;
    // Retained JSON name for existing profile imports; now a fraction of
    // the current reserve per second (0.05 = 5%/s), not a fixed size amount.
    public float GrowthPerSecond { get; set; } = 0.05f;
    public float FullReserveMultiplier { get; set; } = 5f;
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
        FullReserveMultiplier = float.IsFinite(FullReserveMultiplier) ? Math.Clamp(FullReserveMultiplier, 1f, 100f) : 5f;
        MaximumReserve = float.IsFinite(MaximumReserve) ? Math.Clamp(MaximumReserve, 0.001f, 10000f) : 5f;
    }

    public static DigestionSettings CopyOf(DigestionSettings settings) => (DigestionSettings)settings.MemberwiseClone();
}

// Incoming growth is valued once. Only the original hit carries damage/BPM
// feedback; bonus digestion never becomes another hit or reserve deposit.
internal readonly record struct EarnedGrowth(double Growth, double DamageRatio);

internal struct DigestionReserve
{
    public double Growth { get; private set; }
    public bool HasGrowth => Growth > 0;

    public EarnedGrowth Absorb(float growth, float damage, DigestionSettings settings)
    {
        if (!float.IsFinite(growth) || growth <= 0f) return default;
        double validDamage = float.IsFinite(damage) ? Math.Max(0f, damage) : 0;
        if (settings.Enabled)
        {
            double percent = float.IsFinite(settings.StoredPercent) ? Math.Clamp(settings.StoredPercent, 0, 100) : 0;
            double capacity = float.IsFinite(settings.MaximumReserve) ? Math.Max(0, settings.MaximumReserve) : 0;
            double bonus = Math.Min(growth * percent / 100, Math.Max(0, capacity - Growth));
            Growth += bonus;
        }
        // Always grant the whole original hit. A full reserve stops only new
        // bonus storage; lowering capacity preserves the existing balance.
        return new(growth, validDamage);
    }

    public double Advance(bool paused, float seconds, DigestionSettings settings)
    {
        if (!settings.Enabled) { this = default; return 0; }
        if (paused || !HasGrowth || !float.IsFinite(seconds) || seconds <= 0) return 0;
        double rate = float.IsFinite(settings.GrowthPerSecond) ? Math.Clamp(settings.GrowthPerSecond, 0.001f, 100f) : 0.05;
        double dt = Math.Clamp(seconds, 0, 1);
        // Exact integration of dR/dt = -rate * R. More stored energy means
        // faster ambient growth, with the same result at different frame rates.
        double release = Growth * (1 - Math.Exp(-rate * dt));
        // Pay the tiny tail once so digestion cannot block shrinking forever.
        if (Growth - release < 0.000001) release = Growth;
        double before = Growth;
        Growth -= release;
        return ProducedGrowth(before, Growth, settings);
    }

    // Integrate yield over the consumed reserve, rather than sampling fullness
    // once per frame. Changing FPS cannot change total growth from a balance.
    // Yield = 1 + (fullMultiplier - 1) * min(reserve/capacity, 1)^2.
    internal static double ProducedGrowth(double before, double after, DigestionSettings settings)
    {
        double consumed = before - after;
        if (consumed <= 0) return 0;
        double multiplier = float.IsFinite(settings.FullReserveMultiplier)
            ? Math.Clamp(settings.FullReserveMultiplier, 1, 100) : 5;
        double capacity = float.IsFinite(settings.MaximumReserve) && settings.MaximumReserve > 0
            ? settings.MaximumReserve : 5;
        double high = Math.Min(before, capacity);
        double low = Math.Min(after, capacity);
        // Above capacity (after lowering the limit), yield stays capped.
        double above = Math.Max(0, before - Math.Max(after, capacity));
        double upper = high / capacity, lower = low / capacity;
        double weighted = above + (high - low) *
            (upper * upper + upper * lower + lower * lower) / 3;
        return consumed + (multiplier - 1) * weighted;
    }
}
