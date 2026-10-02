using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public sealed class AetherSoundSettings
{
    public bool Enabled { get; set; }
    public HeartbeatSoundSlot Sound { get; set; } = new();
    public void Validate() { Sound ??= new(); Sound.Validate(); }
    public static AetherSoundSettings CopyOf(AetherSoundSettings value) => new()
    {
        Enabled = value.Enabled,
        Sound = new() { Path = value.Sound.Path, Index = value.Sound.Index, Volume = value.Sound.Volume },
    };
}

internal interface IPositionalHeartbeatSoundVoice : IHeartbeatSoundVoice
{
    void SetPosition(float x, float y, float z);
}

// One owned loop per affected character or contributing crystal. Repeated
// requests from overlapping profiles do not layer duplicate source sounds.
internal sealed class AetherSoundPlayer(Func<IPositionalHeartbeatSoundVoice> createVoice) : IDisposable
{
    private sealed class Entry(IPositionalHeartbeatSoundVoice voice, uint entityId)
    {
        public readonly IPositionalHeartbeatSoundVoice Voice = voice;
        public readonly HeartbeatLoopPlayer Loop = new(voice);
        public readonly uint EntityId = entityId;
        public readonly HeartbeatSoundSettings Settings = new()
        { Enabled = true, Loop = true, FadeInSeconds = 0f, FadeOutSeconds = 0f };
    }
    private readonly Dictionary<(nint Address, bool Source), Entry> active = new();
    private readonly HashSet<(nint Address, bool Source)> requested = new();
    private float seconds;

    public void BeginFrame(float deltaSeconds) { requested.Clear(); seconds = deltaSeconds; }

    public void Request(nint address, uint entityId, bool source, AetherSoundSettings settings,
        float x, float y, float z)
    {
        if (!settings.Enabled || settings.Sound.Volume <= 0f || string.IsNullOrWhiteSpace(settings.Sound.Path)) return;
        var key = (address, source);
        if (!requested.Add(key)) return;
        if (active.TryGetValue(key, out var entry) && entry.EntityId != entityId)
        {
            entry.Loop.Dispose();
            active.Remove(key);
            entry = null;
        }
        if (entry == null) active[key] = entry = new Entry(createVoice(), entityId);
        entry.Settings.Path = settings.Sound.Path;
        entry.Settings.Index = settings.Sound.Index;
        entry.Settings.Volume = settings.Sound.Volume;
        entry.Voice.SetPosition(x, y, z);
        entry.Loop.Tick(entry.Settings, true, true, seconds);
    }

    public void EndFrame()
    {
        foreach (var key in new List<(nint Address, bool Source)>(active.Keys))
            if (!requested.Contains(key))
            {
                active[key].Loop.Dispose();
                active.Remove(key);
            }
    }

    public void Dispose()
    {
        foreach (var entry in active.Values) entry.Loop.Dispose();
        active.Clear();
        requested.Clear();
    }
}
