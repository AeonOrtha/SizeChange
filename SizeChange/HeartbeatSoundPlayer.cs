using System;

namespace SizeChange;

[Serializable]
public sealed class HeartbeatSoundSettings
{
    public bool Enabled { get; set; }
    public string Path { get; set; } = string.Empty;
    public int Index { get; set; }
    public float Volume { get; set; } = 0.5f;
    public float FadeInSeconds { get; set; } = 0.5f;
    public float FadeOutSeconds { get; set; } = 0.5f;

    public void Validate()
    {
        Path = (Path ?? string.Empty).Trim().Replace('\\', '/');
        Index = Math.Max(0, Index);
        Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0f, 1f) : 0.5f;
        FadeInSeconds = float.IsFinite(FadeInSeconds) ? Math.Clamp(FadeInSeconds, 0f, 10f) : 0.5f;
        FadeOutSeconds = float.IsFinite(FadeOutSeconds) ? Math.Clamp(FadeOutSeconds, 0f, 10f) : 0.5f;
    }
}

internal interface IHeartbeatSoundVoice
{
    string? Validate(string path, int index);
    bool IsPlaying { get; }
    void Start(string path, int index, float volume);
    void SetVolume(float volume);
    void Stop();
}

// One owned voice, independent of the number of selected bones. An interrupted
// fade reverses smoothly; repeated framework ticks never layer extra voices.
internal sealed class HeartbeatSoundPlayer(IHeartbeatSoundVoice voice) : IDisposable
{
    private string path = string.Empty;
    private int index = -1;
    private float level;
    private float age;
    private bool started;
    private bool validated;
    private bool faulted;
    private bool wasEnabled;
    public string Status { get; private set; } = string.Empty;

    public void Retry()
    {
        Stop();
        validated = false;
        faulted = false;
        Status = string.Empty;
    }

    public void Tick(HeartbeatSoundSettings settings, bool pulsing, bool available, float seconds)
    {
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, 1f) : 0f;
        string selectedPath = settings.Path.Trim().Replace('\\', '/');
        if (path != selectedPath || index != settings.Index)
        {
            Retry();
            path = selectedPath;
            index = settings.Index;
        }
        if (settings.Enabled && !wasEnabled && faulted) Retry();
        wasEnabled = settings.Enabled;
        // Logout, zoning and disposal must not leave a background loop running.
        if (!available) { Stop(); return; }
        if (faulted) return;
        bool run = settings.Enabled && pulsing && settings.Volume > 0;
        float target = run ? 1f : 0f;
        float duration = run ? settings.FadeInSeconds : settings.FadeOutSeconds;
        level = duration <= 0 ? target : Math.Clamp(level + (run ? 1f : -1f) * seconds / duration, 0f, 1f);
        if (level <= 0f && !run) { Stop(); return; }
        try
        {
            if (!validated)
            {
                string? error = voice.Validate(path, index);
                if (error != null) throw new InvalidOperationException(error);
                validated = true;
            }
            age += seconds;
            if (started && age >= 0.25f && !voice.IsPlaying)
            {
                voice.Stop();
                started = false;
            }
            // Native loop points keep a looped SCD running. A one-shot entry
            // repeats when finished, without replaying the fade-in envelope.
            if (!started)
            {
                voice.Start(path, index, level * settings.Volume);
                started = true;
                age = 0f;
            }
            voice.SetVolume(level * settings.Volume);
            Status = run ? "Sound playing." : "Sound fading.";
        }
        catch (Exception ex)
        {
            Stop();
            faulted = true;
            Status = ex.Message;
        }
    }

    private void Stop()
    {
        voice.Stop();
        started = false;
        level = 0f;
        age = 0f;
        if (!faulted) Status = string.Empty;
    }

    public void Dispose() => Stop();
}
