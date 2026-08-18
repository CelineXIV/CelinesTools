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

    // Set only for live entries the native RaptureLogModule.AddMsgSourceEntry hook actually reported
    // a ContentId for (see ChatLogService.OnAddMsgSourceEntry) - never set for entries reconstructed
    // from the plain-text history log files (LoadHistoryFile/LoadWhisperHistory's ParseLine), whose
    // format never captured ContentId in the first place. IsRealPlayer relies on this to tell "we
    // checked and it's not a player" (hide the menu) apart from "we simply have no data either way"
    // (history) - defaulting the latter to true instead of false, since treating unknown historical
    // messages as real players is the far lower-risk mistake: worst case is the same "message
    // couldn't be sent" a whisper attempt to an actual bot always got anyway, versus hiding the menu
    // for the vast majority of ordinary players' own history, which is what defaulting to false did.
    public bool ContentIdKnown { get; set; }

    public List<Payload>? Payloads { get; set; }

    public List<Payload>? SenderPayloads { get; set; }

    // Bots/relay plugins (e.g. Faloop notifications) can post into ordinary channels under a plain
    // display name with no real character behind it. A PlayerPayload on the sender turned out not to
    // reliably distinguish this - the game only attaches one for some channels (Whisper) but not
    // others (Say/Party/...), which quietly broke the player-only actions below everywhere except
    // whisper tabs the one time this was tried. ContentId comes from a completely different source -
    // the native RaptureLogModule.AddMsgSourceEntry hook - and is already relied on elsewhere in this
    // same context menu (the mute-list action's own `entry.AccountId == 0` gate) as the real,
    // channel-independent signal for "this resolved to an actual character," which a bot/relay
    // message that never went through real chat-log-source resolution never gets.
    public bool IsRealPlayer => !ContentIdKnown || ContentId != 0;

    // Set only for XivChatType.ErrorMessage entries recognized as a failed-/tell-notice (see
    // WhisperFailureDetector) - Sender itself stays empty for these, matching every other system
    // message, since it's still not really "from" the named player. This is the player the failure
    // is ABOUT, used purely to route the notice into that player's own whisper tab so it shows up
    // right where the user would actually be looking for it.
    public string? RelatedWhisperTarget { get; set; }

    // Tokenizing/word-splitting entry.Text (or walking entry.Payloads) from scratch every single
    // frame - for every entry that passes a tab's filter, whether it's actually on screen or not -
    // is a real, repeated allocation cost (a fresh split array, fresh per-word tuples, ...) that
    // scales directly with how many messages a tab has, not with what's actually visible. Text and
    // Payloads never change after an entry is created, so the parsed result is safe to compute once
    // and reuse for as long as the entry itself lives - see ColoredTextRenderer.DrawForEntry.
    internal List<(string Word, ColoredTextRenderer.TextSegmentKind Kind, ColoredTextRenderer.LinkTarget? Link, Vector4? Foreground)>? CachedRuns { get; set; }
}
