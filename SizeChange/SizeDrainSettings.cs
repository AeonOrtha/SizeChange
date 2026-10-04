using System;
using System.Collections.Generic;
using System.Linq;

namespace SizeChange;

[Serializable]
public sealed class SizeDrainSettings
{
    public bool Enabled { get; set; }
    public bool EnableInDuties { get; set; }
    internal bool AllowsLocation(bool inDuty) => !inDuty || EnableInDuties;
    public bool Everyone { get; set; }
    public int MaximumTargets { get; set; } = 8;
    public bool RandomContributors { get; set; }
    public float ContributorChancePercent { get; set; } = 10f;
    public float ContributorRetrySeconds { get; set; } = 5f;
    public float ContributorMinimumSeconds { get; set; } = 5f;
    public float ContributorMaximumSeconds { get; set; } = 15f;
    public List<string> Names { get; set; } = new();
    public float Range { get; set; } = 10f;
    public bool SizeDrivenRange { get; set; }
    public float RangePerScale { get; set; } = 2f;
    public float MaximumRange { get; set; } = 30f;
    public bool ExposureBuildup { get; set; }
    public float ExposureSeconds { get; set; } = 5f;
    public bool LingeringCorruption { get; set; }
    public float LingerSeconds { get; set; } = 5f;
    internal float EffectiveRange(float size) => SizeDrivenRange
        ? Math.Min(Math.Max(Range, MaximumRange), Range + Math.Max(0f, size - 1f) * RangePerScale) : Range;
    public float PeakHitPercent { get; set; } = 0.4f;
    public bool ReceiverVfxEnabled { get; set; }
    public string ReceiverVfxPath { get; set; } = string.Empty;
    public bool SourceVfxEnabled { get; set; }
    public string SourceVfxPath { get; set; } = string.Empty;
    public float FadeIn { get; set; } = 0.5f;
    public float FadeOut { get; set; } = 0.5f;

    public void Validate()
    {
        Names = (Names ?? new()).Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Range = float.IsFinite(Range) ? Math.Clamp(Range, 0.1f, 100f) : 10f;
        RangePerScale = float.IsFinite(RangePerScale) ? Math.Clamp(RangePerScale, 0f, 100f) : 2f;
        MaximumRange = float.IsFinite(MaximumRange) ? Math.Clamp(MaximumRange, Range, 100f) : Math.Max(Range, 30f);
        ExposureSeconds = float.IsFinite(ExposureSeconds) ? Math.Clamp(ExposureSeconds, 0.1f, 300f) : 5f;
        LingerSeconds = float.IsFinite(LingerSeconds) ? Math.Clamp(LingerSeconds, 0.1f, 300f) : 5f;
        PeakHitPercent = float.IsFinite(PeakHitPercent) ? Math.Clamp(PeakHitPercent, 0f, 100f) : 0.4f;
        ContributorChancePercent = float.IsFinite(ContributorChancePercent) ? Math.Clamp(ContributorChancePercent, 0f, 100f) : 10f;
        ContributorRetrySeconds = float.IsFinite(ContributorRetrySeconds) ? Math.Clamp(ContributorRetrySeconds, 0.1f, 300f) : 5f;
        ContributorMinimumSeconds = float.IsFinite(ContributorMinimumSeconds) ? Math.Clamp(ContributorMinimumSeconds, 0.1f, 300f) : 5f;
        ContributorMaximumSeconds = float.IsFinite(ContributorMaximumSeconds) ? Math.Clamp(ContributorMaximumSeconds, ContributorMinimumSeconds, 300f) : Math.Max(15f, ContributorMinimumSeconds);
        MaximumTargets = Math.Clamp(MaximumTargets, 1, 64);
        FadeIn = float.IsFinite(FadeIn) ? Math.Clamp(FadeIn, 0f, 10f) : 0.5f;
        FadeOut = float.IsFinite(FadeOut) ? Math.Clamp(FadeOut, 0f, 10f) : 0.5f;
        ReceiverVfxPath = ReceiverVfxPath?.Trim().Replace('\\', '/') ?? string.Empty;
        SourceVfxPath = SourceVfxPath?.Trim().Replace('\\', '/') ?? string.Empty;
    }

    public static SizeDrainSettings CopyOf(SizeDrainSettings settings)
    {
        var copy = (SizeDrainSettings)settings.MemberwiseClone();
        copy.Names = new(settings.Names);
        return copy;
    }
}
