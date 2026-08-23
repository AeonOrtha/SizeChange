using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Extensions;
using SizeChange.Windows;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace SizeChange;

enum SCActorGroup
{
    Self,
    Player,
    Monster,
}

struct SCCharacterState
{
    public nint ActorAddress;
    public SCActorGroup ActorGroup;
    public float PlayerScale;
    public float PreviousScale;
    public float PreviousHealth;
    public float GrowthMultiplier;
    public bool HasPreviousHealth;
    public float BaseDrawOffsetY;
    public float LastAppliedDrawOffsetY;
    public bool HasDrawOffset;
}

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName_SizeChange = "/sizechange";
    private const string CommandName_Scale = "/scale";
    private const string Parameter_Enable = "enable";
    private const string Parameter_Disable = "disable";
    private const float TrackedActorRefreshIntervalSeconds = 1.0f;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SizeChange");
    private ConfigWindow ConfigWindow { get; init; }
    private readonly Dictionary<uint, SCCharacterState> CharacterIdToLastScaleMap = new();
    private readonly Dictionary<string, uint> TrackedPlayerEntityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> TrackedMonsterEntityIds = new();
    private float TrackedActorRefreshElapsed = TrackedActorRefreshIntervalSeconds;
    private bool TrackedActorRefreshRequested = true;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName_SizeChange, new CommandInfo(OnCommand)
        {
            HelpMessage = "opens the SizeChange config window"
        });

        CommandManager.AddHandler(CommandName_Scale, new CommandInfo(OnCommand)
        {
            HelpMessage = "sets character's scale"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
        Framework.Update += OnFrameworkUpdate;
    }

    public unsafe void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        RestoreCharacterTransforms();

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName_SizeChange);
        CommandManager.RemoveHandler(CommandName_Scale);
    }

    private unsafe void RestoreCharacterTransforms()
    {
        foreach (var stateEntry in CharacterIdToLastScaleMap)
        {
            var gameObject = ObjectTable.SearchByEntityId(stateEntry.Key);
            if (gameObject is not IBattleChara ||
                gameObject.Address != stateEntry.Value.ActorAddress)
            {
                continue;
            }

            var actor = (Character*)gameObject.Address;
            var draw = (CharacterBase*)actor->DrawObject;
            if (draw != null && stateEntry.Value.PlayerScale > 0f)
            {
                float baseScale = stateEntry.Value.PlayerScale;
                draw->Scale = new Vector3(baseScale, baseScale, baseScale);
                actor->Scale = baseScale;
            }

            if (stateEntry.Value.HasDrawOffset)
            {
                var currentOffset = actor->GameObject.DrawOffset;
                actor->GameObject.SetDrawOffset(
                    currentOffset.X,
                    stateEntry.Value.BaseDrawOffsetY,
                    currentOffset.Z);
            }
        }
    }

    private unsafe void OnCommand(string command, string args)
    {
        if (command == CommandName_SizeChange)
        {
            if (args == string.Empty)
            {
                ConfigWindow.Toggle();
            }
            else if (args == Parameter_Enable)
            {
                Configuration.Enable = true;
                Configuration.Save();
            }
            else if (args == Parameter_Disable)
            {
                Configuration.Enable = false;
                Configuration.Save();
            }
        }

        if (command == CommandName_Scale && float.TryParse(args, out float scale))
        {
            var player = ObjectTable.LocalPlayer;
            if (player != null)
            {
                UpdateScale((Character*)player.Address, scale);
            }
        }
    }

    private unsafe void UpdateScale(Character* actor, float scale)
    {
        SCCharacterState charState;
        if (TryGetCharacterState(actor, out var existingState))
        {
            charState = existingState;
        }
        else
        {
            charState = new SCCharacterState
            {
                ActorAddress = (nint)actor,
                ActorGroup = SCActorGroup.Self,
                GrowthMultiplier = 1.0f
            };
        }

        var draw = (CharacterBase*)actor->DrawObject;
        if (draw == null) return;

        charState.PlayerScale = scale;
        charState.PreviousScale = draw->Scale.Y;
        CharacterIdToLastScaleMap[actor->EntityId] = charState;
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        bool globallyDisabled = ClientState.IsPvP || !Configuration.Enable;
        bool inCombat = Condition[ConditionFlag.InCombat];
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        float deltaSeconds = (float)Framework.UpdateDelta.TotalSeconds;
        TrackedActorRefreshElapsed += deltaSeconds;
        if (TrackedActorRefreshRequested ||
            TrackedActorRefreshElapsed >= TrackedActorRefreshIntervalSeconds)
        {
            RefreshTrackedActorCaches();
        }

        var processedEntityIds = new HashSet<uint>();
        var localActor = (Character*)localPlayer.Address;
        ProcessSelectedActor(
            localActor,
            Configuration.AffectSelf,
            SCActorGroup.Self,
            Configuration.SelfSettings,
            globallyDisabled,
            inCombat);
        processedEntityIds.Add(localActor->EntityId);

        foreach (var trackedPlayer in TrackedPlayerEntityIds)
        {
            var gameObject = ObjectTable.SearchByEntityId(trackedPlayer.Value);
            if (gameObject is not IPlayerCharacter playerCharacter ||
                !string.Equals(
                    GetPlayerIdentity(playerCharacter),
                    trackedPlayer.Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                TrackedActorRefreshRequested = true;
                continue;
            }

            var actor = (Character*)playerCharacter.Address;
            ProcessSelectedActor(
                actor,
                true,
                SCActorGroup.Player,
                Configuration.PlayerSettings,
                globallyDisabled,
                inCombat);
            processedEntityIds.Add(actor->EntityId);
        }

        foreach (uint entityId in TrackedMonsterEntityIds)
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (gameObject is not IBattleNpc ||
                !Configuration.IsMonsterTracked(gameObject.Name.TextValue))
            {
                TrackedActorRefreshRequested = true;
                continue;
            }

            var actor = (Character*)gameObject.Address;
            ProcessSelectedActor(
                actor,
                true,
                SCActorGroup.Monster,
                Configuration.MonsterSettings,
                globallyDisabled,
                inCombat);
            processedEntityIds.Add(actor->EntityId);
        }

        // Return actors removed from either saved list, and discard state for
        // actors that have despawned or whose entity ID has been reused.
        var previousStates = new List<KeyValuePair<uint, SCCharacterState>>(
            CharacterIdToLastScaleMap);
        foreach (var stateEntry in previousStates)
        {
            if (processedEntityIds.Contains(stateEntry.Key))
            {
                continue;
            }

            var gameObject = ObjectTable.SearchByEntityId(stateEntry.Key);
            if (gameObject is not IBattleChara ||
                gameObject.Address != stateEntry.Value.ActorAddress)
            {
                CharacterIdToLastScaleMap.Remove(stateEntry.Key);
                continue;
            }

            ProcessSelectedActor(
                (Character*)gameObject.Address,
                false,
                stateEntry.Value.ActorGroup,
                GetSettings(stateEntry.Value.ActorGroup),
                globallyDisabled,
                inCombat);
        }
    }

    private unsafe void ProcessSelectedActor(
        Character* actor,
        bool isSelected,
        SCActorGroup actorGroup,
        GrowthSettings settings,
        bool globallyDisabled,
        bool inCombat)
    {
        if (actor == null ||
            (!isSelected && !TryGetCharacterState(actor, out _)))
        {
            return;
        }

        bool outOfCombat = settings.OnlyActiveInCombat && !inCombat;
        AdjustScale(
            actor,
            actorGroup,
            settings,
            globallyDisabled || !isSelected,
            outOfCombat);

        if (!isSelected)
        {
            RemoveStateAfterReturn(actor);
        }
    }

    // Finds the actor's health and shield value and uses it to adjust the model's scale.
    private unsafe void AdjustScale(
        Character* actor,
        SCActorGroup actorGroup,
        GrowthSettings settings,
        bool disable,
        bool outOfCombat)
    {
        if (actor == null) return;

        float maxhp = actor->MaxHealth;
        if (maxhp <= 0f) return;

        float shield = (actor->ShieldValue / 100f) * maxhp;
        float health = actor->Health + shield;
        float hpRatio = health / maxhp;

        var draw = (CharacterBase*)actor->DrawObject;
        if (draw == null) return;

        float scale = draw->Scale.Y;
        float previousScale = scale;
        SCCharacterState charState;
        if (TryGetCharacterState(actor, out var existingState))
        {
            charState = existingState;
            previousScale = charState.PreviousScale;
        }
        else
        {
            charState = new SCCharacterState
            {
                ActorAddress = (nint)actor,
                ActorGroup = actorGroup,
                // Preserve the scale supplied by the game or another appearance
                // plugin as this actor's individual multiplicative base.
                PlayerScale = scale,
                PreviousScale = scale,
                GrowthMultiplier = 1.0f
            };
        }

        charState.ActorGroup = actorGroup;

        if (!settings.GrowthFromDelta &&
            MathF.Abs(previousScale - scale) > 0.0001f)
        {
            charState.PlayerScale = scale;
        }

        if (!charState.HasPreviousHealth)
        {
            charState.PreviousHealth = health;
            charState.GrowthMultiplier = Math.Max(1.0f, charState.GrowthMultiplier);
            charState.HasPreviousHealth = true;
        }

        if (settings.GrowthFromDelta && !disable && !outOfCombat)
        {
            // Only add growth while the effect is active. Health is sampled while
            // inactive so damage cannot be applied retroactively later.
            float healthLost = charState.PreviousHealth - health;
            if (healthLost > 0f)
            {
                float healthLostRatio = healthLost / maxhp;
                charState.GrowthMultiplier +=
                    healthLostRatio * settings.DeltaGrowthMultiplier;
            }
        }

        if (settings.GrowthFromDelta && settings.LimitDeltaGrowth)
        {
            charState.GrowthMultiplier = Math.Min(
                charState.GrowthMultiplier,
                settings.DeltaMaxScaleMultiplier);
        }

        // Ambient decay is exclusive to Growth From Delta.
        if (settings.GrowthFromDelta && charState.GrowthMultiplier > 1.0f)
        {
            float deltaSeconds = (float)Framework.UpdateDelta.TotalSeconds;
            float decayMultiplier =
                outOfCombat && !disable
                    ? settings.OutOfCombatDecayMultiplier
                    : 1.0f;
            float shrinkAmount =
                settings.AmbientShrinkRate *
                decayMultiplier *
                deltaSeconds;
            charState.GrowthMultiplier = Math.Max(
                1.0f,
                charState.GrowthMultiplier - shrinkAmount);
        }

        float targetScale = disable
            ? charState.PlayerScale
            : settings.GrowthFromDelta
                ? charState.PlayerScale * charState.GrowthMultiplier
                : outOfCombat
                    ? charState.PlayerScale
                    : settings.GrowFromDamage
                        ? Math.Clamp(
                            settings.MaxScaleMultiplier -
                            (settings.MaxScaleMultiplier * hpRatio),
                            settings.MinScaleMultiplier,
                            settings.MaxScaleMultiplier) * charState.PlayerScale
                        : Math.Clamp(
                            hpRatio,
                            settings.MinScaleMultiplier,
                            float.PositiveInfinity) * charState.PlayerScale;

        float maximumAllowedScale = float.PositiveInfinity;
        if (settings.GrowthFromDelta && settings.LimitDeltaGrowth)
        {
            maximumAllowedScale =
                charState.PlayerScale * settings.DeltaMaxScaleMultiplier;
            targetScale = Math.Min(targetScale, maximumAllowedScale);
        }

        scale = float.Lerp(previousScale, targetScale, settings.Speed / 100f);
        // Enforce the cap on the final visible scale as well as the target. This
        // immediately brings an actor back inside a newly enabled or lowered cap.
        scale = Math.Min(scale, maximumAllowedScale);
        draw->Scale = new Vector3(scale, scale, scale);
        actor->Scale = scale;

        float desiredHeightOffset = 0f;
        if (settings.GrowthFromDelta &&
            settings.EnableDeltaHeightOffset &&
            charState.PlayerScale > 0f)
        {
            // Follow the visible, lerped scale so height returns with normal
            // ambient decay and the faster out-of-combat return.
            float visibleScaleMultiplier = scale / charState.PlayerScale;
            desiredHeightOffset =
                Math.Max(0f, visibleScaleMultiplier - 1f) *
                settings.DeltaHeightOffsetPerScale;
        }

        if (charState.HasDrawOffset || desiredHeightOffset > 0f)
        {
            ApplyHeightOffset(actor, ref charState, desiredHeightOffset);
        }

        charState.PreviousHealth = health;
        charState.PreviousScale = scale;
        CharacterIdToLastScaleMap[actor->EntityId] = charState;
    }

    private unsafe void ApplyHeightOffset(
        Character* actor,
        ref SCCharacterState charState,
        float desiredHeightOffset)
    {
        const float tolerance = 0.0001f;
        var currentOffset = actor->GameObject.DrawOffset;

        if (!charState.HasDrawOffset)
        {
            charState.BaseDrawOffsetY = currentOffset.Y;
            charState.LastAppliedDrawOffsetY = currentOffset.Y;
            charState.HasDrawOffset = true;
        }
        // Keep this captured base stable while the actor is managed. Re-learning
        // another plugin's rewritten offset here can repeatedly add our own
        // height contribution and launch a listed actor upward every frame.
        float targetY = charState.BaseDrawOffsetY + desiredHeightOffset;
        if (MathF.Abs(currentOffset.Y - targetY) > tolerance)
        {
            actor->GameObject.SetDrawOffset(
                currentOffset.X,
                targetY,
                currentOffset.Z);
        }

        charState.LastAppliedDrawOffsetY = actor->GameObject.DrawOffset.Y;
    }

    private void RefreshTrackedActorCaches()
    {
        TrackedPlayerEntityIds.Clear();
        TrackedMonsterEntityIds.Clear();
        TrackedActorRefreshElapsed = 0f;
        TrackedActorRefreshRequested = false;

        var desiredPlayerIdentities = new HashSet<string>(
            Configuration.TrackedPlayerNames,
            StringComparer.OrdinalIgnoreCase);
        var desiredMonsterNames = new HashSet<string>(
            Configuration.TrackedMonsterNames,
            StringComparer.OrdinalIgnoreCase);
        uint localPlayerEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;

        // This is the only full object-table scan. It runs on demand and at most
        // once per second rather than once per framework update.
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is IPlayerCharacter playerCharacter)
            {
                if (playerCharacter.EntityId == localPlayerEntityId)
                {
                    continue;
                }

                string identity = GetPlayerIdentity(playerCharacter);
                if (desiredPlayerIdentities.Contains(identity))
                {
                    TrackedPlayerEntityIds[identity] = playerCharacter.EntityId;
                }

                continue;
            }

            if (gameObject is IBattleNpc &&
                desiredMonsterNames.Contains(gameObject.Name.TextValue))
            {
                // Multiple monsters may share a name, so retain every matching ID.
                TrackedMonsterEntityIds.Add(gameObject.EntityId);
            }
        }
    }

    private static string GetPlayerIdentity(IPlayerCharacter playerCharacter)
        => $"{playerCharacter.Name.TextValue}@" +
           playerCharacter.HomeWorld.Value.Name.ExtractText();

    internal void InvalidateTrackedActorCaches()
        => TrackedActorRefreshRequested = true;

    private GrowthSettings GetSettings(SCActorGroup actorGroup)
        => actorGroup switch
        {
            SCActorGroup.Self => Configuration.SelfSettings,
            SCActorGroup.Player => Configuration.PlayerSettings,
            SCActorGroup.Monster => Configuration.MonsterSettings,
            _ => Configuration.SelfSettings,
        };

    private unsafe bool TryGetCharacterState(
        Character* actor,
        out SCCharacterState charState)
    {
        return CharacterIdToLastScaleMap.TryGetValue(actor->EntityId, out charState) &&
               charState.ActorAddress == (nint)actor;
    }

    private unsafe void RemoveStateAfterReturn(Character* actor)
    {
        if (!TryGetCharacterState(actor, out var charState))
        {
            return;
        }

        var draw = (CharacterBase*)actor->DrawObject;
        if (draw == null ||
            MathF.Abs(draw->Scale.Y - charState.PlayerScale) >= 0.001f ||
            (charState.HasDrawOffset &&
             MathF.Abs(charState.LastAppliedDrawOffsetY - charState.BaseDrawOffsetY) >= 0.001f))
        {
            return;
        }

        draw->Scale = new Vector3(
            charState.PlayerScale,
            charState.PlayerScale,
            charState.PlayerScale);
        actor->Scale = charState.PlayerScale;

        if (charState.HasDrawOffset)
        {
            var currentOffset = actor->GameObject.DrawOffset;
            actor->GameObject.SetDrawOffset(
                currentOffset.X,
                charState.BaseDrawOffsetY,
                currentOffset.Z);
        }

        CharacterIdToLastScaleMap.Remove(actor->EntityId);
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
