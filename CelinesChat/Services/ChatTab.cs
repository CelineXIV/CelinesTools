using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text;
using CelinesChat;

namespace CelinesChat.Services;

public sealed class ChatTab
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public HashSet<ChatCategory> IncludedChannels { get; set; } = new();

    public HashSet<int> IncludedLinkshellNumbers { get; set; } = new();

    public HashSet<int> IncludedCrossWorldLinkshellNumbers { get; set; } = new();

    public bool Removable { get; set; } = true;

    public bool PositionLocked { get; set; }

    public ChatChannel LastChannel { get; set; } = ChatChannel.Say;

    public int LinkshellNumber { get; set; } = 1;

    public int CrossWorldLinkshellNumber { get; set; } = 1;

    public string LastWhisperTarget { get; set; } = string.Empty;

    public Vector4? BackgroundColor { get; set; }

    public bool Muted { get; set; }

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
