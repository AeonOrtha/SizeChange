using System;
using System.Collections.Generic;
using System.Linq;

namespace SizeChange;

// One render hook, independently owned state for each live actor. The source
// returns only Self and/or explicitly listed Added Players from enabled tabs.
internal sealed class WeaponFollowTargets : ISelfWeaponFollowTarget
{
    internal readonly record struct Key(ushort Index, nint Address, uint Entity, uint Territory, bool Self);
    private readonly Func<IEnumerable<Key>> enumerate;
    private readonly Func<Key, ISelfWeaponFollowTarget> create;
    private readonly Dictionary<Key, ISelfWeaponFollowTarget> targets = new();
    private volatile int selfCount, playerCount;
    private sealed record Captured(Key Key, ISelfWeaponFollowTarget Target, object State);
    public string Status { get; private set; } = "Waiting for eligible players";
    // Read through SelfWeaponFollow's lock; strings are built only while UI is open.
    public string Diagnostics => $"Selected actors — Self: {selfCount} | Added Players: {playerCount}" +
        string.Concat(targets.Where(p => p.Value is SelfWeaponFollowNative).Select(p =>
            $"\n{(p.Key.Self ? "Self" : $"Player slot {p.Key.Index}")}: {((SelfWeaponFollowNative)p.Value).TypesDiagnostic}"));
    internal WeaponFollowTargets(Func<IEnumerable<Key>> enumerate, Func<Key, ISelfWeaponFollowTarget> create)
    { this.enumerate = enumerate; this.create = create; }

    private void Reconcile()
    {
        var wanted = enumerate().Where(k => k.Address != 0).ToHashSet();
        foreach (var key in targets.Keys.Where(k => !wanted.Contains(k)).ToArray())
        {
            var old = targets[key];
            old.Restore();
            targets.Remove(key);
        }
        foreach (var key in wanted)
            if (!targets.ContainsKey(key)) targets.Add(key, create(key));
        selfCount = targets.Keys.Count(k => k.Self);
        playerCount = targets.Count - selfCount;
    }

    public object Capture()
    {
        Reconcile();
        var frames = new List<Captured>(targets.Count);
        foreach (var pair in targets) frames.Add(new(pair.Key, pair.Value, pair.Value.Capture()));
        return frames;
    }

    public int Apply(object state)
    {
        Reconcile(); // Recheck removals, switches and actor identity after C+.
        int count = 0;
        if (state is List<Captured> frames)
            foreach (var frame in frames)
                if (targets.TryGetValue(frame.Key, out var current) && ReferenceEquals(current, frame.Target))
                    count += current.Apply(frame.State);
        Status = targets.Count == 0 ? "Waiting for eligible players" : count > 0 ? "Following weapon attachments" : "Waiting for drawn weapons or hand changes";
        return count;
    }

    public void Restore()
    {
        Exception? failure = null;
        foreach (var target in targets.Values)
            try { target.Restore(); } catch (Exception ex) { failure ??= ex; }
        targets.Clear();
        selfCount = playerCount = 0;
        if (failure != null) throw new InvalidOperationException("Weapon cleanup failed.", failure);
    }
}
