using System;
using FFXIVClientStructs.FFXIV.Client.Sound;

namespace SizeChange;

// Unlike the fire-and-forget growth sound, this entry disables auto-release
// while owned. That keeps its pooled SoundData from being reused under our
// retained pointer. Stop hands ownership back to the game's normal cleanup.
internal sealed unsafe class HeartbeatScdVoice : IPositionalHeartbeatSoundVoice
{
    private SoundData* sound;
    private bool positional;
    private float positionX, positionY, positionZ;
    public void SetPosition(float x, float y, float z)
    {
        positional = true;
        positionX = x; positionY = y; positionZ = z;
        if (sound != null && sound->IsActive) sound->SetPosition(true, x, y, z);
    }
    public bool IsPlaying => sound != null && sound->IsActive &&
        (sound->GetIsLoadingSoundResource() || sound->IsPlaying());

    public string? Validate(string path, int index)
    {
        if (!path.StartsWith("sound/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)) return "Use sound/... .scd.";
        if (!Plugin.DataManager.FileExists(path)) return "SCD not found.";
        byte[]? data = Plugin.DataManager.GetFile(path)?.Data;
        if (data == null || data.Length < 0x32) return "Invalid SCD.";
        int count = BitConverter.ToUInt16(data, 0x30);
        return index < 0 || index >= count ? $"Index range: 0–{count - 1}." : null;
    }

    public void Start(string path, int index, float volume, float playbackRate = 1f)
    {
        Stop();
        var manager = SoundManager.Instance();
        if (manager == null || manager->Disabled) throw new InvalidOperationException("Audio unavailable.");
        // Heartbeats remain non-positional; proximity loops opt into world positions.
        sound = manager->PlaySound(path, volume, 0, positionX, positionY, positionZ, Math.Clamp(playbackRate, 0.1f, 1f), 0,
            (uint)index, false, SoundVolumeCategory.Player, false, -1,
            false, false, positional, false);
        if (sound == null) throw new InvalidOperationException("SCD playback failed.");
    }

    public void SetPlaybackRate(float playbackRate)
    {
        float rate = Math.Clamp(playbackRate, 0.1f, 1f);
        if (sound != null && sound->IsActive && sound->Speed != rate)
            sound->SetSpeed(rate, 0);
    }

    public void SetVolume(float volume)
    {
        if (sound != null && sound->IsActive) sound->SetVolume(volume, 0);
    }

    public void Stop()
    {
        var owned = sound;
        sound = null;
        if (owned == null || !owned->IsActive) return;
        // Do not release pooled memory ourselves while the mixer may use it.
        owned->IsAutoReleaseEnabled = true;
        owned->Stop(0);
    }
}
