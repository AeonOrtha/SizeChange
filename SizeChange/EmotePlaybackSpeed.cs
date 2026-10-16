using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace SizeChange;

// Local emote-slot speeds only. Never change OverallSpeed (which also affects
// attached particles), replace a timeline, or alter the emote growth timer.
internal sealed unsafe class EmotePlaybackSpeed : IDisposable
{
    private delegate void SetSpeedDelegate(ActionTimelineSequencer* sequencer, uint slot, float speed);
    private Hook<SetSpeedDelegate>? hook;
    private readonly object gate = new();
    private readonly Dictionary<nint, State> registered = new();
    private string fault = string.Empty;
    private bool disposed;
    private int activeActors, matchedSlots;
    private long intercepted;
    private string details = string.Empty;
    internal string Status => fault.Length > 0 ? "Emote speed: unavailable" :
        activeActors == 0 ? "Emote speed: waiting" : matchedSlots == 0 ? "Emote speed: no matching slots" : $"Emote speed: {matchedSlots} slot(s)";
    internal string Details => fault.Length > 0 ? fault : $"Game speed writes adjusted: {intercepted}\n{details}";
    internal void Retry() { lock (gate) fault = string.Empty; }

    private sealed class State
    {
        internal uint Entity, Emote;
        internal nint Address, Model, Sequencer;
        internal bool Seen;
        internal float Speed;
        internal HashSet<ushort> Matching = new();
        internal readonly ushort[] Timelines = new ushort[14];
        internal readonly EmoteSlotSpeed[] Slots = new EmoteSlotSpeed[14];
    }
    private readonly Dictionary<uint, State> states = new();
    private readonly Dictionary<uint, HashSet<ushort>> timelines = new();
    private uint territory;
    private bool readable;
    private bool EnsureHook()
    {
        if (disposed || fault.Length > 0) return false;
        try
        {
            if (hook == null)
            {
                nint address = (nint)ActionTimelineSequencer.MemberFunctionPointers.SetSlotSpeed;
                if (address == 0) throw new InvalidOperationException("SetSlotSpeed entry point unavailable.");
                hook = Plugin.GameInteropProvider.HookFromAddress<SetSpeedDelegate>(address, SetSpeed);
            }
            if (!hook.IsEnabled) hook.Enable();
            return true;
        }
        catch (Exception ex) { fault = ex.GetBaseException().Message; return false; }
    }

    private void SetSpeed(ActionTimelineSequencer* sequencer, uint slot, float requested)
    {
        lock (gate)
        {
            float result = requested;
            try
            {
                // The sequencer is supplied by the live game call. Only inspect
                // its parent after matching a registered framework-observed actor.
                if (!disposed && readable && fault.Length == 0 && sequencer != null && slot < 14 &&
                    registered.TryGetValue((nint)sequencer, out var state))
                {
                    var actor = sequencer->Parent;
                    if (actor != null && (nint)actor == state.Address && actor->EntityId == state.Entity &&
                        actor->DrawObject != null && (nint)actor->DrawObject == state.Model &&
                        actor->EmoteController.EmoteId == state.Emote &&
                        state.Matching.Contains(sequencer->TimelineIds[(int)slot]))
                    {
                        result = state.Slots[(int)slot].FromGame(requested, state.Speed);
                        state.Timelines[(int)slot] = sequencer->TimelineIds[(int)slot];
                        intercepted++;
                    }
                    else
                    {
                        // A new animation's speed request supersedes our ownership.
                        state.Slots[(int)slot] = default;
                    }
                }
            }
            catch (Exception ex) { fault = ex.GetBaseException().Message; result = requested; }
            // Exactly once, including on error. Keep bookkeeping and its native
            // write atomic against a framework-thread restoration.
            hook!.Original(sequencer, slot, result);
        }
    }

    private void Write(ActionTimelineSequencer* sequencer, uint slot, float value)
    {
        // Bypass our detour for our own writes: never apply the multiplier twice.
        if (hook != null) hook.Original(sequencer, slot, value);
    }

    internal void BeginFrame(bool readable, uint zone)
    { lock (gate) BeginFrameCore(readable, zone); }

    private void BeginFrameCore(bool readable, uint zone)
    {
        activeActors = matchedSlots = 0; details = string.Empty;
        this.readable = readable;
        // Defer restoration during loading. Resolve fresh identities when the
        // object table is readable again, so a surviving actor cannot compound
        // our old multiplier after a zone transition.
        if (!readable) return;
        if (territory != zone)
        {
            foreach (var state in states.Values) Release(state);
            states.Clear(); registered.Clear();
        }
        territory = zone;
        foreach (var state in states.Values) state.Seen = false;
    }

    internal void Tick(Character* actor, EmoteGrowthRule? rule)
    { lock (gate) TickCore(actor, rule); }

    private void TickCore(Character* actor, EmoteGrowthRule? rule)
    {
        if (!readable || disposed || actor == null || actor->DrawObject == null || rule == null) return;
        float speed = EmoteGrowthSettings.ClampSpeed(rule.PlaySpeed);
        if (speed == 1f) return;
        uint emote = actor->EmoteController.EmoteId;
        if (emote == 0) return;
        activeActors++;
        if (!EnsureHook()) return;
        if (!timelines.TryGetValue(emote, out var matching))
        {
            matching = new();
            var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRowOrDefault(emote);
            if (row.HasValue)
                foreach (var timeline in row.Value.ActionTimeline)
                    if (timeline.RowId is > 0 and <= ushort.MaxValue) matching.Add((ushort)timeline.RowId);
            timelines[emote] = matching;
        }
        if (matching.Count == 0) { details += $"Emote {emote}: no listed timelines.\n"; return; }
        if (details.Length < 4000) details += $"Actor {actor->EntityId}, emote {emote}, listed timelines: {string.Join(", ", matching)}\n";
        if (states.TryGetValue(actor->EntityId, out var old) &&
            (old.Address != (nint)actor || old.Model != (nint)actor->DrawObject))
        {
            // Slot speeds live on Character, so they may survive a model redraw.
            if (old.Address == (nint)actor) Release(actor, old);
            registered.Remove(old.Sequencer);
            states.Remove(actor->EntityId);
        }
        if (!states.TryGetValue(actor->EntityId, out var state))
            states[actor->EntityId] = state = new() { Entity = actor->EntityId, Address = (nint)actor, Model = (nint)actor->DrawObject, Sequencer = (nint)(&actor->Timeline.TimelineSequencer) };
        if (state.Emote != emote || state.Speed != speed)
        {
            Release(actor, state);
            state.Emote = emote; state.Speed = speed;
        }
        state.Seen = true; state.Matching = matching;
        registered[(nint)(&actor->Timeline.TimelineSequencer)] = state;
        ref var sequencer = ref actor->Timeline.TimelineSequencer;
        for (int i = 0; i < state.Slots.Length; i++)
        {
            ushort timeline = sequencer.TimelineIds[i];
            float current = sequencer.TimelineSpeeds[i];
            if (state.Timelines[i] != timeline || !matching.Contains(timeline))
            {
                if (state.Slots[i].Release(ref current)) Write(&actor->Timeline.TimelineSequencer, (uint)i, current);
                state.Timelines[i] = timeline;
            }
            if (matching.Contains(timeline))
            {
                matchedSlots++;
                if (state.Slots[i].Apply(ref current, speed)) Write(&actor->Timeline.TimelineSequencer, (uint)i, current);
                if (details.Length < 4000) details += $"Emote {emote}, slot {i}, timeline {timeline}: {sequencer.TimelineSpeeds[i]:0.###} (setting {speed:0.###})\n";
            }
            else if (timeline != 0 && details.Length < 4000) details += $"Emote {emote}, slot {i}: unmatched timeline {timeline}\n";
        }
    }

    private void Release(Character* actor, State state)
    {
        if (hook == null) return;
        for (int i = 0; i < state.Slots.Length; i++)
        {
            float current = actor->Timeline.TimelineSequencer.TimelineSpeeds[i];
            if (state.Slots[i].Release(ref current)) Write(&actor->Timeline.TimelineSequencer, (uint)i, current);
            state.Timelines[i] = 0;
        }
    }

    private void Release(State state)
    {
        // Resolve fresh and verify actor identity before touching its slot speeds.
        if (Plugin.ObjectTable.SearchByEntityId(state.Entity) is not IBattleChara obj || obj.Address != state.Address) return;
        var actor = (Character*)obj.Address;
        if (actor->DrawObject != null) Release(actor, state);
    }

    internal void EndFrame()
    { lock (gate) EndFrameCore(); }

    private void EndFrameCore()
    {
        if (!readable) return;
        foreach (uint id in new List<uint>(states.Keys))
            if (!states[id].Seen)
            {
                var state = states[id];
                registered.Remove(state.Sequencer);
                Release(state); states.Remove(id);
            }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true; registered.Clear();
            if (readable) foreach (var state in states.Values) Release(state);
            states.Clear(); timelines.Clear();
        }
        hook?.Dispose(); hook = null;
    }
}
