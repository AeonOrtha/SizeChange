using System;

namespace SizeChange;

[Serializable]
public sealed class DigestionSettings
{
    public bool Enabled { get; set; }
    public float StoredPercent { get; set; } = 50f;
    public float DepositBalancePercent { get; set; } = 100f;
    public float DepositBalancePoint { get; set; } = 0.1f;
    public float DepositCutoff { get; set; } = 1f;
    // Legacy JSON name: fraction of reserve digested per second (0.05 = 5%/s).
    // This controls depletion only, never the instantaneous growth speed.
    public float GrowthPerSecond { get; set; } = 0.05f;
    // Legacy JSON name retained for imports: visual bone fullness ONLY.
    // This never limits storage or affects digestion output.
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
        DepositBalancePercent = float.IsFinite(DepositBalancePercent) ? Math.Clamp(DepositBalancePercent, 0f, 100f) : 100f;
        DepositBalancePoint = float.IsFinite(DepositBalancePoint) ? Math.Clamp(DepositBalancePoint, 0.000001f, 10000f) : 0.1f;
        DepositCutoff = CutoffFor(DepositBalancePoint);
        StoredPercent = float.IsFinite(StoredPercent) ? Math.Clamp(StoredPercent, 0f, 500f) : 50f;
        GrowthPerSecond = float.IsFinite(GrowthPerSecond) ? Math.Clamp(GrowthPerSecond, 0f, 100f) : 0.05f;
        MaximumReserve = float.IsFinite(MaximumReserve) ? Math.Clamp(MaximumReserve, 0.001f, 10000f) : 5f;
    }

    private float CutoffFor(float point) => float.IsFinite(DepositCutoff) && DepositCutoff > point
        ? Math.Clamp(DepositCutoff, MathF.BitIncrement(point), 1000000f)
        : Math.Max(1f, point * 10f);

    internal double DepositFor(float growth)
    {
        if (!Enabled || !float.IsFinite(growth) || growth <= 0) return 0;
        double bonusPercent = float.IsFinite(StoredPercent) ? Math.Clamp(StoredPercent, 0, 500) : 0;
        if (bonusPercent <= 0) return 0;
        double balance = float.IsFinite(DepositBalancePercent) ? Math.Clamp(DepositBalancePercent, 0, 100) : 100;
        double point = float.IsFinite(DepositBalancePoint) ? Math.Clamp(DepositBalancePoint, 0.000001f, 10000f) : 0.1;
        double cutoff = CutoffFor((float)point);
        if (growth >= cutoff) return 0;
        double shaped;
        if (growth <= point)
            shaped = balance == 0 ? growth : point * Math.Pow(growth / point, 1 - balance / 200);
        else
        {
            // Taper the actual deposit as well as its multiplier. Multiplying
            // a huge input by a fading factor alone can still produce a hump.
            // At point: deposit/input = 1; at cutoff: 0, with no residual floor.
            double remaining = (cutoff - growth) / (cutoff - point);
            shaped = point * remaining * remaining * (3 - 2 * remaining);
        }
        return shaped * bonusPercent / 100;
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
    // +1x stored directly drives +1x/s of ambient growth.
    public double AmbientGrowthPerSecond => Growth;

    public EarnedGrowth Absorb(float growth, float damage, DigestionSettings settings)
    {
        if (!float.IsFinite(growth) || growth <= 0f) return default;
        double validDamage = float.IsFinite(damage) ? Math.Max(0f, damage) : 0;
        if (settings.Enabled)
        {
            double bonus = settings.DepositFor(growth);
            // No gameplay capacity; only avoid numeric infinity at double's
            // representational boundary.
            Growth = Math.Min(Growth, double.MaxValue - bonus) + bonus;
        }
        // Always grant the whole original hit. The visual fullness threshold
        // does not restrict bonus deposits, even when the bone looks full.
        return new(growth, validDamage);
    }

    public double Advance(bool paused, float seconds, DigestionSettings settings)
    {
        if (!settings.Enabled) { this = default; return 0; }
        if (paused || !HasGrowth || !float.IsFinite(seconds) || seconds <= 0) return 0;
        double rate = float.IsFinite(settings.GrowthPerSecond) ? Math.Clamp(settings.GrowthPerSecond, 0f, 100f) : 0.05;
        double dt = Math.Clamp(seconds, 0, 1);
        double startingSpeed = AmbientGrowthPerSecond;

        // Two independent rules: dSize/dt = reserve; dReserve/dt = -rate*reserve.
        // Average the changing speed over this frame for FPS-independent growth.
        // This is NOT consumed energy converted into size. At zero digestion,
        // the balance remains unchanged and continues driving ambient growth.
        double exponent = rate * dt;
        double averageFraction = exponent < 0.00001
            ? 1 - exponent / 2 + exponent * exponent / 6
            : (1 - Math.Exp(-exponent)) / exponent;
        double ambientGrowth = startingSpeed * dt * averageFraction;
        Growth *= Math.Exp(-exponent);
        // Finish a negligible digestion tail so normal shrink can resume.
        if (rate > 0 && Growth < 0.000001) Growth = 0;
        return ambientGrowth;
    }
}
