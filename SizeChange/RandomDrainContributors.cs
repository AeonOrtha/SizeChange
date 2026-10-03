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
    private readonly HashSet<Key> selected = new();
    private readonly List<SizeDrainSources.Hit> candidates = new();
    private bool wasRandom;
    private static Key Identity(SizeDrainSources.Hit hit) => new(hit.Target.Address, hit.Target.EntityId, hit.Target.Name, hit.Target.Player);

    public void Clear() { leases.Clear(); present.Clear(); expired.Clear(); selected.Clear(); candidates.Clear(); }

    public void Filter(List<SizeDrainSources.Hit> hits, SizeDrainSettings settings,
        double now, Func<double> random)
    {
        if (wasRandom != settings.RandomContributors) Clear();
        wasRandom = settings.RandomContributors;
        candidates.Clear(); candidates.AddRange(hits);
        present.Clear();
        foreach (var hit in candidates) present.Add(Identity(hit));
        expired.Clear();
        foreach (var key in leases.Keys)
            if (!present.Contains(key)) expired.Add(key);
        foreach (var key in expired) leases.Remove(key);
        selected.RemoveWhere(key => !present.Contains(key));
        hits.Clear();
        int limit = Math.Clamp(settings.MaximumTargets, 1, 64);
        // Retain existing active targets before filling spare slots.
        foreach (var hit in candidates)
        {
            var key = Identity(hit);
            bool active = settings.RandomContributors
                ? leases.TryGetValue(key, out var lease) && now < lease.Until
                : selected.Contains(key);
            if (active && hits.Count < limit) hits.Add(hit);
            else if (active) { leases.Remove(key); selected.Remove(key); }
        }
        selected.Clear();
        foreach (var hit in hits) selected.Add(Identity(hit));
        // Shuffle contenders so the object-table order does not monopolize slots.
        for (int i = candidates.Count - 1; i > 0 && hits.Count < limit; i--)
        {
            int j = Math.Clamp((int)(random() * (i + 1)), 0, i);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        foreach (var hit in candidates)
        {
            if (hits.Count >= limit) break;
            var key = Identity(hit);
            if (selected.Contains(key)) continue;
            if (settings.RandomContributors)
            {
                if (leases.TryGetValue(key, out var lease) && now < lease.NextRoll) continue;
                if (random() * 100.0 >= settings.ContributorChancePercent)
                { leases[key] = new Lease(now, now + settings.ContributorRetrySeconds); continue; }
                double duration = settings.ContributorMinimumSeconds + random() *
                    (settings.ContributorMaximumSeconds - settings.ContributorMinimumSeconds);
                leases[key] = new Lease(now + duration, now + duration + settings.ContributorRetrySeconds);
            }
            hits.Add(hit); selected.Add(key);
        }
    }
}
