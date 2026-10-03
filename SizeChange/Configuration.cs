using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public class GrowthSettings
{
    public const string DefaultDeltaGrowthSoundPath =
        "sound/vfx/monster5/se_vfx_monster_inferno_lpowerrise_c.scd";

    public SizeDrainSettings SizeDrain { get; set; } = new();
    public bool AetherProximityGrowth { get; set; }
    public float AetherProximityRange { get; set; } = 10f;
    public float AetherShardHitPercent { get; set; } = 0.4f;
    public float AetherLargeHitPercent { get; set; } = 1.2f;
    public bool AetherActorVfxEnabled { get; set; }
    public string AetherActorVfxPath { get; set; } = string.Empty;
    public bool AetherSourceVfxEnabled { get; set; }
    public float AetherActorVfxFadeIn { get; set; } = 0.5f;
    public float AetherActorVfxFadeOut { get; set; } = 0.5f;
    public float AetherSourceVfxFadeIn { get; set; } = 0.5f;
    public float AetherSourceVfxFadeOut { get; set; } = 0.5f;
    public bool AetherSourceVfxAttached { get; set; } = true;
    public float AetherSourceVfxScale { get; set; } = 1f;
    public float AetherSourceVfxHeight { get; set; } = 0f;
    public string AetherSourceVfxPath { get; set; } = string.Empty;
    public AetherSoundSettings AetherActorSound { get; set; } = new();
    public AetherSoundSettings AetherSourceSound { get; set; } = new();
    public float PreviewHitPercent { get; set; } = 10f;
    public float PreviewHitIntervalSeconds { get; set; } = 1f;
    public float Speed { get; set; } = 2.0f;
    public float MinScaleMultiplier { get; set; } = 0.1f;
    public float MaxScaleMultiplier { get; set; } = 1.0f;
    public float DeltaGrowthMultiplier { get; set; } = 1.0f;
    public bool ScaleGrowthWithSize { get; set; }
    public float MaximumHealthLossRatioPerTrigger { get; set; } = 1.0f;
    public bool LimitDeltaGrowth { get; set; }
    public float DeltaMaxScaleMultiplier { get; set; } = 5.0f;
    public float AccumulatorDelaySeconds { get; set; }
    public float GrowthOvershootSmallBoost { get; set; } = 5f;
    public float GrowthOvershootBoostRangePercent { get; set; } = 5f;
    public float GrowthOvershootPercent { get; set; }
    public float GrowthOvershootRiseSeconds { get; set; } = 0.35f;
    public float GrowthOvershootRiseCurve { get; set; }
    public float GrowthOvershootReturnCurve { get; set; }
    public float GrowthOvershootSettleSeconds { get; set; } = 0.35f;
    public float AmbientShrinkRate { get; set; } = 0.05f;
    public float OutOfCombatDecayMultiplier { get; set; } = 10.0f;
    public bool EnableDeltaHeightOffset { get; set; }
    public float DeltaHeightOffsetPerScale { get; set; } = 0.5f;
    public bool EnableDeltaGrowthSound { get; set; }
    public string DeltaGrowthSoundPath { get; set; } = DefaultDeltaGrowthSoundPath;
    public int DeltaGrowthSoundIndex { get; set; }
    public bool DeltaGrowthSoundSizeDrivenRate { get; set; }
    public float DeltaGrowthSoundRateDropPerScale { get; set; } = 0.1f;
    public float DeltaGrowthSoundMinimumRate { get; set; } = 0.5f;
    public float DeltaGrowthSoundVolume { get; set; } = 1.0f;
    public bool DeltaGrowthSoundSizeDrivenVolume { get; set; }
    public float DeltaGrowthSoundVolumeGainPerScale { get; set; } = 0.25f;
    public float DeltaGrowthSoundMaximumVolume { get; set; } = 20f;
    public float DeltaGrowthSoundCooldownSeconds { get; set; } = 1.0f;
    public bool EnableDeltaGrowthVfx { get; set; }
    public string DeltaGrowthVfxPath { get; set; } = string.Empty;
    public float DeltaGrowthVfxDurationSeconds { get; set; } = 2.0f;
    public float DeltaGrowthVfxCooldownSeconds { get; set; } = 1.0f;
    public float DeltaGrowthVfxScale { get; set; } = 1.0f;
    public bool DeltaGrowthVfxScaleWithActor { get; set; } = true;
    public List<GrowthAnimationChoice>? DeltaGrowthAnimationChoices { get; set; }
    internal List<GrowthAnimationChoice> GetGrowthAnimationChoices()
    {
        // Migrate the old single animation and its chance once. Empty stays empty.
        return DeltaGrowthAnimationChoices ??= string.IsNullOrWhiteSpace(DeltaGrowthAnimationTmbPath)
            ? new() : new() { new GrowthAnimationChoice { Path = DeltaGrowthAnimationTmbPath,
                ChancePercent = DeltaGrowthAnimationChancePercent } };
    }
    public float DeltaGrowthAnimationChancePercent { get; set; } = 100f;
    public bool EnableDeltaGrowthAnimation { get; set; }
    public string DeltaGrowthAnimationTmbPath { get; set; } = string.Empty;
    public float DeltaGrowthAnimationCooldownSeconds { get; set; } = 1.0f;
    public bool OnlyActiveInCombat { get; set; }
    public bool GrowFromDamage { get; set; }
    public bool GrowthFromDelta { get; set; }

    public void Validate()
    {
        SizeDrain ??= new();
        SizeDrain.Validate();
        PreviewHitPercent = float.IsFinite(PreviewHitPercent) ? Math.Clamp(PreviewHitPercent, 0f, 100f) : 10f;
        PreviewHitIntervalSeconds = float.IsFinite(PreviewHitIntervalSeconds)
            ? Math.Clamp(PreviewHitIntervalSeconds, 0.1f, 10f) : 1f;
        Speed = Math.Clamp(Speed, 0.1f, 100f);
        MinScaleMultiplier = Math.Clamp(MinScaleMultiplier, 0.01f, 1f);
        MaxScaleMultiplier = Math.Max(1f, MaxScaleMultiplier);
        AetherProximityRange = float.IsFinite(AetherProximityRange) ? Math.Clamp(AetherProximityRange, 0.1f, 100f) : 10f;
        AetherActorVfxFadeIn = float.IsFinite(AetherActorVfxFadeIn) ? Math.Clamp(AetherActorVfxFadeIn, 0f, 10f) : 0.5f;
        AetherActorVfxFadeOut = float.IsFinite(AetherActorVfxFadeOut) ? Math.Clamp(AetherActorVfxFadeOut, 0f, 10f) : 0.5f;
        AetherSourceVfxFadeIn = float.IsFinite(AetherSourceVfxFadeIn) ? Math.Clamp(AetherSourceVfxFadeIn, 0f, 10f) : 0.5f;
        AetherSourceVfxFadeOut = float.IsFinite(AetherSourceVfxFadeOut) ? Math.Clamp(AetherSourceVfxFadeOut, 0f, 10f) : 0.5f;
        AetherSourceVfxScale = float.IsFinite(AetherSourceVfxScale) ? Math.Clamp(AetherSourceVfxScale, 0.01f, 100f) : 1f;
        AetherSourceVfxHeight = float.IsFinite(AetherSourceVfxHeight) ? Math.Clamp(AetherSourceVfxHeight, -100f, 100f) : 0f;
        AetherActorSound ??= new(); AetherActorSound.Validate();
        AetherSourceSound ??= new(); AetherSourceSound.Validate();
        AetherShardHitPercent = float.IsFinite(AetherShardHitPercent) ? Math.Clamp(AetherShardHitPercent, 0f, 100f) : 0.4f;
        AetherLargeHitPercent = float.IsFinite(AetherLargeHitPercent) ? Math.Clamp(AetherLargeHitPercent, 0f, 100f) : 1.2f;
        AetherActorVfxPath = AetherActorVfxPath?.Trim().Replace('\\', '/') ?? string.Empty;
        AetherSourceVfxPath = AetherSourceVfxPath?.Trim().Replace('\\', '/') ?? string.Empty;
        DeltaGrowthMultiplier = Math.Max(0f, DeltaGrowthMultiplier);
        MaximumHealthLossRatioPerTrigger =
            Math.Clamp(MaximumHealthLossRatioPerTrigger, 0f, 1f);
        DeltaMaxScaleMultiplier = Math.Max(1f, DeltaMaxScaleMultiplier);
        AccumulatorDelaySeconds = Math.Clamp(AccumulatorDelaySeconds, 0f, 60f);
        GrowthOvershootSmallBoost = Math.Clamp(GrowthOvershootSmallBoost, 1f, 100f);
        GrowthOvershootBoostRangePercent = Math.Clamp(GrowthOvershootBoostRangePercent, 0.01f, 100f);
        GrowthOvershootPercent = Math.Clamp(GrowthOvershootPercent, 0f, 500f);
        GrowthOvershootRiseSeconds = Math.Clamp(GrowthOvershootRiseSeconds, 0.05f, 10f);
        GrowthOvershootRiseCurve = Math.Clamp(GrowthOvershootRiseCurve, -2f, 2f);
        GrowthOvershootReturnCurve = Math.Clamp(GrowthOvershootReturnCurve, -2f, 2f);
        GrowthOvershootSettleSeconds =
            Math.Clamp(GrowthOvershootSettleSeconds, 0.05f, 10f);
        AmbientShrinkRate = Math.Max(0f, AmbientShrinkRate);
        OutOfCombatDecayMultiplier = Math.Max(1f, OutOfCombatDecayMultiplier);
        DeltaHeightOffsetPerScale = Math.Max(0f, DeltaHeightOffsetPerScale);
        DeltaGrowthSoundPath = string.IsNullOrWhiteSpace(DeltaGrowthSoundPath)
            ? DefaultDeltaGrowthSoundPath
            : DeltaGrowthSoundPath.Trim().Replace('\\', '/');
        DeltaGrowthSoundIndex = Math.Max(0, DeltaGrowthSoundIndex);
        DeltaGrowthSoundRateDropPerScale = float.IsFinite(DeltaGrowthSoundRateDropPerScale)
            ? Math.Clamp(DeltaGrowthSoundRateDropPerScale, 0f, 1f) : 0.1f;
        DeltaGrowthSoundMinimumRate = float.IsFinite(DeltaGrowthSoundMinimumRate)
            ? Math.Clamp(DeltaGrowthSoundMinimumRate, 0.1f, 1f) : 0.5f;
        DeltaGrowthSoundVolume = float.IsFinite(DeltaGrowthSoundVolume) ? Math.Clamp(DeltaGrowthSoundVolume, 0f, 20f) : 1f;
        DeltaGrowthSoundVolumeGainPerScale = float.IsFinite(DeltaGrowthSoundVolumeGainPerScale) ? Math.Clamp(DeltaGrowthSoundVolumeGainPerScale, 0f, 20f) : 0.25f;
        DeltaGrowthSoundMaximumVolume = float.IsFinite(DeltaGrowthSoundMaximumVolume) ? Math.Clamp(DeltaGrowthSoundMaximumVolume, 0f, 20f) : 20f;
        DeltaGrowthAnimationChancePercent = float.IsFinite(DeltaGrowthAnimationChancePercent) ? Math.Clamp(DeltaGrowthAnimationChancePercent, 0f, 100f) : 100f;
        DeltaGrowthSoundCooldownSeconds =
            Math.Clamp(DeltaGrowthSoundCooldownSeconds, 0f, 60f);
        DeltaGrowthVfxPath = DeltaGrowthVfxPath?.Trim().Replace('\\', '/')
            ?? string.Empty;
        DeltaGrowthVfxDurationSeconds =
            Math.Clamp(DeltaGrowthVfxDurationSeconds, 0.05f, 300f);
        DeltaGrowthVfxCooldownSeconds =
            Math.Clamp(DeltaGrowthVfxCooldownSeconds, 0f, 60f);
        DeltaGrowthVfxScale = Math.Clamp(DeltaGrowthVfxScale, 0.01f, 100f);
        DeltaGrowthAnimationTmbPath =
            DeltaGrowthAnimationTmbPath?.Trim().Replace('\\', '/') ?? string.Empty;
        GrowthAnimationChoice.Validate(GetGrowthAnimationChoices());
        DeltaGrowthAnimationCooldownSeconds =
            Math.Clamp(DeltaGrowthAnimationCooldownSeconds, 0f, 60f);

        if (GrowFromDamage && GrowthFromDelta)
        {
            GrowFromDamage = false;
        }
    }

    public static GrowthSettings Defaults() => new();

    public static GrowthSettings CopyOf(GrowthSettings settings)
        => new()
        {
            SizeDrain = SizeDrainSettings.CopyOf(settings.SizeDrain),
            AetherActorSound = AetherSoundSettings.CopyOf(settings.AetherActorSound),
            AetherSourceSound = AetherSoundSettings.CopyOf(settings.AetherSourceSound),
            AetherShardHitPercent = settings.AetherShardHitPercent,
            AetherLargeHitPercent = settings.AetherLargeHitPercent,
            AetherActorVfxEnabled = settings.AetherActorVfxEnabled,
            AetherActorVfxPath = settings.AetherActorVfxPath,
            AetherActorVfxFadeIn = settings.AetherActorVfxFadeIn,
            AetherActorVfxFadeOut = settings.AetherActorVfxFadeOut,
            AetherSourceVfxFadeIn = settings.AetherSourceVfxFadeIn,
            AetherSourceVfxFadeOut = settings.AetherSourceVfxFadeOut,
            AetherSourceVfxAttached = settings.AetherSourceVfxAttached,
            AetherSourceVfxScale = settings.AetherSourceVfxScale,
            AetherSourceVfxHeight = settings.AetherSourceVfxHeight,
            AetherSourceVfxEnabled = settings.AetherSourceVfxEnabled,
            AetherSourceVfxPath = settings.AetherSourceVfxPath,
            AetherProximityGrowth = settings.AetherProximityGrowth,
            AetherProximityRange = settings.AetherProximityRange,
            PreviewHitPercent = settings.PreviewHitPercent,
            PreviewHitIntervalSeconds = settings.PreviewHitIntervalSeconds,
            Speed = settings.Speed,
            MinScaleMultiplier = settings.MinScaleMultiplier,
            MaxScaleMultiplier = settings.MaxScaleMultiplier,
            DeltaGrowthMultiplier = settings.DeltaGrowthMultiplier,
            ScaleGrowthWithSize = settings.ScaleGrowthWithSize,
            MaximumHealthLossRatioPerTrigger =
                settings.MaximumHealthLossRatioPerTrigger,
            LimitDeltaGrowth = settings.LimitDeltaGrowth,
            DeltaMaxScaleMultiplier = settings.DeltaMaxScaleMultiplier,
            AccumulatorDelaySeconds = settings.AccumulatorDelaySeconds,
            GrowthOvershootSmallBoost = settings.GrowthOvershootSmallBoost,
            GrowthOvershootBoostRangePercent = settings.GrowthOvershootBoostRangePercent,
            GrowthOvershootPercent = settings.GrowthOvershootPercent,
            GrowthOvershootRiseSeconds = settings.GrowthOvershootRiseSeconds,
            GrowthOvershootRiseCurve = settings.GrowthOvershootRiseCurve,
            GrowthOvershootReturnCurve = settings.GrowthOvershootReturnCurve,
            GrowthOvershootSettleSeconds = settings.GrowthOvershootSettleSeconds,
            AmbientShrinkRate = settings.AmbientShrinkRate,
            OutOfCombatDecayMultiplier = settings.OutOfCombatDecayMultiplier,
            EnableDeltaHeightOffset = settings.EnableDeltaHeightOffset,
            DeltaHeightOffsetPerScale = settings.DeltaHeightOffsetPerScale,
            EnableDeltaGrowthSound = settings.EnableDeltaGrowthSound,
            DeltaGrowthSoundPath = settings.DeltaGrowthSoundPath,
            DeltaGrowthSoundIndex = settings.DeltaGrowthSoundIndex,
            DeltaGrowthSoundSizeDrivenRate = settings.DeltaGrowthSoundSizeDrivenRate,
            DeltaGrowthSoundRateDropPerScale = settings.DeltaGrowthSoundRateDropPerScale,
            DeltaGrowthSoundMinimumRate = settings.DeltaGrowthSoundMinimumRate,
            DeltaGrowthSoundVolume = settings.DeltaGrowthSoundVolume,
            DeltaGrowthSoundSizeDrivenVolume = settings.DeltaGrowthSoundSizeDrivenVolume,
            DeltaGrowthSoundVolumeGainPerScale = settings.DeltaGrowthSoundVolumeGainPerScale,
            DeltaGrowthSoundMaximumVolume = settings.DeltaGrowthSoundMaximumVolume,
            DeltaGrowthSoundCooldownSeconds = settings.DeltaGrowthSoundCooldownSeconds,
            EnableDeltaGrowthVfx = settings.EnableDeltaGrowthVfx,
            DeltaGrowthVfxPath = settings.DeltaGrowthVfxPath,
            DeltaGrowthVfxDurationSeconds = settings.DeltaGrowthVfxDurationSeconds,
            DeltaGrowthVfxCooldownSeconds = settings.DeltaGrowthVfxCooldownSeconds,
            DeltaGrowthVfxScale = settings.DeltaGrowthVfxScale,
            DeltaGrowthVfxScaleWithActor = settings.DeltaGrowthVfxScaleWithActor,
            DeltaGrowthAnimationChancePercent = settings.DeltaGrowthAnimationChancePercent,
            EnableDeltaGrowthAnimation = settings.EnableDeltaGrowthAnimation,
            DeltaGrowthAnimationTmbPath = settings.DeltaGrowthAnimationTmbPath,
            DeltaGrowthAnimationChoices = settings.GetGrowthAnimationChoices().ConvertAll(choice =>
                new GrowthAnimationChoice { Path = choice.Path, ChancePercent = choice.ChancePercent }),
            DeltaGrowthAnimationCooldownSeconds =
                settings.DeltaGrowthAnimationCooldownSeconds,
            OnlyActiveInCombat = settings.OnlyActiveInCombat,
            GrowFromDamage = settings.GrowFromDamage,
            GrowthFromDelta = settings.GrowthFromDelta,
        };
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    private const int CurrentVersion = 1;

    public int Version { get; set; }
    public bool Enable { get; set; } = true;
    public bool AffectSelf { get; set; } = true;
    public float SelfFlatHeightOffset { get; set; }
    public BoneHeartbeatSettings SelfBoneHeartbeat { get; set; } = new();

    public GrowthSettings SelfSettings { get; set; } = GrowthSettings.Defaults();
    public GrowthSettings PlayerSettings { get; set; } = GrowthSettings.Defaults();
    public GrowthSettings MonsterSettings { get; set; } = GrowthSettings.Defaults();

    // Players keep the existing Character Name@Home World identity format.
    public List<string> TrackedPlayerNames { get; set; } = new();
    // Monsters use case-insensitive portions of their displayed battle-NPC name.
    public List<string> TrackedMonsterNames { get; set; } = new();

    // Version-0 fields are retained so released 1.3.4.2 configurations can be
    // migrated without losing the user's existing growth behavior.
    public float Speed { get; set; } = 2.0f;
    public float MinScaleMultiplier { get; set; } = 0.1f;
    public float MaxScaleMultiplier { get; set; } = 1.0f;
    public float DeltaGrowthMultiplier { get; set; } = 1.0f;
    public bool LimitDeltaGrowth { get; set; }
    public float DeltaMaxScaleMultiplier { get; set; } = 5.0f;
    public float AmbientShrinkRate { get; set; } = 0.05f;
    public float OutOfCombatDecayMultiplier { get; set; } = 10.0f;
    public bool EnableDeltaHeightOffset { get; set; }
    public float DeltaHeightOffsetPerScale { get; set; } = 0.5f;
    public bool OnlyActiveInCombat { get; set; }
    public bool GrowFromDamage { get; set; }
    public bool GrowthFromDelta { get; set; }

    public bool Migrate()
    {
        if (Version >= CurrentVersion)
        {
            EnsureValid();
            return false;
        }

        var releasedSettings = new GrowthSettings
        {
            Speed = Speed,
            MinScaleMultiplier = MinScaleMultiplier,
            MaxScaleMultiplier = MaxScaleMultiplier,
            DeltaGrowthMultiplier = DeltaGrowthMultiplier,
            LimitDeltaGrowth = LimitDeltaGrowth,
            DeltaMaxScaleMultiplier = DeltaMaxScaleMultiplier,
            AmbientShrinkRate = AmbientShrinkRate,
            OutOfCombatDecayMultiplier = OutOfCombatDecayMultiplier,
            EnableDeltaHeightOffset = EnableDeltaHeightOffset,
            DeltaHeightOffsetPerScale = DeltaHeightOffsetPerScale,
            OnlyActiveInCombat = OnlyActiveInCombat,
            GrowFromDamage = GrowFromDamage,
            GrowthFromDelta = GrowthFromDelta,
        };
        releasedSettings.Validate();

        SelfSettings = GrowthSettings.CopyOf(releasedSettings);
        PlayerSettings = GrowthSettings.CopyOf(releasedSettings);
        MonsterSettings = GrowthSettings.CopyOf(releasedSettings);
        Version = CurrentVersion;
        EnsureValid();
        return true;
    }

    public bool IsMonsterTracked(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
        {
            return false;
        }

        string candidate = monsterName.Trim();
        foreach (string trackedName in TrackedMonsterNames)
        {
            if (!string.IsNullOrWhiteSpace(trackedName) &&
                candidate.Contains(
                    trackedName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void EnsureValid()
    {
        SelfFlatHeightOffset = float.IsFinite(SelfFlatHeightOffset)
            ? Math.Clamp(SelfFlatHeightOffset, 0f, 100f) : 0f;
        SelfBoneHeartbeat ??= new();
        SelfBoneHeartbeat.Validate();
        SelfSettings ??= GrowthSettings.Defaults();
        PlayerSettings ??= GrowthSettings.Defaults();
        MonsterSettings ??= GrowthSettings.Defaults();
        TrackedPlayerNames ??= new List<string>();
        TrackedMonsterNames ??= new List<string>();

        SelfSettings.Validate();
        PlayerSettings.Validate();
        MonsterSettings.Validate();
        NormalizeNames(TrackedPlayerNames);
        NormalizeNames(TrackedMonsterNames);
    }

    // A drain UI edit must not normalize active heartbeat/bone/growth state.
    public void SaveSizeDrain(SizeDrainSettings settings)
    {
        settings.Validate();
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    public void Save()
    {
        EnsureValid();
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    private static void NormalizeNames(List<string> names)
    {
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < names.Count;)
        {
            string name = names[index]?.Trim() ?? string.Empty;
            if (name.Length == 0 || !uniqueNames.Add(name))
            {
                names.RemoveAt(index);
                continue;
            }

            names[index] = name;
            index++;
        }
    }
}
