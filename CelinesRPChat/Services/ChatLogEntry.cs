using System;
using Dalamud.Game.Text;

namespace CelinesRPChat.Services;

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
}
