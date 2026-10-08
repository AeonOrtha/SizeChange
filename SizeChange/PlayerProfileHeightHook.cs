using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SizeChange;

// Optional managed-only compatibility boundary. No Lightless dependency, native
// pointer writes, network interception, or persistent changes to another plugin.
internal sealed class PlayerProfileHeightHook : IDisposable
{
    internal readonly record struct Actor(ushort Index, nint Address, uint EntityId, string Name);
    private sealed record Capture(Actor Actor, string Original, float Offset, object Instance);
    private sealed record Entry(Capture Capture, Guid Id, object Profile);
    private sealed record Replay(object Manager, Guid Id) { public bool Applied { get; set; } }
    [ThreadStatic] private static Replay? replay;
    private static PlayerProfileHeightHook? active;
    private readonly Harmony harmony = new("SizeChange.AddedPlayers.LocalProfileHeight");
    private readonly Func<ushort, Actor?> resolve;
    private readonly Func<Actor, float?> desired;
    private readonly Action<string> log;
    private readonly Dictionary<ushort, Entry> entries = new();
    private MethodInfo? setMethod;
    private MethodInfo? addMethod;
    private FieldInfo? managerField;
    private FieldInfo? profilesField;
    private PropertyInfo? idProperty;
    private bool disposed;
    private bool accepting;
    public bool Installed => setMethod != null;
    public string Status { get; private set; } = "Off";
    public int AppliedCount => entries.Count;
    private long receivedCalls;
    private long appliedCalls;
    public long ReceivedCalls => System.Threading.Interlocked.Read(ref receivedCalls);
    public long AppliedCalls => System.Threading.Interlocked.Read(ref appliedCalls);
    public string LastCall { get; private set; } = "No incoming calls observed";

    public PlayerProfileHeightHook(Func<ushort, Actor?> resolve, Func<Actor, float?> desired, Action<string> log)
    { this.resolve = resolve; this.desired = desired; this.log = log; }

    // Schema checked before patching. Unsupported Customize+ versions fail closed
    // for our feature; their original IPC keeps working unchanged.
    public bool Install(Type ipcType)
    {
        if (disposed || Installed) return Installed;
        try
        {
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
            setMethod = method; addMethod = add; active = this; accepting = true;
            harmony.Patch(add, prefix: new HarmonyMethod(typeof(PlayerProfileHeightHook), nameof(PreserveIdentity)));
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlayerProfileHeightHook), nameof(BeforeApply)),
                postfix: new HarmonyMethod(typeof(PlayerProfileHeightHook), nameof(AfterApply)));
            Status = "Waiting for incoming profiles";
            return true;
        }
        catch (Exception ex)
        {
            RemoveHooks();
            Status = "Unavailable: " + ex.GetBaseException().Message;
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
            var offset = owner.desired(actor.Value);
            if (!offset.HasValue || offset.Value <= 0f)
            { owner.LastCall = $"Skipped {actor.Value.Name}: not selected/eligible, or offset is zero"; return; }
            string modified = PlayerProfileHeightJson.Add(__1, offset.Value);
            __state = new(actor.Value, __1, offset.Value, __instance);
            __1 = modified;
            owner.LastCall = $"Intercepted {actor.Value.Name}: root Y +{offset.Value:0.###}";
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
            owner.LastCall = $"Customize+ accepted offset for {__state.Actor.Name}";
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
                float offset = desired(entry.Capture.Actor) ?? 0f;
                if (offset == entry.Capture.Offset) continue;
                Reapply(entry, offset);
            }
            catch (Exception ex) { Report(ex); }
        }
    }

    private void Reapply(Entry entry, float offset)
    {
        string json = PlayerProfileHeightJson.Add(entry.Capture.Original, offset);
        var previous = replay;
        try
        {
            replay = new(managerField!.GetValue(entry.Capture.Instance)!, entry.Id);
            var result = ((int, Guid?))setMethod!.Invoke(entry.Capture.Instance, new object[] { entry.Capture.Actor.Index, json })!;
            if (result.Item1 != 0 || result.Item2 != entry.Id)
                throw new InvalidOperationException("Customize+ did not preserve profile ownership.");
            var profile = FindProfile(entry.Capture.Instance, entry.Id)
                ?? throw new InvalidOperationException("Updated profile unavailable.");
            if (offset == 0f) entries.Remove(entry.Capture.Actor.Index);
            else entries[entry.Capture.Actor.Index] = new(entry.Capture with { Offset = offset }, entry.Id, profile);
        }
        finally { replay = previous; }
    }

    // Restore only profiles still owned by the same actor and same managed
    // profile object. Never restore onto a replacement from another plugin.
    public void StopAccepting() => accepting = false;

    public void Stop(bool restore = true)
    {
        accepting = false;
        if (!Installed) return;
        if (restore)
            foreach (var entry in entries.Values.ToArray())
                try
                {
                    if (resolve(entry.Capture.Actor.Index) == entry.Capture.Actor &&
                        ReferenceEquals(FindProfile(entry.Capture.Instance, entry.Id), entry.Profile)) Reapply(entry, 0f);
                }
                catch (Exception ex) { Report(ex); }
        entries.Clear();
        RemoveHooks();
        Status = "Off";
    }

    private void RemoveHooks()
    {
        if (setMethod != null) harmony.Unpatch(setMethod, HarmonyPatchType.All, harmony.Id);
        if (addMethod != null) harmony.Unpatch(addMethod, HarmonyPatchType.All, harmony.Id);
        setMethod = addMethod = null;
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
