using System;
using System.Collections.Generic;
using System.Linq;

namespace SizeChange;

[Serializable]
public sealed class SizeDrainSettings
{
    public bool Enabled { get; set; }
    public bool Everyone { get; set; }
    public int MaximumTargets { get; set; } = 8;
    public bool ProjectileEnabled { get; set; }
    public string ProjectileLaunchPath { get; set; } = string.Empty;
    public string ProjectileImpactPath { get; set; } = string.Empty;
    public float ProjectileLaunchSeconds { get; set; } = 0.4f;
    public float ProjectileImpactSeconds { get; set; } = 0.6f;
    public string ProjectilePath { get; set; } = string.Empty;
    public float ProjectileTravelSeconds { get; set; } = 0.8f;
    public float ProjectileScale { get; set; } = 1f;
    public float ProjectileSourceHeight { get; set; } = 1f;
    public float ProjectileReceiverHeight { get; set; } = 1f;
    public int MaximumProjectiles { get; set; } = 16;
    public bool RandomContributors { get; set; }
    public float ContributorChancePercent { get; set; } = 10f;
    public float ContributorRetrySeconds { get; set; } = 5f;
    public float ContributorMinimumSeconds { get; set; } = 5f;
    public float ContributorMaximumSeconds { get; set; } = 15f;
    public List<string> Names { get; set; } = new();
    public float Range { get; set; } = 10f;
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
        PeakHitPercent = float.IsFinite(PeakHitPercent) ? Math.Clamp(PeakHitPercent, 0f, 100f) : 0.4f;
        ContributorChancePercent = float.IsFinite(ContributorChancePercent) ? Math.Clamp(ContributorChancePercent, 0f, 100f) : 10f;
        ContributorRetrySeconds = float.IsFinite(ContributorRetrySeconds) ? Math.Clamp(ContributorRetrySeconds, 0.1f, 300f) : 5f;
        ContributorMinimumSeconds = float.IsFinite(ContributorMinimumSeconds) ? Math.Clamp(ContributorMinimumSeconds, 0.1f, 300f) : 5f;
        ContributorMaximumSeconds = float.IsFinite(ContributorMaximumSeconds) ? Math.Clamp(ContributorMaximumSeconds, ContributorMinimumSeconds, 300f) : Math.Max(15f, ContributorMinimumSeconds);
        MaximumTargets = Math.Clamp(MaximumTargets, 1, 64);
        MaximumProjectiles = Math.Clamp(MaximumProjectiles, 1, 64);
        ProjectileLaunchPath = ProjectileLaunchPath?.Trim().Replace('\\', '/') ?? string.Empty;
        ProjectileImpactPath = ProjectileImpactPath?.Trim().Replace('\\', '/') ?? string.Empty;
        ProjectileLaunchSeconds = float.IsFinite(ProjectileLaunchSeconds) ? Math.Clamp(ProjectileLaunchSeconds, .05f, 10f) : .4f;
        ProjectileImpactSeconds = float.IsFinite(ProjectileImpactSeconds) ? Math.Clamp(ProjectileImpactSeconds, .05f, 10f) : .6f;
        ProjectilePath = ProjectilePath?.Trim().Replace('\\', '/') ?? string.Empty;
        ProjectileTravelSeconds = float.IsFinite(ProjectileTravelSeconds) ? Math.Clamp(ProjectileTravelSeconds, .05f, 10f) : .8f;
        ProjectileScale = float.IsFinite(ProjectileScale) ? Math.Clamp(ProjectileScale, .01f, 100f) : 1f;
        ProjectileSourceHeight = float.IsFinite(ProjectileSourceHeight) ? Math.Clamp(ProjectileSourceHeight, -10f, 100f) : 1f;
        ProjectileReceiverHeight = float.IsFinite(ProjectileReceiverHeight) ? Math.Clamp(ProjectileReceiverHeight, -10f, 100f) : 1f;
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
