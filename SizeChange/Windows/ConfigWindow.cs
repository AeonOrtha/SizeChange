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
    private readonly System.Collections.Generic.Dictionary<string, string> drainNameInputs = new();

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

        DrawGrowthPreview(configuration.SelfSettings, SCActorGroup.Self);

        float flatHeightOffset = configuration.SelfFlatHeightOffset;
        if (ImGui.DragFloat("Flat Height Offset##self", ref flatHeightOffset, 0.01f, 0f, 100f, "%.3f"))
        {
            configuration.SelfFlatHeightOffset = flatHeightOffset;
            configuration.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds to Customize+ base-profile root Y. Requires Customize+. 0 = off.");

        DrawBoneHeartbeat();
        DrawGrowthSettings(configuration.SelfSettings, "self");
        if (ImGui.TreeNode("Transform Values##self"))
        {
            ImGui.TextWrapped(plugin.TransformDiagnostics(SCActorGroup.Self));
            if (ImGui.Button("Recheck Baseline##self")) plugin.RecheckBaselines(SCActorGroup.Self);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Resumes tracking from the current model. Keeps session originals and earned/pending growth.");
            ImGui.TreePop();
        }
        if (ImGui.Button("Reset Self Settings"))
        {
            configuration.SelfFlatHeightOffset = 0f;
            plugin.GetPreview(SCActorGroup.Self).Enabled = false;
            configuration.SelfSettings = GrowthSettings.Defaults();
            configuration.SelfBoneHeartbeat = new BoneHeartbeatSettings();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawGrowthPreview(GrowthSettings settings, SCActorGroup group)
    {
        ImGui.PushID("growth-preview-" + group);
        var preview = plugin.GetPreview(group);
        bool enabled = preview.Enabled;
        if (ImGui.Checkbox("Preview Growth", ref enabled)) preview.Enabled = enabled;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fake hits. Out of combat. Actual HP unchanged.");
        if (preview.Enabled)
        {
            float percent = settings.PreviewHitPercent;
            if (ImGui.SliderFloat("Fake Hit (% HP)", ref percent, 0f, 100f, "%.1f%%"))
            {
                settings.PreviewHitPercent = percent;
                configuration.Save();
            }
            float interval = settings.PreviewHitIntervalSeconds;
            if (ImGui.SliderFloat("Hit Interval (s)", ref interval, 0.1f, 10f, "%.2f"))
            {
                settings.PreviewHitIntervalSeconds = interval;
                configuration.Save();
            }
        }
        ImGui.PopID();
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
            settings.MaximumBeatsPerMinute = Math.Max(bpm, settings.MaximumBeatsPerMinute);
            configuration.Save();
        }
        bool damageBpm = settings.DamageDrivenBpm;
        if (ImGui.Checkbox("Damage BPM", ref damageBpm))
        {
            settings.DamageDrivenBpm = damageBpm;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pending damage raises BPM. Maximum HP Loss Counted Per Trigger sets the full-speed threshold.");
        if (settings.DamageDrivenBpm)
        {
            float maximumBpm = settings.MaximumBeatsPerMinute;
            if (ImGui.SliderFloat("Maximum BPM", ref maximumBpm, settings.BeatsPerMinute, 180f, "%.0f"))
            {
                settings.MaximumBeatsPerMinute = maximumBpm;
                configuration.Save();
            }
        }
        float strength = settings.Strength;
        if (ImGui.SliderFloat("Pulse Strength", ref strength, 0f, 5f, "%.2fx"))
        {
            settings.Strength = strength;
            configuration.Save();
        }

        DrawJawBreathing(settings.Jaw);
        DrawHeartbeatSound(settings.Sound);
        DrawHeartbeatChains(settings);
        DrawBoneGrowthChains(settings);
        if (ImGui.TreeNode("Custom Bones (Overrides)"))
        {
            bool customGrowth = settings.CustomGrowthEnabled;
            if (ImGui.Checkbox("Size-Driven Growth##custom-bones", ref customGrowth))
            {
                settings.CustomGrowthEnabled = customGrowth;
                configuration.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Shared growth for every custom bone. Replaces matching chain growth.");
            if (settings.CustomGrowthEnabled)
            {
                float rate = settings.CustomGrowthPerScale;
                if (ImGui.DragFloat("Growth per 1x##custom-bones", ref rate, 0.005f, 0f, 5f, "%.3f"))
                {
                    settings.CustomGrowthPerScale = Math.Clamp(rate, 0f, 5f);
                    configuration.Save();
                }
                float limit = settings.CustomGrowthLimit;
                if (ImGui.DragFloat("Growth Limit##custom-bones", ref limit, 0.005f, 0f, 5f, "%.3f"))
                {
                    settings.CustomGrowthLimit = Math.Clamp(limit, 0f, 5f);
                    configuration.Save();
                }
            }
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

    private void DrawJawBreathing(JawBreathingSettings settings)
    {
        if (!ImGui.TreeNode("Jaw Breathing")) return;
        bool enabled = settings.Enabled;
        if (ImGui.Checkbox("Enabled##jaw", ref enabled)) { settings.Enabled = enabled; configuration.Save(); }
        int mode = (int)settings.Mode;
        if (ImGui.Combo("When##jaw", ref mode, "Always\0During Growth\0"))
        { settings.Mode = (JawBreathingMode)mode; configuration.Save(); }
        float speed = settings.BreathsPerMinute;
        if (ImGui.SliderFloat("Motion Speed##jaw", ref speed, 2f, 60f, "%.1f /min"))
        { settings.BreathsPerMinute = speed; configuration.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Opening and closing speed, excluding the pause.");
        float holdOpen = settings.HoldOpenSeconds;
        if (ImGui.SliderFloat("Hold Open (s)##jaw", ref holdOpen, 0f, 10f, "%.2f"))
        { settings.HoldOpenSeconds = holdOpen; configuration.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Wait fully open before closing.");
        float pause = settings.PauseSeconds;
        if (ImGui.SliderFloat("Cycle Pause (s)##jaw", ref pause, 0f, 10f, "%.2f"))
        { settings.PauseSeconds = pause; configuration.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold the original closed position between cycles. Zero disables the pause.");
        float angle = settings.OpeningDegrees;
        if (ImGui.SliderFloat("Jaw Opening", ref angle, 0f, 45f, "%.1f deg"))
        { settings.OpeningDegrees = angle; configuration.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Opens along negative Z; returns to baseline.");
        ImGui.TreePop();
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
        bool sizeRate = settings.SizeDrivenRate;
        if (ImGui.Checkbox("Lower Pitch With Size", ref sizeRate))
        {
            settings.SizeDrivenRate = sizeRate;
            configuration.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Slows sound playback as settled size grows. Also lengthens the sound.");
        if (settings.SizeDrivenRate)
        {
            float drop = settings.RateDropPerScale;
            if (ImGui.SliderFloat("Rate Drop per 1x", ref drop, 0f, 1f, "%.2f"))
            { settings.RateDropPerScale = drop; configuration.Save(); }
            float minimum = settings.MinimumRate;
            if (ImGui.SliderFloat("Minimum Rate", ref minimum, 0.1f, 1f, "%.2fx"))
            { settings.MinimumRate = minimum; configuration.Save(); }
        }
        bool sizeVolume = settings.SizeDrivenVolume;
        if (ImGui.Checkbox("Louder With Size##pulse", ref sizeVolume))
        { settings.SizeDrivenVolume = sizeVolume; configuration.Save(); }
        if (settings.SizeDrivenVolume)
        {
            float gain = settings.VolumeGainPerScale;
            if (ImGui.SliderFloat("Volume Gain per 1x##pulse", ref gain, 0f, 20f, "%.2fx"))
            { settings.VolumeGainPerScale = gain; configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fraction of base volume added per extra 1x size. 0.25 adds 25%.");
            float maximum = settings.MaximumSizeVolume;
            if (ImGui.SliderFloat("Maximum Volume##pulse", ref maximum, 0f, 20f, "%.2fx"))
            { settings.MaximumSizeVolume = maximum; configuration.Save(); }
        }
        bool loop = settings.Loop;
        if (ImGui.Checkbox("Loop##pulse-sound", ref loop))
        {
            settings.Loop = loop;
            configuration.Save();
        }
        if (!settings.Loop)
        {
            DrawHeartbeatSoundSlot("First Beat", settings.FirstBeat);
            DrawHeartbeatSoundSlot("Second Beat", settings.SecondBeat);
        }
        else
        {
            // Keep the loop's persisted fields independent from both hit slots.
            var loopSlot = new HeartbeatSoundSlot { Path = settings.Path, Index = settings.Index, Volume = settings.Volume };
            if (DrawHeartbeatSoundSlot("Loop SCD", loopSlot, false))
            {
                settings.Path = loopSlot.Path;
                settings.Index = loopSlot.Index;
                settings.Volume = loopSlot.Volume;
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
        }
        if (ImGui.Button("Retry##pulse-sound")) plugin.HeartbeatSound.Retry();
        if (plugin.HeartbeatSound.Status.Length > 0) ImGui.TextWrapped(plugin.HeartbeatSound.Status);
        ImGui.TreePop();
    }

    private bool DrawHeartbeatSoundSlot(string label, HeartbeatSoundSlot slot, bool save = true)
    {
        ImGui.PushID(label);
        ImGui.TextUnformatted(label);
        bool changed = false;
        string path = slot.Path;
        if (ImGui.InputText("SCD Path", ref path, 256)) { slot.Path = path; changed = true; }
        int index = slot.Index;
        if (ImGui.InputInt("Index", ref index)) { slot.Index = Math.Max(0, index); changed = true; }
        float volume = slot.Volume;
        if (ImGui.SliderFloat("Volume", ref volume, 0f, HeartbeatSoundSlot.MaximumVolume, "%.2fx")) { slot.Volume = volume; changed = true; }
        if (changed && save) configuration.Save();
        ImGui.PopID();
        return changed;
    }

    private void DrawBoneGrowthChains(BoneHeartbeatSettings settings)
    {
        if (!ImGui.TreeNode("Size-Driven Bone Growth")) return;
        foreach (var definition in HeartbeatChains.All)
        {
            ImGui.PushID("growth-chain-" + definition.Id);
            var chain = settings.Chains.Find(x => x.Id == definition.Id);
            bool enabled = chain?.GrowthEnabled ?? false;
            if (ImGui.Checkbox(definition.Label, ref enabled))
            {
                if (chain == null)
                {
                    chain = new HeartbeatChainSettings { Id = definition.Id };
                    settings.Chains.Add(chain);
                }
                chain.GrowthEnabled = enabled;
                configuration.Save();
            }
            if (chain != null && chain.GrowthEnabled)
            {
                float rate = chain.GrowthPerScale;
                if (ImGui.DragFloat("Growth per 1x", ref rate, 0.005f, 0f, 5f, "%.3f"))
                {
                    chain.GrowthPerScale = Math.Clamp(rate, 0f, 5f);
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Added bone scale per extra 1x settled size. Excludes overshoot.");
                float limit = chain.GrowthLimit;
                if (ImGui.DragFloat("Growth Limit", ref limit, 0.005f, 0f, 5f, "%.3f"))
                {
                    chain.GrowthLimit = Math.Clamp(limit, 0f, 5f);
                    configuration.Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Maximum added scale per bone. Heartbeat pulses are added on top.");
            }
            ImGui.PopID();
        }
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
                    ImGui.TextWrapped($"{600f * chain.DelayPercent / settings.BeatsPerMinute:0.0} ms per step at base BPM.");
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

        DrawGrowthPreview(configuration.PlayerSettings, SCActorGroup.Player);
        DrawTrackedPlayers();
        ImGui.Separator();
        DrawGrowthSettings(configuration.PlayerSettings, "players");
        if (ImGui.TreeNode("Transform Values##players"))
        {
            ImGui.TextWrapped(plugin.TransformDiagnostics(SCActorGroup.Player));
            if (ImGui.Button("Recheck Baseline##players")) plugin.RecheckBaselines(SCActorGroup.Player);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Resumes tracking from the current model. Keeps session originals and earned/pending growth.");
            ImGui.TreePop();
        }
        if (ImGui.Button("Reset Player Settings"))
        {
            plugin.GetPreview(SCActorGroup.Player).Enabled = false;
            configuration.PlayerSettings = GrowthSettings.Defaults();
            configuration.Save();
        }

        ImGui.EndTabItem();
    }

    private void DrawMonsterTab()
    {
        if (!ImGui.BeginTabItem("Added Monsters")) return;

        DrawGrowthPreview(configuration.MonsterSettings, SCActorGroup.Monster);
        DrawTrackedMonsters();
        ImGui.Separator();
        DrawGrowthSettings(configuration.MonsterSettings, "monsters");
        if (ImGui.TreeNode("Transform Values##monsters"))
        {
            ImGui.TextWrapped(plugin.TransformDiagnostics(SCActorGroup.Monster));
            if (ImGui.Button("Recheck Baseline##monsters")) plugin.RecheckBaselines(SCActorGroup.Monster);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Resumes tracking from the current model. Keeps session originals and earned/pending growth.");
            ImGui.TreePop();
        }
        if (ImGui.Button("Reset Monster Settings"))
        {
            plugin.GetPreview(SCActorGroup.Monster).Enabled = false;
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

    private void DrawSizeDrain(SizeDrainSettings settings, string id)
    {
        ImGui.PushID($"SizeDrain{id}");
        bool enabled = settings.Enabled;
        if (ImGui.Checkbox("Size Drain", ref enabled))
        { settings.Enabled = enabled; configuration.SaveSizeDrain(settings); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Nearby sources grant growth once per second, even outside combat and beyond the size limit. Sources do not shrink.");
        if (enabled && ImGui.TreeNode("Size Drain Settings"))
        {
            bool everyone = settings.Everyone;
            if (ImGui.Checkbox("Everyone Near Me", ref everyone))
            { settings.Everyone = everyone; configuration.SaveSizeDrain(settings); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("All loaded, living players and NPCs in range of each receiving character, excluding itself. Includes companions and retainers. Overrides the name list while enabled.");
            if (!everyone)
            {
                string input = drainNameInputs.TryGetValue(id, out var saved) ? saved : string.Empty;
                if (ImGui.InputText("Monster / NPC Name", ref input, 128)) drainNameInputs[id] = input;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Part of an NPC name, ignoring case. Behemoth matches any name containing behemoth.");
                ImGui.SameLine();
                if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(input))
                {
                    string name = input.Trim();
                    if (!settings.Names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) settings.Names.Add(name);
                    drainNameInputs[id] = string.Empty;
                    configuration.SaveSizeDrain(settings);
                }
                for (int i = 0; i < settings.Names.Count; i++)
                {
                    ImGui.PushID(i);
                    if (ImGui.SmallButton("Remove"))
                    { settings.Names.RemoveAt(i--); configuration.SaveSizeDrain(settings); ImGui.PopID(); continue; }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(settings.Names[i]);
                    ImGui.PopID();
                }
            }
            float range = settings.Range;
            if (ImGui.DragFloat("Range", ref range, 0.1f, 0.1f, 100f, "%.1f"))
            { settings.Range = Math.Clamp(range, 0.1f, 100f); configuration.SaveSizeDrain(settings); }
            float peak = settings.PeakHitPercent;
            if (ImGui.DragFloat("Peak Hit", ref peak, 0.01f, 0f, 100f, "%.2f%% HP"))
            { settings.PeakHitPercent = Math.Clamp(peak, 0f, 100f); configuration.SaveSizeDrain(settings); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Per-source maximum at zero distance, falling smoothly to zero at range. Sources stack. Uses the profile's damage multiplier, HP allowance and accumulator delay.");
            // Keep these controls in a stable layout before and after enabling.
            string receiverPath = settings.ReceiverVfxPath ?? string.Empty;
            if (ImGui.InputText("Receiver AVFX", ref receiverPath, 512))
            { settings.ReceiverVfxPath = receiverPath; configuration.SaveSizeDrain(settings); }
            bool receiver = settings.ReceiverVfxEnabled;
            bool receiverMissing = string.IsNullOrWhiteSpace(receiverPath);
            ImGui.BeginDisabled(receiverMissing && !receiver);
            if (ImGui.Checkbox("Receiver VFX", ref receiver))
            { settings.ReceiverVfxEnabled = receiver; configuration.SaveSizeDrain(settings); }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(receiverMissing ? "Enter an AVFX path first." : "Looping effect on the growing character.");

            string sourcePath = settings.SourceVfxPath ?? string.Empty;
            if (ImGui.InputText("Target AVFX", ref sourcePath, 512))
            { settings.SourceVfxPath = sourcePath; configuration.SaveSizeDrain(settings); }
            bool source = settings.SourceVfxEnabled;
            bool sourceMissing = string.IsNullOrWhiteSpace(sourcePath);
            ImGui.BeginDisabled(sourceMissing && !source);
            if (ImGui.Checkbox("Target VFX", ref source))
            { settings.SourceVfxEnabled = source; configuration.SaveSizeDrain(settings); }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(sourceMissing ? "Enter an AVFX path first." : "Effect on each contributing source. Shared sources use Self's settings first.");

            float fadeIn = settings.FadeIn, fadeOut = settings.FadeOut;
            if (ImGui.DragFloat("Fade In", ref fadeIn, 0.05f, 0f, 10f, "%.2fs"))
            { settings.FadeIn = Math.Clamp(fadeIn, 0f, 10f); configuration.SaveSizeDrain(settings); }
            if (ImGui.DragFloat("Fade Out", ref fadeOut, 0.05f, 0f, 10f, "%.2fs"))
            { settings.FadeOut = Math.Clamp(fadeOut, 0f, 10f); configuration.SaveSizeDrain(settings); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Visible fading depends on the AVFX asset.");
            ImGui.TreePop();
        }
        ImGui.PopID();
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

        DrawSizeDrain(settings.SizeDrain, id);

        bool aetherGrowth = settings.AetherProximityGrowth;
        if (ImGui.Checkbox($"Aetheryte Proximity Growth##{id}", ref aetherGrowth))
        { settings.AetherProximityGrowth = aetherGrowth; configuration.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Each crystal hits once per second. Damage rises smoothly as you approach. Overlaps stack; crystal growth bypasses the size limit, including outside combat.");
        if (settings.AetherProximityGrowth)
        {
            float range = settings.AetherProximityRange;
            if (ImGui.DragFloat($"Aetheryte Range##{id}", ref range, 0.1f, 0.1f, 100f, "%.1f"))
            { settings.AetherProximityRange = Math.Clamp(range, 0.1f, 100f); configuration.Save(); }

            float shardHit = settings.AetherShardHitPercent;
            if (ImGui.DragFloat($"Shard Peak Hit##{id}", ref shardHit, 0.01f, 0f, 100f, "%.2f%% HP"))
            { settings.AetherShardHitPercent = Math.Clamp(shardHit, 0f, 100f); configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Maximum at the source; fades to zero at range. Includes housing crystals. Per-hit HP allowance still applies.");
            float largeHit = settings.AetherLargeHitPercent;
            if (ImGui.DragFloat($"Large Peak Hit##{id}", ref largeHit, 0.01f, 0f, 100f, "%.2f%% HP"))
            { settings.AetherLargeHitPercent = Math.Clamp(largeHit, 0f, 100f); configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Peak HP-equivalent damage from a full-size aetheryte. Per-hit HP allowance still applies.");

            ImGui.PushID($"AetherAudio{id}");
            bool actorSound = settings.AetherActorSound.Enabled;
            if (ImGui.Checkbox("Proximity Sound", ref actorSound))
            { settings.AetherActorSound.Enabled = actorSound; configuration.Save(); }
            if (actorSound) DrawHeartbeatSoundSlot("Proximity Loop", settings.AetherActorSound.Sound);
            bool sourceSound = settings.AetherSourceSound.Enabled;
            if (ImGui.Checkbox("Crystal Sound", ref sourceSound))
            { settings.AetherSourceSound.Enabled = sourceSound; configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("One positional loop at each contributing crystal. Shared crystals use Self's settings first.");
            if (sourceSound) DrawHeartbeatSoundSlot("Crystal Loop", settings.AetherSourceSound.Sound);
            ImGui.PopID();

            bool actorVfx = settings.AetherActorVfxEnabled;
            if (ImGui.Checkbox($"Proximity VFX##{id}", ref actorVfx))
            { settings.AetherActorVfxEnabled = actorVfx; configuration.Save(); }
            if (actorVfx)
            {
                float fadeIn = settings.AetherActorVfxFadeIn;
                if (ImGui.DragFloat($"Proximity Fade In##{id}", ref fadeIn, 0.05f, 0f, 10f, "%.2fs"))
                { settings.AetherActorVfxFadeIn = Math.Clamp(fadeIn, 0f, 10f); configuration.Save(); }
                float fadeOut = settings.AetherActorVfxFadeOut;
                if (ImGui.DragFloat($"Proximity Fade Out##{id}", ref fadeOut, 0.05f, 0f, 10f, "%.2fs"))
                { settings.AetherActorVfxFadeOut = Math.Clamp(fadeOut, 0f, 10f); configuration.Save(); }
                string path = settings.AetherActorVfxPath;
                if (ImGui.InputText($"Proximity AVFX##{id}", ref path, 512))
                { settings.AetherActorVfxPath = path; configuration.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Character effect while affected. Use a looping game .avfx path. Separate from growth VFX.");
            }
            bool sourceVfx = settings.AetherSourceVfxEnabled;
            if (ImGui.Checkbox($"Crystal VFX##{id}", ref sourceVfx))
            { settings.AetherSourceVfxEnabled = sourceVfx; configuration.Save(); }
            if (sourceVfx)
            {
                float fadeIn = settings.AetherSourceVfxFadeIn;
                if (ImGui.DragFloat($"Crystal Fade In##{id}", ref fadeIn, 0.05f, 0f, 10f, "%.2fs"))
                { settings.AetherSourceVfxFadeIn = Math.Clamp(fadeIn, 0f, 10f); configuration.Save(); }
                float fadeOut = settings.AetherSourceVfxFadeOut;
                if (ImGui.DragFloat($"Crystal Fade Out##{id}", ref fadeOut, 0.05f, 0f, 10f, "%.2fs"))
                { settings.AetherSourceVfxFadeOut = Math.Clamp(fadeOut, 0f, 10f); configuration.Save(); }
                string path = settings.AetherSourceVfxPath;
                if (ImGui.InputText($"Crystal AVFX##{id}", ref path, 512))
                { settings.AetherSourceVfxPath = path; configuration.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use a looping .avfx. Shared crystals show one effect; Self takes priority.");
                bool attached = settings.AetherSourceVfxAttached;
                if (ImGui.Checkbox($"Attach to Crystal##{id}", ref attached))
                { settings.AetherSourceVfxAttached = attached; configuration.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Target-bound playback. Disable for world-space AVFX. Character-only bone bindings may not work on crystals.");
                float crystalScale = settings.AetherSourceVfxScale;
                if (ImGui.DragFloat($"Crystal VFX Scale##{id}", ref crystalScale, 0.05f, 0.01f, 100f, "%.2fx"))
                { settings.AetherSourceVfxScale = Math.Clamp(crystalScale, 0.01f, 100f); configuration.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Major aetherytes use twice this VFX scale. Shards use this value.");
                if (!attached)
                {
                    float height = settings.AetherSourceVfxHeight;
                    if (ImGui.DragFloat($"Crystal VFX Height##{id}", ref height, 0.1f, -100f, 100f, "%.1f"))
                    { settings.AetherSourceVfxHeight = Math.Clamp(height, -100f, 100f); configuration.Save(); }
                }
                ImGui.TextDisabled(plugin.CrystalVfxStatus(settings));
            }
        }

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

        bool scaleGrowthWithSize = settings.ScaleGrowthWithSize;
        if (ImGui.Checkbox($"Scale Growth With Size##{id}", ref scaleGrowthWithSize))
        {
            settings.ScaleGrowthWithSize = scaleGrowthWithSize;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Compound growth from settled size. Overshoot is excluded.");

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
            float smallBoost = settings.GrowthOvershootSmallBoost;
            if (ImGui.DragFloat($"Small Growth Boost##{id}", ref smallBoost, 0.1f, 1f, 100f, "%.1fx"))
            {
                settings.GrowthOvershootSmallBoost = Math.Clamp(smallBoost, 1f, 100f);
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Extra overshoot for small releases. 1x disables the boost.");
            float boostRange = settings.GrowthOvershootBoostRangePercent;
            if (ImGui.DragFloat($"Boost Range (%)##{id}", ref boostRange, 0.1f, 0.01f, 100f, "%.2f"))
            {
                settings.GrowthOvershootBoostRangePercent = Math.Clamp(boostRange, 0.01f, 100f);
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("New growth as % of the growth reference size where the extra boost is halved. Higher extends the boost to larger releases.");

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
                    20.00f))
            {
                settings.DeltaGrowthSoundVolume =
                    Math.Clamp(deltaGrowthSoundVolume, 0f, 20f);
                growthSoundTestResult = string.Empty;
                configuration.Save();
            }

            bool sizeVolume = settings.DeltaGrowthSoundSizeDrivenVolume;
            if (ImGui.Checkbox($"Louder With Size##growth-sound-{id}", ref sizeVolume))
            { settings.DeltaGrowthSoundSizeDrivenVolume = sizeVolume; configuration.Save(); }
            if (settings.DeltaGrowthSoundSizeDrivenVolume)
            {
                float gain = settings.DeltaGrowthSoundVolumeGainPerScale;
                if (ImGui.SliderFloat($"Volume Gain per 1x##growth-sound-{id}", ref gain, 0f, 20f, "%.2fx"))
                { settings.DeltaGrowthSoundVolumeGainPerScale = gain; configuration.Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fraction of base volume added per extra 1x size. 0.25 adds 25%.");
                float maximum = settings.DeltaGrowthSoundMaximumVolume;
                if (ImGui.SliderFloat($"Maximum Volume##growth-sound-{id}", ref maximum, 0f, 20f, "%.2fx"))
                { settings.DeltaGrowthSoundMaximumVolume = maximum; configuration.Save(); }
            }
            bool sizeRate = settings.DeltaGrowthSoundSizeDrivenRate;
            if (ImGui.Checkbox($"Lower Pitch With Size##growth-sound-{id}", ref sizeRate))
            { settings.DeltaGrowthSoundSizeDrivenRate = sizeRate; configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Slows the growth SCD at its starting settled size. Also lengthens the sound.");
            if (settings.DeltaGrowthSoundSizeDrivenRate)
            {
                float drop = settings.DeltaGrowthSoundRateDropPerScale;
                if (ImGui.SliderFloat($"Rate Drop per 1x##growth-sound-{id}", ref drop, 0f, 1f, "%.2f"))
                { settings.DeltaGrowthSoundRateDropPerScale = drop; configuration.Save(); }
                float minimum = settings.DeltaGrowthSoundMinimumRate;
                if (ImGui.SliderFloat($"Minimum Rate##growth-sound-{id}", ref minimum, 0.1f, 1f, "%.2fx"))
                { settings.DeltaGrowthSoundMinimumRate = minimum; configuration.Save(); }
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

            float chance = settings.DeltaGrowthAnimationChancePercent;
            if (ImGui.SliderFloat("Chance per Pulse##self", ref chance, 0f, 100f, "%.1f%%"))
            { settings.DeltaGrowthAnimationChancePercent = chance; configuration.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Roll once per growth pulse after the cooldown. A failed roll does nothing. Test always plays.");

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
