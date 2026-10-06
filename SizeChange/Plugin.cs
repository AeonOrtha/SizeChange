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
using Lumina.Excel.Sheets;
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
    public CharacterTransformBaseline Transform;
    public float PlayerScale;
    public float PreviousScale;
    public float ObservedScale;
    public float SelfRootHeightOffset;
    public float PreviousHealth;
    public float GrowthMultiplier;
    public float PendingGrowth;
    public float PendingDamageRatio;
    public AetherGrowthState Aether;
    public DigestionReserve Digestion;
    public int NearbyAetherSources;
    public int NearbyDrainSources;
    public GrowthAfterglow Afterglow;
    public bool AfterglowActive;
    public List<SizeDrainSources.Hit>? DrainHits;
    public RandomDrainContributors? DrainContributors;
    public SizeDrainSettings? ActiveDrainMode;
    public List<AetherSources.Hit>? AetherHits;
    public float AccumulatorRemainingSeconds;
    public GrowthOvershootPulse OvershootPulse;
    public bool HasPreviousHealth;
    public bool WasPreviewing;
    public float BaseDrawOffsetY;
    public float LastAppliedDrawOffsetY;
    public bool HasDrawOffset;
    public long LastDeltaGrowthSoundTick;
    public long LastDeltaGrowthVfxTick;
    public long LastDeltaGrowthAnimationTick;
}

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    private const string CommandName_SizeChange = "/sizechange";
    private const string CommandName_Scale = "/scale";
    private const string Parameter_Enable = "enable";
    private const string Parameter_Disable = "disable";
    private const float TrackedActorRefreshIntervalSeconds = 1.0f;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SizeChange");
    private ConfigWindow ConfigWindow { get; init; }
    private readonly Dictionary<uint, SCCharacterState> CharacterIdToLastScaleMap = new();
    private readonly Dictionary<nint, uint> vfxActorIdentities = new();
    private readonly Dictionary<nint, nint> vfxActorModels = new();
    private readonly Dictionary<string, uint> TrackedPlayerEntityIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> TrackedMonsterEntityIds = new();
    private readonly SessionScaleBaselines sessionScales = new();
    private readonly SessionHeightBaselines sessionHeights = new();
    private readonly AetherSources aetherSources = new();
    private readonly SizeDrainSources drainSources = new();
    private readonly AetherSoundPlayer aetherSound = new(() => new HeartbeatScdVoice());
    private readonly GrowthPreview selfPreview = new();
    private readonly GrowthPreview playerPreview = new();
    private readonly GrowthPreview monsterPreview = new();
    internal GrowthPreview GetPreview(SCActorGroup group) => group switch
    {
        SCActorGroup.Self => selfPreview,
        SCActorGroup.Player => playerPreview,
        SCActorGroup.Monster => monsterPreview,
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };

    private readonly GrowthSoundPlayer GrowthSoundPlayer;
    internal readonly HeartbeatSoundPlayer HeartbeatSound = new(new HeartbeatScdVoice(), new HeartbeatScdVoice(), new HeartbeatScdVoice());
    private readonly GrowthVfxPlayer GrowthVfxPlayer;
    internal BoneHeartbeatPlayer BoneHeartbeat { get; }
    internal PlayerProfileHeightCompatibility PlayerProfileHeight { get; }
    private bool drainInDuty;
    private bool drainTransition;
    private uint drainTerritory = uint.MaxValue;
    private bool drainTerritoryIsDuty;
    private float TrackedActorRefreshElapsed = TrackedActorRefreshIntervalSeconds;
    private bool TrackedActorRefreshRequested = true;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        GrowthSoundPlayer = new GrowthSoundPlayer();
        GrowthVfxPlayer = new GrowthVfxPlayer(
            (address, id) => vfxActorIdentities.TryGetValue(address, out var current) && current == id,
            address => vfxActorModels.TryGetValue(address, out var model) ? model : 0,
            message => Log.Debug("VFX: {Event}", message));
        BoneHeartbeat = new BoneHeartbeatPlayer(new CustomizeHeartbeatApi(PluginInterface));
        PlayerProfileHeight = new PlayerProfileHeightCompatibility(
            () => Configuration.PlayerProfileHeight,
            ResolveHeightActor,
            actor => Configuration.Enable && !ClientState.IsPvP && ClientState.IsLoggedIn &&
                Configuration.TrackedPlayerNames.Exists(n => string.Equals(n, actor.Name, StringComparison.OrdinalIgnoreCase)),
            PluginInterface, message => Log.Warning("Player height: {Message}", message));

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
        PlayerProfileHeight.Dispose();
        HeartbeatSound.Dispose();
        aetherSound.Dispose();
        BoneHeartbeat.Dispose();
        RestoreCharacterTransforms();
        GrowthVfxPlayer.Dispose();
        GrowthSoundPlayer.Dispose();

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
            var state = stateEntry.Value;
            RestoreOwnedTransforms(actor, ref state);

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

        if (!TryBindTransforms(actor, draw, out charState.Transform)) return;
        sessionScales.SetExplicitReference(HeightKey(actor), scale);
        if (!TryBindTransforms(actor, draw, out charState.Transform)) return;
        charState.PlayerScale = charState.Transform.Y.Baseline;
        charState.PreviousScale = charState.ObservedScale = draw->Scale.Y;
        charState.OvershootPulse = default;
        CharacterIdToLastScaleMap[actor->EntityId] = charState;
    }

    private unsafe bool TryBindTransforms(Character* actor, CharacterBase* draw, out CharacterTransformBaseline result)
    {
        result = default;
        var observed = new ScaleBaseline(draw->Scale.X, draw->Scale.Y, draw->Scale.Z, actor->Scale);
        if (!observed.Valid || !sessionScales.TryGetOrCapture(HeightKey(actor), observed, out var baseline)) return false;
        result = new CharacterTransformBaseline
        {
            Ready = true, DrawAddress = (nint)draw,
            X = OwnedTransformValue.Rebind(baseline.X, observed.X),
            Y = OwnedTransformValue.Rebind(baseline.Y, observed.Y),
            Z = OwnedTransformValue.Rebind(baseline.Z, observed.Z),
            ActorScale = OwnedTransformValue.Rebind(baseline.Actor, observed.Actor),
            Height = sessionHeights.ForModel(HeightKey(actor), (nint)draw, actor->GameObject.DrawOffset.Y),
        };
        return true;
    }

    private PlayerProfileHeightHook.Actor? ResolveHeightActor(ushort index)
    {
        // Never inspect game objects from a foreign IPC thread.
        if (!Framework.IsInFrameworkUpdateThread || !ClientState.IsLoggedIn) return null;
        var obj = ObjectTable[index];
        if (obj is not IPlayerCharacter player || player.Address == ObjectTable.LocalPlayer?.Address) return null;
        return new(index, player.Address, player.EntityId, GetPlayerIdentity(player));
    }

    private void UpdateDrainLocation()
    {
        drainTransition = !ClientState.IsLoggedIn || Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51];
        if (drainTerritory != ClientState.TerritoryType)
        {
            drainTerritory = ClientState.TerritoryType;
            drainTerritoryIsDuty = DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(drainTerritory)?.ContentFinderCondition.RowId > 0;
            // No contributor lease or exposure may cross a territory boundary.
            foreach (var key in new List<uint>(CharacterIdToLastScaleMap.Keys))
            {
                var state = CharacterIdToLastScaleMap[key];
                state.DrainContributors?.Clear(); state.DrainHits?.Clear(); state.NearbyDrainSources = 0;
                CharacterIdToLastScaleMap[key] = state;
            }
            GrowthVfxPlayer.ClearSizeDrainEffects();
        }
        drainInDuty = SizeDrainLocation.IsDuty(
            Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BoundByDuty56] || Condition[ConditionFlag.BoundByDuty95],
            DutyState.ContentFinderCondition.RowId, drainTerritoryIsDuty);
        foreach (var key in new List<uint>(CharacterIdToLastScaleMap.Keys))
        {
            var state = CharacterIdToLastScaleMap[key];
            if (!SizeDrainLocation.Blocked(GetSettings(state.ActorGroup).SizeDrain, drainInDuty, drainTransition)) continue;
            state.DrainContributors?.Clear(); state.DrainHits?.Clear(); state.NearbyDrainSources = 0;
            CharacterIdToLastScaleMap[key] = state;
        }
        if (drainTransition) { drainSources.BeginFrame(); GrowthVfxPlayer.ClearSizeDrainEffects(); }
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        UpdateDrainLocation();
        PlayerProfileHeight.Tick();
        vfxActorIdentities.Clear();
        vfxActorModels.Clear();
        if (ClientState.IsLoggedIn && !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51])
            foreach (var obj in ObjectTable)
                if (obj.Address != 0)
                {
                    vfxActorIdentities[obj.Address] = obj.EntityId;
                    var raw = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
                    vfxActorModels[obj.Address] = (nint)raw->DrawObject;
                }
        aetherSources.Refresh(ObjectTable, DataManager, ClientState.IsLoggedIn && !ClientState.IsPvP && Configuration.Enable && ObjectTable.LocalPlayer is { CurrentHp: > 0 } &&
            !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51] &&
            (Configuration.SelfSettings.AetherProximityGrowth || Configuration.PlayerSettings.AetherProximityGrowth || Configuration.MonsterSettings.AetherProximityGrowth));
        foreach (var crystal in aetherSources.Current)
        {
            vfxActorIdentities[crystal.Address] = crystal.EntityId;
            vfxActorModels[crystal.Address] = crystal.ModelAddress;
        }
        GrowthVfxPlayer.BeginProximityFrame((float)Framework.UpdateDelta.TotalSeconds);
        aetherSound.BeginFrame((float)Framework.UpdateDelta.TotalSeconds);
        GrowthVfxPlayer.Update();

        if (!ClientState.IsLoggedIn)
        {
            sessionHeights.EndSession();
            sessionScales.EndSession();
            CharacterIdToLastScaleMap.Clear();
        }
        bool globallyDisabled = ClientState.IsPvP || !Configuration.Enable;
        bool inCombat = Condition[ConditionFlag.InCombat];
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null || !ClientState.IsLoggedIn)
        {
            GrowthVfxPlayer.EndProximityFrame(true);
            aetherSound.EndFrame();
            selfPreview.Enabled = playerPreview.Enabled = monsterPreview.Enabled = false;
            BoneHeartbeat.Tick(Configuration.SelfBoneHeartbeat, 0, 0, 0,
                false, false, (float)Framework.UpdateDelta.TotalSeconds);
            HeartbeatSound.Tick(Configuration.SelfBoneHeartbeat.Sound, false, false, 0);
            return;
        }

        // Capture Self at the first ready model after login, even if growth is
        // disabled. A transient missing model during redraw does not reset it.
        if (ClientState.IsLoggedIn && ((Character*)localPlayer.Address)->DrawObject != null)
        {
            var loginActor = (Character*)localPlayer.Address;
            var loginDraw = loginActor->DrawObject;
            sessionHeights.GetOrCapture("self", loginActor->GameObject.DrawOffset.Y);
            sessionScales.TryGetOrCapture("self", new ScaleBaseline(loginDraw->Scale.X,
                loginDraw->Scale.Y, loginDraw->Scale.Z, loginActor->Scale), out _);
        }

        float deltaSeconds = (float)Framework.UpdateDelta.TotalSeconds;
        RefreshDrainSources(!globallyDisabled && localPlayer.CurrentHp > 0 &&
            !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51] &&
            (Configuration.SelfSettings.SizeDrain.Enabled || Configuration.PlayerSettings.SizeDrain.Enabled || Configuration.MonsterSettings.SizeDrain.Enabled));
        bool previewAllowed = !globallyDisabled && !inCombat && localPlayer.CurrentHp > 0 &&
            !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51];
        selfPreview.Advance(deltaSeconds, previewAllowed && Configuration.AffectSelf,
            Configuration.SelfSettings.PreviewHitPercent, Configuration.SelfSettings.PreviewHitIntervalSeconds);
        playerPreview.Advance(deltaSeconds, previewAllowed,
            Configuration.PlayerSettings.PreviewHitPercent, Configuration.PlayerSettings.PreviewHitIntervalSeconds);
        monsterPreview.Advance(deltaSeconds, previewAllowed,
            Configuration.MonsterSettings.PreviewHitPercent, Configuration.MonsterSettings.PreviewHitIntervalSeconds);
        TrackedActorRefreshElapsed += deltaSeconds;
        if (TrackedActorRefreshRequested ||
            TrackedActorRefreshElapsed >= TrackedActorRefreshIntervalSeconds)
        {
            RefreshTrackedActorCaches();
        }

        var processedEntityIds = new HashSet<uint>();
        var localActor = (Character*)localPlayer.Address;
        float scaleBeforeUpdate = localActor->DrawObject != null ? localActor->DrawObject->Scale.Y : 0f;
        ProcessSelectedActor(
            localActor,
            Configuration.AffectSelf,
            SCActorGroup.Self,
            Configuration.SelfSettings,
            globallyDisabled,
            inCombat);
        processedEntityIds.Add(localActor->EntityId);

        bool accumulating = Configuration.SelfSettings.GrowthFromDelta &&
            Configuration.SelfSettings.AccumulatorDelaySeconds > 0f &&
            TryGetCharacterState(localActor, out var heartbeatState) && !heartbeatState.Transform.ScaleConflict &&
            (!Configuration.SelfSettings.OnlyActiveInCombat || inCombat || selfPreview.Enabled || heartbeatState.Aether.InRange || heartbeatState.Aether.PendingGrowth > 0f) &&
            heartbeatState.PendingGrowth > 0f;
        float pendingDamage = TryGetCharacterState(localActor, out var bpmState)
            ? bpmState.PendingDamageRatio : 0f;
        float settledSelfScale = TryGetCharacterState(localActor, out var boneGrowthState)
                ? Configuration.SelfSettings.GrowthFromDelta ? boneGrowthState.GrowthMultiplier
                    : boneGrowthState.PlayerScale > 0f ? boneGrowthState.PreviousScale / boneGrowthState.PlayerScale : 1f
                : 1f;
        BoneHeartbeat.Tick(
            Configuration.SelfBoneHeartbeat,
            localPlayer.ObjectIndex,
            localPlayer.Address,
            localActor->EntityId,
            !globallyDisabled && Configuration.AffectSelf && localActor->Health > 0 &&
                localActor->DrawObject != null && !Condition[ConditionFlag.BetweenAreas] &&
                !Condition[ConditionFlag.BetweenAreas51],
            accumulating || (boneGrowthState.AfterglowActive && !boneGrowthState.Transform.ScaleConflict),
            deltaSeconds,
            accumulating || (TryGetCharacterState(localActor, out var jawState) &&
                (jawState.OvershootPulse.IsActive || jawState.PreviousScale > scaleBeforeUpdate + 0.00001f)),
            pendingDamage, Configuration.SelfSettings.MaximumHealthLossRatioPerTrigger,
            settledSelfScale, boneGrowthState.SelfRootHeightOffset,
            Configuration.SelfSettings.Digestion.BoneName,
            Configuration.SelfSettings.Digestion.BoneAddition(boneGrowthState.Digestion.Growth));

        bool soundAvailable = !ClientState.IsPvP && localActor->Health > 0 &&
            !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51];
        HeartbeatSound.Tick(Configuration.SelfBoneHeartbeat.Sound,
            !globallyDisabled && Configuration.AffectSelf && Configuration.SelfBoneHeartbeat.Enabled &&
            BoneHeartbeat.IsPulsing, soundAvailable, deltaSeconds, BoneHeartbeat.Phase, BoneHeartbeat.PhaseGeneration, settledSelfScale);

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
        GrowthVfxPlayer.EndProximityFrame(globallyDisabled || Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51]);
        aetherSound.EndFrame();
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

        var preview = GetPreview(actorGroup);
        bool previewing = isSelected && !globallyDisabled && preview.Enabled && actor->Health > 0;
        bool outOfCombat = GrowthScalingMath.IsOutOfCombat(inCombat, previewing);
        AdjustScale(
            actor,
            actorGroup,
            settings,
            globallyDisabled || !isSelected,
            outOfCombat,
            previewing ? preview : null);

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
        bool outOfCombat,
        GrowthPreview? preview)
    {
        if (actor == null) return;

        float maxhp = actor->MaxHealth;
        bool aetherWithoutHp = actorGroup == SCActorGroup.Monster && settings.GrowthFromDelta &&
            maxhp <= 0f && !actor->GameObject.IsDead();
        if (maxhp <= 0f && !aetherWithoutHp)
        {
            if (TryGetCharacterState(actor, out var unavailableState))
            {
                RestoreOwnedTransforms(actor, ref unavailableState);
                CharacterIdToLastScaleMap.Remove(actor->EntityId);
            }
            return;
        }
        // Crystal hits are already fractions of maximum HP; HP-less battle NPCs
        // need no real health conversion and must not invent ordinary damage.
        if (aetherWithoutHp) maxhp = 1f;
        float shield = aetherWithoutHp ? 0f : (actor->ShieldValue / 100f) * maxhp;
        float health = aetherWithoutHp ? 1f : actor->Health + shield;
        float hpRatio = preview?.HealthRatio ?? health / maxhp;

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
        charState.NearbyAetherSources = 0;
        charState.NearbyDrainSources = 0;
        if (disable)
        {
            RestoreOwnedTransforms(actor, ref charState);
            CharacterIdToLastScaleMap.Remove(actor->EntityId);
            return;
        }
        bool aliveForDigestion = actor->Health > 0 || aetherWithoutHp;
        if (!settings.GrowthFromDelta || !settings.Digestion.Enabled || !aliveForDigestion)
            charState.Digestion = default;
        bool newTracking = !charState.Transform.Ready;
        if (newTracking || charState.Transform.DrawAddress != (nint)draw)
        {
            if (!TryBindTransforms(actor, draw, out var rebound)) return;
            charState.Transform = rebound;
            charState.PlayerScale = rebound.Y.Baseline;
            // Redraw replaces the model, not logical growth or accumulated damage.
            // New tracking can start from the observed visual size without ever
            // treating it as the session's original dimensions.
            if (newTracking) charState.PreviousScale = previousScale = scale;
        }
        charState.ObservedScale = scale;
        charState.Transform.X.Observe(draw->Scale.X);
        charState.Transform.Y.Observe(draw->Scale.Y);
        charState.Transform.Z.Observe(draw->Scale.Z);
        charState.Transform.ActorScale.Observe(actor->Scale);
        if (charState.Transform.ScaleConflict)
        {
            // Pause only. Never restore to 1x, delete accumulated damage or
            // discard the active pulse merely because an external value changed.
            charState.PreviousHealth = health;
            charState.Aether.InRange = false;
            CharacterIdToLastScaleMap[actor->EntityId] = charState;
            return;
        }

        if (charState.WasPreviewing && preview == null)
        {
            // Stop queued fake hits when preview ends. A visible pulse finishes
            // smoothly before normal decay resumes.
            charState.PendingGrowth = charState.Aether.PendingGrowth;
            charState.PendingDamageRatio = charState.Aether.PendingDamageRatio;
            if (charState.PendingGrowth <= 0f) charState.AccumulatorRemainingSeconds = 0f;
        }
        charState.WasPreviewing = preview != null;

        if (!charState.HasPreviousHealth)
        {
            charState.PreviousHealth = health;
            charState.GrowthMultiplier = Math.Max(1.0f, charState.GrowthMultiplier);
            charState.HasPreviousHealth = true;
        }

        bool releasedGrowth = false;
        bool inactiveForCombat = settings.OnlyActiveInCombat && outOfCombat;
        float growthMultiplierBeforeRelease = charState.GrowthMultiplier;
        float deltaSeconds = (float)Framework.UpdateDelta.TotalSeconds;
        var position = actor->GameObject.Position;
        charState.AetherHits ??= new List<AetherSources.Hit>();
        var nearbyCrystals = settings.GrowthFromDelta && settings.AetherProximityGrowth && !disable &&
            (actor->Health > 0 || aetherWithoutHp) && settings.MaximumHealthLossRatioPerTrigger > 0f && settings.DeltaGrowthMultiplier > 0f
                ? aetherSources.GetHits(position.X, position.Y, position.Z, settings, charState.AetherHits) : null;
        int aetherCount = nearbyCrystals?.Count ?? 0;
        charState.NearbyAetherSources = aetherCount;
        if (aetherCount > 0)
        {
            aetherSound.Request((nint)actor, actor->EntityId, false, settings.AetherActorSound,
                position.X, position.Y, position.Z);
            foreach (var hit in nearbyCrystals!)
                aetherSound.Request(hit.Crystal.Address, hit.Crystal.EntityId, true, settings.AetherSourceSound,
                    hit.Crystal.Position.X, hit.Crystal.Position.Y, hit.Crystal.Position.Z);
            if (settings.AetherActorVfxEnabled)
                GrowthVfxPlayer.KeepProximity((nint)actor, actor->EntityId, settings.AetherActorVfxPath, false, position,
                    fadeIn: settings.AetherActorVfxFadeIn, fadeOut: settings.AetherActorVfxFadeOut);
            if (settings.AetherSourceVfxEnabled)
                foreach (var hit in nearbyCrystals!)
                    GrowthVfxPlayer.KeepProximity(hit.Crystal.Address, hit.Crystal.EntityId,
                        settings.AetherSourceVfxPath, true,
                        new Vector3(hit.Crystal.Position.X, hit.Crystal.Position.Y, hit.Crystal.Position.Z),
                        settings.AetherSourceVfxAttached, settings.AetherSourceVfxHeight,
                        settings.AetherSourceVfxFadeIn, settings.AetherSourceVfxFadeOut);
        }
        charState.DrainHits ??= new List<SizeDrainSources.Hit>();

        var drain = settings.SizeDrain.ForLocation(drainInDuty);
        if (!ReferenceEquals(charState.ActiveDrainMode, drain))
        {
            charState.DrainContributors?.Clear();
            charState.DrainHits.Clear();
            charState.ActiveDrainMode = drain;
        }
        var nearbyTargets = settings.GrowthFromDelta && settings.SizeDrain.Enabled &&
            settings.SizeDrain.AllowsLocation(drainInDuty) && !drainTransition && !disable &&
            (actor->Health > 0 || aetherWithoutHp) && settings.MaximumHealthLossRatioPerTrigger > 0f && settings.DeltaGrowthMultiplier > 0f
            ? drainSources.GetHits((nint)actor, new System.Numerics.Vector3(position.X, position.Y, position.Z),
                drain, charState.DrainHits, charState.GrowthMultiplier) : null;
        charState.DrainContributors ??= new RandomDrainContributors();
        if (nearbyTargets != null)
            charState.DrainContributors.Filter(charState.DrainHits, drain,
                Environment.TickCount64 / 1000.0, Random.Shared.NextDouble);
        else
        {
            charState.DrainContributors.Clear();
            charState.DrainHits.Clear();
        }
        int drainCount = nearbyTargets?.Count ?? 0;
        charState.NearbyDrainSources = drainCount;
        if (drainCount > 0)
        {
            if (drain.ReceiverVfxEnabled && !string.IsNullOrWhiteSpace(drain.ReceiverVfxPath))
                GrowthVfxPlayer.KeepProximity((nint)actor, actor->EntityId, drain.ReceiverVfxPath, false, position,
                    fadeIn: drain.FadeIn, fadeOut: drain.FadeOut, sizeDrain: true);
            if (drain.SourceVfxEnabled && !string.IsNullOrWhiteSpace(drain.SourceVfxPath))
                foreach (var hit in nearbyTargets!)
                    GrowthVfxPlayer.KeepProximity(hit.Target.Address, hit.Target.EntityId, drain.SourceVfxPath, true,
                        new Vector3(hit.Target.Position.X, hit.Target.Position.Y, hit.Target.Position.Z),
                        attachToSource: true, fadeIn: drain.FadeIn, fadeOut: drain.FadeOut, sizeDrain: true);
        }
        // Both source types share the existing exposure accumulator, cap bonus
        // and one-second cadence. Per-receiver lists prevent cross-profile loss.
        bool proximityActive = aetherCount + drainCount > 0;
        bool proximityHitDue = charState.Aether.Advance(aetherCount + drainCount, deltaSeconds) > 0;
        void QueueGrowth(EarnedGrowth earned, bool aether)
        {
            float addedGrowth = (float)earned.Growth;
            float healthLostRatio = (float)earned.DamageRatio;
            if (addedGrowth <= 0f) return;
            if (settings.AccumulatorDelaySeconds <= 0f)
            {
                charState.GrowthMultiplier += addedGrowth;
                if (aether) charState.Aether.Bonus += addedGrowth;
                releasedGrowth = true;
            }
            else
            {
                charState.PendingGrowth += addedGrowth;
                charState.PendingDamageRatio = Math.Min(1f, charState.PendingDamageRatio + healthLostRatio);
                if (aether)
                {
                    charState.Aether.PendingGrowth += addedGrowth;
                    charState.Aether.PendingDamageRatio = Math.Min(1f, charState.Aether.PendingDamageRatio + healthLostRatio);
                }
                if (charState.AccumulatorRemainingSeconds <= 0f)
                    charState.AccumulatorRemainingSeconds = settings.AccumulatorDelaySeconds;
            }
        }
        void Accumulate(float healthLostRatio, bool aether)
        {
            healthLostRatio = Math.Min(healthLostRatio, settings.MaximumHealthLossRatioPerTrigger);
            float addedGrowth = GrowthScalingMath.CalculateGain(healthLostRatio, settings.DeltaGrowthMultiplier,
                growthMultiplierBeforeRelease, settings.ScaleGrowthWithSize);
            // Real damage/preview never fills the proximity reserve.
            var earned = aether ? charState.Digestion.Absorb(addedGrowth, healthLostRatio, settings.Digestion)
                : new EarnedGrowth(addedGrowth, healthLostRatio);
            QueueGrowth(earned, aether);
        }
        if (settings.GrowthFromDelta && !disable && !inactiveForCombat && !aetherWithoutHp)
        {
            float healthLost = preview != null ? preview.HitRatio * maxhp : charState.PreviousHealth - health;
            if (healthLost > 0f) Accumulate(healthLost / maxhp, false);
        }
        if (proximityHitDue)
        {
            if (nearbyCrystals != null)
                foreach (var hit in nearbyCrystals) Accumulate(hit.Ratio, true);
            if (nearbyTargets != null)
                foreach (var hit in nearbyTargets) Accumulate(hit.Ratio, true);
        }

        bool digestionPaused = !settings.GrowthFromDelta || disable || !aliveForDigestion ||
            Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51];
        var digested = charState.Digestion.Advance(proximityActive, digestionPaused, deltaSeconds, settings.Digestion);
        // Already valued and already subject to the original hit allowance.
        // It retains proximity's cap bonus and out-of-combat accumulator path.
        if (digested.Growth > 0) QueueGrowth(digested, true);
        bool digesting = settings.Digestion.Enabled && !digestionPaused &&
            (charState.Digestion.HasGrowth || digested.Growth > 0);

        if (!settings.GrowthFromDelta || disable)
        {
            // Pending growth must never survive disabling the mode, entering
            // PvP, disabling the plugin, or removing the actor from its list.
            charState.PendingGrowth = 0f;
            charState.PendingDamageRatio = 0f;
            charState.AccumulatorRemainingSeconds = 0f;
            charState.Aether = default;
        }
        else if (charState.PendingGrowth > 0f)
        {
            bool releasePendingGrowth =
                settings.AccumulatorDelaySeconds <= 0f || (inactiveForCombat && charState.Aether.PendingGrowth <= 0f);
            if (!releasePendingGrowth)
            {
                // If the configured delay is shortened while a window is open,
                // do not retain a timer longer than the new setting.
                charState.AccumulatorRemainingSeconds = Math.Min(
                    charState.AccumulatorRemainingSeconds,
                    settings.AccumulatorDelaySeconds);
                charState.AccumulatorRemainingSeconds = Math.Max(
                    0f,
                    charState.AccumulatorRemainingSeconds - deltaSeconds);
                releasePendingGrowth =
                    charState.AccumulatorRemainingSeconds <= 0f;
            }

            if (releasePendingGrowth)
            {
                charState.GrowthMultiplier += charState.PendingGrowth;
                charState.Aether.Release();
                charState.PendingGrowth = 0f;
                charState.PendingDamageRatio = 0f;
                charState.AccumulatorRemainingSeconds = 0f;
                releasedGrowth = true;
            }
        }
        else
        {
            charState.AccumulatorRemainingSeconds = 0f;
        }

        var accumulationFx = settings.AccumulatingEffects;
        bool feedbackAllowed = settings.GrowthFromDelta && !disable && (actor->Health > 0 || aetherWithoutHp);
        charState.AfterglowActive = charState.Afterglow.Advance(feedbackAllowed,
            charState.PendingGrowth > 0f || proximityActive || releasedGrowth || digesting, accumulationFx, deltaSeconds);
        if (feedbackAllowed && accumulationFx.Enabled && (charState.PendingGrowth > 0f || charState.AfterglowActive))
            for (int layer = 0; layer < Math.Min(16, accumulationFx.Paths.Count); layer++)
                GrowthVfxPlayer.KeepProximity((nint)actor, actor->EntityId, accumulationFx.Paths[layer], false,
                    position, fadeIn: accumulationFx.FadeIn, fadeOut: accumulationFx.FadeOut, accumulatorLayer: layer, durationSeconds: layer < accumulationFx.Durations.Count ? accumulationFx.Durations[layer] : 0f);

        // Measure the damage-driven release before capping earned growth so a
        // character already at the cap can still display a temporary pulse.
        float pulseGrowthAmount = Math.Max(
            0f, charState.GrowthMultiplier - growthMultiplierBeforeRelease);

        if (settings.GrowthFromDelta && settings.LimitDeltaGrowth)
        {
            charState.GrowthMultiplier = Math.Min(
                charState.GrowthMultiplier,
                charState.Aether.Cap(settings.DeltaMaxScaleMultiplier));
        }

        bool triggerOvershoot = settings.GrowthFromDelta && !disable &&
            releasedGrowth && pulseGrowthAmount > 0f && settings.GrowthOvershootPercent > 0f;

        // Trigger feedback for earned growth even when the settled size is capped.
        // A delayed window produces one combined sound/VFX event instead of
        // one event for every hit inside the window.
        // Sound and VFX have independent per-actor cooldowns so rapid attacks do
        // not create an effect on every framework update.
        if (releasedGrowth && pulseGrowthAmount > 0f)
        {
            long currentTick = Environment.TickCount64;
            long soundCooldownMilliseconds =
                (long)(settings.DeltaGrowthSoundCooldownSeconds * 1000f);
            bool soundCooldownElapsed =
                soundCooldownMilliseconds <= 0 ||
                charState.LastDeltaGrowthSoundTick == 0 ||
                currentTick - charState.LastDeltaGrowthSoundTick >= soundCooldownMilliseconds;

            if (soundCooldownElapsed &&
                TryPlayDeltaGrowthSound(actor, settings, false, charState.GrowthMultiplier) == null)
            {
                charState.LastDeltaGrowthSoundTick = currentTick;
            }

            long vfxCooldownMilliseconds =
                (long)(settings.DeltaGrowthVfxCooldownSeconds * 1000f);
            bool vfxCooldownElapsed =
                vfxCooldownMilliseconds <= 0 ||
                charState.LastDeltaGrowthVfxTick == 0 ||
                currentTick - charState.LastDeltaGrowthVfxTick >= vfxCooldownMilliseconds;

            if (vfxCooldownElapsed &&
                TryPlayDeltaGrowthVfx(
                    actor,
                    settings,
                    false) == null)
            {
                charState.LastDeltaGrowthVfxTick = currentTick;
            }

            if (actorGroup == SCActorGroup.Self)
            {
                long animationCooldownMilliseconds =
                    (long)(settings.DeltaGrowthAnimationCooldownSeconds * 1000f);
                bool animationCooldownElapsed =
                    animationCooldownMilliseconds <= 0 ||
                    charState.LastDeltaGrowthAnimationTick == 0 ||
                    currentTick - charState.LastDeltaGrowthAnimationTick >=
                    animationCooldownMilliseconds;

                if (animationCooldownElapsed && settings.EnableDeltaGrowthAnimation &&
                    TryPlayDeltaGrowthAnimation(actor, settings, false) == null)
                {
                    charState.LastDeltaGrowthAnimationTick = currentTick;
                }
            }
        }

        if (!settings.GrowthFromDelta || disable || settings.GrowthOvershootPercent <= 0f)
            charState.OvershootPulse = default;

        // Ambient decay is exclusive to Growth From Delta.
        if (settings.GrowthFromDelta &&
            !proximityActive && !digesting &&
            charState.PendingGrowth <= 0f &&
            !charState.OvershootPulse.IsActive &&
            !triggerOvershoot &&
            charState.GrowthMultiplier > 1.0f)
        {
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
            charState.Aether.Decay(shrinkAmount);
        }

        float intendedTargetScale = disable
            ? charState.PlayerScale
            : settings.GrowthFromDelta
                ? charState.PlayerScale * charState.GrowthMultiplier
                : inactiveForCombat
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
                charState.PlayerScale * charState.Aether.Cap(settings.DeltaMaxScaleMultiplier);
            intendedTargetScale = Math.Min(
                intendedTargetScale,
                maximumAllowedScale);
        }

        if (triggerOvershoot)
        {
            float pulseReference = settings.ScaleGrowthWithSize
                ? Math.Max(1f, growthMultiplierBeforeRelease) : 1f;
            charState.OvershootPulse.Start(
                previousScale,
                charState.PlayerScale * pulseReference * GrowthOvershootPulse.BoostSmallRelease(
                    pulseGrowthAmount / pulseReference, settings.GrowthOvershootSmallBoost,
                    settings.GrowthOvershootBoostRangePercent) * settings.GrowthOvershootPercent / 100f,
                settings.GrowthOvershootRiseSeconds,
                settings.GrowthOvershootSettleSeconds,
                settings.GrowthOvershootRiseCurve,
                settings.GrowthOvershootReturnCurve,
                settings.AccumulatorDelaySeconds,
                deltaSeconds);
        }

        if (charState.OvershootPulse.IsActive)
        {
            // The peak may exceed the cap; only its settled target is capped.
            scale = charState.OvershootPulse.Update(deltaSeconds, intendedTargetScale);
        }
        else
        {
            scale = float.Lerp(previousScale, intendedTargetScale, settings.Speed / 100f);
            scale = Math.Min(scale, maximumAllowedScale);
        }
        if (settings.GrowthFromDelta) scale = Math.Max(charState.PlayerScale, scale);
        float appliedMultiplier = scale / charState.PlayerScale;
        draw->Scale = new Vector3(
            charState.Transform.X.Apply(draw->Scale.X, charState.Transform.X.Baseline * appliedMultiplier),
            charState.Transform.Y.Apply(draw->Scale.Y, charState.Transform.Y.Baseline * appliedMultiplier),
            charState.Transform.Z.Apply(draw->Scale.Z, charState.Transform.Z.Baseline * appliedMultiplier));
        actor->Scale = charState.Transform.ActorScale.Apply(actor->Scale,
            charState.Transform.ActorScale.Baseline * appliedMultiplier);
        charState.ObservedScale = draw->Scale.Y;

        float visibleScaleMultiplier =
            charState.PlayerScale > 0f
                ? scale / charState.PlayerScale
                : 1f;

        // Persistent self-only lift. Combat/accumulator state does not gate it.
        // Self uses Customize+ root translation; other actors use DrawOffset.
        float desiredHeightOffset = actorGroup == SCActorGroup.Self && !disable
            ? Configuration.SelfFlatHeightOffset : 0f;
        if (settings.GrowthFromDelta && !charState.Transform.ScaleConflict &&
            settings.EnableDeltaHeightOffset &&
            charState.PlayerScale > 0f)
        {
            // Follow the visible scale so height also follows overshoot, normal
            // ambient decay, and the faster out-of-combat return.
            desiredHeightOffset +=
                Math.Max(0f, visibleScaleMultiplier - 1f) *
                settings.DeltaHeightOffsetPerScale;
        }

        if (actorGroup == SCActorGroup.Self)
        {
            charState.SelfRootHeightOffset = desiredHeightOffset;
        }
        else if (charState.HasDrawOffset || charState.Transform.Height.Owned || desiredHeightOffset > 0f)
        {
            ApplyHeightOffset(actor, ref charState, desiredHeightOffset);
        }

        charState.PreviousHealth = health;
        charState.PreviousScale = scale;
        CharacterIdToLastScaleMap[actor->EntityId] = charState;
    }

    internal unsafe string TestDeltaGrowthSound(GrowthSettings settings)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return "Cannot test: the local player is unavailable.";
        }

        string? error = TryPlayDeltaGrowthSound(
            (Character*)localPlayer.Address,
            settings,
            true,
            TryGetCharacterState((Character*)localPlayer.Address, out var soundState)
                ? soundState.GrowthMultiplier : 1f);
        if (error != null)
        {
            return error;
        }

        int soundCount = GetScdSoundCount(settings.DeltaGrowthSoundPath);
        return $"Playback request accepted. SCD entries: {soundCount}; " +
               $"requested index: {settings.DeltaGrowthSoundIndex}.";
    }

    private unsafe string? TryPlayDeltaGrowthSound(
        Character* actor,
        GrowthSettings settings,
        bool ignoreEnabled,
        float settledScale = 1f)
    {
        if ((!ignoreEnabled && !settings.EnableDeltaGrowthSound) ||
            settings.DeltaGrowthSoundVolume <= 0f)
        {
            return "Sound is disabled or its volume is zero.";
        }

        string path = settings.DeltaGrowthSoundPath.Trim().Replace('\\', '/');
        if (!path.StartsWith("sound/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
        {
            return "Invalid path: use a game path beginning with sound/ and ending in .scd.";
        }

        if (!DataManager.FileExists(path))
        {
            return $"SCD was not found in the game data: {path}";
        }

        int soundCount = GetScdSoundCount(path);
        if (soundCount <= 0)
        {
            return "The SCD was found, but it contains no playable sound entries.";
        }

        if (settings.DeltaGrowthSoundIndex >= soundCount)
        {
            return $"Sound index {settings.DeltaGrowthSoundIndex} is outside this " +
                   $"SCD's range of 0 to {soundCount - 1}.";
        }

        var position = actor->GameObject.Position;
        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            return "The actor has an invalid world position.";
        }

        return GrowthSoundPlayer.TryPlay(
            path,
            settings.DeltaGrowthSoundIndex,
            SoundVolumeScaling.ForSize(settings.DeltaGrowthSoundSizeDrivenVolume,
                settings.DeltaGrowthSoundVolume, settledScale,
                settings.DeltaGrowthSoundVolumeGainPerScale, settings.DeltaGrowthSoundMaximumVolume),
            position,
            SoundPlaybackRate.ForSize(settings.DeltaGrowthSoundSizeDrivenRate, settledScale,
                settings.DeltaGrowthSoundRateDropPerScale, settings.DeltaGrowthSoundMinimumRate));
    }

    private static int GetScdSoundCount(string path)
    {
        // Standard SCD files have a 0x30-byte header followed by a little-endian
        // Int16 sound-entry count. Reading only that count avoids shipping a full
        // SCD parser merely to validate a configured index.
        byte[]? data = DataManager.GetFile(path)?.Data;
        if (data == null || data.Length < 0x32)
        {
            return 0;
        }

        return BitConverter.ToUInt16(data, 0x30);
    }

    internal string CrystalVfxStatus(GrowthSettings settings) =>
        GrowthVfxPlayer.SourceStatus(settings.AetherSourceVfxPath, settings.AetherSourceVfxAttached);

    internal unsafe string TestDeltaGrowthVfx(GrowthSettings settings)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return "Cannot test: the local player is unavailable.";
        }

        var actor = (Character*)localPlayer.Address;
        string? error = TryPlayDeltaGrowthVfx(
            actor,
            settings,
            true);
        return error ?? "Actor-root VFX created. It will be removed after the configured duration.";
    }

    private unsafe string? TryPlayDeltaGrowthVfx(
        Character* actor,
        GrowthSettings settings,
        bool ignoreEnabled)
    {
        if (!ignoreEnabled && !settings.EnableDeltaGrowthVfx)
        {
            return "Growth VFX is disabled.";
        }

        settings.ValidateGrowthLayers();
        var paths = new List<(string Path, float Duration, bool Restart, int Layer)>();
        if (!string.IsNullOrWhiteSpace(settings.DeltaGrowthVfxPath))
            paths.Add((settings.DeltaGrowthVfxPath, settings.DeltaGrowthVfxDurationSeconds, settings.DeltaGrowthVfxRestart, 0));
        for (int i = 0; i < settings.AdditionalGrowthVfxPaths.Count && i < 15; i++)
            if (!string.IsNullOrWhiteSpace(settings.AdditionalGrowthVfxPaths[i]))
                paths.Add((settings.AdditionalGrowthVfxPaths[i], settings.AdditionalGrowthVfxDurations[i], settings.AdditionalGrowthVfxRestart[i], i + 1));
        if (paths.Count == 0) return "Enter an AVFX path.";
        foreach (var layer in paths)
        {
            string path = layer.Path.Trim().Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) ||
                !path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
                return $"Layer {layer.Layer + 1}: invalid AVFX path.";
            if (!DataManager.FileExists(path)) return $"Layer {layer.Layer + 1}: AVFX not found: {path}";
        }
        string? firstError = null;
        foreach (var layer in paths)
        {
            string? error = GrowthVfxPlayer.TryPlay((nint)actor, layer.Path,
                layer.Duration, layer.Layer, actor->EntityId, layer.Restart);
            firstError ??= error;
        }
        return firstError;
    }

    internal unsafe string TestDeltaGrowthAnimation(GrowthSettings settings, string? selectedPath = null)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return "Cannot test: the local player is unavailable.";
        }

        string? error = TryPlayDeltaGrowthAnimation(
            (Character*)localPlayer.Address,
            settings,
            true, selectedPath);
        if (error != null)
        {
            return error;
        }

        return "Animation timeline started locally.";
    }

    private unsafe string? TryPlayDeltaGrowthAnimation(
        Character* actor,
        GrowthSettings settings,
        bool ignoreEnabled,
        string? selectedPath = null)
    {
        if (!ignoreEnabled && !settings.EnableDeltaGrowthAnimation)
        {
            return "Growth animation is disabled.";
        }

        var localPlayer = ObjectTable.LocalPlayer;
        if (actor == null ||
            localPlayer == null ||
            localPlayer.Address != (nint)actor)
        {
            return "Growth animations can only be played on the local player.";
        }

        if (actor->Mode != CharacterModes.Normal)
        {
            return "The local player is not in a normal animation state. " +
                   "The growth animation was not allowed to interrupt it.";
        }

        string? chosen = selectedPath ?? GrowthAnimationChoice.Select(settings.GetGrowthAnimationChoices(), Random.Shared.NextDouble());
        if (chosen == null) return "None selected.";
        if (!TryResolveActionTimeline(
                chosen,
                out ushort actionTimelineId,
                out string error))
        {
            return error;
        }

        actor->Timeline.PlayActionTimeline(actionTimelineId);
        return null;
    }

    private static bool TryResolveActionTimeline(
        string configuredPath,
        out ushort actionTimelineId,
        out string error)
    {
        actionTimelineId = 0;
        error = string.Empty;

        string path = configuredPath?.Trim().Replace('\\', '/') ?? string.Empty;
        if (path.Length == 0 ||
            path.StartsWith('/') ||
            path.Contains("..", StringComparison.Ordinal) ||
            !path.EndsWith(".tmb", StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid path: paste a game ActionTimeline path ending in .tmb.";
            return false;
        }

        string pathWithoutExtension = path[..^4].TrimEnd('/');
        uint bestRowId = 0;
        int bestKeyLength = -1;

        foreach (var timeline in DataManager.GetExcelSheet<ActionTimeline>())
        {
            string key = timeline.Key.ExtractText().Trim().Replace('\\', '/');
            if (key.Length == 0)
            {
                continue;
            }

            bool matches =
                pathWithoutExtension.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                pathWithoutExtension.EndsWith(
                    "/" + key,
                    StringComparison.OrdinalIgnoreCase);
            if (matches && key.Length > bestKeyLength)
            {
                bestRowId = timeline.RowId;
                bestKeyLength = key.Length;
            }
        }

        if (bestRowId == 0)
        {
            error = "No ActionTimeline row matches that TMB path. Use a path copied " +
                    "from VFXEditor's TMB selector, not an arbitrary local file path.";
            return false;
        }

        if (bestRowId > ushort.MaxValue)
        {
            error = $"ActionTimeline row {bestRowId} is outside the playable range.";
            return false;
        }

        actionTimelineId = (ushort)bestRowId;
        return true;
    }

    private unsafe void ApplyHeightOffset(
        Character* actor,
        ref SCCharacterState charState,
        float desiredHeightOffset)
    {
        var currentOffset = actor->GameObject.DrawOffset;
        float target = charState.Transform.Height.Baseline + desiredHeightOffset;
        float result = charState.Transform.Height.Apply(currentOffset.Y, target);
        if (result != currentOffset.Y)
            actor->GameObject.SetDrawOffset(currentOffset.X, result, currentOffset.Z);
        charState.BaseDrawOffsetY = charState.Transform.Height.Baseline;
        charState.LastAppliedDrawOffsetY = result;
        charState.HasDrawOffset = charState.Transform.Height.Owned;
        sessionHeights.SaveOwnership(HeightKey(actor), charState.Transform.Height);

    }

    private unsafe void RefreshDrainSources(bool allowed)
    {
        drainSources.BeginFrame();
        if (!allowed) return;
        foreach (var obj in ObjectTable)
        {
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            if (native == null) continue;
            var kind = native->ObjectKind;
            bool player = kind == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc;
            bool npc = kind is FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.BattleNpc
                or FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.EventNpc
                or FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Companion
                or FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Retainer;
            if ((!player && !npc) || native->IsDead() || native->DrawObject == null) continue;
            var p = native->Position;
            drainSources.Add(new SizeDrainSources.Source(obj.Address, obj.EntityId,
                new System.Numerics.Vector3(p.X, p.Y, p.Z), obj.Name.TextValue, player));
        }
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
                Configuration.IsMonsterTracked(gameObject.Name.TextValue))
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
        if (!TryGetCharacterState(actor, out var state)) return;
        RestoreOwnedTransforms(actor, ref state);
        CharacterIdToLastScaleMap.Remove(actor->EntityId);
    }

    private unsafe void RestoreOwnedTransforms(Character* actor, ref SCCharacterState state)
    {
        if (!state.Transform.Ready) return;
        var draw = (CharacterBase*)actor->DrawObject;
        if (draw != null && state.Transform.DrawAddress == (nint)draw)
            draw->Scale = new Vector3(state.Transform.X.Restore(draw->Scale.X),
                state.Transform.Y.Restore(draw->Scale.Y), state.Transform.Z.Restore(draw->Scale.Z));
        actor->Scale = state.Transform.ActorScale.Restore(actor->Scale);
        var offset = actor->GameObject.DrawOffset;
        float y = state.Transform.Height.Restore(offset.Y);
        if (y != offset.Y) actor->GameObject.SetDrawOffset(offset.X, y, offset.Z);
        sessionHeights.SaveOwnership(HeightKey(actor), state.Transform.Height);
    }

    private unsafe string HeightKey(Character* actor)
    {
        if (ObjectTable.LocalPlayer?.Address == (nint)actor) return "self";
        var obj = ObjectTable.SearchByEntityId(actor->EntityId);
        return obj is IPlayerCharacter player ? "player:" + GetPlayerIdentity(player)
            : $"actor:{actor->EntityId:X}:{(nint)actor:X}";
    }

    internal unsafe void RecheckBaselines(SCActorGroup group)
    {
        foreach (var entry in new List<KeyValuePair<uint, SCCharacterState>>(CharacterIdToLastScaleMap))
        {
            if (entry.Value.ActorGroup != group) continue;
            var obj = ObjectTable.SearchByEntityId(entry.Key);
            var state = entry.Value;
            if (obj is not IBattleChara || obj.Address != state.ActorAddress) continue;
            var actor = (Character*)obj.Address;
            var draw = (CharacterBase*)actor->DrawObject;
            if (draw == null || !TryBindTransforms(actor, draw, out var rebound)) continue;
            rebound.Height = sessionHeights.RecheckOwnership(HeightKey(actor), actor->GameObject.DrawOffset.Y);
            state.Transform = rebound;
            state.PlayerScale = rebound.Y.Baseline;
            state.PreviousScale = state.ObservedScale = draw->Scale.Y;
            // Resume smoothly from the current visible value. Keep earned and
            // pending growth, but cancel the interrupted visual-only pulse.
            state.OvershootPulse = default;
            CharacterIdToLastScaleMap[entry.Key] = state;
        }
    }

    internal string TransformDiagnostics(SCActorGroup group)
    {
        var lines = new List<string>();
        foreach (var entry in CharacterIdToLastScaleMap)
        {
            var state = entry.Value;
            if (state.ActorGroup != group || !state.Transform.Ready) continue;
            string name = ObjectTable.SearchByEntityId(entry.Key)?.Name.TextValue ?? $"Actor {entry.Key:X}";
            lines.Add($"{name}: Base {state.PlayerScale:0.000} | Current {state.ObservedScale:0.000} | " +
                $"Crystals {state.NearbyAetherSources} | Drain sources {state.NearbyDrainSources} | Growth {state.GrowthMultiplier:0.000} | Pending {state.PendingGrowth:0.000} | Reserve {state.Digestion.Growth:0.000}" +
                (group == SCActorGroup.Self ? $" | Customize+ root lift {state.SelfRootHeightOffset:0.000}" : $" | Session height {state.Transform.Height.Baseline:0.000} | Added {(state.Transform.Height.Owned ? state.Transform.Height.LastWritten - state.Transform.Height.Baseline : 0f):0.000}") +
                (state.Transform.ScaleConflict ? " | Scale paused: external change" : "") +
                (state.Transform.Height.Conflict ? " | Height paused: external change" : ""));
        }
        return lines.Count == 0 ? "No captured model." : string.Join("\n", lines);
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
