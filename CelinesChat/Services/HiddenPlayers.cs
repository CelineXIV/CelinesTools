using System;

namespace CelinesChat.Services;

internal static class HiddenPlayers
{
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
