using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace SizeChange;

// Separate from the action-effect observer: failure here must not disable skill
// casts. The detour forwards every argument unchanged and only queues scalars.
internal sealed unsafe class PeriodicGrowthObserver : IDisposable
{
    private readonly IGameInteropProvider interop;
    private readonly Action<string> log;
    private Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? hook;
    private readonly PeriodicGrowthQueue events = new();
    private volatile bool active, faulted;
    private bool disposed;
    private uint territory;
    public string Status { get; private set; } = "Over time: Off";
    public string LastTick { get; set; } = string.Empty;
    public PeriodicGrowthObserver(IGameInteropProvider interop, Action<string> log)
    { this.interop = interop; this.log = log; }
    public void Fail(Exception error)
    {
        active = false; faulted = true; events.Clear();
        Status = "Over time unavailable: " + error.GetBaseException().Message;
        try { log(Status); } catch { /* Never unwind managed logging into native code. */ }
    }
    public void Retry() { faulted = false; Status = "Over time: Retry pending"; }
    public void Tick(bool enabled, uint currentTerritory)
    {
        if (currentTerritory != territory || !enabled)
        { active = false; events.Clear(); territory = currentTerritory; }
        if (disposed || faulted || !enabled) { if (!faulted) Status = "Over time: Off"; return; }
        try
        {
            if (hook == null)
            {
                nint address = (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket;
                if (address == 0) throw new InvalidOperationException("Actor-control entry point unavailable.");
                hook = interop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(address, Receive);
            }
            if (!hook.IsEnabled) hook.Enable();
            active = true; Status = "Over time: Listening";
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Receive(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
    {
        int epoch = events.Generation;
        uint receivedTerritory = territory;
        bool capture = active && !isRecorded && PeriodicGrowthTick.IsPeriodic(category);
        long receivedAt = capture ? Environment.TickCount64 : 0;
        hook!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        if (!capture || !active) return;
        try { events.Add(entityId, category, receivedTerritory, receivedAt, epoch); }
        catch (Exception ex) { Fail(ex); }
    }
    public bool TryRead(out PeriodicGrowthTick tick) => events.TryRead(territory, Environment.TickCount64, out tick);
    public void Dispose()
    {
        disposed = true; active = false; events.Clear();
        hook?.Dispose(); hook = null;
    }
}
