using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace SizeChange;

/// <summary>
/// Creates AVFX instances bound to an actor's root and keeps enough ownership
/// information to scale and explicitly remove the instances created here.
/// </summary>
internal sealed unsafe class GrowthVfxPlayer : IDisposable
{
    // Cross-checked against VFXEditor's current actor-VFX interop.
    private const string ActorVfxCreateSignature =
        "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? " +
        "48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? " +
        "0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8";

    // This signature resolves an indirection containing the actor-VFX removal
    // function, matching the resolution used by VFXEditor.
    private const string ActorVfxRemoveSignature =
        "0F 11 48 10 48 8D 05";

    private delegate VfxObject* ActorVfxCreateDelegate(
        string path,
        nint caster,
        nint target,
        float a4,
        char a5,
        ushort a6,
        char a7);

    private delegate nint ActorVfxRemoveDelegate(VfxObject* vfx, char a2);

    [Signature(ActorVfxCreateSignature)]
    private readonly ActorVfxCreateDelegate? actorVfxCreate = null;

    private Hook<ActorVfxRemoveDelegate>? actorVfxRemoveHook;
    private delegate nint StaticVfxRunDelegate(VfxObject* vfx, float time, uint flags);
    private delegate nint StaticVfxRemoveDelegate(VfxObject* vfx);
    [Signature("E8 ?? ?? ?? ?? B0 02 EB 02")]
    private readonly StaticVfxRunDelegate? staticVfxRun = null;
    [Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9",
        DetourName = nameof(StaticVfxRemoveDetour))]
    private readonly Hook<StaticVfxRemoveDelegate>? staticVfxRemoveHook = null;
    private readonly HashSet<(nint Actor, int Channel)> proximityRequested = new();
    private readonly Dictionary<(nint Actor, int Channel), (string Path, long At)> retryAfter = new();
    private readonly Dictionary<string, string> sourceErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> validPaths = new(StringComparer.Ordinal);


    private readonly Dictionary<nint, ActiveGrowthVfx> activeByVfx = new();
    private readonly Dictionary<(nint Actor, int Channel), nint> activeVfxByActor = new();

    private sealed class ActiveGrowthVfx
    {
        public required nint VfxAddress { get; init; }
        public required nint ActorAddress { get; init; }
        public int Channel { get; init; }
        public bool IsStatic { get; init; }
        public uint EntityId { get; init; }
        public string Path { get; init; } = string.Empty;
        public required long RemoveAtTick { get; init; }
        public required float BaseScale { get; set; }
        public required bool ScaleWithActor { get; init; }
        public float ActorGrowthMultiplier { get; set; } = 1f;
        public float LastAppliedScale { get; set; } = float.NaN;
        public bool CanApplyScale { get; set; }
    }

    public GrowthVfxPlayer()
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        staticVfxRemoveHook?.Enable();

        if (!Plugin.SigScanner.TryScanText(
                ActorVfxRemoveSignature,
                out nint removeSignatureAddress))
        {
            return;
        }

        try
        {
            nint removeAddressPointer = removeSignatureAddress + 7;
            nint removeAddress = Marshal.ReadIntPtr(
                removeAddressPointer + Marshal.ReadInt32(removeAddressPointer) + 4);

            actorVfxRemoveHook =
                Plugin.GameInteropProvider.HookFromAddress<ActorVfxRemoveDelegate>(
                    removeAddress,
                    ActorVfxRemoveDetour);
            actorVfxRemoveHook.Enable();
        }
        catch
        {
            actorVfxRemoveHook?.Dispose();
            actorVfxRemoveHook = null;
        }
    }

    public void Dispose()
    {
        var activeAddresses = new List<nint>(activeByVfx.Keys);
        foreach (nint vfxAddress in activeAddresses)
        {
            RemoveTrackedVfx(vfxAddress);
        }

        actorVfxRemoveHook?.Dispose();
        actorVfxRemoveHook = null;
        staticVfxRemoveHook?.Dispose();
    }

    public string? TryPlay(
        nint actorAddress,
        string path,
        float durationSeconds,
        float baseScale,
        bool scaleWithActor,
        float actorGrowthMultiplier)
    {
        if (actorVfxCreate == null || actorVfxRemoveHook == null)
        {
            return "The native actor-VFX functions could not be located.";
        }

        // Keep at most one growth effect attached to an actor. A fresh trigger
        // replaces the previous instance and begins a new configured lifetime.
        RemoveForActor(actorAddress);

        VfxObject* vfx = actorVfxCreate(
            path,
            actorAddress,
            actorAddress,
            -1f,
            (char)0,
            0,
            (char)0);
        if (vfx == null)
        {
            return "FFXIV did not create an actor VFX from the requested AVFX.";
        }

        nint vfxAddress = (nint)vfx;
        long durationMilliseconds = Math.Max(
            1L,
            (long)(durationSeconds * 1000f));
        var activeVfx = new ActiveGrowthVfx
        {
            VfxAddress = vfxAddress,
            ActorAddress = actorAddress,
            RemoveAtTick = Environment.TickCount64 + durationMilliseconds,
            BaseScale = baseScale,
            ScaleWithActor = scaleWithActor,
            ActorGrowthMultiplier = actorGrowthMultiplier,
        };

        activeByVfx[vfxAddress] = activeVfx;
        activeVfxByActor[(actorAddress, 0)] = vfxAddress;
        return null;
    }

    // Channel 0 is the existing timed growth effect; 1 and 2 are persistent
    // proximity effects on the character and source respectively.
    public void BeginProximityFrame() => proximityRequested.Clear();

    public void KeepProximity(nint address, uint entityId, string configuredPath, bool source, Vector3 position,
        bool attachToSource = false, float sourceScale = 1f, float sourceHeight = 0f)
    {
        string path = configuredPath.Trim().Replace('\\', '/');
        if (!IsValidProximityPath(path)) return;
        bool isStatic = source && !attachToSource;
        if (isStatic) position.Y += sourceHeight;
        int channel = source ? 2 : 1;
        var key = (address, channel);
        // A crystal shared by profiles gets one effect, with Self taking priority.
        if (!proximityRequested.Add(key)) return;
        if (activeVfxByActor.TryGetValue(key, out var oldAddress) && activeByVfx.TryGetValue(oldAddress, out var old))
        {
            if (old.Path == path && old.EntityId == entityId && old.IsStatic == isStatic)
            {
                if (isStatic) SetSourcePosition((VfxObject*)oldAddress, position);
                if (source)
                {
                    old.BaseScale = sourceScale;
                    if (old.CanApplyScale) ApplyScale(old);
                }
                return;
            }
            RemoveTrackedVfx(oldAddress);
            retryAfter.Remove(key);
        }
        long now = Environment.TickCount64;
        if (retryAfter.TryGetValue(key, out var retry) && retry.Path == path && now < retry.At) return;
        retryAfter[key] = (path, now + 1000);
        VfxObject* vfx;
        if (isStatic)
        {
            if (staticVfxRun == null || staticVfxRemoveHook == null)
            { sourceErrors[path] = "World VFX unavailable."; return; }
            vfx = VfxObject.Create(path, "Client.System.Scheduler.Instance.VfxObject");
            if (vfx == null) { sourceErrors[path] = "VFX creation failed."; return; }
            vfx->Rotation = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.Identity;
            vfx->Scale = new Vector3(sourceScale, sourceScale, sourceScale);
            SetSourcePosition(vfx, position);
            staticVfxRun(vfx, 0f, 0xFFFFFFFF);
        }
        else
        {
            if (actorVfxCreate == null || actorVfxRemoveHook == null)
            { if (source) sourceErrors[path] = "Attached VFX unavailable."; return; }
            vfx = actorVfxCreate(path, address, address, -1f, (char)0, 0, (char)0);
            if (vfx == null) { if (source) sourceErrors[path] = "VFX creation failed."; return; }
        }
        if (source) sourceErrors.Remove(path);
        nint vfxAddress = (nint)vfx;
        activeByVfx[vfxAddress] = new ActiveGrowthVfx
        {
            VfxAddress = vfxAddress, ActorAddress = address, Channel = channel,
            EntityId = entityId, Path = path, IsStatic = isStatic, RemoveAtTick = long.MaxValue,
            BaseScale = source ? sourceScale : 1f, ScaleWithActor = false,
            // Character proximity effects keep their own scale; crystal scale is configurable.
            CanApplyScale = !source || isStatic,
        };
        activeVfxByActor[key] = vfxAddress;
    }

    public string SourceStatus(string configuredPath, bool attached)
    {
        string path = configuredPath.Trim().Replace('\\', '/');
        if (path.Length == 0) return "Choose an AVFX.";
        if (!IsValidProximityPath(path)) return "AVFX invalid or not found.";
        if (attached ? actorVfxCreate == null || actorVfxRemoveHook == null : staticVfxRun == null || staticVfxRemoveHook == null)
            return "VFX functions unavailable.";
        int count = 0;
        foreach (var active in activeByVfx.Values)
            if (active.Channel == 2 && active.Path == path) count++;
        if (count > 0) return $"Active instances: {count}";
        return sourceErrors.TryGetValue(path, out var error) ? error : "Waiting for exposure.";
    }

    private bool IsValidProximityPath(string path)
    {
        if (validPaths.TryGetValue(path, out bool valid)) return valid;
        valid = path.Length > 0 && !path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) &&
            path.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase) && Plugin.DataManager.FileExists(path);
        validPaths[path] = valid;
        return valid;
    }

    private static void SetSourcePosition(VfxObject* vfx, Vector3 position)
    {
        vfx->Position = position;
        vfx->UpdateTransforms(true);
    }

    public void EndProximityFrame()
    {
        var expired = new List<nint>();
        foreach (var pair in activeByVfx)
            if (pair.Value.Channel != 0 && !proximityRequested.Contains((pair.Value.ActorAddress, pair.Value.Channel)))
                expired.Add(pair.Key);
        foreach (var address in expired) RemoveTrackedVfx(address);
        foreach (var key in new List<(nint Actor, int Channel)>(retryAfter.Keys))
            if (!proximityRequested.Contains(key)) retryAfter.Remove(key);
    }

    private nint StaticVfxRemoveDetour(VfxObject* vfx)
    {
        if (activeByVfx.Remove((nint)vfx, out var active))
            activeVfxByActor.Remove((active.ActorAddress, active.Channel));
        return staticVfxRemoveHook!.Original(vfx);
    }

    public void Update()
    {
        long currentTick = Environment.TickCount64;
        var expiredAddresses = new List<nint>();
        foreach (var activeEntry in activeByVfx)
        {
            if (currentTick >= activeEntry.Value.RemoveAtTick)
            {
                expiredAddresses.Add(activeEntry.Key);
                continue;
            }

            // Actor VFX creation returns before every internal graphics object is
            // guaranteed to be ready for transform changes. Defer the first scale
            // write until the following framework update.
            if (!activeEntry.Value.CanApplyScale)
            {
                activeEntry.Value.CanApplyScale = true;
                ApplyScale(activeEntry.Value);
            }
        }

        foreach (nint vfxAddress in expiredAddresses)
        {
            RemoveTrackedVfx(vfxAddress);
        }
    }

    public void UpdateActorScale(nint actorAddress, float actorGrowthMultiplier)
    {
        if (!activeVfxByActor.TryGetValue((actorAddress, 0), out nint vfxAddress) ||
            !activeByVfx.TryGetValue(vfxAddress, out var activeVfx))
        {
            return;
        }

        activeVfx.ActorGrowthMultiplier = actorGrowthMultiplier;
        if (activeVfx.CanApplyScale)
        {
            ApplyScale(activeVfx);
        }
    }

    private static void ApplyScale(ActiveGrowthVfx activeVfx)
    {
        var vfx = (VfxObject*)activeVfx.VfxAddress;
        if (vfx == null)
        {
            return;
        }

        float effectiveScale = activeVfx.BaseScale;
        if (activeVfx.ScaleWithActor)
        {
            effectiveScale *= Math.Max(
                0.01f,
                activeVfx.ActorGrowthMultiplier);
        }

        effectiveScale = Math.Clamp(effectiveScale, 0.01f, 100f);
        if (float.IsFinite(activeVfx.LastAppliedScale) &&
            MathF.Abs(activeVfx.LastAppliedScale - effectiveScale) < 0.0001f)
        {
            return;
        }

        vfx->Scale = new Vector3(effectiveScale, effectiveScale, effectiveScale);
        activeVfx.LastAppliedScale = effectiveScale;
    }

    private void RemoveForActor(nint actorAddress)
    {
        if (activeVfxByActor.TryGetValue((actorAddress, 0), out nint vfxAddress))
        {
            RemoveTrackedVfx(vfxAddress);
        }
    }

    private void RemoveTrackedVfx(nint vfxAddress)
    {
        if (!activeByVfx.Remove(vfxAddress, out var activeVfx))
        {
            return;
        }

        activeVfxByActor.Remove((activeVfx.ActorAddress, activeVfx.Channel));
        if (activeVfx.IsStatic)
            staticVfxRemoveHook?.Original((VfxObject*)vfxAddress);
        else if (actorVfxRemoveHook != null)
            actorVfxRemoveHook.Original((VfxObject*)vfxAddress, (char)1);
    }

    private nint ActorVfxRemoveDetour(VfxObject* vfx, char a2)
    {
        nint vfxAddress = (nint)vfx;
        if (activeByVfx.Remove(vfxAddress, out var activeVfx))
        {
            activeVfxByActor.Remove((activeVfx.ActorAddress, activeVfx.Channel));
        }

        return actorVfxRemoveHook!.Original(vfx, a2);
    }
}
