using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using CelinesChat;

namespace CelinesChat.Services;

internal sealed class ChatLogEntry
{
    public string Sender { get; set; } = string.Empty;

    public string? SenderWorld { get; set; }

    public string Text { get; set; } = string.Empty;

    public XivChatType ChatType { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public long Sequence { get; set; }

    public ulong ContentId { get; set; }

    public ulong AccountId { get; set; }

    public List<Payload>? Payloads { get; set; }

    public List<Payload>? SenderPayloads { get; set; }

    // Tokenizing/word-splitting entry.Text (or walking entry.Payloads) from scratch every single
    // frame - for every entry that passes a tab's filter, whether it's actually on screen or not -
    // is a real, repeated allocation cost (a fresh split array, fresh per-word tuples, ...) that
    // scales directly with how many messages a tab has, not with what's actually visible. Text and
    // Payloads never change after an entry is created, so the parsed result is safe to compute once
    // and reuse for as long as the entry itself lives - see ColoredTextRenderer.DrawForEntry.
    internal List<(string Word, ColoredTextRenderer.TextSegmentKind Kind, ColoredTextRenderer.LinkTarget? Link, Vector4? Foreground)>? CachedRuns { get; set; }
}
