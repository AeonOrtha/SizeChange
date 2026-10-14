using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SizeChange;

// Separate owner for local monster timelines. Retains scalar actor identity and
// timeline IDs only; re-resolves the actor before cleanup, never a stale pointer.
internal sealed unsafe class MonsterAnimationPlayer : IDisposable
{
    internal delegate bool Resolve(string path, out ushort id, out string error);
    private sealed class State
    {
        public uint Entity;
        public nint Address, Model;
        public bool Seen, Idle;
        public ushort Timeline;
        public long Deadline;
        public string Signature = string.Empty;
        public MonsterIdleLatch Latch = new();
    }
    private readonly Dictionary<uint, State> states = new();
    public void BeginFrame() { foreach (var state in states.Values) state.Seen = false; }
    public void Tick(Character* actor, List<MonsterAnimationSlot>? slots, bool allowed, Resolve resolve)
    {
        if (actor == null) return;
        if (states.TryGetValue(actor->EntityId, out var prior) && prior.Address != (nint)actor)
            states.Remove(actor->EntityId); // reused ID: never touch the replacement
        if (!states.TryGetValue(actor->EntityId, out var state))
        {
            if (!allowed || slots == null || slots.Count == 0) return;
            states[actor->EntityId] = state = new() { Entity = actor->EntityId, Address = (nint)actor };
        }
        state.Seen = true;
        if (!allowed || slots == null || slots.Count == 0 || actor->DrawObject == null)
        { Stop(state); states.Remove(actor->EntityId); return; }
        if (actor->Mode != CharacterModes.Normal)
        { if (actor->InCombat) state.Latch.ShouldStart(true, state.Signature); return; }
        if (state.Timeline != 0 && (Environment.TickCount64 >= state.Deadline || (state.Idle && actor->InCombat))) Stop(state);
        var signature = new StringBuilder();
        foreach (var slot in slots) signature.Append(slot.Path).Append('|').Append(slot.DurationSeconds).Append('|').Append(slot.IsIdle).Append(';');
        string configuration = signature.ToString();
        if (state.Signature != configuration) { Stop(state); state.Signature = configuration; }
        if (state.Latch.ShouldStart(actor->InCombat, configuration))
        {
            var slot = MonsterAnimationSlot.Select(slots, true, Random.Shared.NextDouble());
            if (slot != null) Start(actor, state, slot, resolve);
        }
    }
    public void Pulse(Character* actor, List<MonsterAnimationSlot>? slots, Resolve resolve)
    {
        if (actor == null || !states.TryGetValue(actor->EntityId, out var state) || state.Address != (nint)actor ||
            actor->DrawObject == null || actor->Health == 0 || actor->Mode != CharacterModes.Normal) return;
        // A standing pose is intentionally protected from per-pulse retriggers.
        if (state.Timeline != 0 && state.Idle && !actor->InCombat) return;
        var slot = MonsterAnimationSlot.Select(slots, false, Random.Shared.NextDouble());
        if (slot != null) Start(actor, state, slot, resolve);
    }
    private static void Start(Character* actor, State state, MonsterAnimationSlot slot, Resolve resolve)
    {
        if (!resolve(slot.Path, out ushort timeline, out string error))
        { Plugin.Log.Debug("Monster animation: {Error}", error); return; }
        Stop(state);
        actor->Timeline.PlayActionTimeline(timeline);
        state.Model = (nint)actor->DrawObject;
        state.Timeline = timeline;
        state.Idle = slot.IsIdle;
        state.Deadline = Environment.TickCount64 + (long)(Math.Clamp(slot.DurationSeconds, 0.05f, 600f) * 1000f);
    }
    private static void Stop(State state)
    {
        ushort owned = state.Timeline;
        state.Timeline = 0;
        if (owned == 0) return;
        var obj = Plugin.ObjectTable.SearchByEntityId(state.Entity);
        if (obj is not IBattleNpc || obj.Address != state.Address) return;
        var actor = (Character*)obj.Address;
        if (actor->DrawObject == null || (nint)actor->DrawObject != state.Model || actor->Mode != CharacterModes.Normal) return;
        // Clear only this timeline while it is still playing. Never reset a slot
        // replaced by combat, movement, a server animation or another plugin.
        for (uint i = 0; i < 14; i++)
            if (actor->Timeline.TimelineSequencer.TimelineIds[(int)i] == owned)
                actor->Timeline.TimelineSequencer.SetSlotTimeline(i, 0);
    }
    public void EndFrame()
    {
        foreach (uint id in new List<uint>(states.Keys))
            if (!states[id].Seen) { Stop(states[id]); states.Remove(id); }
    }
    public void Dispose() { foreach (var state in states.Values) Stop(state); states.Clear(); }
}
