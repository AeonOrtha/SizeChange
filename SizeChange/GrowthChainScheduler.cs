using System;
using System.Collections.Generic;

namespace SizeChange;

internal readonly record struct GrowthPulse(float Growth, float Aether, float Emote, bool IsChain, bool Exceptional)
{
    public bool FirstChainLink { get; init; }
    public float ExceptionalAllowance { get; init; }
}

// Per actor, separate from the damage accumulator. New roots never reset old
// deadlines. One outstanding entry per chain bounds storage and native feedback.
internal sealed class GrowthChainScheduler
{
    internal const int MaximumActiveChains = 64;
    private readonly List<Link> pending = new();
    private record struct Link(float OriginalGrowth, float Aether, float Emote,
        float BasePercent, float StepPercent, int Number, int Left, float Remaining, int ExceptionalLeft);
    public int PendingCount => pending.Count;
    public ulong DroppedChains { get; private set; }
    public void Clear() => pending.Clear();

    public void Start(float growth, float aether, float emote, GrowthBonusSettings settings, Func<double> random)
    {
        int limit = Math.Max(0, settings.MaximumExtraPulses);
        float basePercent = GrowthBonusSettings.Finite(settings.ChainGrowthPercent, 0, 1000, 50);
        float stepPercent = GrowthBonusSettings.Finite(settings.ChainGrowthStepPercent, 0, 1000, 0);
        if (!settings.ChainsEnabled || limit == 0 || !float.IsFinite(growth) || growth <= 0 || basePercent + stepPercent <= 0) return;
        if (pending.Count >= MaximumActiveChains) { DroppedChains++; return; }
        if (!GrowthBonusRolls.Roll(settings.ChainChancePercent, random)) return;
        aether = float.IsFinite(aether) ? Math.Clamp(aether, 0, growth) : 0;
        emote = float.IsFinite(emote) ? Math.Clamp(emote, 0, growth - aether) : 0;
        // Capture this root's progression. Overlapping chains keep separate
        // counters, and exceptional multipliers never compound the next link.
        pending.Add(new(growth, aether, emote, basePercent, stepPercent, 1, limit, Interval(settings),
            Math.Max(0, settings.MaximumExceptionalPulsesPerChain)));
    }

    private static float Interval(GrowthBonusSettings settings) =>
        GrowthBonusSettings.Finite(settings.ChainIntervalSeconds, .05f, 10, .35f);

    public GrowthPulse? Advance(float seconds, GrowthBonusSettings settings, Func<double> random)
    {
        if (!settings.ChainsEnabled) { Clear(); return null; }
        seconds = float.IsFinite(seconds) ? Math.Clamp(seconds, 0, 1) : 0;
        int due = -1;
        for (int i = 0; i < pending.Count; i++)
        {
            var link = pending[i];
            link.Remaining = Math.Max(0, link.Remaining - seconds);
            pending[i] = link;
            if (due < 0 && link.Remaining <= .000001f) due = i;
        }
        if (due < 0) return null;
        var next = pending[due];
        pending.RemoveAt(due);
        double fraction = (next.BasePercent + (double)next.Number * next.StepPercent) / 100;
        float growth = (float)Math.Min(float.MaxValue, next.OriginalGrowth * fraction);
        double effectiveFraction = (double)growth / next.OriginalGrowth;
        var pulse = GrowthBonusRolls.Pulse(growth,
            (float)(next.Aether * effectiveFraction), (float)(next.Emote * effectiveFraction), true, settings, random,
            allowExceptional: next.ExceptionalLeft > 0)
            with { FirstChainLink = next.Number == 1 };
        if (pulse.Exceptional) next.ExceptionalLeft--;
        // Never catch up an entire sequence after a stalled frame. Other chains
        // keep their deadlines; at most one due link is dispatched per update.
        if (--next.Left > 0 && GrowthBonusRolls.Roll(settings.ChainChancePercent, random))
        {
            next.Number++;
            next.Remaining = Interval(settings);
            pending.Add(next);
        }
        return pulse;
    }
}
