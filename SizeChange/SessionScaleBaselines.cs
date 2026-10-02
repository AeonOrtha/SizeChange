using System;
using System.Collections.Generic;

namespace SizeChange;

internal readonly record struct ScaleBaseline(float X, float Y, float Z, float Actor)
{
    public ScaleBaseline Multiply(float factor) => new(X * factor, Y * factor, Z * factor, Actor * factor);
    public bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) &&
        float.IsFinite(Actor) && X > 0f && Y > 0f && Z > 0f && Actor > 0f;
}

internal sealed class SessionScaleBaselines
{
    private readonly Dictionary<string, ScaleBaseline> originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> explicitOverrides = new(StringComparer.Ordinal);
    public bool TryGetOrCapture(string key, ScaleBaseline observed, out ScaleBaseline value)
    {
        if (!originals.TryGetValue(key, out var original))
        {
            if (!observed.Valid) { value = default; return false; }
            originals.Add(key, original = observed);
        }
        value = original.Multiply(explicitOverrides.GetValueOrDefault(key, 1f));
        return true;
    }
    // /scale is an explicit user choice. Store it separately; never rewrite the
    // original captured dimensions or derive another baseline from rendered size.
    public void SetExplicitReference(string key, float desiredY)
    {
        if (originals.TryGetValue(key, out var original) && float.IsFinite(desiredY) && desiredY > 0f)
            explicitOverrides[key] = desiredY / original.Y;
    }
    public void EndSession() { originals.Clear(); explicitOverrides.Clear(); }
}
