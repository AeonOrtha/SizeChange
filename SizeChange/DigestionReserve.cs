using System;

namespace SizeChange;

[Serializable]
public sealed class DigestionSettings
{
    public bool Enabled { get; set; }
    public float StoredPercent { get; set; } = 50f;
    // Legacy JSON field. LowHpDepositBoost replaces the unbounded power curve.
    public float DepositBalancePercent { get; set; } = 100f;
    public float LowHpDepositBoost { get; set; } = 10f;
    public float DepositBalancePoint { get; set; } = 0.1f;
    public float DepositCutoff { get; set; } = 1f;
    // Legacy JSON name: fraction of reserve digested per second (0.05 = 5%/s).
    // This controls depletion only, never the instantaneous growth speed.
    public float GrowthPerSecond { get; set; } = 0.05f;
    // Legacy JSON field; pulses no longer increase digestion.
    public float DigestionIncreasePerPulse { get; set; }
    public float DigestionNoPulseDelaySeconds { get; set; } = 3f;
    // Fractions of reserve/second: 0.02 adds/removes 2 percentage points/s.
    public float DigestionIncreasePerSecond { get; set; } = 0.02f;
    public float DigestionDecreasePerPulse { get; set; } = 0.02f;
    // Legacy JSON name retained: one full reserve for bone appearance and
    // exceptional fill bonuses. This is a reference amount, not a storage cap.
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
        LowHpDepositBoost = float.IsFinite(LowHpDepositBoost) ? Math.Clamp(LowHpDepositBoost, 1f, 10000f) : 10f;
        DepositBalancePoint = float.IsFinite(DepositBalancePoint) ? Math.Clamp(DepositBalancePoint, 0.000001f, 10000f) : 0.1f;
        DepositCutoff = CutoffFor(DepositBalancePoint);
        StoredPercent = float.IsFinite(StoredPercent) ? Math.Clamp(StoredPercent, 0f, 500f) : 50f;
        GrowthPerSecond = float.IsFinite(GrowthPerSecond) ? Math.Clamp(GrowthPerSecond, 0f, 100f) : 0.05f;
        DigestionIncreasePerPulse = float.IsFinite(DigestionIncreasePerPulse) ? Math.Clamp(DigestionIncreasePerPulse, 0f, 100f) : 0f;
        DigestionNoPulseDelaySeconds = float.IsFinite(DigestionNoPulseDelaySeconds) ? Math.Clamp(DigestionNoPulseDelaySeconds, 0f, 3600f) : 3f;
        DigestionIncreasePerSecond = float.IsFinite(DigestionIncreasePerSecond) ? Math.Clamp(DigestionIncreasePerSecond, 0f, 100f) : 0.02f;
        DigestionDecreasePerPulse = float.IsFinite(DigestionDecreasePerPulse) ? Math.Clamp(DigestionDecreasePerPulse, 0f, 100f) : 0.02f;
        MaximumReserve = float.IsFinite(MaximumReserve) ? Math.Clamp(MaximumReserve, 0.001f, 10000f) : 5f;
    }

    private float CutoffFor(float point) => float.IsFinite(DepositCutoff) && DepositCutoff > point
        ? Math.Clamp(DepositCutoff, MathF.BitIncrement(point), 1000000f)
        : Math.Max(1f, point * 10f);

    internal double DepositFor(float growth, float damageRatio = 0f)
    {
        if (!Enabled || !float.IsFinite(growth) || growth <= 0) return 0;
        double bonusPercent = float.IsFinite(StoredPercent) ? Math.Clamp(StoredPercent, 0, 500) : 0;
        if (bonusPercent <= 0) return 0;
        double point = float.IsFinite(DepositBalancePoint) ? Math.Clamp(DepositBalancePoint, 0.000001f, 10000f) : 0.1;
        double cutoff = CutoffFor((float)point);
        if (growth >= cutoff) return 0;
        double shaped;
        if (growth <= point)
        {
            // 0.00001% HP is a ratio of 0.0000001. Convert that anchor to growth units
            // using THIS contribution's actual HP-to-growth conversion, including
            // its configured multiplier and size scaling. Balance/cutoff retain
            // their existing growth units and take precedence over the boost.
            double anchor = float.IsFinite(damageRatio) && damageRatio > 0f
                ? (double)growth * 0.0000001 / damageRatio : 0;
            // Keep a nonzero blend span if size scaling pushes the HP anchor
            // into the balance point. Clamping continuously avoids a jump when
            // those endpoints cross; the balance value always takes priority.
            anchor = Math.Min(anchor, point * 0.99);
            double t = Math.Clamp((growth - anchor) / (point - anchor), 0, 1);
            double fade = 1 - t * t * (3 - 2 * t);
            double maximumBoost = float.IsFinite(LowHpDepositBoost) ? Math.Clamp(LowHpDepositBoost, 1f, 10000f) : 10;
            // Finite boost even for tiny positive hits; zero input never deposits.
            shaped = growth * (1 + (maximumBoost - 1) * fade);
        }
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
    // Reserve is the fractional growth rate of current settled size per second.
    public double AmbientGrowthPerSecond => Growth;

    internal static double ProportionalGain(double integratedRate, float settledMultiplier,
        bool limited = false, float sizeLimit = float.MaxValue)
    {
        if (!double.IsFinite(integratedRate) || integratedRate <= 0 ||
            !float.IsFinite(settledMultiplier) || settledMultiplier <= 0) return 0;
        double size = Math.Max(1, settledMultiplier);
        double ceiling = limited && float.IsFinite(sizeLimit) ? Math.Max(1, sizeLimit) : float.MaxValue;
        double available = Math.Max(0, ceiling - size);
        if (available <= 0) return 0;
        // Integrate dSize/dt = reserve(t) * size exactly. Clamp only at the
        // representational boundary, before Exp can overflow.
        if (integratedRate >= Math.Log(ceiling / size)) return available;
        double fraction = integratedRate < .00001
            ? integratedRate * (1 + integratedRate * (.5 + integratedRate / 6))
            : Math.Exp(integratedRate) - 1;
        return Math.Min(available, size * fraction);
    }
    private double currentDigestionRate;
    private double secondsWithoutPulse;

    private static double InitialRate(DigestionSettings settings) =>
        float.IsFinite(settings.GrowthPerSecond) ? Math.Clamp(settings.GrowthPerSecond, 0f, 100f) : 0.05;

    public double DigestionRate(DigestionSettings settings) => HasGrowth
        ? currentDigestionRate : InitialRate(settings);

    public void OnGrowthPulse(DigestionSettings settings)
    {
        secondsWithoutPulse = 0;
        if (!settings.Enabled || !HasGrowth) { currentDigestionRate = 0; return; }
        double reduction = float.IsFinite(settings.DigestionDecreasePerPulse)
            ? Math.Clamp(settings.DigestionDecreasePerPulse, 0f, 100f) : 0.02;
        // The configured initial rate starts a new reserve cycle; it is not a
        // floor. Pulses can stop depletion without stopping ambient growth.
        currentDigestionRate = Math.Max(0, currentDigestionRate - reduction);
    }

    public EarnedGrowth Absorb(float growth, float damage, DigestionSettings settings)
    {
        if (!float.IsFinite(growth) || growth <= 0f) return default;
        double validDamage = float.IsFinite(damage) ? Math.Max(0f, damage) : 0;
        if (settings.Enabled)
        {
            double bonus = settings.DepositFor(growth, (float)validDamage);
            if (!HasGrowth) { currentDigestionRate = InitialRate(settings); secondsWithoutPulse = 0; }
            // No gameplay capacity; only avoid numeric infinity at double's
            // representational boundary.
            Growth = Math.Min(Growth, double.MaxValue - bonus) + bonus;
        }
        // Always grant the whole original hit. The visual fullness threshold
        // does not restrict bonus deposits, even when the bone looks full.
        return new(growth, validDamage);
    }

    public double AddFullReserves(float fills, DigestionSettings settings)
    {
        if (!settings.Enabled || !float.IsFinite(fills) || fills <= 0 ||
            !float.IsFinite(settings.MaximumReserve) || settings.MaximumReserve <= 0) return 0;
        // Fixed bonus, independent of hit size and the ordinary deposit curve.
        // Adding to a live reserve must not reset its current digestion rate.
        double bonus = (double)Math.Clamp(fills, 0, 1000) * Math.Clamp(settings.MaximumReserve, .001f, 10000f);
        if (!HasGrowth) { currentDigestionRate = InitialRate(settings); secondsWithoutPulse = 0; }
        Growth = Math.Min(Growth, double.MaxValue - bonus) + bonus;
        return bonus;
    }

    public double Advance(bool paused, float seconds, DigestionSettings settings)
    {
        if (!settings.Enabled) { this = default; return 0; }
        if (!HasGrowth) { this = default; return 0; }
        if (paused || !float.IsFinite(seconds) || seconds <= 0) return 0;
        double delay = float.IsFinite(settings.DigestionNoPulseDelaySeconds)
            ? Math.Clamp(settings.DigestionNoPulseDelaySeconds, 0f, 3600f) : 3;
        double increment = float.IsFinite(settings.DigestionIncreasePerSecond)
            ? Math.Clamp(settings.DigestionIncreasePerSecond, 0f, 100f) : 0.02;
        double remaining = Math.Clamp(seconds, 0, 1);
        double output = 0;
        while (remaining > 0 && HasGrowth)
        {
            // First step is one full second AFTER the quiet-period threshold.
            // Split at the boundary so depletion/output do not depend on FPS.
            double nextStep = delay + Math.Floor(Math.Max(0, secondsWithoutPulse - delay)) + 1;
            double untilStep = nextStep - secondsWithoutPulse;
            bool reachesStep = remaining >= untilStep;
            double dt = Math.Min(remaining, untilStep);
            output += AdvanceAtRate(dt, DigestionRate(settings));
            if (!HasGrowth) break;
            remaining = Math.Max(0, remaining - dt);
            secondsWithoutPulse = reachesStep ? nextStep : secondsWithoutPulse + dt;
            if (reachesStep)
                currentDigestionRate = Math.Min(100, currentDigestionRate + increment);
        }
        return output;
    }

    private double AdvanceAtRate(double dt, double rate)
    {
        double startingSpeed = AmbientGrowthPerSecond;

        // Integrate reserve over this frame; ProportionalGain converts it to
        // size growth. Depletion remains dReserve/dt = -rate*reserve.
        // This is NOT consumed energy converted into size. At zero digestion,
        // the balance remains unchanged and continues driving ambient growth.
        double exponent = rate * dt;
        double averageFraction = exponent < 0.00001
            ? 1 - exponent / 2 + exponent * exponent / 6
            : (1 - Math.Exp(-exponent)) / exponent;
        double ambientGrowth = startingSpeed * dt * averageFraction;
        Growth *= Math.Exp(-exponent);
        // Finish a negligible digestion tail so normal shrink can resume.
        if (rate > 0 && Growth < 0.000001) this = default;
        return ambientGrowth;
    }
}
