using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using CelinesChat.Services;

namespace CelinesChat;

public enum ChatFontChoice
{
    Default,
    NotoSansCjkRegular,
    NotoSansCjkMedium,
    InconsolataRegular,
}

public enum ToolbarIconPack
{
    Classic,

    AeroGlass,

    FrostedGlass,
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
}

public class CharacterState
{
    public ChatChannel LastChannel { get; set; } = ChatChannel.Say;

    public string LastWhisperTarget { get; set; } = string.Empty;

    public List<string> RecentWhisperTargets { get; set; } = new();

    public HashSet<string> LockedWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> MutedWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int LinkshellNumber { get; set; } = 1;

    public int CrossWorldLinkshellNumber { get; set; } = 1;

    public Dictionary<string, string> Drafts { get; set; } = new();

    public List<Snippet> Snippets { get; set; } = new();

    public bool SnippetsMigrated { get; set; }
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

    /// <summary>Off by default, Phase 1 (see EstateTeleportService) - shows an "Estate Teleportation" entry in a chat message's right-click menu when the sender is a friend. Not wired up to actually teleport yet.</summary>
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

    public Dictionary<string, Vector4> WhisperColours { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sayuri Kwiat@Shiva"] = new Vector4(1f, 0.55f, 0.84907705f, 1f),
    };

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

    public bool ChatWindowLocked { get; set; }

    public bool ShowChatDuringCutscenes { get; set; }

    public bool ShowChatDuringLoadingScreens { get; set; }

    public bool HideNativeChat { get; set; } = true;

    public bool ChatToolbarExpanded { get; set; } = true;

    public bool TabStripWheelScrollEnabled { get; set; } = true;

    public bool BlinkUnreadWhisperTabs { get; set; } = true;

    public bool ScrollToBottomOnTabSwitch { get; set; } = true;

    public bool PreloadRecentHistoryOnStartup { get; set; } = true;

    public ToolbarIconPack ToolbarIconPack { get; set; } = ToolbarIconPack.Classic;

    public Vector4 ToolbarIconBackgroundColor { get; set; } = new(0.28f, 0.48f, 0.72f, 0.55f);

    public bool ToolbarUseCustomIconColor { get; set; }

    public Vector4 ToolbarIconColor { get; set; } = new(1f, 1f, 1f, 1f);

    public float WindowOpacity { get; set; } = 0.661f;

    public float UnfocusedWindowOpacity { get; set; } = 0.62f;

    public float ChatLogBackgroundOpacity { get; set; } = 0.496f;

    /// <summary>Off (fully transparent) by default - background fill behind the tab strip row and the footer/compose-preview row, independent of TabStripBackgroundColor (which only tints individual tab buttons, not the strip itself) and of the main window opacity.</summary>
    public float ChromeBackgroundOpacity { get; set; }

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
}
