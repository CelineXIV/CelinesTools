using System;

namespace C4TChat.Services;

internal static class MessageGrouping
{
    public static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(5);

    public static bool SameAuthor(ChatLogEntry a, ChatLogEntry b) =>
        !string.IsNullOrEmpty(a.Sender)
        && a.ChatType == b.ChatType
        && string.Equals(a.Sender, b.Sender, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.SenderWorld, b.SenderWorld, StringComparison.OrdinalIgnoreCase);

    public static bool IsRepeat(ChatLogEntry previous, ChatLogEntry next) =>
        SameAuthor(previous, next) && string.Equals(previous.Text, next.Text, StringComparison.Ordinal);

    public static bool Continues(ChatLogEntry previous, ChatLogEntry next) =>
        SameAuthor(previous, next)
        && previous.Timestamp.Date == next.Timestamp.Date
        && next.Timestamp - previous.Timestamp <= GroupWindow
        && next.Timestamp >= previous.Timestamp;

    public static byte Variant(bool continuation, bool trailingSpacing, int repeatCount, bool discord = false) =>
        (byte)((continuation ? 1 : 0) | (trailingSpacing ? 2 : 0) | (repeatCount > 1 ? 4 : 0) | (discord ? 8 : 0));
}
