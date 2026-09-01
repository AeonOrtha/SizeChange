using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SizeChange.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private string trackedPlayerNameInput = string.Empty;
    private string trackedPlayerInputError = string.Empty;
    private string trackedMonsterNameInput = string.Empty;
    private string trackedMonsterInputError = string.Empty;
    private string growthSoundTestResult = string.Empty;
    private bool growthSoundTestSucceeded;
    private string growthVfxTestResult = string.Empty;
    private bool growthVfxTestSucceeded;
    private string growthAnimationTestResult = string.Empty;
    private bool growthAnimationTestSucceeded;

    public ConfigWindow(Plugin plugin) : base("SizeChange Config")
    {
        SizeCondition = ImGuiCond.Always;
        this.plugin = plugin;
        configuration = plugin.Configuration;
        configuration.EnsureValid();
    }

    public void Dispose() { }

    public override void Draw()
    {
        bool enabled = configuration.Enable;
        if (ImGui.Checkbox("Enable SizeChange", ref enabled))
        {
            configuration.Enable = enabled;
            configuration.Save();
        }

        if (ImGui.BeginTabBar("SizeChangeTargetProfiles"))
        {
            DrawSelfTab();
            DrawPlayerTab();
            DrawMonsterTab();
            ImGui.EndTabBar();
        }

        ImGui.Separator();
        ImGui.Text("This plugin is disabled in PvP.");
    }

    private void DrawSelfTab()
    {
        if (!ImGui.BeginTabItem("Your Character")) return;

        bool affectSelf = configuration.AffectSelf;
        string localPlayerName =
            Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "Not logged in";
        if (ImGui.Checkbox($"Enable for Self ({localPlayerName})", ref affectSelf))
        {
            configuration.AffectSelf = affectSelf;
            configuration.Save();
        }

        DrawGrowthSettings(configuration.SelfSettings, "self");
        if (ImGui.Button("Reset Self Settings"))
        {
            configuration.SelfSettings = GrowthSettings.Defaults();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawPlayerTab()
    {
        if (!ImGui.BeginTabItem("Added Players")) return;

        DrawTrackedPlayers();
        ImGui.Separator();
        DrawGrowthSettings(configuration.PlayerSettings, "players");
        if (ImGui.Button("Reset Player Settings"))
        {
            configuration.PlayerSettings = GrowthSettings.Defaults();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawMonsterTab()
    {
        if (!ImGui.BeginTabItem("Added Monsters")) return;

        DrawTrackedMonsters();
        ImGui.Separator();
        DrawGrowthSettings(configuration.MonsterSettings, "monsters");
        if (ImGui.Button("Reset Monster Settings"))
        {
            configuration.MonsterSettings = GrowthSettings.Defaults();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawTrackedPlayers()
    {
        ImGui.Text("Specific Players");
        ImGui.TextWrapped(
            "Add players as Character Name@Home World.");

        bool submitted = ImGui.InputText(
            "Character Name@Home World",
            ref trackedPlayerNameInput,
            64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Add##player") || submitted)
        {
            AddTrackedPlayer();
        }

        DrawInputError(trackedPlayerInputError);

        if (configuration.TrackedPlayerNames.Count == 0)
        {
            ImGui.TextDisabled("No additional players are enabled.");
            return;
        }

        for (int index = 0; index < configuration.TrackedPlayerNames.Count; index++)
        {
            ImGui.TextUnformatted(configuration.TrackedPlayerNames[index]);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##tracked-player-remove-{index}"))
            {
                configuration.TrackedPlayerNames.RemoveAt(index);
                configuration.Save();
                plugin.InvalidateTrackedActorCaches();
                index--;
            }
        }
    }

    private void DrawTrackedMonsters()
    {
        ImGui.Text("Specific Monsters");
        ImGui.TextWrapped(
            "Add any part of a monster's displayed name. Matching is case-insensitive, " +
            "so Behemoth also matches King Behemoth.");

        bool submitted = ImGui.InputText(
            "Monster Name Filter",
            ref trackedMonsterNameInput,
            128,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Add##monster") || submitted)
        {
            AddTrackedMonster();
        }

        DrawInputError(trackedMonsterInputError);

        if (configuration.TrackedMonsterNames.Count == 0)
        {
            ImGui.TextDisabled("No monsters are enabled.");
            return;
        }

        for (int index = 0; index < configuration.TrackedMonsterNames.Count; index++)
        {
            ImGui.TextUnformatted(configuration.TrackedMonsterNames[index]);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##tracked-monster-remove-{index}"))
            {
                configuration.TrackedMonsterNames.RemoveAt(index);
                configuration.Save();
                plugin.InvalidateTrackedActorCaches();
                index--;
            }
        }
    }

    private void AddTrackedPlayer()
    {
        string playerIdentity = trackedPlayerNameInput.Trim();
        if (playerIdentity.Length == 0)
        {
            trackedPlayerInputError = "Enter a player as Character Name@Home World.";
            return;
        }

        int separatorIndex = playerIdentity.LastIndexOf('@');
        bool validIdentity = separatorIndex > 0 &&
                             separatorIndex < playerIdentity.Length - 1;
        if (!validIdentity)
        {
            trackedPlayerInputError = "Use the format Character Name@Home World.";
            return;
        }

        bool alreadyTracked = configuration.TrackedPlayerNames.Exists(
            trackedPlayer => string.Equals(
                trackedPlayer,
                playerIdentity,
                StringComparison.OrdinalIgnoreCase));
        if (alreadyTracked)
        {
            trackedPlayerInputError = "That player is already in the list.";
            return;
        }

        configuration.TrackedPlayerNames.Add(playerIdentity);
        configuration.Save();
        plugin.InvalidateTrackedActorCaches();
        trackedPlayerInputError = string.Empty;
        trackedPlayerNameInput = string.Empty;
    }

    private void AddTrackedMonster()
    {
        string monsterName = trackedMonsterNameInput.Trim();
        if (monsterName.Length == 0)
        {
            trackedMonsterInputError = "Enter a monster name filter.";
            return;
        }

        bool alreadyTracked = configuration.TrackedMonsterNames.Exists(
            trackedMonsterName => string.Equals(
                trackedMonsterName,
                monsterName,
                StringComparison.OrdinalIgnoreCase));
        if (alreadyTracked)
        {
            trackedMonsterInputError = "That monster filter is already in the list.";
            return;
        }

        configuration.TrackedMonsterNames.Add(monsterName);
        configuration.Save();
        plugin.InvalidateTrackedActorCaches();
        trackedMonsterInputError = string.Empty;
        trackedMonsterNameInput = string.Empty;
    }

    private static void DrawInputError(string error)
    {
        if (error.Length == 0) return;

        ImGui.TextColored(
            new Vector4(1f, 0.35f, 0.35f, 1f),
            error);
    }

    private void DrawGrowthSettings(GrowthSettings settings, string id)
    {
        bool onlyActiveInCombat = settings.OnlyActiveInCombat;
        if (ImGui.Checkbox($"Only Active in Combat##{id}", ref onlyActiveInCombat))
        {
            settings.OnlyActiveInCombat = onlyActiveInCombat;
            configuration.Save();
        }

        bool growFromDamage = settings.GrowFromDamage;
        if (ImGui.Checkbox($"Grow From Damage##{id}", ref growFromDamage))
        {
            settings.GrowFromDamage = growFromDamage;
            if (growFromDamage)
            {
                settings.GrowthFromDelta = false;
            }

            configuration.Save();
        }

        bool growthFromDelta = settings.GrowthFromDelta;
        if (ImGui.Checkbox($"Growth From Delta##{id}", ref growthFromDelta))
        {
            settings.GrowthFromDelta = growthFromDelta;
            if (growthFromDelta)
            {
                settings.GrowFromDamage = false;
            }

            configuration.Save();
        }

        float speed = settings.Speed;
        if (ImGui.DragFloat($"Speed##{id}", ref speed, 0.1f, 0.1f, 100.0f))
        {
            settings.Speed = Math.Clamp(speed, 0.1f, 100f);
            configuration.Save();
        }

        float minScaleMultiplier = settings.MinScaleMultiplier;
        if (ImGui.DragFloat(
                $"Minimum Size Multiplier##{id}",
                ref minScaleMultiplier,
                0.01f,
                0.01f,
                1.00f))
        {
            settings.MinScaleMultiplier = Math.Clamp(minScaleMultiplier, 0.01f, 1f);
            configuration.Save();
        }

        if (settings.GrowFromDamage)
        {
            float maxScaleMultiplier = settings.MaxScaleMultiplier;
            if (ImGui.DragFloat(
                    $"Maximum Size Multiplier##{id}",
                    ref maxScaleMultiplier,
                    0.1f,
                    1.00f,
                    10.00f))
            {
                settings.MaxScaleMultiplier = Math.Max(1f, maxScaleMultiplier);
                configuration.Save();
            }
        }

        if (!settings.GrowthFromDelta) return;

        float deltaGrowthMultiplier = settings.DeltaGrowthMultiplier;
        if (ImGui.DragFloat(
                $"Damage Growth Multiplier##{id}",
                ref deltaGrowthMultiplier,
                0.1f,
                0.00f,
                10.00f))
        {
            settings.DeltaGrowthMultiplier = Math.Max(0f, deltaGrowthMultiplier);
            configuration.Save();
        }

        bool limitDeltaGrowth = settings.LimitDeltaGrowth;
        if (ImGui.Checkbox($"Limit Delta Growth##{id}", ref limitDeltaGrowth))
        {
            settings.LimitDeltaGrowth = limitDeltaGrowth;
            configuration.Save();
        }

        if (settings.LimitDeltaGrowth)
        {
            float deltaMaxScaleMultiplier = settings.DeltaMaxScaleMultiplier;
            if (ImGui.DragFloat(
                    $"Delta Maximum Size Multiplier##{id}",
                    ref deltaMaxScaleMultiplier,
                    0.1f,
                    1.00f,
                    100.00f))
            {
                settings.DeltaMaxScaleMultiplier = Math.Max(1f, deltaMaxScaleMultiplier);
                configuration.Save();
            }
        }

        float accumulatorDelaySeconds = settings.AccumulatorDelaySeconds;
        if (ImGui.DragFloat(
                $"Accumulator Delay (Seconds)##{id}",
                ref accumulatorDelaySeconds,
                0.1f,
                0.00f,
                60.00f,
                "%.1f"))
        {
            settings.AccumulatorDelaySeconds =
                Math.Clamp(accumulatorDelaySeconds, 0f, 60f);
            configuration.Save();
        }


        float ambientShrinkRate = settings.AmbientShrinkRate;
        if (ImGui.DragFloat(
                $"Ambient Shrink Per Second##{id}",
                ref ambientShrinkRate,
                0.01f,
                0.00f,
                10.00f))
        {
            settings.AmbientShrinkRate = Math.Max(0f, ambientShrinkRate);
            configuration.Save();
        }

        if (settings.OnlyActiveInCombat)
        {
            float outOfCombatDecayMultiplier = settings.OutOfCombatDecayMultiplier;
            if (ImGui.DragFloat(
                    $"Out of Combat Decay Multiplier##{id}",
                    ref outOfCombatDecayMultiplier,
                    0.5f,
                    1.00f,
                    100.00f))
            {
                settings.OutOfCombatDecayMultiplier = Math.Max(1f, outOfCombatDecayMultiplier);
                configuration.Save();
            }
        }

        bool enableDeltaHeightOffset = settings.EnableDeltaHeightOffset;
        if (ImGui.Checkbox($"Enable Growth Height Offset##{id}", ref enableDeltaHeightOffset))
        {
            settings.EnableDeltaHeightOffset = enableDeltaHeightOffset;
            configuration.Save();
        }

        if (settings.EnableDeltaHeightOffset)
        {
            float deltaHeightOffsetPerScale = settings.DeltaHeightOffsetPerScale;
            if (ImGui.DragFloat(
                    $"Height Offset Per Extra 1x##{id}",
                    ref deltaHeightOffsetPerScale,
                    0.01f,
                    0.00f,
                    5.00f))
            {
                settings.DeltaHeightOffsetPerScale = Math.Max(0f, deltaHeightOffsetPerScale);
                configuration.Save();
            }
        }

        bool enableDeltaGrowthSound = settings.EnableDeltaGrowthSound;
        if (ImGui.Checkbox($"Play Sound When Delta Growth Triggers##{id}", ref enableDeltaGrowthSound))
        {
            settings.EnableDeltaGrowthSound = enableDeltaGrowthSound;
            configuration.Save();
        }

        if (settings.EnableDeltaGrowthSound)
        {

            string deltaGrowthSoundPath = settings.DeltaGrowthSoundPath;
            if (ImGui.InputText(
                    $"Growth SCD Path##{id}",
                    ref deltaGrowthSoundPath,
                    256))
            {
                settings.DeltaGrowthSoundPath = deltaGrowthSoundPath;
                growthSoundTestResult = string.Empty;
                configuration.Save();
            }

            float deltaGrowthSoundVolume = settings.DeltaGrowthSoundVolume;
            if (ImGui.DragFloat(
                    $"Growth Sound Volume##{id}",
                    ref deltaGrowthSoundVolume,
                    0.01f,
                    0.00f,
                    1.00f))
            {
                settings.DeltaGrowthSoundVolume =
                    Math.Clamp(deltaGrowthSoundVolume, 0f, 1f);
                growthSoundTestResult = string.Empty;
                configuration.Save();
            }

            int deltaGrowthSoundIndex = settings.DeltaGrowthSoundIndex;
            if (ImGui.DragInt(
                    $"SCD Sound Index##{id}",
                    ref deltaGrowthSoundIndex,
                    1f,
                    0,
                    255))
            {
                settings.DeltaGrowthSoundIndex = Math.Max(0, deltaGrowthSoundIndex);
                growthSoundTestResult = string.Empty;
                configuration.Save();
            }

            float deltaGrowthSoundCooldown =
                settings.DeltaGrowthSoundCooldownSeconds;
            if (ImGui.DragFloat(
                    $"Minimum Seconds Between Sounds##{id}",
                    ref deltaGrowthSoundCooldown,
                    0.05f,
                    0.00f,
                    10.00f,
                    "%.2f"))
            {
                settings.DeltaGrowthSoundCooldownSeconds =
                    Math.Clamp(deltaGrowthSoundCooldown, 0f, 60f);
                configuration.Save();
            }


            if (ImGui.Button($"Test Sound at Yourself##{id}"))
            {
                growthSoundTestResult = plugin.TestDeltaGrowthSound(settings);
                growthSoundTestSucceeded =
                    growthSoundTestResult.StartsWith("Playback request accepted", StringComparison.Ordinal);
            }

            if (growthSoundTestResult.Length > 0)
            {
                ImGui.TextColored(
                    growthSoundTestSucceeded
                        ? new Vector4(0.35f, 1f, 0.45f, 1f)
                        : new Vector4(1f, 0.35f, 0.35f, 1f),
                    growthSoundTestResult);
            }
        }

        bool enableDeltaGrowthVfx = settings.EnableDeltaGrowthVfx;
        if (ImGui.Checkbox(
                $"Play Actor VFX When Delta Growth Triggers##{id}",
                ref enableDeltaGrowthVfx))
        {
            settings.EnableDeltaGrowthVfx = enableDeltaGrowthVfx;
            configuration.Save();
        }

        if (settings.EnableDeltaGrowthVfx)
        {

            string deltaGrowthVfxPath = settings.DeltaGrowthVfxPath;
            if (ImGui.InputText(
                    $"Growth AVFX Path##{id}",
                    ref deltaGrowthVfxPath,
                    256))
            {
                settings.DeltaGrowthVfxPath = deltaGrowthVfxPath;
                growthVfxTestResult = string.Empty;
                configuration.Save();
            }

            float deltaGrowthVfxDuration =
                settings.DeltaGrowthVfxDurationSeconds;
            if (ImGui.DragFloat(
                    $"VFX Removal Time (Seconds)##{id}",
                    ref deltaGrowthVfxDuration,
                    0.05f,
                    0.05f,
                    300.00f,
                    "%.2f"))
            {
                settings.DeltaGrowthVfxDurationSeconds =
                    Math.Clamp(deltaGrowthVfxDuration, 0.05f, 300f);
                configuration.Save();
            }

            float deltaGrowthVfxCooldown =
                settings.DeltaGrowthVfxCooldownSeconds;
            if (ImGui.DragFloat(
                    $"Minimum Seconds Between VFX##{id}",
                    ref deltaGrowthVfxCooldown,
                    0.05f,
                    0.00f,
                    60.00f,
                    "%.2f"))
            {
                settings.DeltaGrowthVfxCooldownSeconds =
                    Math.Clamp(deltaGrowthVfxCooldown, 0f, 60f);
                configuration.Save();
            }

            float deltaGrowthVfxScale = settings.DeltaGrowthVfxScale;
            if (ImGui.DragFloat(
                    $"VFX Scale##{id}",
                    ref deltaGrowthVfxScale,
                    0.05f,
                    0.01f,
                    100.00f,
                    "%.2f"))
            {
                settings.DeltaGrowthVfxScale =
                    Math.Clamp(deltaGrowthVfxScale, 0.01f, 100f);
                configuration.Save();
            }

            bool deltaGrowthVfxScaleWithActor =
                settings.DeltaGrowthVfxScaleWithActor;
            if (ImGui.Checkbox(
                    $"Scale VFX With Actor Growth##{id}",
                    ref deltaGrowthVfxScaleWithActor))
            {
                settings.DeltaGrowthVfxScaleWithActor =
                    deltaGrowthVfxScaleWithActor;
                configuration.Save();
            }


            if (ImGui.Button($"Test VFX at Yourself##{id}"))
            {
                growthVfxTestResult = plugin.TestDeltaGrowthVfx(settings);
                growthVfxTestSucceeded =
                    growthVfxTestResult.StartsWith(
                        "Actor-root VFX created",
                        StringComparison.Ordinal);
            }

            if (growthVfxTestResult.Length > 0)
            {
                ImGui.TextColored(
                    growthVfxTestSucceeded
                        ? new Vector4(0.35f, 1f, 0.45f, 1f)
                        : new Vector4(1f, 0.35f, 0.35f, 1f),
                    growthVfxTestResult);
            }
        }

        if (!string.Equals(id, "self", StringComparison.Ordinal)) return;

        bool enableDeltaGrowthAnimation = settings.EnableDeltaGrowthAnimation;
        if (ImGui.Checkbox(
                "Play Local Animation When Delta Growth Triggers##self",
                ref enableDeltaGrowthAnimation))
        {
            settings.EnableDeltaGrowthAnimation = enableDeltaGrowthAnimation;
            configuration.Save();
        }

        if (settings.EnableDeltaGrowthAnimation)
        {
            ImGui.TextWrapped(
                "Self only. This plays an ActionTimeline directly and never sends an " +
                "emote or chat command. Use a one-shot TMB path copied from VFXEditor; " +
                "looping timelines are not recommended.");

            string deltaGrowthAnimationPath = settings.DeltaGrowthAnimationTmbPath;
            if (ImGui.InputText(
                    "Growth Animation TMB Path##self",
                    ref deltaGrowthAnimationPath,
                    256))
            {
                settings.DeltaGrowthAnimationTmbPath = deltaGrowthAnimationPath;
                growthAnimationTestResult = string.Empty;
                configuration.Save();
            }

            float deltaGrowthAnimationCooldown =
                settings.DeltaGrowthAnimationCooldownSeconds;
            if (ImGui.DragFloat(
                    "Minimum Seconds Between Animations##self",
                    ref deltaGrowthAnimationCooldown,
                    0.05f,
                    0.00f,
                    60.00f,
                    "%.2f"))
            {
                settings.DeltaGrowthAnimationCooldownSeconds =
                    Math.Clamp(deltaGrowthAnimationCooldown, 0f, 60f);
                configuration.Save();
            }

            if (ImGui.Button("Test Animation on Yourself##self"))
            {
                growthAnimationTestResult =
                    plugin.TestDeltaGrowthAnimation(settings);
                growthAnimationTestSucceeded =
                    growthAnimationTestResult.StartsWith(
                        "Animation timeline",
                        StringComparison.Ordinal);
            }

            if (growthAnimationTestResult.Length > 0)
            {
                ImGui.TextColored(
                    growthAnimationTestSucceeded
                        ? new Vector4(0.35f, 1f, 0.45f, 1f)
                        : new Vector4(1f, 0.35f, 0.35f, 1f),
                    growthAnimationTestResult);
            }
        }
    }
}
