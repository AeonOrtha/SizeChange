using System;
using System.Linq;
using System.Reflection;

namespace SizeChange;

[Serializable]
public sealed class SelfWeaponFollowSettings
{
    public bool Enabled { get; set; }
}

// Optional boundary around C+'s render-time bone application. No native detours,
// C+ profile edits, attachment metadata edits or changes to other actors.
// Remove these three SelfWeaponFollow files and the small Plugin/UI/config wiring
// to remove this experiment. Its Harmony owner is separate from height/blend sync.
internal sealed class SelfWeaponFollow : IDisposable
{
    private static SelfWeaponFollow? active;
    private readonly string assemblyPath;
    private readonly Action<string> log;
    private readonly Func<Type?> findType;
    private readonly ISelfWeaponFollowTarget target;
    private readonly object gate = new();
    private PlayerHeightPatchRuntime? runtime;
    private MethodInfo? method;
    private bool accepting, disposed, faulted;
    private long nextCheck, frames, corrections;
    public string Status { get; private set; } = "Off";
    public string Counters => $"Frames: {frames} | Corrections: {corrections}";

    internal SelfWeaponFollow(string assemblyPath, Action<string> log, ISelfWeaponFollowTarget target, Func<Type?>? findType = null)
    {
        this.assemblyPath = assemblyPath; this.log = log; this.target = target;
        this.findType = findType ?? (() => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "CustomizePlus")
            .Select(a => a.GetType("CustomizePlus.Armatures.Services.ArmatureManager")).FirstOrDefault(t => t != null));
    }

    public void Tick(bool enabled, bool allowed)
    {
        lock (gate)
        {
            if (disposed) return;
            if (!enabled) { Stop(); if (!faulted) Status = "Off"; return; }
            if (faulted) return;
            accepting = allowed;
            if (!allowed)
            {
                try { target.Restore(); Status = "Paused"; }
                catch (Exception ex) { Fail(ex); }
                return;
            }
            if (method != null) return;
            if (Environment.TickCount64 < nextCheck) return;
            nextCheck = Environment.TickCount64 + 2000;
            try
            {
                var type = findType();
                if (type == null) { Status = "Waiting for Customize+"; return; }
                var candidate = type.GetMethod("ApplyArmatureTransforms", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (candidate == null || candidate.ReturnType != typeof(void))
                    throw new NotSupportedException("Customize+ render method changed.");
                runtime ??= new("SizeChange.SelfWeaponFollow.Experimental", assemblyPath);
                active = this;
                // Save before attempting installation, so a partial failure can be unpatched.
                method = candidate;
                runtime.Patch(candidate, typeof(SelfWeaponFollow).GetMethod(nameof(Before), BindingFlags.NonPublic | BindingFlags.Static)!,
                    typeof(SelfWeaponFollow).GetMethod(nameof(After), BindingFlags.NonPublic | BindingFlags.Static)!);
                Status = "Ready — draw your weapon";
            }
            catch (Exception ex) { Fail(ex); }
        }
    }

    // A state token is local to this invocation, never a pointer retained for later
    // callbacks. The native adapter re-resolves Self and both models before use.
    private static void Before(out object? __state)
    {
        __state = null;
        var owner = active;
        if (owner == null) return;
        lock (owner.gate)
        {
            if (!owner.accepting || owner.faulted || owner.disposed) return;
            try { __state = owner.target.Capture(); }
            catch (Exception ex) { owner.Fail(ex); }
        }
    }

    private static void After(object? __state)
    {
        var owner = active;
        if (owner == null || __state == null) return;
        lock (owner.gate)
        {
            if (!owner.accepting || owner.faulted || owner.disposed) return;
            try
            {
                owner.frames++;
                int count = owner.target.Apply(__state);
                owner.corrections += count;
                owner.Status = count > 0 ? "Following Self hands" : owner.target.Status;
            }
            catch (Exception ex) { owner.Fail(ex); }
        }
    }

    private void Fail(Exception ex)
    {
        accepting = false; faulted = true;
        Status = "Unavailable: " + ex.GetBaseException().Message;
        log(Status);
        try { target.Restore(); } catch (Exception restore) { log("Weapon restore: " + restore.GetBaseException().Message); }
        // Do not unpatch from inside a running callback. Tick/Retry/Dispose handle it.
    }

    private void Stop()
    {
        accepting = false;
        nextCheck = 0;
        try { target.Restore(); }
        catch (Exception ex) { Fail(ex); }
        if (method == null) return;
        try { runtime!.Unpatch(method); method = null; if (ReferenceEquals(active, this)) active = null; }
        catch (Exception ex) { Fail(ex); }
    }

    public void Retry()
    {
        lock (gate)
        {
            Stop();
            if (method != null) return;
            faulted = false; nextCheck = 0; Status = "Waiting";
        }
    }

    public void Dispose() { lock (gate) { Stop(); disposed = true; } }
}

internal interface ISelfWeaponFollowTarget
{
    string Status { get; }
    object Capture();
    int Apply(object state);
    void Restore();
}
