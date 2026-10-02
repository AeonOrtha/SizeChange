using System;
using System.Collections.Generic;

namespace SizeChange;

// Height is a session value, not a draw-object value. Keep ownership metadata
// alongside it so redraws cannot convert our last added offset into a baseline.
internal sealed class SessionHeightBaselines
{
    private readonly Dictionary<string, OwnedTransformValue> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, nint> models = new(StringComparer.Ordinal);
    public OwnedTransformValue GetOrCapture(string key, float observed)
    {
        if (values.TryGetValue(key, out var value)) return value;
        value = OwnedTransformValue.Capture(observed);
        if (float.IsFinite(observed)) values.Add(key, value);
        return value;
    }
    public void SaveOwnership(string key, OwnedTransformValue value)
    {
        if (!values.TryGetValue(key, out var original)) return;
        // Ownership may change, but the captured original cannot.
        value.Baseline = original.Baseline;
        values[key] = value;
    }
    public OwnedTransformValue ForModel(string key, nint model, float observed)
    {
        var value = GetOrCapture(key, observed);
        if (models.TryGetValue(key, out var previous) && previous != model && float.IsFinite(observed))
        {
            // A known model replacement can reset the live offset. Rebind our
            // write ownership, never the session's original height.
            value.LastWritten = observed;
            value.Owned = true;
            value.Conflict = false;
            SaveOwnership(key, value);
        }
        models[key] = model;
        return value;
    }
    public void EndSession() { values.Clear(); models.Clear(); }
}
