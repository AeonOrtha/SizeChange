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
    public bool EnableHeightOffset { get; set; }
    public float HeightOffset { get; set; }
    public float PlaySpeed { get; set; } = 1f;
}

[Serializable]
public sealed class EmoteGrowthSettings
{
    public bool Enabled { get; set; }
    public List<EmoteGrowthRule> Rules { get; set; } = new();
    public EmoteGrowthRule? Match(uint emoteId, EmoteGrowthTrigger trigger) =>
        Enabled && emoteId != 0 ? Rules.Find(r => r.EmoteId == emoteId && r.Trigger == trigger) : null;
    // Pose settings affect the performer, regardless of growth direction.
    // The first row for the active emote wins, so duplicate rows never stack.
    public EmoteGrowthRule? PoseFor(uint emoteId) => Enabled && emoteId != 0
        ? Rules.Find(r => r.EmoteId == emoteId) : null;
    public float? HeightFor(uint emoteId) => PoseFor(emoteId) is { EnableHeightOffset: true } rule
        ? PoseHeightOffset.Clamp(rule.HeightOffset) : null;
    public static float ClampSpeed(float value) => float.IsFinite(value) ? Math.Clamp(value, 0.1f, 2f) : 1f;
    public static float ClampInterval(float value) => !float.IsFinite(value) ? 1f :
        value <= 0f ? 0f : Math.Clamp(value, 0.1f, 3600f);
    public void Validate()
    {
        Rules ??= new();
        Rules.RemoveAll(r => r == null);
        if (Rules.Count > 128) Rules.RemoveRange(128, Rules.Count - 128);
        foreach (var rule in Rules)
        {
            if (!Enum.IsDefined(rule.Trigger)) rule.Trigger = EmoteGrowthTrigger.Used;
            rule.Growth = float.IsFinite(rule.Growth) ? Math.Clamp(rule.Growth, 0, ImmediateGrowth.Maximum) : 0;
            rule.IntervalSeconds = ClampInterval(rule.IntervalSeconds);
            rule.HeightOffset = PoseHeightOffset.Clamp(rule.HeightOffset);
            rule.PlaySpeed = ClampSpeed(rule.PlaySpeed);
        }
    }
    public static EmoteGrowthSettings CopyOf(EmoteGrowthSettings value) => new()
    {
        Enabled = value.Enabled,
        Rules = value.Rules.ConvertAll(r => new EmoteGrowthRule
        { EmoteId = r.EmoteId, Trigger = r.Trigger, Growth = r.Growth, IntervalSeconds = r.IntervalSeconds, EnableHeightOffset = r.EnableHeightOffset, HeightOffset = r.HeightOffset, PlaySpeed = r.PlaySpeed }),
    };
}
