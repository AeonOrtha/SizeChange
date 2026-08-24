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

    private readonly Dictionary<nint, ActiveGrowthVfx> activeByVfx = new();
    private readonly Dictionary<nint, nint> activeVfxByActor = new();

    private sealed class ActiveGrowthVfx
    {
        public required nint VfxAddress { get; init; }
        public required nint ActorAddress { get; init; }
        public required long RemoveAtTick { get; init; }
        public required float BaseScale { get; init; }
        public required bool ScaleWithActor { get; init; }
    }

    public GrowthVfxPlayer()
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);

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
        };

        activeByVfx[vfxAddress] = activeVfx;
        activeVfxByActor[actorAddress] = vfxAddress;
        ApplyScale(activeVfx, actorGrowthMultiplier);
        return null;
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
            }
        }

        foreach (nint vfxAddress in expiredAddresses)
        {
            RemoveTrackedVfx(vfxAddress);
        }
    }

    public void UpdateActorScale(nint actorAddress, float actorGrowthMultiplier)
    {
        if (!activeVfxByActor.TryGetValue(actorAddress, out nint vfxAddress) ||
            !activeByVfx.TryGetValue(vfxAddress, out var activeVfx))
        {
            return;
        }

        ApplyScale(activeVfx, actorGrowthMultiplier);
    }

    private static void ApplyScale(
        ActiveGrowthVfx activeVfx,
        float actorGrowthMultiplier)
    {
        var vfx = (VfxObject*)activeVfx.VfxAddress;
        if (vfx == null)
        {
            return;
        }

        float effectiveScale = activeVfx.BaseScale;
        if (activeVfx.ScaleWithActor)
        {
            effectiveScale *= Math.Max(0.01f, actorGrowthMultiplier);
        }

        effectiveScale = Math.Clamp(effectiveScale, 0.01f, 100f);
        vfx->Scale = new Vector3(effectiveScale, effectiveScale, effectiveScale);
        vfx->UpdateTransforms(true);
    }

    private void RemoveForActor(nint actorAddress)
    {
        if (activeVfxByActor.TryGetValue(actorAddress, out nint vfxAddress))
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

        activeVfxByActor.Remove(activeVfx.ActorAddress);
        if (actorVfxRemoveHook != null)
        {
            actorVfxRemoveHook.Original((VfxObject*)vfxAddress, (char)1);
        }
    }

    private nint ActorVfxRemoveDetour(VfxObject* vfx, char a2)
    {
        nint vfxAddress = (nint)vfx;
        if (activeByVfx.Remove(vfxAddress, out var activeVfx))
        {
            activeVfxByActor.Remove(activeVfx.ActorAddress);
        }

        return actorVfxRemoveHook!.Original(vfx, a2);
    }
}
