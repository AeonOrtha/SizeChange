using System;
using System.Collections.Generic;

namespace SizeChange;

// One instance per receiver. Stores identity and timers, never native pointers
// for dereferencing. Only currently eligible snapshots can contribute.
internal sealed class RandomDrainContributors
{
    private readonly record struct Key(nint Address, uint EntityId, string Name, bool Player);
    private readonly record struct Lease(double Until, double NextRoll);
    private readonly Dictionary<Key, Lease> leases = new();
    private readonly HashSet<Key> present = new();
    private readonly List<Key> expired = new();

    public void Clear() { leases.Clear(); present.Clear(); expired.Clear(); }

    public void Filter(List<SizeDrainSources.Hit> hits, SizeDrainSettings settings,
        double now, Func<double> random)
    {
        if (!settings.RandomContributors) { Clear(); return; }
        present.Clear();
        int kept = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var source = hit.Target;
            var key = new Key(source.Address, source.EntityId, source.Name, source.Player);
            present.Add(key);
            if (!leases.TryGetValue(key, out var lease)) lease = new Lease(now, now);
            if (now >= lease.Until && now >= lease.NextRoll)
            {
                if (random() * 100.0 < settings.ContributorChancePercent)
                {
                    double duration = settings.ContributorMinimumSeconds + random() *
                        (settings.ContributorMaximumSeconds - settings.ContributorMinimumSeconds);
                    lease = new Lease(now + duration, now + duration + settings.ContributorRetrySeconds);
                }
                else lease = new Lease(now, now + settings.ContributorRetrySeconds);
                leases[key] = lease;
            }
            if (now < lease.Until) hits[kept++] = hit;
        }
        if (kept < hits.Count) hits.RemoveRange(kept, hits.Count - kept);
        expired.Clear();
        foreach (var key in leases.Keys)
            if (!present.Contains(key)) expired.Add(key);
        foreach (var key in expired) leases.Remove(key);
    }
}
