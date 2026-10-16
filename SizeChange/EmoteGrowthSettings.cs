using System;
using System.Collections.Generic;

namespace SizeChange;

public enum EmoteGrowthTrigger { Used, Received }

[Serializable]
public sealed class EmoteGrowthRule
{
    public uint EmoteId { get; set; }
    public EmoteGrowthTrigger Trigger { get; set; }
    public float Growth { get; set; } = 0.1f;
    public float IntervalSeconds { get; set; } = 1f;
}

[Serializable]
public sealed class EmoteGrowthSettings
{
    public bool Enabled { get; set; }
    public List<EmoteGrowthRule> Rules { get; set; } = new();
    public EmoteGrowthRule? Match(uint emoteId, EmoteGrowthTrigger trigger) =>
        Enabled && emoteId != 0 ? Rules.Find(r => r.EmoteId == emoteId && r.Trigger == trigger) : null;
    public void Validate()
    {
        Rules ??= new();
        Rules.RemoveAll(r => r == null);
        if (Rules.Count > 128) Rules.RemoveRange(128, Rules.Count - 128);
        foreach (var rule in Rules)
        {
            if (!Enum.IsDefined(rule.Trigger)) rule.Trigger = EmoteGrowthTrigger.Used;
            rule.Growth = float.IsFinite(rule.Growth) ? Math.Clamp(rule.Growth, 0, ImmediateGrowth.Maximum) : 0;
            rule.IntervalSeconds = float.IsFinite(rule.IntervalSeconds) ? Math.Clamp(rule.IntervalSeconds, 0.1f, 3600f) : 1f;
        }
    }
    public static EmoteGrowthSettings CopyOf(EmoteGrowthSettings value) => new()
    {
        Enabled = value.Enabled,
        Rules = value.Rules.ConvertAll(r => new EmoteGrowthRule
        { EmoteId = r.EmoteId, Trigger = r.Trigger, Growth = r.Growth, IntervalSeconds = r.IntervalSeconds }),
    };
}
