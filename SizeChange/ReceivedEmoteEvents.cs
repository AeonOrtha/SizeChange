using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SizeChange;

internal readonly record struct ReceivedEmoteEvent(uint Source, uint Emote, ulong Target,
    uint Flags, uint Territory, long ReceivedAt, int Generation)
{
    // PerformEmote: arg1 is Emote row ID; arg3 contains motion/log flags;
    // ActorControlTarget carries the actual recipient (not current selection).
    internal const uint Category = 0x122;
    internal bool LogOnly => (Flags & 2) != 0;
    internal bool IsCurrent(uint territory, long now, int generation) =>
        Territory == territory && Generation == generation && now >= ReceivedAt && now - ReceivedAt <= 2000;
}

internal sealed class ReceivedEmoteQueue
{
    private readonly ConcurrentQueue<ReceivedEmoteEvent> items = new();
    private int count, generation;
    internal int Generation => Volatile.Read(ref generation);
    internal void Add(uint source, uint emote, ulong target, uint flags, uint territory, long now, int epoch)
    {
        if (source is 0 or 0xE0000000 || emote is 0 or > ushort.MaxValue || epoch != Generation) return;
        if (Interlocked.Increment(ref count) > 1024) { Interlocked.Decrement(ref count); return; }
        items.Enqueue(new(source, emote, target, flags, territory, now, epoch));
    }
    internal bool TryRead(uint territory, long now, out ReceivedEmoteEvent item)
    {
        while (items.TryDequeue(out item))
        {
            Interlocked.Decrement(ref count);
            if (item.IsCurrent(territory, now, Generation)) return true;
        }
        item = default; return false;
    }
    internal void Clear()
    {
        Interlocked.Increment(ref generation);
        while (items.TryDequeue(out _)) Interlocked.Decrement(ref count);
    }
}
