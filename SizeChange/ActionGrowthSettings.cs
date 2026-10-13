using System;
using System.Collections.Generic;
using System.Globalization;

namespace SizeChange;

[Serializable]
public sealed class ActionGrowthRule
{
    public string Name { get; set; } = string.Empty;
    public float Growth { get; set; } = 0.1f;
}

[Serializable]
public sealed class ActionGrowthSettings
{
    public bool Enabled { get; set; }
    public List<ActionGrowthRule> Rules { get; set; } = new();
    public static ActionGrowthSettings CopyOf(ActionGrowthSettings value) => new()
    {
        Enabled = value.Enabled,
        Rules = value.Rules.ConvertAll(r => new ActionGrowthRule { Name = r.Name, Growth = r.Growth }),
    };
    public float GrowthFor(string name)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(name)) return 0;
        foreach (var rule in Rules)
            if (string.Equals(rule.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return float.IsFinite(rule.Growth) ? Math.Clamp(rule.Growth, 0, ImmediateGrowth.Maximum) : 0;
        return 0;
    }
    public void Validate()
    {
        Rules ??= new();
        if (Rules.Count > 128) Rules.RemoveRange(128, Rules.Count - 128);
        Rules.RemoveAll(r => r == null);
        foreach (var rule in Rules)
        {
            rule.Name ??= string.Empty; // Preserve spaces while typing; matching trims the name.
            rule.Growth = float.IsFinite(rule.Growth) ? Math.Clamp(rule.Growth, 0, ImmediateGrowth.Maximum) : 0;
        }
    }
}

// Direct growth is already valued: no damage cap, damage multiplier, size-driven
// damage gain, reserve share or timer conversion is applied a second time.
internal sealed class ImmediateGrowth
{
    internal const float Maximum = 10000f;
    private readonly Dictionary<(uint Entity, nint Address), float> pending = new();
    public static bool Parse(string text, out float amount) => float.TryParse(text.Trim(),
        NumberStyles.Float, CultureInfo.InvariantCulture, out amount) && float.IsFinite(amount) && amount > 0 && amount <= Maximum;
    public void Add(uint entity, nint address, float amount)
    {
        if (address == 0 || !float.IsFinite(amount) || amount <= 0) return;
        var key = (entity, address);
        pending[key] = Math.Min(Maximum, pending.GetValueOrDefault(key) + Math.Min(Maximum, amount));
    }
    public float Take(uint entity, nint address) => pending.Remove((entity, address), out var value) ? value : 0;
    public void Clear() => pending.Clear();
}

// Shared by ordinary accumulation and immediate contributions. Only the new
// contribution bypasses the timer; pending damage and its deadline are preserved.
internal static class GrowthContribution
{
    public static bool Queue(ref float settled, ref float pending, ref float pendingDamage,
        ref float remainingSeconds, float growth, float damageRatio, float delay, bool immediate)
    {
        if (!float.IsFinite(growth) || growth <= 0) return false;
        if (immediate || delay <= 0) { settled += growth; return true; }
        pending += growth;
        pendingDamage = Math.Min(1f, pendingDamage + damageRatio);
        if (remainingSeconds <= 0) remainingSeconds = delay;
        return false;
    }
}

internal readonly record struct ReceivedAction(uint Caster, uint Sequence, uint ActionId, ulong Target, uint Territory, long ReceivedAt)
{
    internal static bool Successful(byte type, byte flags, bool selfTarget = false)
    {
        if ((flags & 0x80) != 0 && !selfTarget) return false;
        return type switch
        {
            3 or 5 or 6 => (flags & 0x10) == 0,
            15 or 18 or 62 => selfTarget,
            4 or 10 or 11 or 14 or 16 or 17 or 24 or 25 or 26 or 31 or 32 or 33 or 34 or 35 or 36 or 50 or 59 or 74 or 75 or 76 => true,
            _ => false,
        };
    }
}
internal sealed class ActionGrowthDeduplication
{
    private readonly Dictionary<(uint Caster, uint Sequence, uint Action, ulong Target), long> seen = new();
    public bool Accept(ReceivedAction action, long now)
    {
        if (now - action.ReceivedAt > 2000 || now < action.ReceivedAt) return false;
        if (seen.Count > 4096) seen.Clear();
        if (action.Sequence == 0) return true;
        var key = (action.Caster, action.Sequence, action.ActionId, action.Target);
        if (seen.TryGetValue(key, out long time) && now - time < 10000) return false;
        seen[key] = now;
        return true;
    }
    public void Clear() => seen.Clear();
}
