using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace SizeChange;

internal readonly record struct GrowthChatMessage(GrowthChatChannels Channel, bool? LocalSpeaker, string Text, long ReceivedAt);

// Managed chat subscription only. Copy text and identity during the callback;
// all actor resolution and growth happens later on the framework thread.
internal sealed class ChatGrowthObserver : IDisposable
{
    private readonly IChatGui chat;
    private readonly object gate = new();
    private readonly Queue<GrowthChatMessage> queue = new();
    private bool accepting, disposed;
    private uint territory;
    private string localName = string.Empty;
    private uint localWorld;
    public string? Error { get; private set; }

    public ChatGrowthObserver(IChatGui chat)
    { this.chat = chat; chat.ChatMessage += OnChat; }

    public void Context(bool allowed, uint zone, string name, uint world)
    {
        lock (gate)
        {
            if (!allowed || zone != territory || name != localName || world != localWorld) queue.Clear();
            accepting = allowed && !disposed; territory = zone; localName = name; localWorld = world;
        }
    }

    private void OnChat(IHandleableChatMessage message)
    {
        lock (gate)
        {
            if (!accepting || disposed || queue.Count >= 256) return;
            try
            {
                var channel = Channel(message.LogKind);
                if (channel == GrowthChatChannels.None) return;
                string text = message.OriginalMessage.ExtractText();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) return;
                bool? self = null;
                if (message.LogKind == XivChatType.TellOutgoing) self = true;
                else if (message.LogKind == XivChatType.TellIncoming) self = false;
                else
                {
                    var sender = SeString.Parse(message.OriginalSender.Data.Span);
                    var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
                    if (player != null)
                        self = string.Equals(player.PlayerName, localName, StringComparison.OrdinalIgnoreCase) && player.World.RowId == localWorld;
                    else if (message.SourceKind == XivChatRelationKind.LocalPlayer) self = true;
                    else if (message.SourceKind is XivChatRelationKind.OtherPlayer or XivChatRelationKind.PartyMember or XivChatRelationKind.AllianceMember) self = false;
                    else if (string.Equals(sender.TextValue, localName, StringComparison.OrdinalIgnoreCase)) self = true;
                }
                queue.Enqueue(new(channel, self, text, Environment.TickCount64));
                Error = null;
            }
            catch (Exception ex) { Error = ex.GetBaseException().Message; }
        }
    }

    internal static GrowthChatChannels Channel(XivChatType type) => type switch
    {
        XivChatType.Say => GrowthChatChannels.Say,
        XivChatType.TellIncoming or XivChatType.TellOutgoing => GrowthChatChannels.Tell,
        XivChatType.Party or XivChatType.CrossParty => GrowthChatChannels.Party,
        XivChatType.Alliance => GrowthChatChannels.Alliance,
        XivChatType.FreeCompany => GrowthChatChannels.FreeCompany,
        XivChatType.Shout => GrowthChatChannels.Shout,
        XivChatType.Yell => GrowthChatChannels.Yell,
        XivChatType.CustomEmote => GrowthChatChannels.Emote,
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => GrowthChatChannels.Linkshell,
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or
            XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or
            XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => GrowthChatChannels.CrossLinkshell,
        _ => GrowthChatChannels.None,
    };

    public bool TryRead(out GrowthChatMessage message)
    {
        lock (gate)
        {
            while (queue.TryDequeue(out message))
            {
                long age = Environment.TickCount64 - message.ReceivedAt;
                if (age is >= 0 and <= 2000) return true;
            }
            message = default; return false;
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; accepting = false; queue.Clear(); }
        chat.ChatMessage -= OnChat;
    }
}
