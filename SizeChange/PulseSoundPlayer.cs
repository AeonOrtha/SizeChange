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
    void Stop();
}

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

    public void Tick(float seconds, bool available, Func<int, PulseSoundKind, PulseSoundSettings?> currentSettings,
        Func<nint, uint, bool> actorValid, Func<nint, uint, bool> actorTracked)
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
