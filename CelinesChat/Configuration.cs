using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using CelinesChat.Services;

namespace CelinesChat;

/// <summary>
/// Fonts safe to offer for the chat log/compose text - limited to ones Dalamud ships with itself
/// (loaded via IFontAtlasBuildToolkitPreBuild.AddDalamudAssetFont), not an arbitrary system-font
/// browser: those bundled files are guaranteed to exist and load correctly, unlike an arbitrary
/// path a user could pick that might not exist, might not be a valid font file, or might behave
/// oddly at unusual sizes.
/// </summary>
public enum ChatFontChoice
{
    Default,
    NotoSansCjkRegular,
    NotoSansCjkMedium,
    InconsolataRegular,
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

    /// <summary>
    /// Whisper targets whose tab is excluded from the tab bar's native drag-to-reorder - set via
    /// the right-click quick-edit popup on the whisper tab itself.
    /// </summary>
    public HashSet<string> LockedWhisperTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    // Tints the whole compose box while it contains a recognised ("/dance") vs unrecognised
    // ("/blabliblubb") slash command - see ChatWindow's compose-box color push and
    // Plugin.IsValidCommand. Matches Chat2's own InputHandler, which does the same whole-box tint.
    public Vector4 ValidCommandColor { get; set; } = new(0.8830769f, 0.44017988f, 0f, 1f);

    public Vector4 InvalidCommandColor { get; set; } = new(1f, 0.4f, 0.4f, 1f);

    public ChatFontChoice ChatFont { get; set; } = ChatFontChoice.NotoSansCjkMedium;

    public float ChatFontSizePx { get; set; } = 16f;

    // Per-message-type text colors, keyed by the small logical ChatCategory set (not the raw
    // XivChatType enum) so the settings UI shows one row per category instead of 20+. Missing
    // entries (e.g. right after upgrading from an older config) fall back to
    // ChannelDisplay.DefaultColor - see ChannelDisplay.Color().
    public Dictionary<ChatCategory, Vector4> ChatColours { get; set; } = new();

    // Overrides for one specific linkshell/cross-world linkshell number (1-8), since a
    // character can be in several at once and telling them apart by color alone needs more
    // than the one shared Linkshell/CrossWorldLinkshell entry in ChatColours above. Missing
    // entries fall back to that shared category color - see ChannelDisplay.Color().
    public Dictionary<int, Vector4> LinkshellColours { get; set; } = new();

    public Dictionary<int, Vector4> CrossWorldLinkshellColours { get; set; } = new()
    {
        [1] = new Vector4(0.5261538f, 0f, 0f, 1f),
        [2] = new Vector4(0.18153846f, 0.448483f, 1f, 1f),
        [3] = new Vector4(0.41444552f, 0.32871005f, 0.47692305f, 1f),
        [4] = new Vector4(0.3669964f, 1f, 0.36307693f, 1f),
        [5] = new Vector4(0.82941526f, 0.27999997f, 1f, 1f),
    };

    // Per-conversation-partner color override, keyed by whisper target ("Name" or "Name@World",
    // matching how whisper tabs/targets are keyed elsewhere) - falls back to the shared Whisper
    // category color if that partner hasn't been customized. Case-insensitive since player names
    // are (matches CharacterState.LockedWhisperTargets' own comparer).
    public Dictionary<string, Vector4> WhisperColours { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sayuri Kwiat@Shiva"] = new Vector4(1f, 0.55f, 0.84907705f, 1f),
    };

    // The tab bar shown above the chat log: the default "Alle" tab (seeded on first run by
    // Plugin, see GetOrSeedChatTabs) plus any custom tabs the user adds. Which channel types
    // show up in the log is now purely a per-tab concern - there's no separate global toggle.
    public List<ChatTab> ChatTabs { get; set; } = new();

    /// <summary>
    /// Off by default (tabs keep their plain, no-fill look). On, every tab in the strip gets
    /// TabStripBackgroundColor as its background unless it has its own override - a fixed tab via
    /// its own BackgroundColor, a whisper tab via WhisperTabBackgroundColours.
    /// </summary>
    public bool TabStripBackgroundColorEnabled { get; set; }

    public Vector4 TabStripBackgroundColor { get; set; } = new(0.45f, 0.22f, 0.02f, 1f);

    // Per-conversation-partner background override, same keying as WhisperColours above - falls
    // back to TabStripBackgroundColor (if enabled) when a partner has no override of their own.
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

    /// <summary>
    /// Master switch for the sound cues below - separate from each individual toggle so muting
    /// everything at once doesn't require unchecking each one and losing its chosen sound effect.
    /// </summary>
    public bool PlaySounds { get; set; } = true;

    /// <summary>
    /// A whisper is easy to miss entirely while this plugin's window isn't focused (or the game
    /// window isn't even focused) - on by default since that's the one most people expect a chat
    /// plugin to have, unlike the rest below.
    /// </summary>
    public bool WhisperSoundEnabled { get; set; } = true;

    /// <summary>
    /// One of the game's own 16 built-in "system sound effect" slots (the same ones available to
    /// macros via &lt;se.1&gt;-&lt;se.16&gt;) - played through UIGlobals.PlayChatSoundEffect, so it
    /// respects the game's own SFX volume/mute settings automatically. Exposed as a plain 1-16
    /// choice (matching how every FFXIV player already knows these) rather than this plugin
    /// guessing which one "the" tell sound is meant to be - there's no single canonical answer.
    /// </summary>
    public int WhisperSoundEffect { get; set; } = 3;

    /// <summary>
    /// Off by default, unlike the whisper sound - being mentioned by name in busy party/FC/shout
    /// chat is common enough that an always-on sound for it would likely be noise rather than a
    /// useful alert, so this is opt-in.
    /// </summary>
    public bool MentionSoundEnabled { get; set; }

    public int MentionSoundEffect { get; set; } = 6;

    /// <summary>
    /// Prevents dragging/resizing the main chat window entirely (ImGuiWindowFlags.NoMove/
    /// NoResize) - toggled from a button in the log toolbar, for once a window's position and
    /// size are exactly how someone wants them and an accidental drag would just be annoying.
    /// </summary>
    public bool ChatWindowLocked { get; set; }

    /// <summary>
    /// Off by default - the window auto-hides during cutscenes (Enter still brings it back for
    /// that cutscene, see Plugin.OnFrameworkUpdate/ApplyGameStateVisibility), matching how the
    /// game's own chat log behaves. Turning this on keeps it showing through cutscenes instead.
    /// </summary>
    public bool ShowChatDuringCutscenes { get; set; }

    /// <summary>
    /// Off by default - same idea as <see cref="ShowChatDuringCutscenes"/> but for zone/loading
    /// transitions, which have nothing worth reading or typing into a chat window during anyway.
    /// </summary>
    public bool ShowChatDuringLoadingScreens { get; set; }

    /// <summary>
    /// On by default (matches the plugin's original always-visible toolbar) - toggled from the
    /// small chevron button next to the tab bar (see ChatWindow.DrawLogTabBar). Off collapses the
    /// whole search/history/clear/hide/settings/lock/preview/RP-actions row to reclaim the
    /// vertical space it takes up, for anyone who only reaches for those occasionally.
    /// </summary>
    public bool ChatToolbarExpanded { get; set; } = true;

    /// <summary>
    /// On by default - lets the mouse wheel scroll the tab strip left/right while hovering it, on
    /// top of the always-available arrow buttons. Off for anyone who'd rather the wheel do nothing
    /// there (e.g. it conflicting with some other hover-scroll expectation).
    /// </summary>
    public bool TabStripWheelScrollEnabled { get; set; } = true;

    public float WindowOpacity { get; set; } = 0.661f;

    public float UnfocusedWindowOpacity { get; set; } = 0.62f;

    public float ChatLogBackgroundOpacity { get; set; } = 0.496f;

    public Vector4 SendAccentColor { get; set; } = new(0.16f, 0.45f, 0.24f, 1f);

    // Height in pixels reserved for the compose area (input box + action row) at the bottom of
    // the chat window, dragged via the splitter between it and the chat log above.
    public float ComposeAreaHeight { get; set; } = 70f;

    // true (default): Enter sends the message, Shift+Enter inserts a newline.
    // false: Enter inserts a newline (multiline default), Ctrl+Enter sends.
    public bool SendOnEnter { get; set; } = true;

    /// <summary>
    /// Off by default (sending drops focus, same as vanilla FFXIV's own chat box). Turning this
    /// on keeps the compose box focused right after a message goes out, so a string of messages
    /// can be typed and sent back-to-back without re-clicking or re-focusing in between.
    /// </summary>
    public bool KeepInputFocusAfterSend { get; set; }

    // Legacy global template list from before templates became per-character. Kept only so
    // existing saved templates survive the update; Plugin.GetCharacterState() copies them into
    // each character once (see CharacterState.SnippetsMigrated) and new templates are saved
    // per-character from then on.
    public List<Snippet> Snippets { get; set; } = new();

    public Dictionary<ulong, CharacterState> Characters { get; set; } = new();

    /// <summary>
    /// Off by default - hosts a small local HTTP server (see Services/Web) so the chat can be read
    /// and used from a browser or phone on the same network. A real (if opt-in, LAN-only) network
    /// attack surface, hence off until explicitly turned on and gated behind the generated code
    /// below - see WebClientPage's warning text.
    /// </summary>
    public bool WebClientEnabled { get; set; }

    public int WebClientPort { get; set; } = 47829;

    /// <summary>
    /// The short code shown in Settings and entered once in the web page to log in - generated
    /// lazily (see Plugin) the first time the feature is enabled with none set yet, not eagerly on
    /// every plugin load, so a user who never touches this feature never has an unused credential
    /// sitting in their config.
    /// </summary>
    public string WebClientAuthCode { get; set; } = string.Empty;

    /// <summary>
    /// Session tokens issued after a successful code entry (see Services/Web/WebAuth) - checked on
    /// every authenticated web request. Regenerating the code (WebClientPage's "Regenerate"
    /// button) clears this entirely, logging out every previously-connected device at once.
    /// </summary>
    public HashSet<string> WebClientAuthStore { get; set; } = new();
}
