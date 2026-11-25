using System;

namespace CelinesChat.Services;

/// <summary>
/// Shared by both chat windows: when two neighbouring messages are the same repeated message
/// (shown once with "×N") or the same sender continuing (name shown only once).
/// </summary>
internal static class MessageGrouping
{
    /// <summary>How far apart two messages may be and still count as one "block" from the same sender.</summary>
    public static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(5);

    public static bool SameAuthor(ChatLogEntry a, ChatLogEntry b) =>
        !string.IsNullOrEmpty(a.Sender)
        && a.ChatType == b.ChatType
        && string.Equals(a.Sender, b.Sender, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.SenderWorld, b.SenderWorld, StringComparison.OrdinalIgnoreCase);

    /// <summary>The next message is an exact repeat of this one (spam) - shown once, with a counter.</summary>
    public static bool IsRepeat(ChatLogEntry previous, ChatLogEntry next) =>
        SameAuthor(previous, next) && string.Equals(previous.Text, next.Text, StringComparison.Ordinal);

    /// <summary>The next message continues this one's block - same sender, same channel, same day, close in time.</summary>
    public static bool Continues(ChatLogEntry previous, ChatLogEntry next) =>
        SameAuthor(previous, next)
        && previous.Timestamp.Date == next.Timestamp.Date
        && next.Timestamp - previous.Timestamp <= GroupWindow
        && next.Timestamp >= previous.Timestamp;

    /// <summary>Cache key part for an entry's measured height - it depends on how the entry was drawn.</summary>
    public static byte Variant(bool continuation, bool trailingSpacing, int repeatCount) =>
        (byte)((continuation ? 1 : 0) | (trailingSpacing ? 2 : 0) | (repeatCount > 1 ? 4 : 0));
}
