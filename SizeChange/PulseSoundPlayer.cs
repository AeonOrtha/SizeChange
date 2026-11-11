using System;
using System.Collections.Generic;

namespace SizeChange;

internal enum PulseSoundKind { Chain, Exceptional }

internal interface IPulseSoundVoice
{
    string? Validate(string path, int index);
    bool IsPlaying { get; }
    void SetPosition(float x, float y, float z);
    void Start(string path, int index, float volume, float playbackRate = 1f);
    void SetVolume(float volume);
    void SetPlaybackRate(float rate);
    void Stop();
}

internal readonly record struct ChainSoundState(bool Active, float Scale, float X, float Y, float Z);

// Bonus sounds have owned voices, separate from the ordinary growth preview
// wrapper. Retriggering one kind cannot stop the other kind or another actor.
internal sealed class PulseSoundPlayer(Func<IPulseSoundVoice> createVoice) : IDisposable
{
    internal const int MaximumVoices = 32;
    private readonly record struct Key(int Group, PulseSoundKind Kind, nint Actor, uint Entity);
    private sealed class Entry(IPulseSoundVoice voice, PulseSoundSettings settings, bool test = false)
    {
        public readonly IPulseSoundVoice Voice = voice;
        public readonly PulseSoundSettings Settings = settings;
        public readonly bool Test = test;
        public float Age;
        public bool ChainSession;
        public string Path = settings.Path.Trim().Replace('\\', '/');
        public int Index = settings.Index;
        public float RepeatAge;
        public float Gain = 1;
        public float FadeStartGain = 1;
        public float FadeAge;
        public bool Fading;
        public bool Ended;
    }
    private readonly Dictionary<Key, Entry> active = new();
    private readonly Dictionary<(int, PulseSoundKind), string> statuses = new();
    private readonly List<Key> remove = new();
    public int ActiveCount => active.Count;

    public string Status(int group, PulseSoundKind kind) => statuses.GetValueOrDefault((group, kind), "No playback request yet.");
    private string Report(int group, PulseSoundKind kind, string message)
    { statuses[(group, kind)] = message; return message; }

    public string Play(int group, PulseSoundKind kind, nint actor, uint entity, PulseSoundSettings settings,
        float scale, float x, float y, float z, bool test = false)
    {
        if (!settings.Enabled && !test) return Report(group, kind, "Sound disabled.");
        string path = (settings.Path ?? string.Empty).Trim().Replace('\\', '/');
        if (actor == 0 || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            return Report(group, kind, "Actor unavailable.");
        if (!path.StartsWith("sound/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase) || path.Contains("..") || path.Contains('\0'))
            return Report(group, kind, "Use a game path: sound/... .scd.");
        if (settings.Index < 0 || !float.IsFinite(settings.Volume) || settings.Volume <= 0)
            return Report(group, kind, "Check sound index and volume.");
        float volume = SoundVolumeScaling.ForSize(settings.SizeVolume, settings.Volume,
            scale, settings.VolumeGain, settings.MaximumVolume);
        if (!float.IsFinite(volume) || volume <= 0) return Report(group, kind, "Effective volume is zero.");
        float rate = SoundPlaybackRate.ForSize(settings.SizeRate, scale, settings.RateDrop, settings.MinimumRate);
        var key = new Key(group, kind, actor, entity);
        if (!active.TryGetValue(key, out var entry))
        {
            if (active.Count >= MaximumVoices) return Report(group, kind, "Pulse sound voice limit reached.");
            entry = new(createVoice(), settings);
        }
        try
        {
            string? error = entry.Voice.Validate(path, settings.Index);
            if (error != null) return Report(group, kind, error);
            entry.Voice.Stop();
            entry.Voice.SetPosition(x, y, z);
            entry.Voice.Start(path, settings.Index, volume, rate);
            active[key] = new(entry.Voice, settings, test);
            return Report(group, kind, $"Playback requested: index {settings.Index}, volume {volume:0.00}x, rate {rate:0.000}.");
        }
        catch (Exception ex)
        {
            entry.Voice.Stop(); active.Remove(key);
            return Report(group, kind, "Playback failed: " + ex.GetBaseException().Message);
        }
    }

    // One sound session per actor across overlapping chains. Only a first link
    // may start a session; subsequent links never restart or resurrect it.
    public string ChainPulse(int group, nint actor, uint entity, PulseSoundSettings settings,
        float scale, float x, float y, float z, bool firstLink, bool test = false)
    {
        var key = new Key(group, PulseSoundKind.Chain, actor, entity);
        if (!firstLink) return Status(group, PulseSoundKind.Chain);
        if (!test && active.TryGetValue(key, out var existing) && existing.ChainSession && !existing.Test &&
            ReferenceEquals(settings, existing.Settings))
        {
            existing.Fading = false;
            existing.FadeAge = 0;
            return Status(group, PulseSoundKind.Chain);
        }
        string result = Play(group, PulseSoundKind.Chain, actor, entity, settings, scale, x, y, z, test);
        if (result.StartsWith("Playback requested", StringComparison.Ordinal) && active.TryGetValue(key, out var entry))
            entry.ChainSession = true;
        return result;
    }

    private bool TickChain(Key key, Entry entry, float seconds, ChainSoundState state)
    {
        var s = entry.Settings;
        bool running = entry.Test ? entry.Age < 3 : state.Active;
        if (running)
        {
            entry.Fading = false; entry.FadeAge = 0;
            // A new chain during a fade resumes from current volume, without
            // a hard jump or rewinding an internally looping SCD.
            entry.Gain = Math.Min(1, entry.Gain + seconds / .15f);
        }
        else
        {
            if (!entry.Fading)
            {
                entry.Fading = true; entry.FadeStartGain = entry.Gain; entry.FadeAge = 0;
                Report(key.Group, key.Kind, "Chain finished; fading out.");
            }
            entry.FadeAge += seconds;
            float t = Math.Clamp(entry.FadeAge / GrowthBonusSettings.Finite(s.ChainFadeOutSeconds, .05f, 30, 1), 0, 1);
            entry.Gain = entry.FadeStartGain * (1 - t * t * (3 - 2 * t));
            if (t >= 1) { Report(key.Group, key.Kind, "Chain sound finished."); return false; }
        }
        float volume = SoundVolumeScaling.ForSize(s.SizeVolume, s.Volume, state.Scale, s.VolumeGain, s.MaximumVolume);
        float rate = SoundPlaybackRate.ForSize(s.SizeRate, state.Scale, s.RateDrop, s.MinimumRate);
        if (!float.IsFinite(volume) || !float.IsFinite(rate) || !float.IsFinite(state.X) ||
            !float.IsFinite(state.Y) || !float.IsFinite(state.Z)) return false;
        entry.Voice.SetPosition(state.X, state.Y, state.Z);
        entry.RepeatAge += seconds;
        if (running && s.KeepThroughChain && s.ChainRepeat &&
            entry.RepeatAge >= GrowthBonusSettings.Finite(s.ChainRepeatSeconds, .05f, 300, 1))
        {
            // One restart at most per update, never a catch-up burst. The
            // interval is wall-clock time, independent of pitch and links.
            entry.RepeatAge = 0; entry.Ended = false;
            entry.Voice.Stop();
            entry.Voice.Start(entry.Path, entry.Index, volume * entry.Gain, rate);
        }
        if (!s.KeepThroughChain && entry.Age >= 60 && !entry.Ended)
        { entry.Voice.Stop(); entry.Ended = true; }
        if (entry.RepeatAge >= .25f && !entry.Voice.IsPlaying && !entry.Ended)
        {
            entry.Ended = true;
            if (running && s.KeepThroughChain && !s.ChainRepeat)
                Report(key.Group, key.Kind, "SCD ended; use Replay Every for a non-looping sound.");
        }
        entry.Voice.SetVolume(Math.Max(0, volume * entry.Gain));
        entry.Voice.SetPlaybackRate(rate);
        return true;
    }

    public void Tick(float seconds, bool available, Func<int, PulseSoundKind, PulseSoundSettings?> currentSettings,
        Func<nint, uint, bool> actorValid, Func<nint, uint, bool> actorTracked,
        Func<nint, uint, ChainSoundState?>? chainState = null)
    {
        if (!available) { Dispose(); return; }
        remove.Clear();
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0, 1) : 0;
        foreach (var pair in active)
        {
            var entry = pair.Value; entry.Age += seconds;
            var settings = currentSettings(pair.Key.Group, pair.Key.Kind);
            bool invalid = !actorValid(pair.Key.Actor, pair.Key.Entity) || (!entry.Test &&
                (!ReferenceEquals(settings, entry.Settings) || settings == null || !settings.Enabled ||
                 settings.Volume <= 0 || !actorTracked(pair.Key.Actor, pair.Key.Entity)));
            if (entry.ChainSession && !invalid)
            {
                // Changing the selected file invalidates this session. Do not
                // validate/read game files repeatedly in the framework loop.
                invalid = entry.Path != (entry.Settings.Path ?? string.Empty).Trim().Replace('\\', '/') || entry.Index != entry.Settings.Index;
                var state = chainState?.Invoke(pair.Key.Actor, pair.Key.Entity);
                if (!invalid && state.HasValue)
                {
                    try { if (TickChain(pair.Key, entry, seconds, state.Value)) continue; }
                    catch (Exception ex) { Report(pair.Key.Group, pair.Key.Kind, "Playback failed: " + ex.GetBaseException().Message); }
                }
                invalid = true;
            }
            // Includes resource-loading time, matching the existing heartbeat
            // voice. Bound internal SCD loops even if a file never finishes.
            if (invalid || entry.Age >= 60 || (entry.Age >= .25f && !entry.Voice.IsPlaying))
            {
                entry.Voice.Stop(); remove.Add(pair.Key);
            }
        }
        foreach (var key in remove) active.Remove(key);
    }

    public void Stop(int group, PulseSoundKind kind)
    {
        remove.Clear();
        foreach (var pair in active)
            if (pair.Key.Group == group && pair.Key.Kind == kind)
            { pair.Value.Voice.Stop(); remove.Add(pair.Key); }
        foreach (var key in remove) active.Remove(key);
        Report(group, kind, "Stopped.");
    }

    public void Dispose()
    {
        foreach (var entry in active.Values) entry.Voice.Stop();
        active.Clear(); remove.Clear();
    }
}
