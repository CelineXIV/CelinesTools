using System;

namespace CelinesChat.Services;

/// <summary>
/// Players the user hid from this plugin's chat (Configuration.HiddenPlayers). Purely local - unlike
/// the game's blacklist nothing is sent anywhere, and un-hiding brings their messages straight back,
/// since entries are still received and logged, only left out of the views.
/// </summary>
internal static class HiddenPlayers
{
    /// <summary>The key a sender is stored under: "Name@World", or "Name" if the world isn't known.</summary>
    public static string KeyOf(ChatLogEntry entry) => entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

    public static bool IsHidden(ChatLogEntry entry, Configuration config)
    {
        var hidden = config.HiddenPlayers;
        if (hidden.Count == 0 || string.IsNullOrEmpty(entry.Sender))
        {
            return false;
        }

        if (hidden.Contains(entry.Sender))
        {
            return true;
        }

        if (entry.SenderWorld != null)
        {
            return hidden.Contains(entry.SenderKey);
        }

        // No world on this entry (e.g. from an old log line) - a hidden "Name@World" still applies.
        foreach (var key in hidden)
        {
            var atIndex = key.IndexOf('@');
            if (atIndex > 0 && key.AsSpan(0, atIndex).Equals(entry.Sender, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
