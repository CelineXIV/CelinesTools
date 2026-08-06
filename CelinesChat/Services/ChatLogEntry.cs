using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace CelinesChat.Services;

internal sealed class ChatLogEntry
{
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// The sender's home world, if known (e.g. from the game's PlayerPayload). Used to build a
    /// fully-qualified "Name@World" whisper target, since /tell needs the world to reliably reach
    /// a player of the same name on a different world.
    /// </summary>
    public string? SenderWorld { get; set; }

    public string Text { get; set; } = string.Empty;

    public XivChatType ChatType { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Monotonically increasing per-entry counter assigned by <see cref="ChatLogService"/> when a
    /// live message arrives (history entries loaded from file default to 0). Lets consumers
    /// detect "entries added since I last looked" reliably even once the buffer is full and
    /// starts evicting old entries from the front, unlike a plain count comparison.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The sender's content ID, filled in shortly after the message arrives by
    /// <see cref="ChatLogService"/>'s AddMsgSourceEntry hook (0 if not yet known or unavailable,
    /// e.g. for history loaded from a log file). Needed for cross-world/in-instance party
    /// invites and the mute list, which - unlike a same-world invite or the blacklist - aren't
    /// reachable via a plain name+world lookup.
    /// </summary>
    public ulong ContentId { get; set; }

    /// <summary>
    /// The sender's account ID, filled in the same way as <see cref="ContentId"/> and needed for
    /// the mute list specifically.
    /// </summary>
    public ulong AccountId { get; set; }

    /// <summary>
    /// The message's own raw SeString payloads, captured as-is by <see cref="ChatLogService"/>.
    /// Used to render map links/item links/Dalamud links (from other plugins, e.g. Lifestream)
    /// inline and clickable - see <see cref="ColoredTextRenderer.DrawRich"/>. Null for history
    /// reloaded from a log file (that format only ever kept plain text - see <see cref="Text"/>),
    /// which falls back to plain, non-clickable rendering, same as
    /// <see cref="ContentId"/>/<see cref="AccountId"/> being unavailable there too.
    /// </summary>
    public List<Payload>? Payloads { get; set; }

    /// <summary>
    /// The sender name field's own raw payloads (separate from <see cref="Payloads"/>, which is
    /// the message body) - this is where the game embeds a status icon (Mentor crown, Sprout/New
    /// Adventurer, Returner, Role-Playing, ...) as an <c>IconPayload</c> right alongside the
    /// sender's name, confirmed against Chat2's own Message.cs (its "Sender" field goes through
    /// the exact same SeString-to-chunks conversion as the message body, which is what turns an
    /// embedded IconPayload there into a drawn icon). Null for history reloaded from a log file,
    /// same as <see cref="Payloads"/>.
    /// </summary>
    public List<Payload>? SenderPayloads { get; set; }
}
