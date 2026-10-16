using System;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace SizeChange;

// Read-only notification capture, including log-only repeats that never change
// EmoteController.EmoteId. No actor dereferences or growth work in the detour.
internal sealed unsafe class ReceivedEmoteObserver : IDisposable
{
    private readonly IGameInteropProvider interop;
    private Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? hook;
    private readonly ReceivedEmoteQueue events = new();
    private volatile bool active, faulted;
    private bool disposed;
    private uint territory;
    private long observed;
    private string failure = string.Empty;
    internal bool Listening => active && !faulted;
    internal string Status { get; private set; } = "Received emotes: off";
    internal string Details => failure.Length > 0 ? failure : $"Server emote notifications: {Interlocked.Read(ref observed)}";
    internal ReceivedEmoteObserver(IGameInteropProvider interop) { this.interop = interop; }
    internal void Retry() { faulted = false; failure = string.Empty; Status = "Received emotes: retry pending"; }
    private void Fail(Exception error)
    {
        active = false; faulted = true; events.Clear();
        failure = error.GetBaseException().Message; Status = "Received emotes: unavailable";
    }
    internal void Tick(bool enabled, uint zone)
    {
        if (zone != territory || !enabled)
        { active = false; events.Clear(); territory = zone; }
        if (disposed || faulted || !enabled) { if (!faulted) Status = "Received emotes: off"; return; }
        try
        {
            if (hook == null)
            {
                nint address = (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket;
                if (address == 0) throw new InvalidOperationException("Actor-control entry point unavailable.");
                hook = interop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(address, Receive);
            }
            if (!hook.IsEnabled) hook.Enable();
            active = true; Status = "Received emotes: listening";
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Receive(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
    {
        int epoch = events.Generation;
        uint zone = territory;
        bool capture = active && !isRecorded && category == ReceivedEmoteEvent.Category;
        long now = capture ? Environment.TickCount64 : 0;
        // Preserve the native call, its arguments, and call count exactly.
        hook!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        if (!capture || !active) return;
        try
        {
            events.Add(entityId, arg1, targetId.Id, arg3, zone, now, epoch);
            Interlocked.Increment(ref observed);
        }
        catch (Exception ex) { Fail(ex); }
    }
    internal bool TryRead(out ReceivedEmoteEvent item) => events.TryRead(territory, Environment.TickCount64, out item);
    public void Dispose()
    {
        disposed = true; active = false; events.Clear();
        hook?.Dispose(); hook = null;
    }
}
