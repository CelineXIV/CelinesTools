using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using C4TChat.Services;

namespace C4TChat;

public enum ChatFontChoice
{
    Default,
    NotoSansCjkRegular,
    NotoSansCjkMedium,
    InconsolataRegular,

    Rajdhani,
}

public enum ChatDesign
{
    Standard,

    C4T,

    C4TPurple,
}

public enum ToolbarIconPack
{
    Classic,

    AeroGlass,

    FrostedGlass,

    Flat,
}

public enum PreviewPosition
{
    Window,
    Inside,
}

public enum ChatChannel
{
    Say,
    Party,
    Whisper,
    Yell,
    Shout,
    FreeCompany,
    Linkshell,
    Alliance,
    PvpTeam,
    NoviceNetwork,
    CrossWorldLinkshell,
}

public class Snippet
{
    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Shortcut { get; set; } = string.Empty;
}

public class SentHistoryItem
{
    public string Text { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;
}

public enum TabNotifyMode
{
    All,
    MentionsOnly,
    Never,
}

public class ChannelSoundSetting
{
    public bool Enabled { get; set; }

    public int SoundEffect { get; set; } = 1;
}

public class CharacterState
{
    public List<CachedFriend> FriendCache { get; set; } = new();

    public Dictionary<ulong, DateTime> FriendLastOnline { get; set; } = new();

    public ChatChannel LastChannel { get; set; } = ChatChannel.Say;

    public string LastWhisperTarget { get; set; } = string.Empty;

    public List<string> RecentWhisperTargets { get; set; } = new();

    public HashSet<string> LockedWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> MutedWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> KnownFcMembers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int LinkshellNumber { get; set; } = 1;

    public int CrossWorldLinkshellNumber { get; set; } = 1;

    public Dictionary<string, string> Drafts { get; set; } = new();

    public List<Snippet> Snippets { get; set; } = new();

    public bool SnippetsMigrated { get; set; }

    public List<SentHistoryItem> SentHistory { get; set; } = new();

    public Guid? LastActiveTabId { get; set; }

    public string? LastActiveWhisperTarget { get; set; }

    public bool LastActiveNoviceNetwork { get; set; }
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int MaxMessageLength { get; set; } = 400;

    public int DelayMs { get; set; } = 600;

    public Vector4 DefaultTextColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 EmoteTextColor { get; set; } = new(0.68f, 0.42f, 0.87f, 1f);

    public Vector4 OocTextColor { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    public Vector4 MentionColor { get; set; } = new(1f, 0.85f, 0.2f, 1f);

    public Vector4 TimestampColor { get; set; } = new(1f, 1f, 1f, 1f);

    public bool ShowSenderStatusIcons { get; set; } = true;

    public bool EstateTeleportContextMenuEnabled { get; set; }

    public Vector4 ValidCommandColor { get; set; } = new(0.8830769f, 0.44017988f, 0f, 1f);

    public Vector4 InvalidCommandColor { get; set; } = new(1f, 0.4f, 0.4f, 1f);

    public ChatFontChoice ChatFont { get; set; } = ChatFontChoice.NotoSansCjkMedium;

    public float ChatFontSizePx { get; set; } = 16f;

    public Dictionary<ChatCategory, Vector4> ChatColours { get; set; } = new();

    public Dictionary<int, Vector4> LinkshellColours { get; set; } = new();

    public Dictionary<int, Vector4> CrossWorldLinkshellColours { get; set; } = new()
    {
        [1] = new Vector4(0.5261538f, 0f, 0f, 1f),
        [2] = new Vector4(0.18153846f, 0.448483f, 1f, 1f),
        [3] = new Vector4(0.41444552f, 0.32871005f, 0.47692305f, 1f),
        [4] = new Vector4(0.3669964f, 1f, 0.36307693f, 1f),
        [5] = new Vector4(0.82941526f, 0.27999997f, 1f, 1f),
    };

    public Dictionary<string, Vector4> WhisperColours { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ChatTab> ChatTabs { get; set; } = new();

    public HashSet<Guid> SecondaryChatTabIds { get; set; } = new();

    public HashSet<string> SecondaryChatWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Vector2? SecondaryChatWindowPosition { get; set; }

    public Vector2? SecondaryChatWindowSize { get; set; }

    public bool TabStripBackgroundColorEnabled { get; set; }

    public Vector4 TabStripBackgroundColor { get; set; } = new(0.45f, 0.22f, 0.02f, 1f);

    public Dictionary<string, Vector4> WhisperTabBackgroundColours { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileLogSay { get; set; } = true;

    public bool FileLogParty { get; set; } = true;

    public bool FileLogTell { get; set; } = true;

    public bool FileLogYell { get; set; } = true;

    public bool FileLogShout { get; set; } = true;

    public bool FileLogFreeCompany { get; set; } = true;

    public bool FileLogLinkshell { get; set; } = true;

    public bool FileLogAlliance { get; set; }

    public bool FileLogPvpTeam { get; set; }

    public bool FileLogNoviceNetwork { get; set; }

    public bool PlaySounds { get; set; } = true;

    public bool WhisperSoundEnabled { get; set; } = true;

    public int WhisperSoundEffect { get; set; } = 3;

    public bool MentionSoundEnabled { get; set; }

    public int MentionSoundEffect { get; set; } = 6;

    public Dictionary<ChatCategory, ChannelSoundSetting> ChannelSounds { get; set; } = new();

    public HashSet<ChatCategory> RpFormattingCategories { get; set; } = new()
    {
        ChatCategory.Say,
        ChatCategory.Party,
        ChatCategory.Whisper,
        ChatCategory.Linkshell,
        ChatCategory.CrossWorldLinkshell,
    };

    public HashSet<int> RpFormattingLinkshellNumbers { get; set; } = new();

    public HashSet<int> RpFormattingCrossWorldLinkshellNumbers { get; set; } = new();

    public bool AllowsRpFormatting(XivChatType type)
    {
        if (ChannelDisplay.CategoryOf(type) is not { } category || !RpFormattingCategories.Contains(category))
        {
            return false;
        }

        if (category == ChatCategory.Linkshell && RpFormattingLinkshellNumbers.Count > 0)
        {
            return ChannelDisplay.LinkshellIndex(type) is { } number && RpFormattingLinkshellNumbers.Contains(number);
        }

        if (category == ChatCategory.CrossWorldLinkshell && RpFormattingCrossWorldLinkshellNumbers.Count > 0)
        {
            return ChannelDisplay.CrossWorldLinkshellIndex(type) is { } number && RpFormattingCrossWorldLinkshellNumbers.Contains(number);
        }

        return true;
    }

    public bool ChatWindowLocked { get; set; }

    public bool ChatWindowLockPosition { get; set; }

    public bool ChatWindowLockSize { get; set; }

    public bool ShowChatDuringCutscenes { get; set; }

    public bool ShowChatDuringLoadingScreens { get; set; }

    public bool HideNativeChat { get; set; } = true;

    public bool ChatToolbarExpanded { get; set; } = true;

    public bool TabStripWheelScrollEnabled { get; set; } = true;

    public bool BlinkUnreadWhisperTabs { get; set; } = true;

    public bool ScrollToBottomOnTabSwitch { get; set; } = true;

    public bool PreloadRecentHistoryOnStartup { get; set; } = true;

    public ChatDesign Design { get; set; } = ChatDesign.Standard;

    public bool ShowC4tLogo { get; set; } = true;

    public bool ShowC4tWatermark { get; set; } = true;

    public ToolbarIconPack ToolbarIconPack { get; set; } = ToolbarIconPack.Flat;

    public bool ToolbarFlatIntroduced { get; set; }

    public Vector4 ToolbarIconBackgroundColor { get; set; } = new(0.28f, 0.48f, 0.72f, 0.55f);

    public bool ToolbarUseCustomIconColor { get; set; }

    public Vector4 ToolbarIconColor { get; set; } = new(1f, 1f, 1f, 1f);

    public float WindowOpacity { get; set; } = 0.661f;

    public float UnfocusedWindowOpacity { get; set; } = 0.62f;

    public float ChatLogBackgroundOpacity { get; set; } = 0.496f;

    public float ChromeBackgroundOpacity { get; set; }

    public float TabStripBackgroundOpacity { get; set; } = -1f;

    public PreviewPosition PreviewPosition { get; set; } = PreviewPosition.Window;

    public Vector4 SendAccentColor { get; set; } = new(0.16f, 0.45f, 0.24f, 1f);

    public float ComposeAreaHeight { get; set; } = 70f;

    public bool SendOnEnter { get; set; } = true;

    public bool KeepInputFocusAfterSend { get; set; }

    public List<Snippet> Snippets { get; set; } = new();

    public Dictionary<ulong, CharacterState> Characters { get; set; } = new();

    public bool WebClientEnabled { get; set; }

    public int WebClientPort { get; set; } = 47829;

    public string WebClientAuthCode { get; set; } = string.Empty;

    public HashSet<string> WebClientAuthStore { get; set; } = new();

    public Dictionary<string, DateTime> WebClientSessions { get; set; } = new();

    public int WebClientSessionDays { get; set; } = 30;

    public List<string> MentionKeywords { get; set; } = new();

    public bool FlashTaskbarOnWhisper { get; set; } = true;

    public bool FlashTaskbarOnMention { get; set; } = true;

    public HashSet<string> HiddenPlayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowNewMessagesDivider { get; set; } = true;

    public bool ShowDateSeparators { get; set; } = true;

    public List<WordFilterRule> WordFilters { get; set; } = new();

    public bool CollapseRepeatedMessages { get; set; } = true;

    public bool GroupConsecutiveMessages { get; set; } = true;

    public float LineSpacing { get; set; } = -1f;

    public float MessageSpacing { get; set; } = -1f;

    public Dictionary<string, string> PlayerNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ChatBookmark> Bookmarks { get; set; } = new();

    public bool AwaySummaryEnabled { get; set; } = true;

    public int AwaySummaryMinutes { get; set; } = 2;

    public bool HighlightFriends { get; set; } = true;

    public Vector4 FriendNameColor { get; set; } = new(0.55f, 0.78f, 1f, 1f);

    public bool HighlightFcMembers { get; set; } = true;

    public Vector4 FcMemberNameColor { get; set; } = new(0.55f, 0.88f, 0.6f, 1f);

    public bool ShowRelationIcons { get; set; } = true;

    public HashSet<string> FavoritePlayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HighlightFavorites { get; set; } = true;

    public List<int> RelationPriority { get; set; } = new();

    public Vector4 FavoriteNameColor { get; set; } = new(1f, 0.55f, 0.78f, 1f);

    public bool SentHistoryPerChannel { get; set; } = true;

    public bool SnippetShortcutsEnabled { get; set; } = true;

    public bool ReplyHotkeyEnabled { get; set; } = true;

    public bool PlayerCardOnClick { get; set; } = true;

    public int LogArchiveAfterDays { get; set; }

    public int LogDeleteAfterDays { get; set; }

    public bool DiscordModeWhispers { get; set; }

    public HashSet<string> WhisperMediaHidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LodestoneAvatars { get; set; } = true;

    public bool RememberPlayerExtras { get; set; } = true;

    public bool ImageLinkPreviews { get; set; }

    public bool ConfirmLinks { get; set; } = true;

    public bool VideoCards { get; set; } = true;

    public Dictionary<string, string> PlayerBirthdays { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PlayerAnniversaries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DateReminders { get; set; } = true;

    public string? LastDateNoticeDay { get; set; }

    public List<string> TrustedLinkHosts { get; set; } = new();

    public FriendNotifyMode FriendOnlineNotify { get; set; } = FriendNotifyMode.Favorites;

    public bool FriendBackgroundRefresh { get; set; } = true;

    public Dictionary<string, string> CustomAvatars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Vector4> CustomAvatarCrops { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PlayerNicknames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool SocialCardView { get; set; } = true;

    public float SocialWindowOpacity { get; set; } = 0.92f;

    public bool SocialGreyOffline { get; set; } = true;

    public bool SocialOfflineCollapsed { get; set; }

    public bool ReplaceSocialWindow { get; set; }

    public bool PlayerCardInGameMenus { get; set; } = true;

    public bool ShowNicknames { get; set; } = true;

    public bool DetectNameChanges { get; set; } = true;

    public Dictionary<ulong, KnownCharacter> KnownCharacters { get; set; } = new();

    public bool ShowPerformanceOverlay { get; set; }

    internal void RestoreComparers()
    {
        WhisperColours = new Dictionary<string, Vector4>(WhisperColours, StringComparer.OrdinalIgnoreCase);
        SecondaryChatWhisperTargets = new HashSet<string>(SecondaryChatWhisperTargets, StringComparer.OrdinalIgnoreCase);
        WhisperTabBackgroundColours = new Dictionary<string, Vector4>(WhisperTabBackgroundColours, StringComparer.OrdinalIgnoreCase);
        HiddenPlayers = new HashSet<string>(HiddenPlayers, StringComparer.OrdinalIgnoreCase);
        WhisperMediaHidden = new HashSet<string>(WhisperMediaHidden, StringComparer.OrdinalIgnoreCase);
        PlayerNotes = new Dictionary<string, string>(PlayerNotes, StringComparer.OrdinalIgnoreCase);
        FavoritePlayers = new HashSet<string>(FavoritePlayers, StringComparer.OrdinalIgnoreCase);
        CustomAvatars = new Dictionary<string, string>(CustomAvatars, StringComparer.OrdinalIgnoreCase);
        CustomAvatarCrops = new Dictionary<string, Vector4>(CustomAvatarCrops, StringComparer.OrdinalIgnoreCase);
        PlayerNicknames = new Dictionary<string, string>(PlayerNicknames, StringComparer.OrdinalIgnoreCase);
        PlayerBirthdays = new Dictionary<string, string>(PlayerBirthdays, StringComparer.OrdinalIgnoreCase);
        PlayerAnniversaries = new Dictionary<string, string>(PlayerAnniversaries, StringComparer.OrdinalIgnoreCase);
        foreach (var character in Characters.Values)
        {
            character.LockedWhisperTargets = new HashSet<string>(character.LockedWhisperTargets, StringComparer.OrdinalIgnoreCase);
            character.MutedWhisperTargets = new HashSet<string>(character.MutedWhisperTargets, StringComparer.OrdinalIgnoreCase);
            character.KnownFcMembers = new HashSet<string>(character.KnownFcMembers, StringComparer.OrdinalIgnoreCase);
        }
    }
}

public enum FriendNotifyMode
{
    Off,
    Favorites,
    AllFriends,
}

public class CachedFriend
{
    public ulong ContentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ushort HomeWorld { get; set; }

    public ushort CurrentWorld { get; set; }

    public ushort Location { get; set; }

    public byte Job { get; set; }

    public ulong State { get; set; }

    public string FcTag { get; set; } = string.Empty;

    public byte Languages { get; set; }

    public byte MainLanguage { get; set; }

    public sbyte Group { get; set; }

    public bool Pending { get; set; }
}

public class KnownCharacter
{
    public string Name { get; set; } = string.Empty;

    public string? World { get; set; }

    public List<string> PreviousNames { get; set; } = new();

    public DateTime? ChangedAt { get; set; }
}

public class WordFilterRule
{
    public string Text { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public HashSet<ChatCategory> Categories { get; set; } = new();
}

public class ChatBookmark
{
    public DateTime Timestamp { get; set; }

    public XivChatType ChatType { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string? SenderWorld { get; set; }

    public string Text { get; set; } = string.Empty;

    internal string Key => KeyOf(Timestamp, Sender, Text);

    internal static string KeyOf(DateTime timestamp, string sender, string text) =>
        timestamp.ToString("yyyyMMddHHmmss") + "|" + sender + "|" + text;
}
