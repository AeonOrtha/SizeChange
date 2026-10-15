using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SizeChange;

internal readonly record struct PeriodicGrowthTick(uint Target, bool Healing, uint Territory, long ReceivedAt, int Generation)
{
    // Global actor-control categories since 7.1. Observe only these messages:
    // normal HP updates also include natural regeneration and direct attacks.
    internal static bool IsPeriodic(uint category) => category is 0x604 or 0x605;
    internal bool IsCurrent(uint territory, long now, int generation) =>
        Territory == territory && Generation == generation && now >= ReceivedAt && now - ReceivedAt <= 2000;
}

// No time-window deduplication: separate ground effects and simultaneous DoT/HoT
// notifications are separate ticks, even if their target and timestamp match.
internal sealed class PeriodicGrowthQueue
{
    private readonly ConcurrentQueue<PeriodicGrowthTick> items = new();
    private int count, generation;
    public int Generation => Volatile.Read(ref generation);
    public void Add(uint target, uint category, uint territory, long now, int epoch)
    {
        if (!PeriodicGrowthTick.IsPeriodic(category) || target is 0 or 0xE0000000 || epoch != Generation) return;
        if (Interlocked.Increment(ref count) > 1024) { Interlocked.Decrement(ref count); return; }
        items.Enqueue(new(target, category == 0x604, territory, now, epoch));
    }
    public bool TryRead(uint territory, long now, out PeriodicGrowthTick tick)
    {
        while (items.TryDequeue(out tick))
        {
            Interlocked.Decrement(ref count);
            if (tick.IsCurrent(territory, now, Generation)) return true;
        }
        tick = default; return false;
    }
    public void Clear()
    {
        Interlocked.Increment(ref generation);
        while (items.TryDequeue(out _)) Interlocked.Decrement(ref count);
    }
}
