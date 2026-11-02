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
    internal const string CaptureInterfaceName = "LightlessSync.Interop.Ipc.ICustomizePlusCharacterIpc";
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
    public string CompatibilityDetails { get; private set; } = "No Lightless runtime inspected yet.";
    public string Diagnostics => $"SizeChange {typeof(LightlessBlendSync).Assembly.GetName().Version}\n{Status}\n{Counters}\n{CompatibilityDetails}";
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
                    CompatibilityDetails = $"Expected type: {CallerTypeName}\nMatches: {types.Length}\n" +
                        string.Join("\n", types.Select(t => t.Assembly.GetName().FullName));
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
            CaptureStatus = "Hook not installed; copy diagnostics";
            log(Diagnostics);
        }
    }

    internal static (MethodInfo Method, PropertyInfo Available) CheckSignature(Type type)
    {
        MethodInfo candidate;
        var contracts = type.GetInterfaces().Where(i => i.FullName == CaptureInterfaceName).ToArray();
        if (contracts.Length != 0)
        {
            if (contracts.Length != 1) throw new NotSupportedException("Ambiguous Customize+ capture interface. Copy diagnostics.");
            // Lightless 3.3.1.1 explicitly implements this interface. Resolve its
            // concrete target instead of patching the abstract interface or
            // guessing a private method by a substring of its name.
            var contract = contracts[0];
            var captures = contract.GetMethods().Where(m => m.Name == "CaptureScaleAsync" && !m.IsGenericMethod &&
                m.ReturnType == typeof(ValueTask<string>) && m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(nint) &&
                m.GetParameters()[1].ParameterType == typeof(CancellationToken)).ToArray();
            if (captures.Length != 1)
                throw new NotSupportedException("Expected ValueTask<String> ICustomizePlusCharacterIpc.CaptureScaleAsync(IntPtr, CancellationToken). Copy diagnostics.");
            var map = type.GetInterfaceMap(contract);
            int index = Array.IndexOf(map.InterfaceMethods, captures[0]);
            if (index < 0) throw new NotSupportedException("Customize+ capture interface target is missing. Copy diagnostics.");
            candidate = map.TargetMethods[index];
            if (candidate.IsAbstract || candidate.IsStatic || candidate.ContainsGenericParameters || candidate.DeclaringType?.IsInterface == true ||
                candidate.ReturnType != typeof(ValueTask<string>) || candidate.GetMethodBody() == null)
                throw new NotSupportedException("Customize+ capture implementation cannot be patched. Copy diagnostics.");
        }
        else
        {
            // Retain the older Lightless reader without accepting unverified
            // overloads or return types.
            var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "GetScaleAsync" && !m.IsGenericMethod &&
                    m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(nint)).ToArray();
            if (candidates.Length != 1)
                throw new NotSupportedException($"No supported capture interface; expected public GetScaleAsync(IntPtr), found {candidates.Length} matching methods. Copy diagnostics.");
            candidate = candidates[0];
            if (candidate.ReturnType != typeof(Task<string>))
                throw new NotSupportedException($"GetScaleAsync returns {TypeName(candidate.ReturnType)}; expected Task<String>. Copy diagnostics.");
        }
        var available = type.GetProperty("APIAvailable", BindingFlags.Public | BindingFlags.Instance);
        if (available == null)
            throw new NotSupportedException("Missing public APIAvailable property. Copy diagnostics.");
        if (available.PropertyType != typeof(bool) || available.GetMethod?.IsPublic != true || available.GetIndexParameters().Length != 0)
            throw new NotSupportedException("APIAvailable must have a public Boolean getter. Copy diagnostics.");
        return (candidate, available);
    }

    private static string TypeName(Type type) => type.IsGenericType
        ? (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0] + "<" +
            string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">"
        : type.FullName ?? type.Name;

    // Inspect metadata only: never invoke unknown methods/getters or log JSON,
    // actor identities, credentials, or runtime field values.
    internal static string DescribeRuntime(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var text = new StringBuilder();
        text.AppendLine("Assembly: " + type.Assembly.GetName().FullName);
        text.AppendLine("Type: " + type.FullName);
        text.AppendLine("Supported: ValueTask<String> ICustomizePlusCharacterIpc.CaptureScaleAsync(IntPtr, CancellationToken), or public Task<String> GetScaleAsync(IntPtr); public Boolean APIAvailable { get; }");
        text.AppendLine("Observed methods:");
        foreach (var m in type.GetMethods(flags).Where(m => m.DeclaringType != typeof(object) && !m.IsSpecialName)
                     .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.GetParameters().Length).Take(64))
            text.AppendLine($"  {(m.IsPublic ? "public" : "non-public")} {(m.IsStatic ? "static" : "instance")} {TypeName(m.ReturnType)} {m.Name}" +
                (m.IsGenericMethod ? "<generic>" : "") + "(" + string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ")");
        text.AppendLine("Observed availability members:");
        foreach (var p in type.GetProperties(flags).Where(p => p.Name.Contains("Available", StringComparison.OrdinalIgnoreCase)).Take(16))
            text.AppendLine($"  property {TypeName(p.PropertyType)} {p.Name}; getter: {(p.GetMethod == null ? "none" : p.GetMethod.IsPublic ? "public" : "non-public")}");
        foreach (var f in type.GetFields(flags).Where(f => f.Name.Contains("Available", StringComparison.OrdinalIgnoreCase)).Take(16))
            text.AppendLine($"  field {TypeName(f.FieldType)} {f.Name}; {(f.IsPublic ? "public" : "non-public")}");
        return text.ToString();
    }

    internal void Install(Type type)
    {
        RuntimeVersion = type.Assembly.GetName().Name + " " + type.Assembly.GetName().Version;
        CompatibilityDetails = DescribeRuntime(type);
        var signature = CheckSignature(type);
        CompatibilityDetails += $"Selected: {TypeName(signature.Method.ReturnType)} {signature.Method.Name}\n";
        if (active != null && active != this) throw new InvalidOperationException("Another outgoing blend hook is active.");
        runtime ??= new("SizeChange.Self.LightlessBlendSnapshot", assemblyPath);
        callerType = type; apiAvailable = signature.Available; method = signature.Method;
        active = this;
        bool valueTask = method.ReturnType == typeof(ValueTask<string>);
        var prefix = typeof(LightlessBlendSync).GetMethod(valueTask ? nameof(BeforeCapture) : nameof(BeforeRead), BindingFlags.NonPublic | BindingFlags.Static)!;
        try { runtime.Patch(method, prefix); }
        catch
        {
            // Keep the method handle for cleanup/retry if patching partly succeeded.
            accepting = false; throw;
        }
        accepting = true;
        Status = $"Hook ready: {(valueTask ? "CaptureScaleAsync" : "GetScaleAsync")} (manual resync)";
    }

    private static bool BeforeRead(object __instance, nint __0, ref Task<string?> __result)
    {
        if (!TrySnapshot(__instance, __0, CancellationToken.None, out string encoded)) return true;
        __result = Task.FromResult<string?>(encoded);
        return false;
    }

    private static bool BeforeCapture(object __instance, nint __0, CancellationToken __1, ref ValueTask<string?> __result)
    {
        if (!TrySnapshot(__instance, __0, __1, out string encoded)) return true;
        __result = new ValueTask<string?>(encoded);
        return false;
    }

    private static bool TrySnapshot(object instance, nint address, CancellationToken cancellationToken, out string encoded)
    {
        encoded = string.Empty;
        var owner = active;
        if (owner == null || !owner.accepting) return false;
        Interlocked.Increment(ref owner.reads);
        try
        {
            // Leave cancellation to Lightless's own implementation; never turn
            // an already-cancelled capture into a successful snapshot read.
            if (cancellationToken.IsCancellationRequested || owner.apiAvailable?.GetValue(instance) is not true ||
                !owner.snapshot.TryRead(address, Environment.TickCount64, out encoded, out long revision)) return false;
            Interlocked.Increment(ref owner.served);
            Interlocked.Exchange(ref owner.lastRevision, revision);
            return true;
        }
        catch
        {
            // Never break Lightless's original task or leave a partial result.
            owner.accepting = false;
            owner.failed = true;
            owner.Status = "Outgoing hook failed; normal Lightless reads resumed. Retry.";
            return false;
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
