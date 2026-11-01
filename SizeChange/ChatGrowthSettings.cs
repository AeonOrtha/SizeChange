using System;
using System.Collections.Generic;
using System.Globalization;

namespace SizeChange;

[Flags]
public enum GrowthChatChannels
{
    None = 0, Say = 1, Tell = 2, Party = 4, Alliance = 8, FreeCompany = 16,
    Linkshell = 32, CrossLinkshell = 64, Shout = 128, Yell = 256, Emote = 512,
    All = 1023,
}

public enum GrowthChatSpeaker { Anyone, Yourself, Others }

[Serializable]
public sealed class ChatGrowthRule
{
    public string Text { get; set; } = string.Empty;
    public float Growth { get; set; } = 0.05f;
}

[Serializable]
public sealed class ChatGrowthSettings
{
    public bool Enabled { get; set; }
    public GrowthChatChannels Channels { get; set; } = GrowthChatChannels.Say | GrowthChatChannels.Tell | GrowthChatChannels.Party;
    public GrowthChatSpeaker Speaker { get; set; }
    public bool WholeWords { get; set; } = true;
    public bool BypassSizeLimit { get; set; }
    public float CooldownSeconds { get; set; }
    public List<ChatGrowthRule> Rules { get; set; } = new();

    // Literal matching only: no user regular expressions or unbounded backtracking.
    // Each non-overlapping occurrence in EACH rule contributes to one message total.
    public float GrowthFor(string text)
    {
        if (!Enabled || string.IsNullOrEmpty(text)) return 0;
        double total = 0;
        foreach (var rule in Rules)
        {
            string phrase = rule.Text.Trim();
            if (phrase.Length == 0 || !float.IsFinite(rule.Growth) || rule.Growth <= 0) continue;
            int offset = 0;
            while (offset <= text.Length - phrase.Length)
            {
                int at = text.IndexOf(phrase, offset, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                int end = at + phrase.Length;
                if (!WholeWords || ((at == 0 || !Word(text[at - 1])) && (end == text.Length || !Word(text[end]))))
                {
                    total += rule.Growth;
                    if (total >= ImmediateGrowth.Maximum) return ImmediateGrowth.Maximum;
                }
                offset = end;
            }
        }
        return (float)total;
    }

    private static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_' ||
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    public bool Accepts(GrowthChatChannels channel, bool? localSpeaker) => Enabled &&
        channel != GrowthChatChannels.None && (Channels & channel) != 0 &&
        (Speaker == GrowthChatSpeaker.Anyone || localSpeaker.HasValue &&
            (Speaker == GrowthChatSpeaker.Yourself ? localSpeaker.Value : !localSpeaker.Value));

    public void Validate()
    {
        Channels &= GrowthChatChannels.All;
        if (!Enum.IsDefined(Speaker)) Speaker = GrowthChatSpeaker.Anyone;
        CooldownSeconds = float.IsFinite(CooldownSeconds) ? Math.Clamp(CooldownSeconds, 0, 3600) : 0;
        Rules ??= new();
        Rules.RemoveAll(r => r == null);
        if (Rules.Count > 128) Rules.RemoveRange(128, Rules.Count - 128);
        foreach (var rule in Rules)
        {
            rule.Text ??= string.Empty;
            if (rule.Text.Length > 128) rule.Text = rule.Text[..128];
            rule.Growth = float.IsFinite(rule.Growth) ? Math.Clamp(rule.Growth, 0, ImmediateGrowth.Maximum) : 0;
        }
    }

    public static ChatGrowthSettings CopyOf(ChatGrowthSettings value) => new()
    {
        Enabled = value.Enabled, Channels = value.Channels, Speaker = value.Speaker,
        WholeWords = value.WholeWords, BypassSizeLimit = value.BypassSizeLimit, CooldownSeconds = value.CooldownSeconds,
        Rules = value.Rules.ConvertAll(r => new ChatGrowthRule { Text = r.Text, Growth = r.Growth }),
    };
}

// A profile-wide cooldown, measured at receipt rather than delayed queue draining.
internal sealed class ChatGrowthCooldown
{
    private long? last;
    public bool Ready(long receivedAt, float seconds) => !last.HasValue ||
        receivedAt >= last.Value && receivedAt - last.Value >= seconds * 1000.0;
    public void Accept(long receivedAt) => last = receivedAt;
    public void Clear() => last = null;
}
