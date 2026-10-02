using System;

namespace SizeChange;

// A fixed baseline and the last value we actually wrote. Never learn a new
// baseline from our animated output or an unexplained external write.
internal struct OwnedTransformValue
{
    public float Baseline;
    public float LastWritten;
    public bool Owned;
    public bool Conflict;
    public static OwnedTransformValue Capture(float value) => new() { Baseline = value, LastWritten = value };
    private static bool Same(float a, float b) => float.IsFinite(a) && float.IsFinite(b) &&
        MathF.Abs(a - b) <= Math.Max(0.0001f, MathF.Abs(b) * 0.00001f);
    public bool Observe(float current)
    {
        if (!Same(current, Baseline) && !(Owned && Same(current, LastWritten)))
        { Conflict = true; Owned = false; }
        return !Conflict;
    }
    public float Apply(float current, float target)
    {
        if (!Observe(current) || !float.IsFinite(target)) return current;
        LastWritten = target;
        Owned = !Same(target, Baseline);
        return target;
    }
    public float Restore(float current)
    {
        float result = Owned && Same(current, LastWritten) ? Baseline : current;
        Owned = false;
        return result;
    }
}

internal struct CharacterTransformBaseline
{
    public nint DrawAddress;
    public OwnedTransformValue X, Y, Z, ActorScale, Height;
    public bool Ready;
    public bool ScaleConflict => X.Conflict || Y.Conflict || Z.Conflict || ActorScale.Conflict;
}
