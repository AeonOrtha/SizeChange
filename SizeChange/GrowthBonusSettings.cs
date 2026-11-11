using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public sealed class BonusVfxLayer
{
    public string Path { get; set; } = string.Empty;
    public float DurationSeconds { get; set; } = 2f;
    public bool Restart { get; set; } = true;
}

[Serializable]
public sealed class GrowthBonusSettings
{
    public bool ChainsEnabled { get; set; }
    public float ChainChancePercent { get; set; } = 20;
    public float ChainGrowthPercent { get; set; } = 50;
    public int MaximumExtraPulses { get; set; } = 3;
    public bool ExceptionalEnabled { get; set; }
    public float ExceptionalChancePercent { get; set; } = 10;
    public float ExceptionalMultiplier { get; set; } = 2;
    public bool ExceptionalVfxEnabled { get; set; }
    public List<BonusVfxLayer> VfxLayers { get; set; } = new();

    internal static float Finite(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    public void Validate()
    {
        ChainChancePercent = Finite(ChainChancePercent, 0, 100, 20);
        ChainGrowthPercent = Finite(ChainGrowthPercent, 0, 100, 50);
        MaximumExtraPulses = Math.Clamp(MaximumExtraPulses, 0, 16);
        ExceptionalChancePercent = Finite(ExceptionalChancePercent, 0, 100, 10);
        ExceptionalMultiplier = Finite(ExceptionalMultiplier, 1, 100, 2);
        VfxLayers ??= new();
        VfxLayers.RemoveAll(x => x == null);
        if (VfxLayers.Count > 16) VfxLayers.RemoveRange(16, VfxLayers.Count - 16);
        foreach (var layer in VfxLayers)
        {
            layer.Path ??= string.Empty;
            layer.DurationSeconds = Finite(layer.DurationSeconds, .05f, 300, 2);
        }
    }

    public static GrowthBonusSettings CopyOf(GrowthBonusSettings s) => new()
    {
        ChainsEnabled = s.ChainsEnabled, ChainChancePercent = s.ChainChancePercent,
        ChainGrowthPercent = s.ChainGrowthPercent, MaximumExtraPulses = s.MaximumExtraPulses,
        ExceptionalEnabled = s.ExceptionalEnabled, ExceptionalChancePercent = s.ExceptionalChancePercent,
        ExceptionalMultiplier = s.ExceptionalMultiplier, ExceptionalVfxEnabled = s.ExceptionalVfxEnabled,
        VfxLayers = (s.VfxLayers ?? new()).FindAll(x => x != null).ConvertAll(x => new BonusVfxLayer
        { Path = x.Path, DurationSeconds = x.DurationSeconds, Restart = x.Restart }),
    };
}

internal readonly record struct GrowthBonusResult(float TotalGrowth, int ExtraPulses, int ExceptionalPulses)
{
    // Preserve the permissions of each contributing source in a mixed release.
    internal (float Growth, float Aether, float Emote) ExtraForSources(float originalGrowth, float aetherGrowth, float emoteGrowth)
    {
        if (!float.IsFinite(originalGrowth) || originalGrowth <= 0 || !float.IsFinite(TotalGrowth)) return default;
        double extra = Math.Max(0, (double)TotalGrowth - originalGrowth);
        double aetherShare = float.IsFinite(aetherGrowth) ? Math.Clamp((double)aetherGrowth / originalGrowth, 0, 1) : 0;
        double emoteShare = float.IsFinite(emoteGrowth) ? Math.Clamp((double)emoteGrowth / originalGrowth, 0, 1 - aetherShare) : 0;
        return ((float)extra, (float)(extra * aetherShare), (float)(extra * emoteShare));
    }
}

internal static class GrowthBonusRolls
{
    private static bool Roll(float chance, Func<double> random)
    {
        if (!float.IsFinite(chance) || chance <= 0) return false;
        if (chance >= 100) return true;
        double draw = random();
        return double.IsFinite(draw) && draw >= 0 && draw < chance / 100d;
    }

    internal static GrowthBonusResult Resolve(float originalGrowth, GrowthBonusSettings settings, Func<double> random)
    {
        if (!float.IsFinite(originalGrowth) || originalGrowth <= 0) return default;
        int limit = settings.ChainsEnabled ? Math.Clamp(settings.MaximumExtraPulses, 0, 16) : 0;
        double linkGrowth = originalGrowth * (double)GrowthBonusSettings.Finite(settings.ChainGrowthPercent, 0, 100, 50) / 100;
        double total = 0;
        int extra = 0, exceptional = 0;
        for (int depth = 0; ; depth++)
        {
            // Each link has an independent exceptional roll. Its nominal amount
            // is based on the original pulse, never a previously boosted link.
            double amount = depth == 0 ? originalGrowth : linkGrowth;
            if (settings.ExceptionalEnabled && Roll(settings.ExceptionalChancePercent, random))
            {
                amount *= GrowthBonusSettings.Finite(settings.ExceptionalMultiplier, 1, 100, 2);
                exceptional++;
            }
            total += amount;
            if (depth >= limit || linkGrowth <= 0 || !Roll(settings.ChainChancePercent, random)) break;
            extra++;
        }
        return new((float)Math.Min(float.MaxValue, total), extra, exceptional);
    }
}
