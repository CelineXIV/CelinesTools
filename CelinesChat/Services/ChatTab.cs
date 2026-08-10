using System;
using System.Collections.Generic;
using System.Numerics;
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
    /// Null (default) means "use whatever Configuration.TabStripBackgroundColor says, or no
    /// background fill at all if that's off" - set via this tab's right-click quick-edit popup.
    /// </summary>
    public Vector4? BackgroundColor { get; set; }

    /// <summary>
    /// Suppresses this tab's unread badge for a missed message - set via the right-click
    /// quick-edit popup. The tab still tracks and shows everything as normal while you're
    /// actually looking at it; it just stops demanding attention with a number while you aren't.
    /// </summary>
    public bool Muted { get; set; }

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

    /// <summary>
    /// The fallback view that always has to exist, seeded on a brand new install with the general
    /// chat categories plus notices/errors/system feedback and a couple of extras (loot, some FC
    /// announcements) - not derived from LegacyDefaultCategories/CoreAnnouncementDefaultCategories
    /// (those two now only matter for migrating a tab saved before this set was chosen), this is a
    /// fixed snapshot of one real, in-use tab setup rather than an assembled "every core category"
    /// default.
    /// </summary>
    public static ChatTab CreateDefaultGeneralTab()
    {
        return new ChatTab
        {
            Name = "General",
            IncludedChannels = new HashSet<ChatCategory>
            {
                ChatCategory.Say,
                ChatCategory.Party,
                ChatCategory.Whisper,
                ChatCategory.Yell,
                ChatCategory.Shout,
                ChatCategory.FreeCompany,
                ChatCategory.Linkshell,
                ChatCategory.CrossWorldLinkshell,
                ChatCategory.Alliance,
                ChatCategory.PvpTeam,
                ChatCategory.NoviceNetwork,
                ChatCategory.StandardEmote,
                ChatCategory.CustomEmote,
                ChatCategory.Notice,
                ChatCategory.Urgent,
                ChatCategory.Debug,
                ChatCategory.SystemMessage,
                ChatCategory.SystemError,
                ChatCategory.Echo,
                ChatCategory.NoviceNetworkAnnouncements,
                ChatCategory.FreeCompanyLoginLogout,
                ChatCategory.LootNotice,
                ChatCategory.LootRoll,
            },
            Removable = false,
            PositionLocked = true,
        };
    }

    /// <summary>
    /// A second tab seeded alongside the general one on a brand new install - every regular
    /// conversation channel plus every linkshell/cross-world linkshell number, kept separate from
    /// the general tab's own broader mix of notices/system feedback/loot. Removable, unlike the
    /// general tab - this one's just a starting convenience, not a fallback view that always has
    /// to exist.
    /// </summary>
    public static ChatTab CreateDefaultChatTab()
    {
        return new ChatTab
        {
            Name = "Chat",
            IncludedChannels = new HashSet<ChatCategory>
            {
                ChatCategory.Say,
                ChatCategory.Party,
                ChatCategory.Whisper,
                ChatCategory.FreeCompany,
                ChatCategory.Linkshell,
                ChatCategory.CrossWorldLinkshell,
                ChatCategory.Alliance,
                ChatCategory.PvpTeam,
            },
            Removable = true,
        };
    }

    /// <summary>
    /// A third tab seeded alongside the general one, covering system/gathering feedback and
    /// Novice Network announcements - the more "background noise" side of what the general tab
    /// already includes, split out for anyone who'd rather not have it mixed into regular chat.
    /// </summary>
    public static ChatTab CreateDefaultSystemTab()
    {
        return new ChatTab
        {
            Name = "System",
            IncludedChannels = new HashSet<ChatCategory>
            {
                ChatCategory.SystemMessage,
                ChatCategory.GatheringSystemMessage,
                ChatCategory.NoviceNetworkAnnouncements,
            },
            Removable = true,
        };
    }

    /// <summary>
    /// A fourth tab seeded alongside the general one, covering item/gil drops and crafting/party-
    /// recruitment notices - kept separate since it's a different kind of "feedback" than regular
    /// conversation or system messages.
    /// </summary>
    public static ChatTab CreateDefaultLootTab()
    {
        return new ChatTab
        {
            Name = "Loot",
            IncludedChannels = new HashSet<ChatCategory>
            {
                ChatCategory.LootNotice,
                ChatCategory.LootRoll,
                ChatCategory.PeriodicRecruitmentNotification,
                ChatCategory.Crafting,
            },
            Removable = true,
        };
    }
}
