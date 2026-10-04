using System;
using System.Collections.Generic;

namespace SizeChange;

// Per receiver; identity is checked against fresh, living object-table snapshots.
internal sealed class RandomDrainContributors
{
    private readonly record struct Key(nint Address, uint EntityId, string Name, bool Player);
    private sealed class State
    {
        public float Exposure;
        public double Until;
        public double NextRoll;
        public double LeftAt = -1;
        public float LastRatio;
        public bool Selected;
    }
    private readonly Dictionary<Key, State> states = new();
    private readonly HashSet<Key> present = new();
    private readonly List<Key> expired = new();
    private readonly List<SizeDrainSources.Hit> candidates = new();
    private bool wasRandom;
    private double lastTime = double.NaN;
    private static Key Identity(SizeDrainSources.Hit hit) => new(hit.Target.Address, hit.Target.EntityId, hit.Target.Name, hit.Target.Player);
    public void Clear()
    {
        states.Clear(); present.Clear(); expired.Clear(); candidates.Clear(); lastTime = double.NaN;
    }

    public void Filter(List<SizeDrainSources.Hit> hits, SizeDrainSettings settings, double now, Func<double> random)
    {
        if (wasRandom != settings.RandomContributors) Clear();
        wasRandom = settings.RandomContributors;
        float dt = double.IsNaN(lastTime) ? 0f : (float)Math.Clamp(now - lastTime, 0, 1);
        lastTime = now;
        candidates.Clear(); candidates.AddRange(hits);
        present.Clear();
        foreach (var hit in candidates) present.Add(Identity(hit));
        expired.Clear();
        foreach (var key in states.Keys)
            if (!present.Contains(key)) expired.Add(key);
        foreach (var key in expired) states.Remove(key);
        hits.Clear();
        int limit = Math.Clamp(settings.MaximumTargets, 1, 64);
        foreach (var hit in candidates)
        {
            var key = Identity(hit);
            if (!states.TryGetValue(key, out var state)) states[key] = state = new State();
            if (hit.Ratio > 0f)
            {
                state.LeftAt = -1;
                // At the source: configured exposure time. Near the edge: four times longer.
                float closeness = Math.Clamp(hit.Ratio / Math.Max(0.000001f, settings.PeakHitPercent / 100f), 0f, 1f);
                state.Exposure = settings.ExposureBuildup
                    ? Math.Min(1f, state.Exposure + dt * (0.25f + 0.75f * closeness) / settings.ExposureSeconds) : 1f;
                state.LastRatio = hit.Ratio;
            }
            else
            {
                state.Exposure = 0f;
                if (state.LeftAt < 0) state.LeftAt = now;
            }
            bool active = state.Selected && (!settings.RandomContributors || now < state.Until);
            float ratio = hit.Ratio;
            if (ratio <= 0f && active && settings.LingeringCorruption)
                ratio = state.LastRatio * Math.Clamp(1f - (float)(now - state.LeftAt) / settings.LingerSeconds, 0f, 1f);
            if (active && ratio > 0f && hits.Count < limit)
                hits.Add(new SizeDrainSources.Hit(hit.Target, ratio));
            else state.Selected = false;
        }
        // Preserve active contributors, then fill available slots fairly.
        for (int i = candidates.Count - 1; i > 0 && hits.Count < limit; i--)
        {
            int j = Math.Clamp((int)(random() * (i + 1)), 0, i);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        foreach (var hit in candidates)
        {
            if (hits.Count >= limit) break;
            var state = states[Identity(hit)];
            if (state.Selected || hit.Ratio <= 0f || state.Exposure < 1f) continue;
            if (settings.RandomContributors)
            {
                if (now < state.NextRoll) continue;
                if (random() * 100.0 >= settings.ContributorChancePercent)
                { state.NextRoll = now + settings.ContributorRetrySeconds; continue; }
                double duration = settings.ContributorMinimumSeconds + random() *
                    (settings.ContributorMaximumSeconds - settings.ContributorMinimumSeconds);
                state.Until = now + duration;
                state.NextRoll = state.Until + settings.ContributorRetrySeconds;
            }
            state.Selected = true;
            hits.Add(hit);
        }
    }
}
