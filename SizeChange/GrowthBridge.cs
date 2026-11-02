using System;
using System.Collections.Generic;

namespace SizeChange;

// Finite extra earned growth around a real release: anticipation before it,
// then a slow tail starting at its expected overshoot peak. Never feeds itself.
internal struct GrowthBridge
{
    private struct Segment
    {
        public double Amount, BeforeSeconds, PeakDelay, TailSeconds, Elapsed, Paid, PaidAtRelease, EmoteFraction;
        public bool Waiting, Aether;
    }
    internal readonly record struct Gain(float Growth, float AetherBonus, float EmoteBonus);
    private List<Segment>? segments;
    internal bool HasGrowth => segments is { Count: > 0 };
    internal int ActiveSegments => segments?.Count ?? 0;

    internal void Queue(float earned, float percent, float untilRelease, float pulseDuration,
        bool aether, float emoteFraction = 0, bool released = false, float risePercent = 30)
    {
        if (!float.IsFinite(earned) || earned <= 0 || !float.IsFinite(percent) || percent <= 0) return;
        double amount = earned * (double)Math.Clamp(percent, 0, 500) / 100;
        double before = float.IsFinite(untilRelease) ? Math.Clamp(untilRelease, 0, 60) : 0;
        double tail = float.IsFinite(pulseDuration) ? Math.Clamp(pulseDuration, .1f, 20f) : .7;
        double peak = tail * (float.IsFinite(risePercent) ? Math.Clamp(risePercent, 0, 99) / 100d : .3);
        double emote = float.IsFinite(emoteFraction) ? Math.Clamp(emoteFraction, 0, 1) : 0;
        bool waiting = !released && before > 0;
        segments ??= new(4);
        if (segments.Count > 0)
        {
            var last = segments[^1];
            if (last.Elapsed == 0 && last.Paid == 0 && last.Waiting == waiting && last.BeforeSeconds == before &&
                last.PeakDelay == peak && last.TailSeconds == tail && last.Aether == aether && last.EmoteFraction == emote)
            {
                last.Amount += amount; segments[^1] = last; return;
            }
        }
        segments.Add(new() { Amount = amount, BeforeSeconds = before, PeakDelay = peak, TailSeconds = tail,
            Waiting = waiting, Aether = aether, EmoteFraction = emote });
    }

    // Called when the actual accumulator empties, not when an immediate command
    // merely adds a separate pulse. Early releases transfer any unused pre-share
    // into the tail without an instantaneous size increase.
    internal void ReleaseQueued()
    {
        if (segments == null) return;
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!segment.Waiting) continue;
            segment.Waiting = false;
            segment.Elapsed = 0;
            segment.PaidAtRelease = segment.Paid;
            segments[i] = segment;
        }
    }

    internal Gain Advance(float seconds)
    {
        if (segments == null || !float.IsFinite(seconds) || seconds <= 0) return default;
        double earned = 0, aether = 0, emote = 0;
        int live = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            double end = segment.Waiting ? segment.BeforeSeconds : segment.PeakDelay + segment.TailSeconds;
            segment.Elapsed = Math.Min(end, segment.Elapsed + seconds);
            double cumulative = segment.Waiting
                ? segment.Amount * .3 * Ease(segment.Elapsed / segment.BeforeSeconds)
                : segment.PaidAtRelease + (segment.Amount - segment.PaidAtRelease) *
                    Ease((segment.Elapsed - segment.PeakDelay) / segment.TailSeconds);
            double added = Math.Max(0, cumulative - segment.Paid);
            segment.Paid = cumulative;
            earned += added;
            if (segment.Aether) aether += added;
            else emote += added * segment.EmoteFraction;
            if (segment.Waiting || segment.Elapsed < end) segments[live++] = segment;
        }
        if (live < segments.Count) segments.RemoveRange(live, segments.Count - live);
        return new((float)Math.Min(float.MaxValue, earned), (float)Math.Min(float.MaxValue, aether),
            (float)Math.Min(float.MaxValue, emote));
    }

    private static double Ease(double value)
    {
        double t = Math.Clamp(value, 0, 1);
        return Math.Clamp(t * t * t * (t * (6 * t - 15) + 10), 0, 1);
    }
    internal void Clear() => segments?.Clear();
}
