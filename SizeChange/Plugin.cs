using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SizeChange.Windows;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace SizeChange;

struct SCCharacterState {
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
    [PluginService] internal static IPluginLog Logger { get; private set; } = null!;

    private const string CommandName_SizeChange = "/sizechange";
    private const string CommandName_Scale = "/scale";
    private const string Parameter_Enable = "enable";
    private const string Parameter_Disable = "disable";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SizeChange");
    private ConfigWindow ConfigWindow { get; init; }
    private Dictionary<uint, SCCharacterState> CharacterIdToLastScaleMap = new Dictionary<uint, SCCharacterState>();
    public Plugin()
    {
        Framework.Update += OnFrameworkUpdate;
        
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        
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
    }

    public unsafe void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        RestoreHeightOffsets();

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName_SizeChange);
        CommandManager.RemoveHandler(CommandName_Scale);
    }

    private unsafe void RestoreHeightOffsets()
    {
        foreach (var thing in ObjectTable.PlayerObjects)
        {
            if (thing == null) continue;

            var actor = (Character*)thing.Address;
            if (actor == null ||
                !CharacterIdToLastScaleMap.TryGetValue(actor->EntityId, out var charState) ||
                !charState.HasDrawOffset)
            {
                continue;
            }

            var currentOffset = actor->GameObject.DrawOffset;
            actor->GameObject.SetDrawOffset(
                currentOffset.X,
                charState.BaseDrawOffsetY,
                currentOffset.Z);
        }
    }

    private unsafe void OnCommand(string command, string args)
    {
        if(command == CommandName_SizeChange)
        {
            if(args == "")
            {
                ConfigWindow.Toggle();
            }
            if(args == Parameter_Enable)
            {
                Configuration.Enable = true;
            }
            if(args == Parameter_Disable)
            {
                Configuration.Enable = false;
            }
        }

        if(command == CommandName_Scale)
        {
            float from_arg;
            if(float.TryParse(args, out from_arg))
            {
                var player = ObjectTable.LocalPlayer;
                if (player != null) 
                {
                    UpdateScale((Character*)player.Address, from_arg);
                }
            }
        }
    }

    private unsafe void UpdateScale(Character* actor, float scale)
    {
        SCCharacterState charState;
        if(CharacterIdToLastScaleMap.ContainsKey(actor->EntityId))
        {
            charState = CharacterIdToLastScaleMap[actor->EntityId];
        }
        else
        {
            charState = new SCCharacterState
            {
                GrowthMultiplier = 1.0f
            };
        }
        charState.PlayerScale = scale;
        var draw = (CharacterBase*)actor->DrawObject;
        if (draw == null) return;
        var currentScale = draw->Scale.Y;
        charState.PreviousScale = currentScale;

        CharacterIdToLastScaleMap[actor->EntityId] = charState;
    }
    
    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        bool disable = ClientState.IsPvP || !Configuration.Enable;
        bool outOfCombat =
            Configuration.OnlyActiveInCombat &&
            !Condition[ConditionFlag.InCombat];
        
        var player = ObjectTable.LocalPlayer;
            if (player == null) return;

        foreach (var thing in ObjectTable.PlayerObjects)
        {
            var actor = thing;
            if (actor == null) continue;
            bool isLocalPlayer = ((Character*)player.Address)->EntityId == ((Character*)thing.Address)->EntityId;
            
            AdjustScale(
                (Character*)actor.Address,
                Configuration.GrowFromDamage,
                Configuration.GrowthFromDelta,
                disable || (!isLocalPlayer && !Configuration.AlterAnyone),
                outOfCombat);
        }
    }

    // find the actor's health and shield value and uses that to adjust the model's scale
    public unsafe void AdjustScale(
        Character* actor,
        bool growFromDamage,
        bool growthFromDelta,
        bool disable,
        bool outOfCombat)
    {
        if (actor == null) return;
        float maxhp = actor->MaxHealth;
        if (maxhp <= 0f) return;
        float shield = (actor->ShieldValue / 100f) * maxhp;
        float health = actor->Health + shield;
        float hpRatio = health / maxhp;
        Logger.Information("hpRatio is {hpRatio}", hpRatio);

        var draw = (CharacterBase*)actor->DrawObject;

        if (draw != null)
        {
            float scale = draw->Scale.Y;
            Logger.Information("current scale is {scale}", scale);
            float previousScale = scale;
            SCCharacterState charState;
            if(CharacterIdToLastScaleMap.ContainsKey(actor->EntityId))
            {
                charState = CharacterIdToLastScaleMap[actor->EntityId];
                previousScale = charState.PreviousScale;
            }
            else
            {
                charState = new SCCharacterState
                {
                    PlayerScale = 1.0f,
                    GrowthMultiplier = 1.0f
                };
            }
            Logger.Information("Previous scale is {scale}", previousScale);
            if (previousScale != scale)
            {
                charState.PlayerScale = scale;
            }

            if (!charState.HasPreviousHealth)
            {
                charState.PreviousHealth = health;
                charState.GrowthMultiplier = Math.Max(1.0f, charState.GrowthMultiplier);
                charState.HasPreviousHealth = true;
            }

            if (growthFromDelta && !disable && !outOfCombat)
            {
                // Only add growth while the effect is active. Health is still
                // sampled while disabled, preventing retroactive growth later.
                float healthLost = charState.PreviousHealth - health;
                if (healthLost > 0f)
                {
                    float healthLostRatio = healthLost / maxhp;
                    charState.GrowthMultiplier += healthLostRatio * Configuration.DeltaGrowthMultiplier;
                }
            }

            if (growthFromDelta && Configuration.LimitDeltaGrowth)
            {
                charState.GrowthMultiplier = Math.Min(
                    charState.GrowthMultiplier,
                    Configuration.DeltaMaxScaleMultiplier);
            }

            // Ambient decay belongs only to the Growth From Delta mode.
            if (growthFromDelta && charState.GrowthMultiplier > 1.0f)
            {
                float deltaSeconds = (float)Framework.UpdateDelta.TotalSeconds;
                float decayMultiplier =
                    outOfCombat && !disable
                        ? Configuration.OutOfCombatDecayMultiplier
                        : 1.0f;
                float shrinkAmount =
                    Configuration.AmbientShrinkRate *
                    decayMultiplier *
                    deltaSeconds;
                charState.GrowthMultiplier = Math.Max(1.0f, charState.GrowthMultiplier - shrinkAmount);
            }

            float targetScale = disable
                ? charState.PlayerScale
                : growthFromDelta
                    ? charState.PlayerScale * charState.GrowthMultiplier
                    : outOfCombat
                        ? charState.PlayerScale
                        : growFromDamage
                            ? Math.Clamp(
                                Configuration.MaxScaleMultiplier -
                                (Configuration.MaxScaleMultiplier * hpRatio),
                                Configuration.MinScaleMultiplier,
                                Configuration.MaxScaleMultiplier) * charState.PlayerScale
                            : Math.Clamp(
                                hpRatio,
                                Configuration.MinScaleMultiplier,
                                float.PositiveInfinity) * charState.PlayerScale;
            Logger.Information("targetScale is {targetScale}", targetScale);

            
            scale = float.Lerp(previousScale, targetScale, Configuration.Speed / 100f);
            Logger.Information("scale after lerp is {scale}", scale);
            draw->Scale = new Vector3(scale, scale, scale);
            actor->Scale = scale;

            float desiredHeightOffset = 0f;
            if (growthFromDelta &&
                Configuration.EnableDeltaHeightOffset &&
                charState.PlayerScale > 0f)
            {
                // Follow the visible, lerped scale so height returns at the
                // same pace as normal and out-of-combat delta decay.
                float visibleScaleMultiplier = scale / charState.PlayerScale;
                desiredHeightOffset =
                    Math.Max(0f, visibleScaleMultiplier - 1f) *
                    Configuration.DeltaHeightOffsetPerScale;
            }

            if (charState.HasDrawOffset || desiredHeightOffset > 0f)
            {
                ApplyHeightOffset(actor, ref charState, desiredHeightOffset);
            }

            charState.PreviousHealth = health;
            charState.PreviousScale = scale;
            CharacterIdToLastScaleMap[actor->EntityId] = charState;
        }
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
        else if (MathF.Abs(currentOffset.Y - charState.LastAppliedDrawOffsetY) > tolerance)
        {
            // Preserve legitimate base-offset changes made by the game while
            // this plugin is running instead of accumulating our own offset.
            charState.BaseDrawOffsetY = currentOffset.Y;
        }

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
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
