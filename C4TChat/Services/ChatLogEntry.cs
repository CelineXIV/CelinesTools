using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using C4TChat;

namespace C4TChat.Services;

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

    public bool ContentIdKnown { get; set; }

    public List<Payload>? Payloads { get; set; }

    public List<Payload>? SenderPayloads { get; set; }

    public bool IsRealPlayer => !ContentIdKnown || ContentId != 0;

    public string? RelatedWhisperTarget { get; set; }

    internal List<(string Word, ColoredTextRenderer.TextSegmentKind Kind, ColoredTextRenderer.LinkTarget? Link, Vector4? Foreground)>? CachedRuns { get; set; }

    internal string? CachedRunsMentionTerm { get; set; }

    internal bool CachedRunsRpFormatting { get; set; }

    internal int FilterVersion { get; set; } = -1;

    internal bool FilteredOut { get; set; }

    internal int RelationVersion { get; set; } = -1;

    internal int Relation { get; set; }

    internal int NicknameVersion { get; set; } = -1;

    internal string? Nickname { get; set; }

    private string? bookmarkKey;

    internal string BookmarkKey => bookmarkKey ??= ChatBookmark.KeyOf(Timestamp, Sender, Text);

    internal int NotifyKind { get; set; }

    private bool senderIconResolved;
    private Dalamud.Game.Text.SeStringHandling.Payloads.IconPayload? senderIcon;

    internal Dalamud.Game.Text.SeStringHandling.Payloads.IconPayload? SenderIcon
    {
        get
        {
            if (!senderIconResolved)
            {
                senderIconResolved = true;
                senderIcon = SenderPayloads?.OfType<Dalamud.Game.Text.SeStringHandling.Payloads.IconPayload>().FirstOrDefault();
            }

            return senderIcon;
        }
    }

    private string? senderKey;

    internal string SenderKey => senderKey ??= HiddenPlayers.KeyOf(this);

    private string? searchTimestamp;

    internal string SearchTimestamp => searchTimestamp ??= Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}
