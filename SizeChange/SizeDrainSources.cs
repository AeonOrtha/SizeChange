using System;
using System.Collections.Generic;
using System.Numerics;

namespace SizeChange;

// Frame-local character snapshots. Never retain or dereference source pointers
// after the object-table scan; each receiver owns its own hit list.
internal sealed class SizeDrainSources
{
    internal readonly record struct Source(nint Address, uint EntityId, Vector3 Position, string Name, bool Player);
    internal readonly record struct Hit(Source Target, float Ratio);
    private readonly List<Source> sources = new();
    private readonly HashSet<nint> seen = new();
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public void BeginFrame() { sources.Clear(); seen.Clear(); }
    public void Add(Source source)
    {
        var p = source.Position;
        if (source.Address != 0 && float.IsFinite(p.X) && float.IsFinite(p.Y) &&
            float.IsFinite(p.Z) && seen.Add(source.Address)) sources.Add(source);
    }

    public IReadOnlyList<Hit> GetHits(nint receiver, Vector3 position, SizeDrainSettings settings, List<Hit> hits)
    {
        hits.Clear();
        if (!settings.Enabled) return hits;
        names.Clear();
        if (!settings.Everyone)
            foreach (var name in settings.Names)
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        foreach (var source in sources)
        {
            if (source.Address == receiver ||
                (!settings.Everyone && (source.Player || !names.Contains(source.Name)))) continue;
            float ratio = AetherExposure.HitRatio(Vector3.Distance(position, source.Position), settings.Range, settings.PeakHitPercent);
            if (ratio > 0f) hits.Add(new Hit(source, ratio));
        }
        return hits;
    }
}
