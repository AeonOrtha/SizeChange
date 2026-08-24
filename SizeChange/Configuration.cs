using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public class GrowthSettings
{
    public const string DefaultDeltaGrowthSoundPath =
        "sound/vfx/monster5/se_vfx_monster_inferno_lpowerrise_c.scd";

    public float Speed { get; set; } = 2.0f;
    public float MinScaleMultiplier { get; set; } = 0.1f;
    public float MaxScaleMultiplier { get; set; } = 1.0f;
    public float DeltaGrowthMultiplier { get; set; } = 1.0f;
    public bool LimitDeltaGrowth { get; set; }
    public float DeltaMaxScaleMultiplier { get; set; } = 5.0f;
    public float AccumulatorDelaySeconds { get; set; }
    public float AmbientShrinkRate { get; set; } = 0.05f;
    public float OutOfCombatDecayMultiplier { get; set; } = 10.0f;
    public bool EnableDeltaHeightOffset { get; set; }
    public float DeltaHeightOffsetPerScale { get; set; } = 0.5f;
    public bool EnableDeltaGrowthSound { get; set; }
    public string DeltaGrowthSoundPath { get; set; } = DefaultDeltaGrowthSoundPath;
    public int DeltaGrowthSoundIndex { get; set; }
    public float DeltaGrowthSoundVolume { get; set; } = 1.0f;
    public float DeltaGrowthSoundCooldownSeconds { get; set; } = 1.0f;
    public bool EnableDeltaGrowthVfx { get; set; }
    public string DeltaGrowthVfxPath { get; set; } = string.Empty;
    public float DeltaGrowthVfxDurationSeconds { get; set; } = 2.0f;
    public float DeltaGrowthVfxCooldownSeconds { get; set; } = 1.0f;
    public float DeltaGrowthVfxScale { get; set; } = 1.0f;
    public bool DeltaGrowthVfxScaleWithActor { get; set; } = true;
    public bool OnlyActiveInCombat { get; set; }
    public bool GrowFromDamage { get; set; }
    public bool GrowthFromDelta { get; set; }

    public void Validate()
    {
        Speed = Math.Clamp(Speed, 0.1f, 100f);
        MinScaleMultiplier = Math.Clamp(MinScaleMultiplier, 0.01f, 1f);
        MaxScaleMultiplier = Math.Max(1f, MaxScaleMultiplier);
        DeltaGrowthMultiplier = Math.Max(0f, DeltaGrowthMultiplier);
        DeltaMaxScaleMultiplier = Math.Max(1f, DeltaMaxScaleMultiplier);
        AccumulatorDelaySeconds = Math.Clamp(AccumulatorDelaySeconds, 0f, 60f);
        AmbientShrinkRate = Math.Max(0f, AmbientShrinkRate);
        OutOfCombatDecayMultiplier = Math.Max(1f, OutOfCombatDecayMultiplier);
        DeltaHeightOffsetPerScale = Math.Max(0f, DeltaHeightOffsetPerScale);
        DeltaGrowthSoundPath = string.IsNullOrWhiteSpace(DeltaGrowthSoundPath)
            ? DefaultDeltaGrowthSoundPath
            : DeltaGrowthSoundPath.Trim().Replace('\\', '/');
        DeltaGrowthSoundIndex = Math.Max(0, DeltaGrowthSoundIndex);
        DeltaGrowthSoundVolume = Math.Clamp(DeltaGrowthSoundVolume, 0f, 1f);
        DeltaGrowthSoundCooldownSeconds =
            Math.Clamp(DeltaGrowthSoundCooldownSeconds, 0f, 60f);
        DeltaGrowthVfxPath = DeltaGrowthVfxPath?.Trim().Replace('\\', '/')
            ?? string.Empty;
        DeltaGrowthVfxDurationSeconds =
            Math.Clamp(DeltaGrowthVfxDurationSeconds, 0.05f, 300f);
        DeltaGrowthVfxCooldownSeconds =
            Math.Clamp(DeltaGrowthVfxCooldownSeconds, 0f, 60f);
        DeltaGrowthVfxScale = Math.Clamp(DeltaGrowthVfxScale, 0.01f, 100f);

        if (GrowFromDamage && GrowthFromDelta)
        {
            GrowFromDamage = false;
        }
    }

    public static GrowthSettings Defaults() => new();

    public static GrowthSettings CopyOf(GrowthSettings settings)
        => new()
        {
            Speed = settings.Speed,
            MinScaleMultiplier = settings.MinScaleMultiplier,
            MaxScaleMultiplier = settings.MaxScaleMultiplier,
            DeltaGrowthMultiplier = settings.DeltaGrowthMultiplier,
            LimitDeltaGrowth = settings.LimitDeltaGrowth,
            DeltaMaxScaleMultiplier = settings.DeltaMaxScaleMultiplier,
            AccumulatorDelaySeconds = settings.AccumulatorDelaySeconds,
            AmbientShrinkRate = settings.AmbientShrinkRate,
            OutOfCombatDecayMultiplier = settings.OutOfCombatDecayMultiplier,
            EnableDeltaHeightOffset = settings.EnableDeltaHeightOffset,
            DeltaHeightOffsetPerScale = settings.DeltaHeightOffsetPerScale,
            EnableDeltaGrowthSound = settings.EnableDeltaGrowthSound,
            DeltaGrowthSoundPath = settings.DeltaGrowthSoundPath,
            DeltaGrowthSoundIndex = settings.DeltaGrowthSoundIndex,
            DeltaGrowthSoundVolume = settings.DeltaGrowthSoundVolume,
            DeltaGrowthSoundCooldownSeconds = settings.DeltaGrowthSoundCooldownSeconds,
            EnableDeltaGrowthVfx = settings.EnableDeltaGrowthVfx,
            DeltaGrowthVfxPath = settings.DeltaGrowthVfxPath,
            DeltaGrowthVfxDurationSeconds = settings.DeltaGrowthVfxDurationSeconds,
            DeltaGrowthVfxCooldownSeconds = settings.DeltaGrowthVfxCooldownSeconds,
            DeltaGrowthVfxScale = settings.DeltaGrowthVfxScale,
            DeltaGrowthVfxScaleWithActor = settings.DeltaGrowthVfxScaleWithActor,
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
