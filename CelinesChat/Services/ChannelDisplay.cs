using System.Numerics;
using Dalamud.Game.Text;

namespace CelinesChat.Services;

public enum ChatCategory
{
    Say,
    Party,
    Whisper,
    Yell,
    Shout,
    FreeCompany,
    Linkshell,
    CrossWorldLinkshell,
    Alliance,
    PvpTeam,
    NoviceNetwork,

    StandardEmote,
    CustomEmote,
    Notice,
    Urgent,
    Debug,

    SystemMessage,
    SystemError,
    GatheringSystemMessage,
    Echo,
    NoviceNetworkAnnouncements,
    FreeCompanyAnnouncement,
    FreeCompanyLoginLogout,
    PvpTeamAnnouncement,
    PvpTeamLoginLogout,
    RetainerSale,
    NpcDialogue,
    NpcDialogueAnnouncements,
    LootNotice,
    Progress,
    LootRoll,
    Crafting,
    Gathering,
    PeriodicRecruitmentNotification,
    Sign,
    RandomNumber,
    Orchestrion,
    MessageBook,
    Alarm,
    GlamourNotifications,

    Damage,
    Miss,
    ActionUsed,
    ItemUsed,
    Healing,
    GainBuff,
    LoseBuff,
    GainDebuff,
    LoseDebuff,

    AmbientBattle,
}

public enum ChatCategoryGroup
{
    Standard,
    Announcements,
    Battle,
}

internal static class ChannelDisplay
{
    public static readonly ChatCategoryGroup[] AllGroups =
    {
        ChatCategoryGroup.Standard,
        ChatCategoryGroup.Announcements,
        ChatCategoryGroup.Battle,
    };

    public static readonly ChatCategory[] AllCategories =
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
        ChatCategory.GatheringSystemMessage,
        ChatCategory.Echo,
        ChatCategory.NoviceNetworkAnnouncements,
        ChatCategory.FreeCompanyAnnouncement,
        ChatCategory.FreeCompanyLoginLogout,
        ChatCategory.PvpTeamAnnouncement,
        ChatCategory.PvpTeamLoginLogout,
        ChatCategory.RetainerSale,
        ChatCategory.NpcDialogue,
        ChatCategory.NpcDialogueAnnouncements,
        ChatCategory.LootNotice,
        ChatCategory.Progress,
        ChatCategory.LootRoll,
        ChatCategory.Crafting,
        ChatCategory.Gathering,
        ChatCategory.PeriodicRecruitmentNotification,
        ChatCategory.Sign,
        ChatCategory.RandomNumber,
        ChatCategory.Orchestrion,
        ChatCategory.MessageBook,
        ChatCategory.Alarm,
        ChatCategory.GlamourNotifications,
        ChatCategory.Damage,
        ChatCategory.Miss,
        ChatCategory.ActionUsed,
        ChatCategory.ItemUsed,
        ChatCategory.Healing,
        ChatCategory.GainBuff,
        ChatCategory.LoseBuff,
        ChatCategory.GainDebuff,
        ChatCategory.LoseDebuff,
        ChatCategory.AmbientBattle,
    };

    public static readonly ChatCategory[] LegacyDefaultCategories =
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
    };

    public static readonly ChatCategory[] CoreAnnouncementDefaultCategories =
    {
        ChatCategory.Urgent,
        ChatCategory.SystemError,
        ChatCategory.SystemMessage,
        ChatCategory.Echo,
        ChatCategory.Debug,
    };

    public static ChatCategoryGroup GroupOf(ChatCategory category) => category switch
    {
        ChatCategory.Damage or ChatCategory.Miss or ChatCategory.ActionUsed or ChatCategory.ItemUsed
            or ChatCategory.Healing or ChatCategory.GainBuff or ChatCategory.LoseBuff
            or ChatCategory.GainDebuff or ChatCategory.LoseDebuff or ChatCategory.AmbientBattle => ChatCategoryGroup.Battle,
        ChatCategory.Say or ChatCategory.Party or ChatCategory.Whisper or ChatCategory.Yell
            or ChatCategory.Shout or ChatCategory.FreeCompany or ChatCategory.Linkshell
            or ChatCategory.CrossWorldLinkshell or ChatCategory.Alliance or ChatCategory.PvpTeam
            or ChatCategory.NoviceNetwork or ChatCategory.StandardEmote or ChatCategory.CustomEmote
            or ChatCategory.Notice or ChatCategory.Urgent or ChatCategory.Debug => ChatCategoryGroup.Standard,
        _ => ChatCategoryGroup.Announcements,
    };

    public static string GroupName(ChatCategoryGroup group) => group switch
    {
        ChatCategoryGroup.Standard => Loc.T("ChannelGroup.Standard"),
        ChatCategoryGroup.Announcements => Loc.T("ChannelGroup.Announcements"),
        ChatCategoryGroup.Battle => Loc.T("ChannelGroup.Battle"),
        _ => group.ToString(),
    };

    public static int? LinkshellIndex(XivChatType type) => type switch
    {
        XivChatType.Ls1 => 1,
        XivChatType.Ls2 => 2,
        XivChatType.Ls3 => 3,
        XivChatType.Ls4 => 4,
        XivChatType.Ls5 => 5,
        XivChatType.Ls6 => 6,
        XivChatType.Ls7 => 7,
        XivChatType.Ls8 => 8,
        _ => null,
    };

    public static int? CrossWorldLinkshellIndex(XivChatType type) => type switch
    {
        XivChatType.CrossLinkShell1 => 1,
        XivChatType.CrossLinkShell2 => 2,
        XivChatType.CrossLinkShell3 => 3,
        XivChatType.CrossLinkShell4 => 4,
        XivChatType.CrossLinkShell5 => 5,
        XivChatType.CrossLinkShell6 => 6,
        XivChatType.CrossLinkShell7 => 7,
        XivChatType.CrossLinkShell8 => 8,
        _ => null,
    };

    public static bool IsLinkshell(XivChatType type) => LinkshellIndex(type) != null;

    public static bool IsCrossWorldLinkshell(XivChatType type) => CrossWorldLinkshellIndex(type) != null;

    public static ChatCategory? CategoryOf(XivChatType type)
    {
        if (IsLinkshell(type))
        {
            return ChatCategory.Linkshell;
        }

        if (IsCrossWorldLinkshell(type))
        {
            return ChatCategory.CrossWorldLinkshell;
        }

        return type switch
        {
            XivChatType.Say => ChatCategory.Say,
            XivChatType.Party or XivChatType.CrossParty => ChatCategory.Party,
            XivChatType.TellIncoming => ChatCategory.Whisper,
            XivChatType.TellOutgoing => ChatCategory.Whisper,
            XivChatType.Yell => ChatCategory.Yell,
            XivChatType.Shout => ChatCategory.Shout,
            XivChatType.FreeCompany => ChatCategory.FreeCompany,
            XivChatType.Alliance => ChatCategory.Alliance,
            XivChatType.PvPTeam => ChatCategory.PvpTeam,
            XivChatType.NoviceNetwork => ChatCategory.NoviceNetwork,
            XivChatType.StandardEmote => ChatCategory.StandardEmote,
            XivChatType.CustomEmote => ChatCategory.CustomEmote,
            XivChatType.Notice => ChatCategory.Notice,
            XivChatType.Urgent => ChatCategory.Urgent,
            XivChatType.Debug => ChatCategory.Debug,
            XivChatType.SystemMessage => ChatCategory.SystemMessage,
            XivChatType.SystemError => ChatCategory.AmbientBattle,
            XivChatType.ErrorMessage => ChatCategory.SystemError,
            XivChatType.GatheringSystemMessage => ChatCategory.GatheringSystemMessage,
            XivChatType.Echo => ChatCategory.Echo,
            XivChatType.NoviceNetworkSystem => ChatCategory.NoviceNetworkAnnouncements,
            XivChatType.FreeCompanyAnnouncement => ChatCategory.FreeCompanyAnnouncement,
            XivChatType.FreeCompanyLoginLogout => ChatCategory.FreeCompanyLoginLogout,
            XivChatType.PvpTeamAnnouncement => ChatCategory.PvpTeamAnnouncement,
            XivChatType.PvpTeamLoginLogout => ChatCategory.PvpTeamLoginLogout,
            XivChatType.RetainerSale => ChatCategory.RetainerSale,
            XivChatType.NPCDialogue => ChatCategory.NpcDialogue,
            XivChatType.NPCDialogueAnnouncements => ChatCategory.NpcDialogueAnnouncements,
            XivChatType.LootNotice => ChatCategory.LootNotice,
            XivChatType.Progress => ChatCategory.Progress,
            XivChatType.LootRoll => ChatCategory.LootRoll,
            XivChatType.Crafting => ChatCategory.Crafting,
            XivChatType.Gathering => ChatCategory.Gathering,
            XivChatType.PeriodicRecruitmentNotification => ChatCategory.PeriodicRecruitmentNotification,
            XivChatType.Sign => ChatCategory.Sign,
            XivChatType.RandomNumber => ChatCategory.RandomNumber,
            XivChatType.Orchestrion => ChatCategory.Orchestrion,
            XivChatType.MessageBook => ChatCategory.MessageBook,
            XivChatType.Alarm => ChatCategory.Alarm,
            XivChatType.GlamourNotifications => ChatCategory.GlamourNotifications,
            XivChatType.Damage => ChatCategory.Damage,
            XivChatType.Miss => ChatCategory.Miss,
            XivChatType.Action => ChatCategory.ActionUsed,
            XivChatType.Item => ChatCategory.ItemUsed,
            XivChatType.Healing => ChatCategory.Healing,
            XivChatType.GainBuff => ChatCategory.GainBuff,
            XivChatType.LoseBuff => ChatCategory.LoseBuff,
            XivChatType.GainDebuff => ChatCategory.GainDebuff,
            XivChatType.LoseDebuff => ChatCategory.LoseDebuff,
            _ => null,
        };
    }

    public static string CategoryName(ChatCategory category) => category switch
    {
        ChatCategory.Say => Loc.T("Channel.Say"),
        ChatCategory.Party => Loc.T("Channel.Party"),
        ChatCategory.Whisper => Loc.T("Channel.Whisper"),
        ChatCategory.Yell => Loc.T("Channel.Yell"),
        ChatCategory.Shout => Loc.T("Channel.Shout"),
        ChatCategory.FreeCompany => Loc.T("Channel.FreeCompany"),
        ChatCategory.Linkshell => Loc.T("Channel.Linkshell"),
        ChatCategory.CrossWorldLinkshell => Loc.T("Channel.CrossWorldLinkshell"),
        ChatCategory.Alliance => Loc.T("Channel.Alliance"),
        ChatCategory.PvpTeam => Loc.T("Channel.PvpTeam"),
        ChatCategory.NoviceNetwork => Loc.T("Channel.NoviceNetwork"),
        ChatCategory.StandardEmote => Loc.T("Channel.StandardEmote"),
        ChatCategory.CustomEmote => Loc.T("Channel.CustomEmote"),
        ChatCategory.Notice => Loc.T("Channel.Notice"),
        ChatCategory.Urgent => Loc.T("Channel.Urgent"),
        ChatCategory.Debug => Loc.T("Channel.Debug"),
        ChatCategory.SystemMessage => Loc.T("Channel.SystemMessage"),
        ChatCategory.SystemError => Loc.T("Channel.SystemError"),
        ChatCategory.GatheringSystemMessage => Loc.T("Channel.GatheringSystemMessage"),
        ChatCategory.Echo => Loc.T("Channel.Echo"),
        ChatCategory.NoviceNetworkAnnouncements => Loc.T("Channel.NoviceNetworkAnnouncements"),
        ChatCategory.FreeCompanyAnnouncement => Loc.T("Channel.FreeCompanyAnnouncement"),
        ChatCategory.FreeCompanyLoginLogout => Loc.T("Channel.FreeCompanyLoginLogout"),
        ChatCategory.PvpTeamAnnouncement => Loc.T("Channel.PvpTeamAnnouncement"),
        ChatCategory.PvpTeamLoginLogout => Loc.T("Channel.PvpTeamLoginLogout"),
        ChatCategory.RetainerSale => Loc.T("Channel.RetainerSale"),
        ChatCategory.NpcDialogue => Loc.T("Channel.NpcDialogue"),
        ChatCategory.NpcDialogueAnnouncements => Loc.T("Channel.NpcDialogueAnnouncements"),
        ChatCategory.LootNotice => Loc.T("Channel.LootNotice"),
        ChatCategory.Progress => Loc.T("Channel.Progress"),
        ChatCategory.LootRoll => Loc.T("Channel.LootRoll"),
        ChatCategory.Crafting => Loc.T("Channel.Crafting"),
        ChatCategory.Gathering => Loc.T("Channel.Gathering"),
        ChatCategory.PeriodicRecruitmentNotification => Loc.T("Channel.PeriodicRecruitmentNotification"),
        ChatCategory.Sign => Loc.T("Channel.Sign"),
        ChatCategory.RandomNumber => Loc.T("Channel.RandomNumber"),
        ChatCategory.Orchestrion => Loc.T("Channel.Orchestrion"),
        ChatCategory.MessageBook => Loc.T("Channel.MessageBook"),
        ChatCategory.Alarm => Loc.T("Channel.Alarm"),
        ChatCategory.GlamourNotifications => Loc.T("Channel.GlamourNotifications"),
        ChatCategory.Damage => Loc.T("Channel.Damage"),
        ChatCategory.Miss => Loc.T("Channel.Miss"),
        ChatCategory.ActionUsed => Loc.T("Channel.ActionUsed"),
        ChatCategory.ItemUsed => Loc.T("Channel.ItemUsed"),
        ChatCategory.Healing => Loc.T("Channel.Healing"),
        ChatCategory.GainBuff => Loc.T("Channel.GainBuff"),
        ChatCategory.LoseBuff => Loc.T("Channel.LoseBuff"),
        ChatCategory.GainDebuff => Loc.T("Channel.GainDebuff"),
        ChatCategory.LoseDebuff => Loc.T("Channel.LoseDebuff"),
        ChatCategory.AmbientBattle => Loc.T("Channel.AmbientBattle"),
        _ => category.ToString(),
    };

    public static string Tag(XivChatType type)
    {
        if (LinkshellIndex(type) is { } lsIndex)
        {
            return $"[LS{lsIndex}]";
        }

        if (CrossWorldLinkshellIndex(type) is { } cwlsIndex)
        {
            return $"[CW{cwlsIndex}]";
        }

        return type switch
        {
            XivChatType.Say => "[S]",
            XivChatType.Party or XivChatType.CrossParty => "[P]",
            XivChatType.TellIncoming => "[T<]",
            XivChatType.TellOutgoing => "[T>]",
            XivChatType.Yell => "[Y]",
            XivChatType.Shout => "[Sh]",
            XivChatType.FreeCompany => "[FC]",
            XivChatType.Alliance => "[A]",
            XivChatType.PvPTeam => "[PT]",
            XivChatType.NoviceNetwork => "[NN]",
            XivChatType.StandardEmote => "[E]",
            XivChatType.CustomEmote => "[CE]",
            XivChatType.Notice => "[N]",
            XivChatType.Urgent => "[U]",
            XivChatType.Debug => "[Dbg]",
            XivChatType.SystemMessage => "[Sys]",
            XivChatType.SystemError => "[SysErr]",
            XivChatType.ErrorMessage => "[SysErr]",
            XivChatType.GatheringSystemMessage => "[GathSys]",
            XivChatType.Echo => "[Echo]",
            XivChatType.NoviceNetworkSystem => "[NNSys]",
            XivChatType.FreeCompanyAnnouncement => "[FCAnn]",
            XivChatType.FreeCompanyLoginLogout => "[FCLogin]",
            XivChatType.PvpTeamAnnouncement => "[PvPAnn]",
            XivChatType.PvpTeamLoginLogout => "[PvPLogin]",
            XivChatType.RetainerSale => "[Retainer]",
            XivChatType.NPCDialogue => "[NPC]",
            XivChatType.NPCDialogueAnnouncements => "[NPCAnn]",
            XivChatType.LootNotice => "[Loot]",
            XivChatType.Progress => "[Prog]",
            XivChatType.LootRoll => "[Roll]",
            XivChatType.Crafting => "[Craft]",
            XivChatType.Gathering => "[Gath]",
            XivChatType.PeriodicRecruitmentNotification => "[Recruit]",
            XivChatType.Sign => "[Sign]",
            XivChatType.RandomNumber => "[Rand]",
            XivChatType.Orchestrion => "[Orch]",
            XivChatType.MessageBook => "[MsgBook]",
            XivChatType.Alarm => "[Alarm]",
            XivChatType.GlamourNotifications => "[Glam]",
            XivChatType.Damage => "[Dmg]",
            XivChatType.Miss => "[Miss]",
            XivChatType.Action => "[Act]",
            XivChatType.Item => "[ItemUse]",
            XivChatType.Healing => "[Heal]",
            XivChatType.GainBuff => "[Buff+]",
            XivChatType.LoseBuff => "[Buff-]",
            XivChatType.GainDebuff => "[Debuff+]",
            XivChatType.LoseDebuff => "[Debuff-]",
            _ => "[?]",
        };
    }

    public static string DisplayTag(XivChatType type) => type switch
    {
        XivChatType.Debug
            or XivChatType.SystemMessage
            or XivChatType.SystemError
            or XivChatType.ErrorMessage
            or XivChatType.GatheringSystemMessage
            or XivChatType.Echo
            or XivChatType.NoviceNetworkSystem => string.Empty,
        _ => Tag(type),
    };

    public static XivChatType ParseTag(string tag)
    {
        switch (tag)
        {
            case "[S]": return XivChatType.Say;
            case "[P]": return XivChatType.Party;
            case "[T]": return XivChatType.TellIncoming;
            case "[T<]": return XivChatType.TellIncoming;
            case "[T>]": return XivChatType.TellOutgoing;
            case "[Y]": return XivChatType.Yell;
            case "[Sh]": return XivChatType.Shout;
            case "[FC]": return XivChatType.FreeCompany;
            case "[A]": return XivChatType.Alliance;
            case "[PT]": return XivChatType.PvPTeam;
            case "[NN]": return XivChatType.NoviceNetwork;
            case "[LS]": return XivChatType.Ls1;
            case "[CWLS]": return XivChatType.CrossLinkShell1;
        }

        if (tag.StartsWith("[LS") && tag.EndsWith(']') && int.TryParse(tag[3..^1], out var lsNumber))
        {
            return lsNumber switch
            {
                1 => XivChatType.Ls1,
                2 => XivChatType.Ls2,
                3 => XivChatType.Ls3,
                4 => XivChatType.Ls4,
                5 => XivChatType.Ls5,
                6 => XivChatType.Ls6,
                7 => XivChatType.Ls7,
                8 => XivChatType.Ls8,
                _ => XivChatType.Say,
            };
        }

        if (tag.StartsWith("[CW") && tag.EndsWith(']') && int.TryParse(tag[3..^1], out var cwlsNumber))
        {
            return cwlsNumber switch
            {
                1 => XivChatType.CrossLinkShell1,
                2 => XivChatType.CrossLinkShell2,
                3 => XivChatType.CrossLinkShell3,
                4 => XivChatType.CrossLinkShell4,
                5 => XivChatType.CrossLinkShell5,
                6 => XivChatType.CrossLinkShell6,
                7 => XivChatType.CrossLinkShell7,
                8 => XivChatType.CrossLinkShell8,
                _ => XivChatType.Say,
            };
        }

        return XivChatType.Say;
    }

    public static Vector4 DefaultColor(ChatCategory category) => category switch
    {
        ChatCategory.Say => new Vector4(0.9f, 0.9f, 0.9f, 1f),
        ChatCategory.Party => new Vector4(0.4f, 0.8f, 1f, 1f),
        ChatCategory.Whisper => new Vector4(1f, 0.55f, 0.85f, 1f),
        ChatCategory.Yell => new Vector4(1f, 0.65f, 0.2f, 1f),
        ChatCategory.Shout => new Vector4(1f, 0.4f, 0.2f, 1f),
        ChatCategory.FreeCompany => new Vector4(0.5f, 0.9f, 0.5f, 1f),
        ChatCategory.Linkshell => new Vector4(0.4f, 0.9f, 0.85f, 1f),
        ChatCategory.CrossWorldLinkshell => new Vector4(0.4f, 0.9f, 0.85f, 1f),
        ChatCategory.Alliance => new Vector4(0.6f, 0.7f, 1f, 1f),
        ChatCategory.PvpTeam => new Vector4(1f, 0.5f, 0.5f, 1f),
        ChatCategory.NoviceNetwork => new Vector4(0.7f, 1f, 0.6f, 1f),

        ChatCategory.StandardEmote => new Vector4(1f, 0.75f, 0.86f, 1f),
        ChatCategory.CustomEmote => new Vector4(1f, 0.75f, 0.86f, 1f),
        ChatCategory.Notice => new Vector4(1f, 0.82f, 0.18f, 1f),
        ChatCategory.Urgent => new Vector4(1f, 0.7f, 0.3f, 1f),
        ChatCategory.Debug => new Vector4(0.6f, 0.6f, 0.6f, 1f),

        ChatCategory.SystemMessage => new Vector4(1f, 0.9f, 0.4f, 1f),
        ChatCategory.SystemError => new Vector4(1f, 0.4f, 0.4f, 1f),
        ChatCategory.GatheringSystemMessage => new Vector4(0.6f, 1f, 0.6f, 1f),
        ChatCategory.Echo => new Vector4(0.7f, 0.85f, 1f, 1f),
        ChatCategory.NoviceNetworkAnnouncements => new Vector4(0.7f, 1f, 0.6f, 1f),
        ChatCategory.FreeCompanyAnnouncement => new Vector4(0.5f, 0.9f, 0.5f, 1f),
        ChatCategory.FreeCompanyLoginLogout => new Vector4(0.5f, 0.9f, 0.5f, 1f),
        ChatCategory.PvpTeamAnnouncement => new Vector4(1f, 0.5f, 0.5f, 1f),
        ChatCategory.PvpTeamLoginLogout => new Vector4(1f, 0.5f, 0.5f, 1f),
        ChatCategory.RetainerSale => new Vector4(1f, 0.85f, 0.4f, 1f),
        ChatCategory.NpcDialogue => new Vector4(0.95f, 0.9f, 0.8f, 1f),
        ChatCategory.NpcDialogueAnnouncements => new Vector4(0.95f, 0.9f, 0.8f, 1f),
        ChatCategory.LootNotice => new Vector4(1f, 0.85f, 0.3f, 1f),
        ChatCategory.Progress => new Vector4(0.7f, 0.9f, 1f, 1f),
        ChatCategory.LootRoll => new Vector4(1f, 0.85f, 0.3f, 1f),
        ChatCategory.Crafting => new Vector4(0.8f, 0.7f, 0.5f, 1f),
        ChatCategory.Gathering => new Vector4(0.6f, 0.8f, 0.5f, 1f),
        ChatCategory.PeriodicRecruitmentNotification => new Vector4(1f, 0.7f, 0.3f, 1f),
        ChatCategory.Sign => new Vector4(0.7f, 1f, 0.7f, 1f),
        ChatCategory.RandomNumber => new Vector4(0.8f, 0.7f, 1f, 1f),
        ChatCategory.Orchestrion => new Vector4(0.7f, 0.8f, 1f, 1f),
        ChatCategory.MessageBook => new Vector4(1f, 0.9f, 0.6f, 1f),
        ChatCategory.Alarm => new Vector4(1f, 0.5f, 0.3f, 1f),
        ChatCategory.GlamourNotifications => new Vector4(1f, 0.7f, 0.9f, 1f),

        ChatCategory.Damage => new Vector4(0.9f, 0.9f, 0.9f, 1f),
        ChatCategory.Miss => new Vector4(0.7f, 0.7f, 0.7f, 1f),
        ChatCategory.ActionUsed => new Vector4(0.8f, 0.9f, 1f, 1f),
        ChatCategory.ItemUsed => new Vector4(0.8f, 1f, 0.8f, 1f),
        ChatCategory.Healing => new Vector4(0.5f, 1f, 0.5f, 1f),
        ChatCategory.GainBuff => new Vector4(0.6f, 1f, 0.6f, 1f),
        ChatCategory.LoseBuff => new Vector4(0.6f, 0.8f, 0.6f, 1f),
        ChatCategory.GainDebuff => new Vector4(0.9f, 0.5f, 0.9f, 1f),
        ChatCategory.LoseDebuff => new Vector4(0.7f, 0.6f, 0.8f, 1f),
        ChatCategory.AmbientBattle => new Vector4(0.75f, 0.75f, 0.75f, 1f),

        _ => new Vector4(0.8f, 0.8f, 0.8f, 1f),
    };

    public static Vector4 WhisperColor(string target, Configuration config) =>
        config.WhisperColours.TryGetValue(target, out var color) ? color : Color(ChatCategory.Whisper, config);

    public static Vector4 LinkshellColor(int number, Configuration config) =>
        config.LinkshellColours.TryGetValue(number, out var color) ? color : Color(ChatCategory.Linkshell, config);

    public static Vector4 CrossWorldLinkshellColor(int number, Configuration config) =>
        config.CrossWorldLinkshellColours.TryGetValue(number, out var color) ? color : Color(ChatCategory.CrossWorldLinkshell, config);

    public static Vector4 Color(XivChatType type, Configuration config)
    {
        if (LinkshellIndex(type) is { } lsIndex)
        {
            return LinkshellColor(lsIndex, config);
        }

        if (CrossWorldLinkshellIndex(type) is { } cwlsIndex)
        {
            return CrossWorldLinkshellColor(cwlsIndex, config);
        }

        return Color(CategoryOf(type) ?? ChatCategory.Say, config);
    }

    public static Vector4 Color(ChatCategory category, Configuration config)
    {
        return config.ChatColours.TryGetValue(category, out var color) ? color : DefaultColor(category);
    }

    private static string[] SearchKeywords(ChatCategory category) => category switch
    {
        ChatCategory.Debug => new[] { "plugin", "addon" },
        ChatCategory.Urgent => new[] { "plugin", "addon" },
        ChatCategory.SystemError => new[] { "fehler", "error", "tell", "whisper", "anwhispern" },
        ChatCategory.AmbientBattle => new[] { "kampf", "besiegt", "defeats", "monster", "npc" },
        ChatCategory.LootNotice => new[] { "beute", "loot", "gil", "item" },
        ChatCategory.LootRoll => new[] { "würfeln", "wuerfeln", "roll", "beute", "loot" },
        ChatCategory.Crafting => new[] { "synthese", "craften", "herstellen", "craft" },
        ChatCategory.Gathering => new[] { "sammeln", "abbauen", "fischen", "botanik", "mining" },
        ChatCategory.RetainerSale => new[] { "gehilfe", "verkauf", "markt", "retainer" },
        ChatCategory.GlamourNotifications => new[] { "glamour", "transmog", "aussehen" },
        ChatCategory.Alarm => new[] { "wecker", "timer", "alarm" },
        ChatCategory.Orchestrion => new[] { "musik", "orchestrion", "lied" },
        ChatCategory.RandomNumber => new[] { "würfel", "wuerfel", "zufallszahl", "random" },
        ChatCategory.PeriodicRecruitmentNotification => new[] { "rekrutierung", "gruppensuche", "pf", "party finder" },
        ChatCategory.NpcDialogue or ChatCategory.NpcDialogueAnnouncements => new[] { "npc", "quest" },
        _ => System.Array.Empty<string>(),
    };

    public static bool CategoryMatchesSearch(ChatCategory category, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        if (CategoryName(category).Contains(search, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var keyword in SearchKeywords(category))
        {
            if (keyword.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
