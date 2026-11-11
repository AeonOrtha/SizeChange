using System;
using System.Collections.Generic;

namespace SizeChange;

internal readonly record struct GrowthPulse(float Growth, float Aether, float Emote, bool IsChain, bool Exceptional);

// Per actor, separate from the damage accumulator. New roots never reset old
// deadlines. One outstanding entry per chain bounds storage and native feedback.
internal sealed class GrowthChainScheduler
{
    internal const int MaximumActiveChains = 64;
    private readonly List<Link> pending = new();
    private record struct Link(float Growth, float Aether, float Emote, int Left, float Remaining);
    public int PendingCount => pending.Count;
    public ulong DroppedChains { get; private set; }
    public void Clear() => pending.Clear();

    public void Start(float growth, float aether, float emote, GrowthBonusSettings settings, Func<double> random)
    {
        int limit = Math.Clamp(settings.MaximumExtraPulses, 0, 16);
        float fraction = GrowthBonusSettings.Finite(settings.ChainGrowthPercent, 0, 1000, 50) / 100;
        if (!settings.ChainsEnabled || limit == 0 || !float.IsFinite(growth) || growth <= 0 || fraction <= 0) return;
        if (pending.Count >= MaximumActiveChains) { DroppedChains++; return; }
        if (!GrowthBonusRolls.Roll(settings.ChainChancePercent, random)) return;
        aether = float.IsFinite(aether) ? Math.Clamp(aether, 0, growth) : 0;
        emote = float.IsFinite(emote) ? Math.Clamp(emote, 0, growth - aether) : 0;
        float linkGrowth = (float)Math.Min(float.MaxValue, (double)growth * fraction);
        double effectiveFraction = (double)linkGrowth / growth;
        pending.Add(new(linkGrowth, (float)(aether * effectiveFraction), (float)(emote * effectiveFraction), limit, Interval(settings)));
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
        var pulse = GrowthBonusRolls.Pulse(next.Growth, next.Aether, next.Emote, true, settings, random);
        // Never catch up an entire sequence after a stalled frame. Other chains
        // keep their deadlines; at most one due link is dispatched per update.
        if (--next.Left > 0 && GrowthBonusRolls.Roll(settings.ChainChancePercent, random))
        {
            next.Remaining = Interval(settings);
            pending.Add(next);
        }
        return pulse;
    }
}
