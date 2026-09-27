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
    private string heartbeatBoneInput = string.Empty;
    private string heartbeatInputError = string.Empty;

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

        DrawBoneHeartbeat();
        DrawGrowthSettings(configuration.SelfSettings, "self");
        if (ImGui.Button("Reset Self Settings"))
        {
            configuration.SelfSettings = GrowthSettings.Defaults();
            configuration.SelfBoneHeartbeat = new BoneHeartbeatSettings();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawBoneHeartbeat()
    {
        if (!ImGui.CollapsingHeader("Bone Heartbeat (Self)")) return;
        var settings = configuration.SelfBoneHeartbeat;
        bool enabled = settings.Enabled;
        if (ImGui.Checkbox("Enable Bone Heartbeat", ref enabled))
        {
            settings.Enabled = enabled;
            configuration.Save();
        }
        int mode = (int)settings.Mode;
        if (ImGui.Combo("Pulse When", ref mode, "While growth accumulates\0Always\0"))
        {
            settings.Mode = (BoneHeartbeatMode)mode;
            configuration.Save();
        }

        if (ImGui.Button("Refresh Profiles")) plugin.BoneHeartbeat.RefreshProfiles();
        ImGui.SameLine();
        if (ImGui.Button("Reload / Retry")) plugin.BoneHeartbeat.Retry();
        string profileLabel = settings.BaseProfileId == Guid.Empty
            ? "Active profile (Auto)" : settings.BaseProfileId.ToString();
        foreach (var profile in plugin.BoneHeartbeat.Profiles)
            if (profile.Id == settings.BaseProfileId) profileLabel = profile.Name;
        if (ImGui.BeginCombo("Base Profile", profileLabel))
        {
            if (ImGui.Selectable("Active profile (Auto)", settings.BaseProfileId == Guid.Empty))
            {
                settings.BaseProfileId = Guid.Empty;
                configuration.Save();
            }
            foreach (var profile in plugin.BoneHeartbeat.Profiles)
            {
                if (ImGui.Selectable($"{profile.Name}##{profile.Id}", settings.BaseProfileId == profile.Id))
                {
                    settings.BaseProfileId = profile.Id;
                    configuration.Save();
                }
            }
            ImGui.EndCombo();
        }

        float bpm = settings.BeatsPerMinute;
        if (ImGui.SliderFloat("Heartbeat BPM", ref bpm, 30f, 180f, "%.0f"))
        {
            settings.BeatsPerMinute = bpm;
            configuration.Save();
        }
        float strength = settings.Strength;
        if (ImGui.SliderFloat("Pulse Strength", ref strength, 0f, 5f, "%.2fx"))
        {
            settings.Strength = strength;
            configuration.Save();
        }

        DrawHeartbeatSound(settings.Sound);
        DrawHeartbeatChains(settings);
        if (ImGui.TreeNode("Custom Bones (Overrides)"))
        {
            ImGui.TextDisabled("JP bone names. Overrides presets.");
            bool add = ImGui.InputText("Bone Name", ref heartbeatBoneInput, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            add |= ImGui.Button("Add Bone");
            if (add)
            {
                string name = heartbeatBoneInput.Trim();
                if (name.Length == 0) heartbeatInputError = "Enter a JP bone name.";
                else if (name.Equals("n_root", StringComparison.OrdinalIgnoreCase))
                    heartbeatInputError = "n_root is reserved.";
                else if (settings.Bones.Exists(bone => bone.Name == name)) heartbeatInputError = "Bone already listed.";
                else
                {
                    settings.Bones.Add(new HeartbeatBone { Name = name });
                    heartbeatBoneInput = string.Empty;
                    heartbeatInputError = string.Empty;
                    configuration.Save();
                }
            }
            if (heartbeatInputError.Length > 0) ImGui.TextWrapped(heartbeatInputError);
            for (int i = 0; i < settings.Bones.Count; i++)
            {
                var bone = settings.Bones[i];
                ImGui.PushID($"heartbeat-{i}");
                float amount = bone.Strength;
                if (ImGui.DragFloat($"{bone.Name} — Additive Scale", ref amount, 0.005f, 0f, 5f, "%.3f"))
                {
                    bone.Strength = Math.Clamp(amount, 0f, 5f);
                    configuration.Save();
                }
                ImGui.SameLine();
                bool remove = ImGui.Button("Remove");
                ImGui.PopID();
                if (remove)
                {
                    settings.Bones.RemoveAt(i--);
                    configuration.Save();
                }
            }
            ImGui.TreePop();
        }
        ImGui.TextDisabled("Customize+ required. Self only.");
        ImGui.TextWrapped(plugin.BoneHeartbeat.Status);
        ImGui.Separator();
    }

    private void DrawHeartbeatSound(HeartbeatSoundSettings settings)
    {
        if (!ImGui.TreeNode("Pulse Sound")) return;
        bool enabled = settings.Enabled;
        if (ImGui.Checkbox("Enabled##pulse-sound", ref enabled))
        {
            settings.Enabled = enabled;
            configuration.Save();
        }
        string path = settings.Path;
        if (ImGui.InputText("SCD Path##pulse-sound", ref path, 256))
        {
            settings.Path = path;
            configuration.Save();
        }
        int index = settings.Index;
        if (ImGui.InputInt("SCD Index##pulse-sound", ref index))
        {
            settings.Index = Math.Max(0, index);
            configuration.Save();
        }
        float volume = settings.Volume;
        if (ImGui.SliderFloat("Volume##pulse-sound", ref volume, 0f, 1f, "%.2f"))
        {
            settings.Volume = volume;
            configuration.Save();
        }
        float fadeIn = settings.FadeInSeconds;
        if (ImGui.SliderFloat("Fade In (s)##pulse-sound", ref fadeIn, 0f, 10f, "%.2f"))
        {
            settings.FadeInSeconds = fadeIn;
            configuration.Save();
        }
        float fadeOut = settings.FadeOutSeconds;
        if (ImGui.SliderFloat("Fade Out (s)##pulse-sound", ref fadeOut, 0f, 10f, "%.2f"))
        {
            settings.FadeOutSeconds = fadeOut;
            configuration.Save();
        }
        ImGui.TextDisabled("Loops while pulsing.");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Looped SCDs are seamless. One-shots repeat.");
        if (ImGui.Button("Retry##pulse-sound")) plugin.HeartbeatSound.Retry();
        if (plugin.HeartbeatSound.Status.Length > 0) ImGui.TextWrapped(plugin.HeartbeatSound.Status);
        ImGui.TreePop();
    }

    private void DrawHeartbeatChains(BoneHeartbeatSettings settings)
    {
        int selected = settings.Chains.FindAll(chain => chain.Enabled).Count;
        if (ImGui.BeginCombo("Bone Chains", selected == 0 ? "Select chains..." : $"{selected} selected"))
        {
            foreach (var definition in HeartbeatChains.All)
            {
                var chain = settings.Chains.Find(x => x.Id == definition.Id);
                bool active = chain?.Enabled ?? false;
                if (ImGui.Checkbox(definition.Label, ref active))
                {
                    if (chain == null)
                    {
                        chain = new HeartbeatChainSettings { Id = definition.Id };
                        settings.Chains.Add(chain);
                    }
                    chain.Enabled = active;
                    configuration.Save();
                }
            }
            ImGui.EndCombo();
        }
        foreach (var definition in HeartbeatChains.All)
        {
            var chain = settings.Chains.Find(x => x.Id == definition.Id);
            if (chain == null || !chain.Enabled) continue;
            ImGui.PushID("heartbeat-chain-" + definition.Id);
            ImGui.Separator();
            ImGui.TextUnformatted(definition.Label);
            float amount = chain.Strength;
            if (ImGui.DragFloat("Additive Scale", ref amount, 0.005f, 0f, 5f, "%.3f"))
            {
                chain.Strength = Math.Clamp(amount, 0f, 5f);
                configuration.Save();
            }
            if (definition.Bones.Length > 1)
            {
                bool stagger = chain.Stagger;
                if (ImGui.Checkbox("Stagger", ref stagger))
                {
                    chain.Stagger = stagger;
                    configuration.Save();
                }
                if (chain.Stagger)
                {
                    float delay = chain.DelayPercent;
                    if (ImGui.SliderFloat("Delay per bone (% cycle)", ref delay, 0f, 10f, "%.1f%%"))
                    {
                        chain.DelayPercent = Math.Clamp(delay, 0f, 10f);
                        configuration.Save();
                    }
                    ImGui.TextWrapped($"{600f * chain.DelayPercent / settings.BeatsPerMinute:0.0} ms per step.");
                }
            }
            else ImGui.TextDisabled("Single bone.");
            if (ImGui.TreeNode("Bone Order"))
            {
                foreach (var bone in definition.Bones)
                {
                    var custom = settings.Bones.Find(x => x.Name == bone.Name);
                    string suffix = custom == null ? string.Empty : $" [Override: {custom.Strength:0.###}]";
                    ImGui.TextUnformatted($"Step {bone.Depth}: {bone.Name}{suffix}");
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
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
            ImGui.TextDisabled("No players added.");
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
        ImGui.TextDisabled("Partial names; case-insensitive.");

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
            ImGui.TextDisabled("No monsters added.");
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

        float maximumHealthLossPercent =
            settings.MaximumHealthLossRatioPerTrigger * 100f;
        if (ImGui.DragFloat(
                $"Maximum HP Loss Counted Per Trigger (%)##{id}",
                ref maximumHealthLossPercent,
                1.0f,
                0.00f,
                100.00f,
                "%.1f"))
        {
            settings.MaximumHealthLossRatioPerTrigger =
                Math.Clamp(maximumHealthLossPercent / 100f, 0f, 1f);
            configuration.Save();
        }
        ImGui.TextDisabled("Caps counted HP loss only.");

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

        float growthOvershootPercent = settings.GrowthOvershootPercent;
        if (ImGui.DragFloat(
                $"Growth Overshoot (% of New Growth)##{id}",
                ref growthOvershootPercent,
                1.0f,
                0.00f,
                500.00f,
                "%.1f"))
        {
            settings.GrowthOvershootPercent =
                Math.Clamp(growthOvershootPercent, 0f, 500f);
            configuration.Save();
        }

        if (settings.GrowthOvershootPercent > 0f)
        {
            float riseSeconds = settings.GrowthOvershootRiseSeconds;
            if (ImGui.DragFloat($"Overshoot Rise Time (Seconds)##{id}",
                    ref riseSeconds, 0.05f, 0.05f, 10f, "%.2f"))
            {
                settings.GrowthOvershootRiseSeconds = Math.Clamp(riseSeconds, 0.05f, 10f);
                configuration.Save();
            }

            float growthOvershootSettleSeconds =
                settings.GrowthOvershootSettleSeconds;
            if (ImGui.DragFloat(
                    $"Overshoot Settle Time (Seconds)##{id}",
                    ref growthOvershootSettleSeconds,
                    0.05f,
                    0.05f,
                    10.00f,
                    "%.2f"))
            {
                settings.GrowthOvershootSettleSeconds =
                    Math.Clamp(growthOvershootSettleSeconds, 0.05f, 10f);
                configuration.Save();
            }

            float riseCurve = settings.GrowthOvershootRiseCurve;
            if (ImGui.SliderFloat($"Rise Curve##{id}", ref riseCurve, -2f, 2f, "%.2f"))
            {
                settings.GrowthOvershootRiseCurve = riseCurve;
                configuration.Save();
            }
            float returnCurve = settings.GrowthOvershootReturnCurve;
            if (ImGui.SliderFloat($"Return Curve##{id}", ref returnCurve, -2f, 2f, "%.2f"))
            {
                settings.GrowthOvershootReturnCurve = returnCurve;
                configuration.Save();
            }
            ImGui.TextDisabled("Curve: negative earlier; positive later.");

            ImGui.TextDisabled("Peak can exceed cap.");
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
            ImGui.TextDisabled("Local animation. One-shot TMB only.");

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
