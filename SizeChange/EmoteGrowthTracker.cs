using System;
using System.Collections.Generic;

namespace SizeChange;

// Scalars only. Live actors are sampled afresh on the framework thread; this
// class never retains or dereferences actor pointers. No native hook is needed.
internal sealed class EmoteGrowthTracker
{
    internal readonly record struct Actor(uint Entity, nint Address, nint Model);
    private readonly record struct Key(Actor Source, Actor Recipient, EmoteGrowthTrigger Trigger);
    private sealed class State
    {
        public uint Emote;
        public EmoteGrowthRule Rule = null!;
        public float Growth;
        public long Interval, NextAt, LastSeen;
        public bool Seen;
    }
    private readonly Dictionary<Key, State> states = new();
    private readonly List<Key> expired = new();
    private uint territory;
    private long now;
    private bool allowed;
    public void BeginFrame(bool enabled, uint currentTerritory, long timestamp)
    {
        if (!enabled || currentTerritory != territory || timestamp < now) Clear();
        territory = currentTerritory; now = timestamp; allowed = enabled;
        foreach (var state in states.Values) state.Seen = false;
    }
    public float Observe(Actor source, Actor recipient, uint emoteId, EmoteGrowthRule rule,
        bool eventStarts = false) => Advance(source, recipient, emoteId, rule, false, eventStarts);
    // Each server notification is a distinct use, even for the same emote and
    // target in one frame. The following animation observation must not duplicate it.
    public float Started(Actor source, Actor recipient, uint emoteId, EmoteGrowthRule rule) =>
        Advance(source, recipient, emoteId, rule, true, true);
    private float Advance(Actor source, Actor recipient, uint emoteId, EmoteGrowthRule rule,
        bool started, bool eventStarts)
    {
        if (!allowed || emoteId == 0 || rule.EmoteId != emoteId ||
            source.Entity is 0 or 0xE0000000 || recipient.Entity is 0 or 0xE0000000 ||
            source.Address == 0 || recipient.Address == 0 || source.Model == 0 || recipient.Model == 0 ||
            !float.IsFinite(rule.Growth) || rule.Growth <= 0 || !float.IsFinite(rule.IntervalSeconds)) return 0;
        float growth = Math.Min(rule.Growth, ImmediateGrowth.Maximum);
        long interval = (long)Math.Round(EmoteGrowthSettings.ClampInterval(rule.IntervalSeconds) * 1000d);
        var key = new Key(source, recipient, rule.Trigger);
        if (started || !states.TryGetValue(key, out var state) || state.Emote != emoteId || !ReferenceEquals(state.Rule, rule))
        {
            states[key] = new() { Emote = emoteId, Rule = rule, Growth = growth, Interval = interval,
                NextAt = now + interval, LastSeen = now, Seen = true };
            return started || !eventStarts ? growth : 0f; // Events own Received starts; polling only sustains them.
        }
        if (state.Seen) return 0;
        state.Seen = true;
        // After a long frame stall, do not manufacture ticks for an unobserved
        // period. Editing values also starts a new interval, without a bonus tick.
        bool reset = now - state.LastSeen > 2000 || state.Growth != growth || state.Interval != interval;
        state.LastSeen = now; state.Growth = growth; state.Interval = interval;
        if (reset) { state.NextAt = now + interval; return 0; }
        // Zero is a one-shot: keep observing the active emote without repeating,
        // even after a stall or configuration edit. Ending the emote rearms it.
        if (interval == 0 || now < state.NextAt) return 0;
        long ticks = 1 + (now - state.NextAt) / interval;
        state.NextAt += ticks * interval;
        return (float)Math.Min(ImmediateGrowth.Maximum, ticks * (double)growth);
    }
    public void EndFrame()
    {
        expired.Clear();
        foreach (var pair in states) if (!pair.Value.Seen) expired.Add(pair.Key);
        foreach (var key in expired) states.Remove(key);
    }
    public void Clear() { states.Clear(); expired.Clear(); allowed = false; }
}
