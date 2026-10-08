using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SizeChange;

// A separate animation clock and pristine profile cache for each remote actor.
// This class never claims a Customize+ profile; the interceptor retains ownership.
internal sealed class AddedPlayerEffects : IDisposable
{
    private sealed class State : IDisposable
    {
        public readonly List<HeartbeatBone> Bones = new();
        public bool Seen, Allowed;
        public double Phase, JawPhase;
        public long Generation;
        public float Envelope, JawEnvelope, JawAngle, RootHeight, Scale = 1, Seconds;
        public BoneHeartbeatSettings Settings = new();
        public string? Original;
        public BoneHeartbeatProfile? Profile;
        public readonly HeartbeatScdVoice Loop = new(), First = new(), Second = new();
        public HeartbeatSoundPlayer? Sound;
        public void Dispose() { Sound?.Dispose(); }
    }
    private readonly Dictionary<PlayerProfileHeightHook.Actor, State> states = new();
    private readonly Func<BoneHeartbeatSettings> settings;
    private readonly Func<float> flatHeight;
    public AddedPlayerEffects(Func<BoneHeartbeatSettings> settings, Func<float> flatHeight)
    { this.settings = settings; this.flatHeight = flatHeight; }
    public string SoundStatus => states.Values.Select(s => s.Sound?.Status).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty;
    public void RetrySounds() { foreach (var state in states.Values) state.Sound?.Retry(); }
    public void BeginFrame() { foreach (var state in states.Values) state.Seen = false; }
    public void EndFrame()
    {
        foreach (var pair in states.ToArray())
            if (!pair.Value.Seen) { pair.Value.Dispose(); states.Remove(pair.Key); }
    }
    private State Get(PlayerProfileHeightHook.Actor actor)
    {
        if (!states.TryGetValue(actor, out var state)) states[actor] = state = new State();
        return state;
    }
    public void Update(PlayerProfileHeightHook.Actor actor, bool allowed, bool accumulating, bool growing,
        float seconds, float pendingDamage, float maximumDamage, float scale, float height,
        string reserveBone, float reserveAddition, Vector3 position)
    {
        var state = Get(actor);
        var config = settings();
        state.Seen = true; state.Allowed = allowed; state.Settings = config;
        state.Seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0;
        state.Scale = scale; state.RootHeight = allowed ? height : 0;
        config.ResolveBones(state.Bones, scale, reserveBone, allowed ? reserveAddition : 0);
        bool bones = allowed && config.Enabled && config.Strength > 0 && state.Bones.Exists(b => b.Strength > 0);
        bool jaw = allowed && config.Jaw.Enabled && config.Jaw.OpeningDegrees > 0;
        bool run = bones && (config.Mode == BoneHeartbeatMode.Always || accumulating);
        bool jawRun = jaw && (config.Jaw.Mode == JawBreathingMode.Always || growing);
        if (!bones) state.Envelope = 0;
        if (!jaw) state.JawEnvelope = 0;
        state.Envelope = Math.Clamp(state.Envelope + (run ? state.Seconds : -state.Seconds) / 0.12f, 0, 1);
        state.JawEnvelope = Math.Clamp(state.JawEnvelope + (jawRun ? state.Seconds : -state.Seconds) / 0.25f, 0, 1);
        if (state.Envelope <= 0 && state.Phase != 0) { state.Generation++; state.Phase = 0; }
        if (state.JawEnvelope <= 0) state.JawPhase = 0;
        float bpm = config.DamageDrivenBpm ? BoneHeartbeatMath.DamageBpm(config.BeatsPerMinute,
            config.MaximumBeatsPerMinute, pendingDamage, maximumDamage) : config.BeatsPerMinute;
        if (state.Envelope > 0) state.Phase += state.Seconds * bpm / 60.0;
        if (state.JawEnvelope > 0) state.JawPhase = (state.JawPhase + state.Seconds / config.Jaw.CycleSeconds) % 1.0;
        state.JawAngle = -config.Jaw.SampleCycle(state.JawPhase) * config.Jaw.OpeningDegrees * state.JawEnvelope;
        state.Loop.SetPosition(position.X, position.Y, position.Z);
        state.First.SetPosition(position.X, position.Y, position.Z);
        state.Second.SetPosition(position.X, position.Y, position.Z);
    }
    public string Build(PlayerProfileHeightHook.Actor actor, string original)
    {
        var state = Get(actor);
        if (!state.Seen)
            // A sync can arrive just before the actor's first growth update.
            return PlayerProfileHeightJson.Add(original, flatHeight());
        if (!state.Allowed) return original;
        if (state.Original != original)
        {
            state.Profile = new BoneHeartbeatProfile(original);
            state.Original = original;
        }
        return state.Profile!.Build(state.Bones, state.Envelope, state.Settings.Strength,
            state.Phase, state.JawAngle, 2, state.RootHeight);
    }
    public void UpdateSounds(Func<PlayerProfileHeightHook.Actor, bool> captured)
    {
        foreach (var pair in states)
        {
            var state = pair.Value;
            bool available = state.Allowed && captured(pair.Key);
            if (state.Settings.Sound.Enabled && available)
                state.Sound ??= new HeartbeatSoundPlayer(state.Loop, state.First, state.Second);
            state.Sound?.Tick(state.Settings.Sound, state.Settings.Enabled && state.Envelope > 0,
                available, state.Seconds, state.Phase, state.Generation, state.Scale);
        }
    }
    public void Dispose() { foreach (var state in states.Values) state.Dispose(); states.Clear(); }
}
