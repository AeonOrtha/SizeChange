using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public sealed class MonsterAnimationSlot
{
    public string Path { get; set; } = string.Empty;
    public float DurationSeconds { get; set; } = 6f;
    public bool IsIdle { get; set; }

    public static void Validate(List<MonsterAnimationSlot> slots)
    {
        slots.RemoveAll(s => s == null);
        if (slots.Count > 32) slots.RemoveRange(32, slots.Count - 32);
        foreach (var slot in slots)
        {
            slot.Path ??= string.Empty;
            slot.DurationSeconds = float.IsFinite(slot.DurationSeconds) ? Math.Clamp(slot.DurationSeconds, 0f, 600f) : 6f;
        }
    }
    internal static MonsterAnimationSlot? Select(List<MonsterAnimationSlot>? slots, bool idle, double roll)
    {
        if (slots == null) return null;
        var eligible = slots.FindAll(s => s.IsIdle == idle && !string.IsNullOrWhiteSpace(s.Path));
        return eligible.Count == 0 ? null : eligible[Math.Clamp((int)(roll * eligible.Count), 0, eligible.Count - 1)];
    }
}

// Latch survives animation expiry: an idle starts only once per combat/movement transition
// or changed configuration, not every time growth pulses or a timeline ends.
internal sealed class MonsterIdleLatch
{
    private bool attempted;
    private string signature = string.Empty;
    public bool ShouldStart(bool blocked, string configuration)
    {
        if (configuration != signature) { signature = configuration; attempted = false; }
        if (blocked) { attempted = false; return false; }
        if (attempted) return false;
        attempted = true;
        return true;
    }
}
