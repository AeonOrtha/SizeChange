namespace SizeChange;

// Pure ownership policy. Never restore over a newer animation owner.
internal struct MonsterTimelineOverride
{
    public ushort Timeline;
    private ushort previousBase;
    private byte previousMode, previousParam;
    public bool Active => Timeline != 0;
    public void Acquire(ushort timeline, ref ushort baseOverride, ref byte mode, ref byte parameter)
    {
        previousBase = baseOverride; previousMode = mode; previousParam = parameter;
        Timeline = timeline; baseOverride = timeline; mode = 8; parameter = 0;
    }
    public bool Release(ref ushort baseOverride, ref byte mode, ref byte parameter)
    {
        bool owned = Active && baseOverride == Timeline;
        if (owned)
        {
            baseOverride = previousBase;
            if (mode == 8 && parameter == 0) { mode = previousMode; parameter = previousParam; }
        }
        Timeline = 0;
        return owned;
    }
    public bool Matches(ushort baseOverride, byte mode, byte parameter) =>
        Active && baseOverride == Timeline && mode == 8 && parameter == 0;
}
