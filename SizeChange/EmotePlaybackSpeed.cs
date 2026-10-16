using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace SizeChange;

// Local emote-slot speeds only. Never change OverallSpeed (which also affects
// attached particles), replace a timeline, or alter the emote growth timer.
internal sealed unsafe class EmotePlaybackSpeed : IDisposable
{
    private sealed class State
    {
        internal uint Entity, Emote;
        internal nint Address, Model;
        internal bool Seen;
        internal float Speed;
        internal readonly ushort[] Timelines = new ushort[14];
        internal readonly EmoteSlotSpeed[] Slots = new EmoteSlotSpeed[14];
    }
    private readonly Dictionary<uint, State> states = new();
    private readonly Dictionary<uint, HashSet<ushort>> timelines = new();
    private uint territory;
    private bool readable;
    private static bool Available => ActionTimelineSequencer.MemberFunctionPointers.SetSlotSpeed != null;

    internal void BeginFrame(bool readable, uint zone)
    {
        this.readable = readable;
        // Defer restoration during loading. Resolve fresh identities when the
        // object table is readable again, so a surviving actor cannot compound
        // our old multiplier after a zone transition.
        if (!readable) return;
        if (territory != zone)
        {
            foreach (var state in states.Values) Release(state);
            states.Clear();
        }
        territory = zone;
        foreach (var state in states.Values) state.Seen = false;
    }

    internal void Tick(Character* actor, EmoteGrowthRule? rule)
    {
        if (!readable || !Available || actor == null || actor->DrawObject == null || rule == null) return;
        float speed = EmoteGrowthSettings.ClampSpeed(rule.PlaySpeed);
        if (speed == 1f) return;
        uint emote = actor->EmoteController.EmoteId;
        if (emote == 0) return;
        if (!timelines.TryGetValue(emote, out var matching))
        {
            matching = new();
            var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRowOrDefault(emote);
            if (row.HasValue)
                foreach (var timeline in row.Value.ActionTimeline)
                    if (timeline.RowId is > 0 and <= ushort.MaxValue) matching.Add((ushort)timeline.RowId);
            timelines[emote] = matching;
        }
        if (matching.Count == 0) return;
        if (states.TryGetValue(actor->EntityId, out var old) &&
            (old.Address != (nint)actor || old.Model != (nint)actor->DrawObject))
        {
            // Slot speeds live on Character, so they may survive a model redraw.
            if (old.Address == (nint)actor) Release(actor, old);
            states.Remove(actor->EntityId);
        }
        if (!states.TryGetValue(actor->EntityId, out var state))
            states[actor->EntityId] = state = new() { Entity = actor->EntityId, Address = (nint)actor, Model = (nint)actor->DrawObject };
        if (state.Emote != emote || state.Speed != speed)
        {
            Release(actor, state);
            state.Emote = emote; state.Speed = speed;
        }
        state.Seen = true;
        ref var sequencer = ref actor->Timeline.TimelineSequencer;
        for (int i = 0; i < state.Slots.Length; i++)
        {
            ushort timeline = sequencer.TimelineIds[i];
            float current = sequencer.TimelineSpeeds[i];
            if (state.Timelines[i] != timeline || !matching.Contains(timeline))
            {
                if (state.Slots[i].Release(ref current)) sequencer.SetSlotSpeed((uint)i, current);
                state.Timelines[i] = timeline;
            }
            if (matching.Contains(timeline) && state.Slots[i].Apply(ref current, speed))
                sequencer.SetSlotSpeed((uint)i, current);
        }
    }

    private static void Release(Character* actor, State state)
    {
        if (!Available) return;
        for (int i = 0; i < state.Slots.Length; i++)
        {
            float current = actor->Timeline.TimelineSequencer.TimelineSpeeds[i];
            if (state.Slots[i].Release(ref current)) actor->Timeline.TimelineSequencer.SetSlotSpeed((uint)i, current);
            state.Timelines[i] = 0;
        }
    }

    private static void Release(State state)
    {
        // Resolve fresh and verify actor identity before touching its slot speeds.
        if (Plugin.ObjectTable.SearchByEntityId(state.Entity) is not IBattleChara obj || obj.Address != state.Address) return;
        var actor = (Character*)obj.Address;
        if (actor->DrawObject != null) Release(actor, state);
    }

    internal void EndFrame()
    {
        if (!readable) return;
        foreach (uint id in new List<uint>(states.Keys))
            if (!states[id].Seen) { Release(states[id]); states.Remove(id); }
    }

    public void Dispose()
    {
        if (readable) foreach (var state in states.Values) Release(state);
        states.Clear(); timelines.Clear();
    }
}
