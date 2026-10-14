using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SizeChange;

internal sealed unsafe class MonsterAnimationPlayer : IDisposable
{
    internal delegate bool Resolve(string path, out ushort id, out string error);
    private sealed class State
    {
        public uint Entity;
        public nint Address, Model;
        public bool Seen, Idle, SuppressIdle, Available;
        public long Deadline;
        public int Pulses, Requests;
        public string Signature = string.Empty, Status = "Waiting for pulse";
        public MonsterIdleLatch Latch = new();
        public MonsterTimelineOverride Override;
    }
    private readonly Dictionary<uint, State> states = new();
    public string Status(uint entity) => states.TryGetValue(entity, out var state)
        ? $"{state.Status} | Pulses {state.Pulses}, requests {state.Requests}" : "Waiting for actor update";
    public void BeginFrame() { foreach (var state in states.Values) state.Seen = false; }
    private static string Signature(List<MonsterAnimationSlot> slots)
    {
        var text = new StringBuilder();
        foreach (var slot in slots) text.Append(slot.Path).Append('|').Append(slot.DurationSeconds).Append('|').Append(slot.IsIdle).Append(';');
        return text.ToString();
    }
    public void Tick(Character* actor, List<MonsterAnimationSlot>? slots, bool allowed, Resolve resolve)
    {
        if (actor == null) return;
        if (states.TryGetValue(actor->EntityId, out var prior) && prior.Address != (nint)actor)
            states.Remove(actor->EntityId);
        if (!states.TryGetValue(actor->EntityId, out var state))
        {
            if (slots == null || slots.Count == 0) return;
            states[actor->EntityId] = state = new() { Entity = actor->EntityId, Address = (nint)actor };
        }
        state.Seen = true;
        if (!allowed || slots == null || slots.Count == 0 || actor->DrawObject == null)
        {
            Stop(state);
            if (state.Available) state.Latch = new();
            state.Available = false; state.Status = "Inactive or model unavailable"; return;
        }
        state.Available = true;
        string configuration = Signature(slots);
        if (configuration != state.Signature)
        { Stop(state); state.Signature = configuration; state.SuppressIdle = false; }
        if (state.Override.Active)
        {
            if ((nint)actor->DrawObject != state.Model || !state.Override.Matches(actor->Timeline.BaseOverride, (byte)actor->Mode, actor->ModeParam))
            { Stop(state); state.Status = "Interrupted by actor state"; }
            else if (Environment.TickCount64 >= state.Deadline || (state.Idle && actor->InCombat))
            { Stop(state); state.Status = "Finished"; }
            else
            {
                ushort playing = actor->Timeline.TimelineSequencer.TimelineIds[0];
                state.Status = $"Requested {state.Override.Timeline}; base slot {playing}; mode {(byte)actor->Mode}";
            }
        }
        // Our own AnimLock must not fail the Normal-mode guard or prevent expiry.
        if (!state.Override.Active && actor->Mode != CharacterModes.Normal)
        {
            if (actor->InCombat) state.Latch.ShouldStart(true, configuration);
            state.Status = $"Blocked: actor mode {actor->Mode} ({(byte)actor->Mode})";
            return;
        }
        if (actor->InCombat) state.SuppressIdle = false;
        bool idleDue = state.Latch.ShouldStart(actor->InCombat, configuration);
        if (idleDue && !state.SuppressIdle)
        {
            var slot = MonsterAnimationSlot.Select(slots, true, Random.Shared.NextDouble());
            if (slot != null) Start(actor, state, slot, resolve);
        }
    }
    public void Pulse(Character* actor, List<MonsterAnimationSlot>? slots, Resolve resolve)
    {
        if (actor == null || !states.TryGetValue(actor->EntityId, out var state) || state.Address != (nint)actor) return;
        state.Pulses++;
        if (state.Override.Active && state.Idle && !actor->InCombat) return;
        var slot = MonsterAnimationSlot.Select(slots, false, Random.Shared.NextDouble());
        if (slot != null) Start(actor, state, slot, resolve);
    }
    public void Test(Character* actor, MonsterAnimationSlot? slot, Resolve resolve)
    {
        if (actor == null || !states.TryGetValue(actor->EntityId, out var state) || state.Address != (nint)actor) return;
        if (slot == null) { Stop(state); state.SuppressIdle = true; state.Status = "Stopped"; }
        else if (slot.IsIdle && actor->InCombat) state.Status = "Idle test requires out of combat";
        else { state.SuppressIdle = false; Start(actor, state, slot, resolve); }
    }
    private static void Start(Character* actor, State state, MonsterAnimationSlot slot, Resolve resolve)
    {
        if (actor->DrawObject == null || actor->Health == 0) { state.Status = "Model unavailable or dead"; return; }
        if (!resolve(slot.Path, out ushort timeline, out string error)) { state.Status = error; return; }
        Stop(state);
        if (actor->Mode != CharacterModes.Normal || actor->Timeline.BaseOverride != 0)
        { state.Status = $"Blocked: mode {(byte)actor->Mode}, override {actor->Timeline.BaseOverride}"; return; }
        state.Model = (nint)actor->DrawObject;
        ushort value = actor->Timeline.BaseOverride;
        byte mode = (byte)actor->Mode, parameter = actor->ModeParam;
        state.Override.Acquire(timeline, ref value, ref mode, ref parameter);
        // Same base-override/AnimLock entry route as VFXEditor's preview. Let
        // the game load the timeline and its model-specific PAP on the next update.
        actor->Timeline.BaseOverride = value;
        actor->ModeParam = parameter;
        actor->Mode = (CharacterModes)mode;
        actor->Timeline.TimelineSequencer.TimelineIds[0] = 0;
        state.Idle = slot.IsIdle;
        state.Deadline = slot.IsIdle && slot.DurationSeconds <= 0 ? long.MaxValue :
            Environment.TickCount64 + (long)(Math.Clamp(slot.DurationSeconds, 0.05f, 600f) * 1000f);
        state.Requests++;
        state.Status = $"Requested timeline {timeline}";
    }
    private static void Stop(State state)
    {
        if (!state.Override.Active) return;
        var obj = Plugin.ObjectTable.SearchByEntityId(state.Entity);
        if (obj is not IBattleNpc || obj.Address != state.Address) { state.Override = default; return; }
        var actor = (Character*)obj.Address;
        ushort timeline = state.Override.Timeline, value = actor->Timeline.BaseOverride;
        byte mode = (byte)actor->Mode, parameter = actor->ModeParam;
        if (state.Override.Release(ref value, ref mode, ref parameter))
        {
            actor->Timeline.BaseOverride = value;
            actor->Mode = (CharacterModes)mode;
            actor->ModeParam = parameter;
            // Model replacement still needs character override restoration, but
            // must never clear the new model's animation slots.
            if (actor->DrawObject != null && (nint)actor->DrawObject == state.Model)
                for (int i = 0; i < 14; i++)
                    if (actor->Timeline.TimelineSequencer.TimelineIds[i] == timeline)
                        actor->Timeline.TimelineSequencer.SetSlotTimeline((uint)i, 0);
        }
    }
    public void EndFrame()
    {
        foreach (uint id in new List<uint>(states.Keys))
            if (!states[id].Seen) { Stop(states[id]); states.Remove(id); }
    }
    public void Dispose() { foreach (var state in states.Values) Stop(state); states.Clear(); }
}
