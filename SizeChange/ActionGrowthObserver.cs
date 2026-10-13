using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Numerics;

namespace SizeChange;

// Read-only observation: copy scalar identifiers, run the original exactly once,
// and let the framework update resolve recipients and apply growth. No retained
// actor/effect pointers, game writes, or growth work inside the native detour.
internal sealed unsafe class ActionGrowthObserver : IDisposable
{
    private delegate void ReceiveDelegate(uint caster, Character* actor, Vector3* position,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets);
    private readonly IGameInteropProvider interop;
    private readonly Action<string> log;
    private Hook<ReceiveDelegate>? hook;
    private readonly ConcurrentQueue<ReceivedAction> events = new();
    private int queued, generation;
    private volatile bool active, faulted;
    private uint territory;
    private bool disposed;
    public string Status { get; private set; } = "Off";
    public string LastAction { get; set; } = string.Empty;
    public ActionGrowthObserver(IGameInteropProvider interop, Action<string> log)
    { this.interop = interop; this.log = log; }
    public void Fail(Exception error)
    {
        active = false; faulted = true; Clear();
        Status = "Observer failed: " + error.GetBaseException().Message; log(Status);
    }
    public void Retry() { faulted = false; Status = "Retry pending"; }
    public void Tick(bool enabled, uint currentTerritory)
    {
        if (currentTerritory != territory || !enabled)
        {
            active = false; Interlocked.Increment(ref generation); territory = currentTerritory; Clear();
        }
        if (disposed || faulted || !enabled) { if (!faulted) Status = "Off"; return; }
        try
        {
            if (hook == null)
            {
                nint address = (nint)ActionEffectHandler.MemberFunctionPointers.Receive;
                if (address == 0) throw new InvalidOperationException("Action-effect entry point unavailable.");
                hook = interop.HookFromAddress<ReceiveDelegate>(address, Receive);
            }
            if (!hook.IsEnabled) hook.Enable();
            active = true; Status = "Listening";
        }
        catch (Exception ex) { active = false; faulted = true; Status = "Unavailable: " + ex.GetBaseException().Message; log(Status); }
    }
    // These are successful effects applied to the target. Miss/resist/immune,
    // empty entries, source-side effects and resource-only caster feedback do not
    // establish that an action successfully affected this recipient.
    private void Receive(uint caster, Character* actor, Vector3* position,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        List<ReceivedAction>? copied = null;
        int epoch = Volatile.Read(ref generation);
        try
        {
            if (active && header != null && effects != null && targets != null &&
                header->ActionType == 1 && header->ActionId > 0 && header->NumTargets is > 0 and <= 64)
            {
                copied = new();
                var unique = new HashSet<ulong>();
                for (int i = 0; i < header->NumTargets; i++)
                {
                    ulong id = ((ulong*)targets)[i];
                    bool selfTarget = id == caster;
                    bool success = false;
                    for (int j = 0; j < 8; j++)
                    {
                        var effect = effects[i].Effects[j];
                        if (ReceivedAction.Successful(effect.Type, effect.Param4, selfTarget)) { success = true; break; }
                    }
                    if (success && id != 0 && unique.Add(id)) copied.Add(new(caster, header->GlobalSequence,
                        header->ActionId, id, territory, Environment.TickCount64));
                }
            }
        }
        catch (Exception ex) { Fail(ex); }
        hook!.Original(caster, actor, position, header, effects, targets);
        if (copied == null || !active || epoch != Volatile.Read(ref generation)) return;
        foreach (var item in copied)
        {
            if (Interlocked.Increment(ref queued) > 1024) { Interlocked.Decrement(ref queued); continue; }
            events.Enqueue(item);
        }
    }
    public bool TryRead(out ReceivedAction action)
    {
        if (!events.TryDequeue(out action)) return false;
        Interlocked.Decrement(ref queued); return true;
    }
    private void Clear() { while (TryRead(out _)) {} }
    public void Dispose() { disposed = true; active = false; Interlocked.Increment(ref generation); hook?.Dispose(); hook = null; Clear(); }
}
