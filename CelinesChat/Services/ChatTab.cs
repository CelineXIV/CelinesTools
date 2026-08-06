using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using CelinesChat;

namespace CelinesChat.Services;

/// <summary>
/// A user-defined chat log tab: a saved filter over <see cref="ChatCategory"/> values, shown
/// alongside the auto-generated per-whisper tabs in the unified tab bar. It's a display filter
/// first and foremost - switching tabs never sends anything anywhere on its own - but each tab
/// does remember which send channel was last active while it was open (see LastChannel and
/// friends below), restored whenever you switch back to it.
/// </summary>
public sealed class ChatTab
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public HashSet<ChatCategory> IncludedChannels { get; set; } = new();

    /// <summary>
    /// Which specific linkshell/cross-world linkshell numbers (1-8) show up in this tab, on top
    /// of IncludedChannels containing the Linkshell/CrossWorldLinkshell category in the first
    /// place. An empty set means "no restriction, show all of them" - both a sane default for
    /// new tabs and what a tab saved before this feature existed deserializes to, so nothing
    /// that used to show every linkshell suddenly shows none of them.
    /// </summary>
    public HashSet<int> IncludedLinkshellNumbers { get; set; } = new();

    public HashSet<int> IncludedCrossWorldLinkshellNumbers { get; set; } = new();

    /// <summary>
    /// The default "Alle" tab is kept around as the fallback view and can't be deleted from the
    /// Tabs settings page - everything else the user creates can be removed freely.
    /// </summary>
    public bool Removable { get; set; } = true;

    /// <summary>
    /// Excludes this tab from the tab bar's native drag-to-reorder (ImGuiTabItemFlags.NoReorder)
    /// - set via the right-click quick-edit popup on the tab itself.
    /// </summary>
    public bool PositionLocked { get; set; }

    /// <summary>
    /// The send channel (plus, where relevant, which specific linkshell/CWLS number or whisper
    /// target) that was active the last time this tab was the one being viewed - restored on
    /// switching back to it, so e.g. picking Party while on "Alle" and Free Company while on a
    /// "Gilde" tab keeps each tab's own choice instead of them fighting over one shared value.
    /// </summary>
    public ChatChannel LastChannel { get; set; } = ChatChannel.Say;

    public int LinkshellNumber { get; set; } = 1;

    public int CrossWorldLinkshellNumber { get; set; } = 1;

    public string LastWhisperTarget { get; set; } = string.Empty;

    /// <summary>
    /// Whether a message of this type belongs in this tab - the category check every tab has
    /// always done, refined for Linkshell/CrossWorldLinkshell by which specific numbers were
    /// picked (see IncludedLinkshellNumbers/IncludedCrossWorldLinkshellNumbers above).
    /// </summary>
    public bool Matches(XivChatType type)
    {
        if (ChannelDisplay.CategoryOf(type) is not { } category || !IncludedChannels.Contains(category))
        {
            return false;
        }

        if (category == ChatCategory.Linkshell && IncludedLinkshellNumbers.Count > 0)
        {
            return ChannelDisplay.LinkshellIndex(type) is { } number && IncludedLinkshellNumbers.Contains(number);
        }

        if (category == ChatCategory.CrossWorldLinkshell && IncludedCrossWorldLinkshellNumbers.Count > 0)
        {
            return ChannelDisplay.CrossWorldLinkshellIndex(type) is { } number && IncludedCrossWorldLinkshellNumbers.Contains(number);
        }

        return true;
    }

    public static ChatTab CreateDefaultAllTab()
    {
        return new ChatTab
        {
            Name = Loc.T("Tabs.DefaultAllName"),
            // Deliberately the conservative legacy set, not every selectable category - Battle
            // categories in particular (Damage, Miss, Action, ...) are extremely high-volume, and
            // a brand new "Alle" tab flooding with combat spam by default would be a bad first
            // impression. Announcements/Battle categories are opt-in per tab via settings instead,
            // matching the same choice made for existing tabs after an update (see Plugin's ctor).
            IncludedChannels = new HashSet<ChatCategory>(ChannelDisplay.LegacyDefaultCategories),
            Removable = false,
        };
    }

    /// <summary>
    /// A second tab seeded alongside the general one on a brand new install, covering what
    /// happens in battle (damage/misses/actions/items used/healing/buffs) plus item and gil
    /// drops (loot notices/rolls) - unlike the general tab, these are opt-in-by-default here
    /// specifically (not silently added to it) since they're high-volume in a way the general
    /// chat categories aren't, and a separate tab keeps them out of the way of normal
    /// conversation while still being one click away. Removable, unlike the general tab - this
    /// one's just a starting convenience, not a fallback view that always has to exist.
    /// </summary>
    public static ChatTab CreateDefaultBattleLootTab()
    {
        return new ChatTab
        {
            Name = Loc.T("Tabs.DefaultBattleLootName"),
            IncludedChannels = new HashSet<ChatCategory>
            {
                ChatCategory.Damage,
                ChatCategory.Miss,
                ChatCategory.ActionUsed,
                ChatCategory.ItemUsed,
                ChatCategory.Healing,
                ChatCategory.GainBuff,
                ChatCategory.LoseBuff,
                ChatCategory.GainDebuff,
                ChatCategory.LoseDebuff,
                ChatCategory.LootNotice,
                ChatCategory.LootRoll,
            },
            Removable = true,
        };
    }
}
