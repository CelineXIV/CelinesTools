using System;
using Dalamud.Game.Text;

namespace CelinesRPChat.Services;

internal sealed class ChatLogEntry
{
    public string Sender { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public XivChatType ChatType { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;
}
