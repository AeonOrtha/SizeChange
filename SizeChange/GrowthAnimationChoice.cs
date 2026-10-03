using System;
using System.Collections.Generic;

namespace SizeChange;

[Serializable]
public sealed class GrowthAnimationChoice
{
    public string Path { get; set; } = string.Empty;
    public float ChancePercent { get; set; }

    internal static void Validate(List<GrowthAnimationChoice> choices)
    {
        choices.RemoveAll(choice => choice == null);
        float remaining = 100f;
        foreach (var choice in choices)
        {
            choice.Path = choice.Path?.Trim().Replace('\\', '/') ?? string.Empty;
            choice.ChancePercent = float.IsFinite(choice.ChancePercent)
                ? Math.Clamp(choice.ChancePercent, 0f, remaining) : 0f;
            remaining -= choice.ChancePercent;
        }
    }

    internal static string? Select(IReadOnlyList<GrowthAnimationChoice> choices, double roll)
    {
        double boundary = 0;
        foreach (var choice in choices)
        {
            boundary += choice.ChancePercent / 100.0;
            if (roll < boundary) return string.IsNullOrWhiteSpace(choice.Path) ? null : choice.Path;
        }
        return null;
    }
}
