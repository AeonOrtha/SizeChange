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
public sealed class PulseSoundSettings
{
    public bool Enabled { get; set; }
    public string Path { get; set; } = string.Empty;
    public int Index { get; set; }
    public float Volume { get; set; } = 0.5f;
    public bool SizeVolume { get; set; }
    public float VolumeGain { get; set; } = 0.25f;
    public float MaximumVolume { get; set; } = 20f;
    public bool SizeRate { get; set; }
    public float RateDrop { get; set; } = 0.1f;
    public float MinimumRate { get; set; } = 0.5f;
    public bool KeepThroughChain { get; set; }
    public bool ChainRepeat { get; set; }
    public float ChainRepeatSeconds { get; set; } = 1f;
    public float ChainFadeOutSeconds { get; set; } = 1f;
    public void Validate()
    {
        Path = (Path ?? string.Empty).Trim().Replace('\\', '/');
        Index = Math.Max(0, Index);
        Volume = GrowthBonusSettings.Finite(Volume, 0, 20, .5f);
        VolumeGain = GrowthBonusSettings.Finite(VolumeGain, 0, 20, .25f);
        MaximumVolume = GrowthBonusSettings.Finite(MaximumVolume, 0, 20, 20);
        RateDrop = GrowthBonusSettings.Finite(RateDrop, 0, 1, .1f);
        MinimumRate = GrowthBonusSettings.Finite(MinimumRate, .1f, 1, .5f);
        ChainRepeatSeconds = GrowthBonusSettings.Finite(ChainRepeatSeconds, .05f, 300, 1);
        ChainFadeOutSeconds = GrowthBonusSettings.Finite(ChainFadeOutSeconds, .05f, 30, 1);
    }
    public static PulseSoundSettings CopyOf(PulseSoundSettings s) => (PulseSoundSettings)s.MemberwiseClone();
}

[Serializable]
public sealed class GrowthBonusSettings
{
    public bool ChainsEnabled { get; set; }
    public float ChainChancePercent { get; set; } = 20;
    public float ChainGrowthPercent { get; set; } = 50;
    public float ChainGrowthStepPercent { get; set; }
    public float ChainIntervalSeconds { get; set; } = 0.35f;
    public PulseSoundSettings ExceptionalSound { get; set; } = new();
    public PulseSoundSettings ChainSound { get; set; } = new();
    public bool ChainVfxEnabled { get; set; }
    public List<BonusVfxLayer> ChainVfxLayers { get; set; } = new();
    public int MaximumExtraPulses { get; set; } = 3;
    public bool ExceptionalEnabled { get; set; }
    public float ExceptionalChancePercent { get; set; } = 10;
    public float ChainExceptionalChancePercent { get; set; }
    public int MaximumExceptionalPulsesPerChain { get; set; } = 1;
    public float ExceptionalMultiplier { get; set; } = 2;
    public float ExceptionalReserveFills { get; set; }
    public bool ExceptionalVfxEnabled { get; set; }
    public List<BonusVfxLayer> VfxLayers { get; set; } = new();

    internal static float Finite(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    public void Validate()
    {
        ChainIntervalSeconds = Finite(ChainIntervalSeconds, .05f, 10, .35f);
        ExceptionalSound ??= new(); ExceptionalSound.Validate();
        ChainSound ??= new(); ChainSound.Validate();
        ChainChancePercent = Finite(ChainChancePercent, 0, 100, 20);
        ChainGrowthPercent = Finite(ChainGrowthPercent, 0, 1000, 50);
        ChainGrowthStepPercent = Finite(ChainGrowthStepPercent, 0, 1000, 0);
        MaximumExtraPulses = Math.Max(0, MaximumExtraPulses);
        ExceptionalChancePercent = Finite(ExceptionalChancePercent, 0, 100, 10);
        ChainExceptionalChancePercent = Finite(ChainExceptionalChancePercent, 0, 100, 0);
        MaximumExceptionalPulsesPerChain = Math.Max(0, MaximumExceptionalPulsesPerChain);
        ExceptionalMultiplier = Finite(ExceptionalMultiplier, 1, 100, 2);
        ExceptionalReserveFills = Finite(ExceptionalReserveFills, 0, 1000, 0);
        VfxLayers = ValidateLayers(VfxLayers);
        ChainVfxLayers = ValidateLayers(ChainVfxLayers);
    }

    private static List<BonusVfxLayer> ValidateLayers(List<BonusVfxLayer>? layers)
    {
        layers ??= new();
        layers.RemoveAll(x => x == null);
        if (layers.Count > 16) layers.RemoveRange(16, layers.Count - 16);
        foreach (var layer in layers)
        {
            layer.Path ??= string.Empty;
            layer.DurationSeconds = Finite(layer.DurationSeconds, .05f, 300, 2);
        }
        return layers;
    }

    private static List<BonusVfxLayer> CopyLayers(List<BonusVfxLayer> layers) =>
        (layers ?? new()).FindAll(x => x != null).ConvertAll(x => new BonusVfxLayer
        { Path = x.Path, DurationSeconds = x.DurationSeconds, Restart = x.Restart });

    public static GrowthBonusSettings CopyOf(GrowthBonusSettings s) => new()
    {
        ChainIntervalSeconds = s.ChainIntervalSeconds,
        ExceptionalSound = PulseSoundSettings.CopyOf(s.ExceptionalSound),
        ChainSound = PulseSoundSettings.CopyOf(s.ChainSound),
        ChainVfxEnabled = s.ChainVfxEnabled, ChainVfxLayers = CopyLayers(s.ChainVfxLayers),
        ChainsEnabled = s.ChainsEnabled, ChainChancePercent = s.ChainChancePercent,
        ChainGrowthPercent = s.ChainGrowthPercent, MaximumExtraPulses = s.MaximumExtraPulses,
        ChainGrowthStepPercent = s.ChainGrowthStepPercent,
        ExceptionalEnabled = s.ExceptionalEnabled, ExceptionalChancePercent = s.ExceptionalChancePercent,
        ChainExceptionalChancePercent = s.ChainExceptionalChancePercent,
        MaximumExceptionalPulsesPerChain = s.MaximumExceptionalPulsesPerChain,
        ExceptionalMultiplier = s.ExceptionalMultiplier, ExceptionalVfxEnabled = s.ExceptionalVfxEnabled,
        ExceptionalReserveFills = s.ExceptionalReserveFills,
        VfxLayers = CopyLayers(s.VfxLayers),
    };
}

internal readonly record struct GrowthBonusResult(float TotalGrowth, bool Exceptional)
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
    internal static bool Roll(float chance, Func<double> random)
    {
        if (!float.IsFinite(chance) || chance <= 0) return false;
        if (chance >= 100) return true;
        double draw = random();
        return double.IsFinite(draw) && draw >= 0 && draw < chance / 100d;
    }

    internal static GrowthBonusResult Resolve(float originalGrowth, GrowthBonusSettings settings, Func<double> random, bool chain = false)
    {
        if (!float.IsFinite(originalGrowth) || originalGrowth <= 0) return default;
        bool exceptional = settings.ExceptionalEnabled &&
            Roll(chain ? settings.ChainExceptionalChancePercent : settings.ExceptionalChancePercent, random);
        double total = originalGrowth * (exceptional
            ? (double)GrowthBonusSettings.Finite(settings.ExceptionalMultiplier, 1, 100, 2) : 1);
        return new((float)Math.Min(float.MaxValue, total), exceptional);
    }

    internal static GrowthPulse Pulse(float growth, float aether, float emote, bool chain,
        GrowthBonusSettings settings, Func<double> random, bool allowExceptional = true)
    {
        if (!float.IsFinite(growth) || growth <= 0) return default;
        aether = float.IsFinite(aether) ? Math.Clamp(aether, 0, growth) : 0;
        emote = float.IsFinite(emote) ? Math.Clamp(emote, 0, growth - aether) : 0;
        var rolled = allowExceptional ? Resolve(growth, settings, random, chain) : new GrowthBonusResult(growth, false);
        var extra = rolled.ExtraForSources(growth, aether, emote);
        return new(rolled.TotalGrowth, (float)Math.Min(float.MaxValue, (double)aether + extra.Aether),
            (float)Math.Min(float.MaxValue, (double)emote + extra.Emote),
            chain, rolled.Exceptional);
    }
}
