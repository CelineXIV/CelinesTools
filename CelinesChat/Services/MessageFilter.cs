using System;
using Dalamud.Game.Text;

namespace CelinesChat.Services;

internal static class MessageFilter
{
    public static bool IsFilteredOut(ChatLogEntry entry, Configuration config, string? ownCharacterName)
    {
        if (entry.ChatType == XivChatType.TellOutgoing
            || (!string.IsNullOrEmpty(ownCharacterName) && string.Equals(entry.Sender, ownCharacterName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return HiddenPlayers.IsHidden(entry, config) || MatchesWordFilter(entry, config);
    }

    private static bool MatchesWordFilter(ChatLogEntry entry, Configuration config)
    {
        var rules = config.WordFilters;
        if (rules.Count == 0 || string.IsNullOrEmpty(entry.Text))
        {
            return false;
        }

        ChatCategory? category = null;
        var categoryResolved = false;
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Text))
            {
                continue;
            }

            if (rule.Categories.Count > 0)
            {
                if (!categoryResolved)
                {
                    category = ChannelDisplay.CategoryOf(entry.ChatType);
                    categoryResolved = true;
                }

                if (category is not { } c || !rule.Categories.Contains(c))
                {
                    continue;
                }
            }

            if (entry.Text.Contains(rule.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
