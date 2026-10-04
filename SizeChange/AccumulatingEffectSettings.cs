using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public sealed class AccumulatingEffectSettings
{
    public bool Enabled { get; set; }
    public List<string> Paths { get; set; } = new();
    public List<float> Durations { get; set; } = new();
    public float FadeIn { get; set; } = 0.25f;
    public float FadeOut { get; set; } = 0.5f;
    public bool Afterglow { get; set; }
    public float AfterglowSeconds { get; set; } = 3f;
    public void Validate()
    {
        Paths ??= new();
        if (Paths.Count > 16) Paths.RemoveRange(16, Paths.Count - 16);
        for (int i = 0; i < Paths.Count; i++) Paths[i] = Paths[i]?.Trim().Replace('\\', '/') ?? string.Empty;
        Durations ??= new();
        while (Durations.Count < Paths.Count) Durations.Add(0f);
        if (Durations.Count > Paths.Count) Durations.RemoveRange(Paths.Count, Durations.Count - Paths.Count);
        for (int i = 0; i < Durations.Count; i++)
            Durations[i] = float.IsFinite(Durations[i]) ? Math.Clamp(Durations[i], 0f, 300f) : 0f;
        FadeIn = float.IsFinite(FadeIn) ? Math.Clamp(FadeIn, 0f, 10f) : 0.25f;
        FadeOut = float.IsFinite(FadeOut) ? Math.Clamp(FadeOut, 0f, 10f) : 0.5f;
        AfterglowSeconds = float.IsFinite(AfterglowSeconds) ? Math.Clamp(AfterglowSeconds, 0f, 60f) : 3f;
    }
    public static AccumulatingEffectSettings CopyOf(AccumulatingEffectSettings settings)
    {
        var copy = (AccumulatingEffectSettings)settings.MemberwiseClone();
        copy.Paths = new(settings.Paths);
        copy.Durations = new(settings.Durations);
        return copy;
    }
}

internal struct GrowthAfterglow
{
    private float remaining;
    public bool Advance(bool allowed, bool active, AccumulatingEffectSettings settings, float seconds)
    {
        if (!allowed || !settings.Afterglow) { remaining = 0f; return false; }
        remaining = active ? settings.AfterglowSeconds : Math.Max(0f,
            Math.Min(remaining, settings.AfterglowSeconds) - Math.Max(0f, seconds));
        return remaining > 0f;
    }
}
