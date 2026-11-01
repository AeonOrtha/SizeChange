using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SizeChange;

[Serializable]
public sealed class OutgoingBlendSyncSettings
{
    public bool Enabled { get; set; }
}

// Frozen, validated snapshots only. The hook may run off the framework thread;
// it never reads actors, Customize+ state, or the mutable settings there.
internal sealed class OutgoingBlendSnapshot
{
    internal readonly record struct Actor(nint Address, uint EntityId, uint Territory);
    private sealed record Live(Actor Actor, long CheckedAt);
    private sealed record Frame(Actor Actor, string Encoded, long Revision, int Bytes);
    private Live? live;
    private Frame? frame;
    private long revisions;
    public long Revision => Volatile.Read(ref frame)?.Revision ?? 0;
    public int Bytes => Volatile.Read(ref frame)?.Bytes ?? 0;

    public void Observe(Actor? actor, long now)
    {
        var old = Volatile.Read(ref live);
        if (!actor.HasValue || actor.Value.Address == 0)
        { Volatile.Write(ref live, null); Clear(); return; }
        if (old?.Actor != actor.Value) Clear();
        Volatile.Write(ref live, new(actor.Value, now));
    }

    public void Capture(string json)
    {
        var current = Volatile.Read(ref live) ?? throw new InvalidOperationException("Self is unavailable.");
        // Do not serve an old snapshot if its attempted replacement is invalid.
        Clear();
        CustomizeProfileBlend.Parse(json);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Snapshot exceeds 8 MB.");
        var next = new Frame(current.Actor, Convert.ToBase64String(bytes), Interlocked.Increment(ref revisions), bytes.Length);
        Volatile.Write(ref frame, next);
    }

    public bool TryRead(nint address, long now, out string encoded, out long revision)
    {
        encoded = string.Empty; revision = 0;
        var current = Volatile.Read(ref live);
        var saved = Volatile.Read(ref frame);
        if (current == null || saved == null || address == 0 || current.Actor.Address != address ||
            saved.Actor != current.Actor || now < current.CheckedAt || now - current.CheckedAt > 1000) return false;
        encoded = saved.Encoded; revision = saved.Revision;
        return true;
    }

    public void Clear() => Volatile.Write(ref frame, null);
}

// Optional outgoing boundary. Remove this file and its Plugin/UI/settings wiring
// to remove it. No server changes, persistent C+ edits, or automatic sync events.
internal sealed class LightlessBlendSync : IDisposable
{
    internal const string CallerTypeName = "LightlessSync.Interop.Ipc.IpcCallerCustomize";
    private static LightlessBlendSync? active;
    private readonly string assemblyPath;
    private readonly Action<string> log;
    private readonly Func<Type[]> findTypes;
    private readonly OutgoingBlendSnapshot snapshot = new();
    private PlayerHeightPatchRuntime? runtime;
    private MethodInfo? method;
    private PropertyInfo? apiAvailable;
    private Type? callerType;
    private volatile bool accepting;
    private volatile bool failed;
    private bool cleanupFailed;
    private bool disposed;
    private long nextCheck;
    private int captureRequested;
    private long reads, served, lastRevision;
    public string Status { get; private set; } = "Off";
    public string CaptureStatus { get; private set; } = "No snapshot";
    public string RuntimeVersion { get; private set; } = string.Empty;
    public string Counters => $"Reads: {Interlocked.Read(ref reads)} | Served: {Interlocked.Read(ref served)} | Last snapshot: {Interlocked.Read(ref lastRevision)}";
    public long SnapshotRevision => snapshot.Revision;
    public bool Installed => method != null && accepting;

    public LightlessBlendSync(string assemblyPath, Action<string> log, Func<Type[]>? findTypes = null)
    {
        this.assemblyPath = assemblyPath; this.log = log;
        this.findTypes = findTypes ?? (() => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "LightlessSync")
            .Select(a => a.GetType(CallerTypeName)).Where(t => t != null).Cast<Type>().ToArray());
    }

    public void RequestCapture() => Interlocked.Exchange(ref captureRequested, 1);
    public void ClearSnapshot()
    {
        Interlocked.Exchange(ref captureRequested, 0);
        snapshot.Clear(); CaptureStatus = "No snapshot; resync to restore normal appearance";
    }
    public void Retry()
    { failed = false; cleanupFailed = false; nextCheck = 0; Status = "Retry pending"; }

    // Called only on the framework thread. null actor also clears stale data on
    // logout, zoning, disabling Self/blending, and failed local frame production.
    public void Tick(bool enabled, OutgoingBlendSnapshot.Actor? actor, string? appliedFrame)
    {
        if (disposed) return;
        try
        {
            long now = Environment.TickCount64;
            if (!enabled)
            {
                snapshot.Observe(null, now); Interlocked.Exchange(ref captureRequested, 0);
                if (cleanupFailed) return; // Explicit Retry or disposal retries failed cleanup.
                Stop(); failed = false; nextCheck = 0; Status = "Off"; CaptureStatus = "No snapshot"; return;
            }
            long previousRevision = snapshot.Revision;
            snapshot.Observe(appliedFrame == null ? null : actor, now);
            if (previousRevision != 0 && snapshot.Revision == 0)
                CaptureStatus = "Snapshot cleared; capture again when ready";
            if (now >= nextCheck && !failed)
            {
                nextCheck = now + 1000;
                var types = findTypes();
                if (types.Length != 1)
                {
                    Stop(); Status = $"Lightless runtime types: {types.Length}; expected 1";
                }
                else if (!Installed || callerType != types[0])
                { Stop(); Install(types[0]!); }
            }
            if (Interlocked.Exchange(ref captureRequested, 0) != 0)
            {
                snapshot.Clear();
                if (!Installed) CaptureStatus = "Hook unavailable; retry first";
                else if (appliedFrame == null || !actor.HasValue) CaptureStatus = "No active Self blend to capture";
                else
                {
                    try
                    {
                        snapshot.Capture(appliedFrame);
                        CaptureStatus = $"Snapshot {snapshot.Revision}: {snapshot.Bytes:N0} bytes. Resync in Lightless.";
                    }
                    catch (Exception ex) { CaptureStatus = "Capture failed: " + ex.GetBaseException().Message; }
                }
            }
        }
        catch (Exception ex)
        {
            accepting = false; snapshot.Observe(null, Environment.TickCount64); failed = true;
            if (!enabled) cleanupFailed = true;
            Status = "Unavailable: " + ex.GetBaseException().Message;
            log(Status);
        }
    }

    internal static (MethodInfo Method, PropertyInfo Available) CheckSignature(Type type)
    {
        var candidate = type.GetMethod("GetScaleAsync", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(nint) }, null);
        var available = type.GetProperty("APIAvailable", BindingFlags.Public | BindingFlags.Instance);
        if (candidate == null || candidate.ReturnType != typeof(Task<string>) || candidate.IsGenericMethod ||
            available?.PropertyType != typeof(bool) || available.GetMethod == null || available.GetIndexParameters().Length != 0)
            throw new NotSupportedException("Lightless Customize+ reader signature changed.");
        return (candidate, available);
    }

    internal void Install(Type type)
    {
        var signature = CheckSignature(type);
        if (active != null && active != this) throw new InvalidOperationException("Another outgoing blend hook is active.");
        runtime ??= new("SizeChange.Self.LightlessBlendSnapshot", assemblyPath);
        callerType = type; apiAvailable = signature.Available; method = signature.Method;
        active = this;
        var prefix = typeof(LightlessBlendSync).GetMethod(nameof(BeforeRead), BindingFlags.NonPublic | BindingFlags.Static)!;
        try { runtime.Patch(method, prefix); }
        catch
        {
            // Keep the method handle for cleanup/retry if patching partly succeeded.
            accepting = false; throw;
        }
        accepting = true;
        RuntimeVersion = type.Assembly.GetName().Name + " " + type.Assembly.GetName().Version;
        Status = "Hook ready (manual resync)";
    }

    private static bool BeforeRead(object __instance, nint __0, ref Task<string?> __result)
    {
        var owner = active;
        if (owner == null || !owner.accepting) return true;
        Interlocked.Increment(ref owner.reads);
        try
        {
            if (owner.apiAvailable?.GetValue(__instance) is not true ||
                !owner.snapshot.TryRead(__0, Environment.TickCount64, out string encoded, out long revision)) return true;
            __result = Task.FromResult<string?>(encoded);
            Interlocked.Increment(ref owner.served);
            Interlocked.Exchange(ref owner.lastRevision, revision);
            return false;
        }
        catch
        {
            // Never break Lightless's original task or leave a partial result.
            owner.accepting = false;
            owner.failed = true;
            owner.Status = "Outgoing hook failed; normal Lightless reads resumed. Retry.";
            return true;
        }
    }

    private void Stop()
    {
        accepting = false;
        if (snapshot.Revision != 0)
        {
            snapshot.Clear();
            CaptureStatus = "Snapshot cleared; capture again when ready";
        }
        if (method != null) { runtime!.Unpatch(method); method = null; }
        if (active == this) active = null;
        callerType = null; apiAvailable = null; RuntimeVersion = string.Empty;
    }

    public void Dispose()
    {
        accepting = false; snapshot.Observe(null, Environment.TickCount64); disposed = true;
        try { Stop(); }
        catch (Exception ex) { log("Outgoing hook cleanup failed: " + ex.GetBaseException().Message); }
    }
}
