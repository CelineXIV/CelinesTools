using System.Numerics;
using Dalamud.Game.Text;

namespace CelinesChat.Services;

/// <summary>
/// The message categories a user can show/hide/color per tab. Several individual
/// <see cref="XivChatType"/> values (e.g. all 8 linkshells, tell-incoming/outgoing) collapse into
/// one category here, since they share one color and one tab-membership checkbox rather than
/// needing 20+ individually configurable entries.
///
/// Battle/system/announcement message types are as fine-grained as Dalamud's XivChatType exposes,
/// which is coarser than the native game's own "Log Window Settings" (that panel additionally
/// splits battle/error messages by source/target actor relationship - e.g. "damage I dealt" vs
/// "damage dealt to me" - which isn't a distinct XivChatType value Dalamud surfaces, so this
/// plugin can't offer that same subdivision).
/// </summary>
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
}

/// <summary>
/// Purely a settings-UI grouping (collapsible sections in TabsPage/ColorsPage, matching Chat2's
/// own settings tree) - has no bearing on filtering/matching logic, which only ever cares about
/// individual <see cref="ChatCategory"/> values.
/// </summary>
public enum ChatCategoryGroup
{
    Standard,
    Announcements,
    Battle,
}

internal static class ChannelDisplay
{
    /// <summary>
    /// Display order for the collapsible group headers in TabsPage/ColorsPage.
    /// </summary>
    public static readonly ChatCategoryGroup[] AllGroups =
    {
        ChatCategoryGroup.Standard,
        ChatCategoryGroup.Announcements,
        ChatCategoryGroup.Battle,
    };

    /// <summary>
    /// Every selectable category - used to populate settings pages. NOT used to decide what a
    /// pre-existing "Alle" tab should auto-include after an update (see
    /// <see cref="LegacyDefaultCategories"/>) - Battle/Announcement categories are opt-in only,
    /// since several of them (Damage, Miss, Action, ...) are extremely high-volume and silently
    /// enabling them for existing users would flood their default tab with combat spam they never
    /// asked for.
    /// </summary>
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
    };

    /// <summary>
    /// The original category set from before Announcements/Battle categories existed - frozen on
    /// purpose (see <see cref="AllCategories"/>'s remarks). Only ever used by Plugin's
    /// default-tab migration to decide what a pre-existing "Alle" tab should have added to it.
    /// </summary>
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

    /// <summary>
    /// A second, later addition to the default tab's out-of-the-box set - migrated in the same
    /// additive way as <see cref="LegacyDefaultCategories"/> (see Plugin's ctor) but kept as its
    /// own list rather than extending the frozen one above, so each migration step stays separately
    /// auditable. Despite the name (kept for that migration-step reasoning, not because every
    /// entry is strictly an "Announcements"-group category - Debug is Standard-group), these were
    /// all being silently excluded from the default tab entirely: Urgent is how most plugins post
    /// messages into chat, SystemError covers native errors including a failed "/tell" ("player
    /// not found", etc.), SystemMessage/Echo are core game feedback, and Debug is what plain
    /// Dalamud IChatGui.Print() calls actually default to (Dalamud's own DalamudConfiguration.
    /// GeneralChatType defaults to XivChatType.Debug, confirmed by decompile) - meaning most
    /// small utility plugins (that don't call the rarer PrintError(), which does use Urgent) show
    /// up as Debug, not Urgent, despite the misleading name. Chat2 includes all of these in every
    /// one of its own default tab presets, not just its general one. The remaining
    /// Announcements-group categories (loot, crafting, glamour, ...) stay opt-in via the Tabs
    /// settings page instead, since they're closer to "nice to have" than "silently missing
    /// something important".
    /// </summary>
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
            or ChatCategory.GainDebuff or ChatCategory.LoseDebuff => ChatCategoryGroup.Battle,
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

    // XivChatType's numeric values are NOT contiguous across these two families - CrossLinkShell1
    // is 37 but CrossLinkShell2 is 101, so a ">= CrossLinkShell1 and <= CrossLinkShell8" range
    // check (which this code used to do) actually captures every value in between too: Damage,
    // Loot, Crafting, Gathering, system/error messages, Sprint/minigame notices, and more all
    // landed in that 37-107 range and got misclassified as cross-world linkshell chat. Matching
    // each named value explicitly instead of by numeric range avoids that entirely - see also
    // LinkshellIndex/CrossWorldLinkshellIndex below, used for showing which specific
    // linkshell/CWLS a message came from.
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

    /// <summary>
    /// Null for any XivChatType this plugin doesn't recognise at all (GM-only channels, raw
    /// numeric variants Dalamud doesn't otherwise resolve, ...) - <see cref="ChatLogService"/>'s
    /// IsKnownChannel is just "CategoryOf(type) != null", so this is the single place that decides
    /// what the plugin buffers/displays at all.
    /// </summary>
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
            // CrossParty is what a message from a cross-world duty finder party member actually
            // arrives as (Party is only same-world) - found missing the same way ErrorMessage was
            // above, by diffing every value in Dalamud's real XivChatType enum against this
            // switch. Chat2's own SortOrder lists Party and CrossParty side by side in the same
            // group for the same reason: from the player's perspective it's just "party chat".
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
            // Dalamud's XivChatType has two distinct "error" values - SystemError (raw 58, rarely
            // actually seen) and ErrorMessage (raw 60), which is what the retail client sends for
            // user-facing errors like "Player not found" after /tell-ing someone offline or
            // misspelled. Chat2's own ChatType enum only defines the raw-60 one at all (named
            // "Error" there), confirming which one actually matters - only mapping SystemError
            // here meant an actual failed-tell error had no category at all and silently vanished
            // (CategoryOf returning null makes ChatLogService drop it before any tab ever sees
            // it), regardless of the "Fehlermeldungen" checkbox being on.
            XivChatType.SystemError => ChatCategory.SystemError,
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
        _ => category.ToString(),
    };

    /// <summary>
    /// Shows which specific linkshell/CWLS a message came from (e.g. "[LS3]", "[CW2]") instead
    /// of a generic "[LS]"/"[CWLS]" that made every linkshell indistinguishable from every other.
    /// </summary>
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
            // Arrow direction mirrors the [T>] outgoing tag below rather than a bare "[T]" - also
            // still distinct enough that a reloaded log file can tell direction apart, same reason
            // outgoing needed its own tag: the sender slot on an outgoing line always holds the
            // conversation partner's name (needed for whisper-tab matching), not "you", so
            // direction is the only way to know who to actually display as the author (see
            // DrawLogEntry).
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

    /// <summary>
    /// What actually gets drawn in front of a message in the live log - <see cref="Tag"/> itself
    /// for almost everything, but blank for the small cluster of "technical" categories (Debug,
    /// System*, Echo) whose abbreviations ("[SysErr]", "[Dbg]") read as cryptic noise rather than
    /// useful information: these messages already say what they are in their own text, and are
    /// already color-coded, so the bracket added nothing. Kept as a separate function rather than
    /// changing <see cref="Tag"/> itself, since that one also writes the on-disk log file format -
    /// LoadHistoryFile's parser requires a non-empty "[...]" token right after the timestamp, so
    /// blanking it there would silently break history reload for every future line of these types.
    /// </summary>
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
            // "[T]" is the pre-"[T<]" tag - old log files on disk may still have it.
            case "[T]": return XivChatType.TellIncoming;
            case "[T<]": return XivChatType.TellIncoming;
            case "[T>]": return XivChatType.TellOutgoing;
            case "[Y]": return XivChatType.Yell;
            case "[Sh]": return XivChatType.Shout;
            case "[FC]": return XivChatType.FreeCompany;
            case "[A]": return XivChatType.Alliance;
            case "[PT]": return XivChatType.PvPTeam;
            case "[NN]": return XivChatType.NoviceNetwork;
            // Legacy tags from before per-number linkshell tags existed - old log files on disk
            // may still have these, so keep parsing them (as LS1/CW1, since the specific number
            // wasn't recorded back then).
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

        // Every other tag above (System/Announcement/Battle categories) is never written to a log
        // file in the first place - see ChatLogService.IsFileLoggingEnabled, which only has
        // Configuration flags for the original category set - so there's nothing to parse back.
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

        _ => new Vector4(0.8f, 0.8f, 0.8f, 1f),
    };

    // Each whisper conversation partner can have their own color override, keyed the same way
    // whisper tabs/targets already are elsewhere ("Name" or "Name@World") - falls back to the
    // shared Whisper category color if not customized, same pattern as linkshells below.
    public static Vector4 WhisperColor(string target, Configuration config) =>
        config.WhisperColours.TryGetValue(target, out var color) ? color : Color(ChatCategory.Whisper, config);

    // Each linkshell/cross-world linkshell number can have its own color override, since a
    // character can be in several at once and one shared "Linkshell" color makes them
    // indistinguishable - falls back to that shared category color if not customized.
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

    /// <summary>
    /// Extra search terms for the Tabs settings page's category search - a category's own display
    /// name isn't always the word someone would actually type. The motivating case: a small
    /// utility plugin's notification (e.g. a login/doorbell alert) shows up under "Debug" (see its
    /// own tooltip for why), which nobody would think to search for by that name - searching
    /// "plugin" finds it via this list instead. Empty for every category whose own name already
    /// covers the obvious search terms.
    /// </summary>
    private static string[] SearchKeywords(ChatCategory category) => category switch
    {
        ChatCategory.Debug => new[] { "plugin", "addon" },
        ChatCategory.Urgent => new[] { "plugin", "addon" },
        ChatCategory.SystemError => new[] { "fehler", "error", "tell", "whisper", "anwhispern" },
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
