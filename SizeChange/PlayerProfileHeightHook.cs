using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SizeChange;

// Optional managed-only compatibility boundary. No Lightless dependency, native
// pointer writes, network interception, or persistent changes to another plugin.
internal sealed class PlayerProfileHeightHook : IDisposable
{
    internal readonly record struct Actor(ushort Index, nint Address, uint EntityId, string Name);
    private sealed record Capture(Actor Actor, string Original, string Frame, object Instance);
    private sealed record Entry(Capture Capture, Guid Id, object Profile);
    private sealed record Replay(object Manager, Guid Id) { public bool Applied { get; set; } }
    [ThreadStatic] private static Replay? replay;
    private static PlayerProfileHeightHook? active;
    private PlayerHeightPatchRuntime? harmony;
    private readonly string pluginAssemblyPath;
    private bool installed;
    private readonly Func<ushort, Actor?> resolve;
    private readonly Func<Actor, float?> desired;
    private readonly Func<Actor, string, string?>? transform;
    private readonly Action<string> log;
    private readonly Dictionary<ushort, Entry> entries = new();
    private MethodInfo? setMethod;
    private MethodInfo? addMethod;
    private FieldInfo? managerField;
    private FieldInfo? profilesField;
    private PropertyInfo? idProperty;
    private bool disposed;
    private bool accepting;
    public bool Installed => installed;
    public string Status { get; private set; } = "Off";
    public int AppliedCount => entries.Count;
    private long receivedCalls;
    private long appliedCalls;
    public long ReceivedCalls => System.Threading.Interlocked.Read(ref receivedCalls);
    public long AppliedCalls => System.Threading.Interlocked.Read(ref appliedCalls);
    public string LastCall { get; private set; } = "No incoming calls observed";

    public PlayerProfileHeightHook(Func<ushort, Actor?> resolve, Func<Actor, float?> desired, Action<string> log, string pluginAssemblyPath, Func<Actor, string, string?>? transform = null)
    { this.resolve = resolve; this.desired = desired; this.log = log; this.pluginAssemblyPath = pluginAssemblyPath; this.transform = transform; }

    public bool IsCaptured(Actor actor) => Installed && accepting && entries.TryGetValue(actor.Index, out var entry) && entry.Capture.Actor == actor;
    private string? Frame(Actor actor, string original)
    {
        var offset = desired(actor);
        if (!offset.HasValue) return null;
        return transform != null ? transform(actor, original) : PlayerProfileHeightJson.Add(original, offset.Value);
    }

    // Schema checked before patching. Unsupported Customize+ versions fail closed
    // for our feature; their original IPC keeps working unchanged.
    public bool Install(Type ipcType)
    {
        if (disposed || Installed) return Installed;
        try
        {
            RemoveHooks(); // Finish any previously failed cleanup before another attempt.
            harmony ??= new("SizeChange.AddedPlayers.LocalProfileHeight", pluginAssemblyPath);
            if (active != null && active != this) throw new InvalidOperationException("Another height hook is active.");
            var method = ipcType.GetMethod("SetTemporaryProfileOnCharacter", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(ushort), typeof(string) }, null);
            if (method == null || method.ReturnType != typeof(ValueTuple<int, Guid?>))
                throw new NotSupportedException("Customize+ profile API signature changed.");
            var field = ipcType.GetField("_profileManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new NotSupportedException("Customize+ profile manager unavailable.");
            var add = field.FieldType.GetMethods().SingleOrDefault(m => m.Name == "AddTemporaryProfile" && m.GetParameters().Length == 2)
                ?? throw new NotSupportedException("Customize+ profile insertion changed.");
            var profiles = field.FieldType.GetField("Profiles")
                ?? throw new NotSupportedException("Customize+ profile collection changed.");
            var id = add.GetParameters()[0].ParameterType.GetProperty("UniqueId");
            if (id?.PropertyType != typeof(Guid) || id.SetMethod == null ||
                !typeof(IEnumerable).IsAssignableFrom(profiles.FieldType))
                throw new NotSupportedException("Customize+ profile identity changed.");
            managerField = field; profilesField = profiles; idProperty = id;
            setMethod = method; addMethod = add; active = this; accepting = false;
            MethodInfo Callback(string name) => typeof(PlayerProfileHeightHook).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            harmony.Patch(add, Callback(nameof(PreserveIdentity)));
            harmony.Patch(method, Callback(nameof(BeforeApply)), Callback(nameof(AfterApply)));
            installed = true; accepting = true;
            Status = "Waiting for incoming profiles";
            return true;
        }
        catch (Exception ex)
        {
            string failure = ex.GetBaseException().Message;
            try { RemoveHooks(); }
            catch (Exception cleanup) { failure += "; cleanup: " + cleanup.GetBaseException().Message; }
            Status = "Unavailable: " + failure;
            LastCall = Status;
            log(Status);
            return false;
        }
    }

    private static void BeforeApply(object __instance, ushort __0, ref string __1, out Capture? __state)
    {
        __state = null;
        var owner = active;
        if (owner == null || owner.disposed || !owner.accepting || replay != null) return;
        System.Threading.Interlocked.Increment(ref owner.receivedCalls);
        try
        {
            var actor = owner.resolve(__0);
            if (!actor.HasValue)
            { owner.LastCall = $"Skipped actor index {__0}: unavailable, Self, non-player or outside framework thread"; return; }
            string? modified = owner.Frame(actor.Value, __1);
            if (modified == null)
            { owner.LastCall = $"Skipped {actor.Value.Name}: not selected or eligible"; return; }
            // Capture selected baselines even at zero offset: subsequent growth
            // must work without waiting for another network profile update.
            __state = new(actor.Value, __1, modified, __instance);
            __1 = modified;
            owner.LastCall = $"Intercepted {actor.Value.Name}";
        }
        catch (Exception ex) { owner.Report(ex); } // Leave the original input untouched.
    }

    private static void AfterApply(ValueTuple<int, Guid?> __result, Capture? __state)
    {
        var owner = active;
        if (owner == null || __state == null) return;
        if (__result.Item1 != 0 || !__result.Item2.HasValue)
        { owner.LastCall = $"Customize+ rejected profile: code {__result.Item1}"; return; }
        try
        {
            var profile = owner.FindProfile(__state.Instance, __result.Item2.Value);
            if (profile == null) throw new InvalidOperationException("Applied profile could not be tracked.");
            owner.entries[__state.Actor.Index] = new(__state, __result.Item2.Value, profile);
            System.Threading.Interlocked.Increment(ref owner.appliedCalls);
            owner.LastCall = $"Customize+ accepted profile effects for {__state.Actor.Name}";
            owner.Status = $"Active: {owner.entries.Count} player(s)";
        }
        catch (Exception ex) { owner.Report(ex); }
    }

    // Only our explicit local replay may reuse an ID. Incoming calls always get
    // Customize+'s normal new ID, returned to Lightless without modification.
    private static void PreserveIdentity(object __instance, object __0)
    {
        if (replay is { Applied: false } state && ReferenceEquals(state.Manager, __instance))
        {
            // Consume before manager events can make any reentrant IPC call.
            state.Applied = true;
            active!.idProperty!.SetValue(__0, state.Id);
        }
    }

    private object? FindProfile(object instance, Guid id)
    {
        var manager = managerField!.GetValue(instance);
        return ((IEnumerable)profilesField!.GetValue(manager)!).Cast<object>()
            .FirstOrDefault(p => (Guid)idProperty!.GetValue(p)! == id);
    }

    public void Tick()
    {
        if (!Installed || disposed) return;
        foreach (var pair in entries.ToArray())
        {
            try
            {
                var entry = pair.Value;
                if (resolve(pair.Key) != entry.Capture.Actor ||
                    !ReferenceEquals(FindProfile(entry.Capture.Instance, entry.Id), entry.Profile))
                { entries.Remove(pair.Key); continue; }
                string? frame = Frame(entry.Capture.Actor, entry.Capture.Original);
                if (frame == entry.Capture.Frame) continue;
                Reapply(entry, frame);
            }
            catch (Exception ex) { Report(ex); }
        }
    }

    private void Reapply(Entry entry, string? frame)
    {
        string json = frame ?? entry.Capture.Original;
        var previous = replay;
        try
        {
            replay = new(managerField!.GetValue(entry.Capture.Instance)!, entry.Id);
            var result = ((int, Guid?))setMethod!.Invoke(entry.Capture.Instance, new object[] { entry.Capture.Actor.Index, json })!;
            if (result.Item1 != 0 || result.Item2 != entry.Id)
                throw new InvalidOperationException("Customize+ did not preserve profile ownership.");
            var profile = FindProfile(entry.Capture.Instance, entry.Id)
                ?? throw new InvalidOperationException("Updated profile unavailable.");
            if (frame == null) entries.Remove(entry.Capture.Actor.Index);
            else entries[entry.Capture.Actor.Index] = new(entry.Capture with { Frame = frame }, entry.Id, profile);
        }
        finally { replay = previous; }
    }

    // Restore only profiles still owned by the same actor and same managed
    // profile object. Never restore onto a replacement from another plugin.
    public void StopAccepting() => accepting = false;

    public void Stop(bool restore = true)
    {
        accepting = false;
        if (setMethod == null && addMethod == null) return;
        if (restore && Installed)
            foreach (var entry in entries.Values.ToArray())
                try
                {
                    if (resolve(entry.Capture.Actor.Index) == entry.Capture.Actor &&
                        ReferenceEquals(FindProfile(entry.Capture.Instance, entry.Id), entry.Profile)) Reapply(entry, null);
                }
                catch (Exception ex) { Report(ex); }
        entries.Clear();
        RemoveHooks();
        Status = "Off";
    }

    private void RemoveHooks()
    {
        accepting = false;
        installed = false;
        var failures = new List<Exception>();
        void Remove(ref MethodInfo? method)
        {
            if (method == null) return;
            try { harmony!.Unpatch(method); method = null; }
            catch (Exception ex) { failures.Add(new InvalidOperationException(ex.GetBaseException().Message, ex)); }
        }
        Remove(ref setMethod);
        Remove(ref addMethod);
        // Keep failed handles for an explicit Retry; never claim they were removed.
        if (failures.Count > 0) throw new AggregateException("Height hook cleanup failed", failures);
        managerField = null; profilesField = null; idProperty = null;
        if (active == this) active = null;
    }

    private void Report(Exception ex)
    {
        string message = "Height override: " + ex.GetBaseException().Message;
        if (Status != message) log(message);
        Status = message;
        LastCall = message;
    }
    public void Dispose() { if (disposed) return; Stop(); disposed = true; }
}
