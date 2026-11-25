using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using CelinesChat.Services;
using CelinesChat.Services.Web;
using CelinesChat.Windows;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace CelinesChat;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/celineschat";
    private const string LogCommandName = "/celineschatlog";

    private const int MaxSentHistory = 10;

    private const long EnterActivationGraceMs = 500;

    // Configuration changes are written to disk once they've settled for this long (but at the
    // latest after ConfigSaveMaxDelayMs) instead of on every call - SavePluginConfig serializes the
    // whole config (every character's tabs, drafts, whisper colours, ...) on the game's main thread,
    // and settings sliders/colour pickers used to trigger that on every single frame while dragged.
    private const long ConfigSaveDebounceMs = 1000;
    private const long ConfigSaveMaxDelayMs = 5000;

    internal const int MinWhisperDelayMs = 1200;

    private static readonly string[] NativeChatAddonNames =
    {
        "ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3",
    };

    private long configDirtySince = -1;
    private long lastConfigChange;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPartyList partyList;
    private readonly ITargetManager targetManager;
    private readonly IPlayerState playerState;
    private readonly IObjectTable objectTable;
    private readonly IKeyState keyState;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    internal ITextureProvider TextureProvider { get; }

    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly MessageQueueSender sender;
    private readonly ChatActivationWatcher chatActivationWatcher;
    private readonly WindowSystem windowSystem = new("CelinesChat");
    private readonly WebServerService webServerService;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly BookmarksWindow bookmarksWindow;
    private readonly PreviewWindow previewWindow;
    private readonly CommandInfo openCommandInfo;
    private readonly CommandInfo openLogCommandInfo;
    private readonly EstateTeleportService estateTeleportService;
    private readonly List<string> sentHistory = new();

    public Configuration Configuration { get; }

    internal ChatLogService ChatLog { get; }

    internal IPluginLog Log => log;

    internal ChatWindow ChatWindowInstance => chatWindow;

    internal IFramework Framework => framework;

    internal bool IsSending => sender.IsSending;

    internal int SendTotal => sender.Total;

    internal int SendRemaining => sender.Remaining;

    internal IReadOnlyList<string> SentHistory => sentHistory;

    internal string? PendingWhisperTarget { get; set; }

    internal string? PendingChatPrefillText { get; set; }

    internal List<string> CurrentPreviewChunks { get; set; } = new();

    internal List<MappedChunk> CurrentPreviewMappedChunks { get; set; } = new();

    internal AutoTranslateService AutoTranslate { get; } = new();

    internal void EnsureAutoTranslateLoaded() => AutoTranslate.EnsureLoaded(dataManager, log);

    private readonly CommandValidator commandValidator = new();

    internal void EnsureCommandValidatorLoaded() => commandValidator.EnsureLoaded(dataManager, log);

    private readonly ChatFontManager chatFontManager;

    // Reflects whether the chat font was actually pushed by the most recent PushChatFont() call -
    // callers use this to avoid caching a text measurement taken with the wrong font's metrics
    // (see ChatFontManager.IsAvailable's own remarks for why that happens at all).
    internal bool IsChatFontAvailable => chatFontManager.IsAvailable;

    internal IDisposable? PushChatFont()
    {
        chatFontManager.EnsureFont(Configuration.ChatFont, Configuration.ChatFontSizePx);
        return chatFontManager.IsAvailable ? chatFontManager.Push() : null;
    }

    internal bool IsFriend(string name, string? world) => estateTeleportService.IsFriend(name, world);

    internal void TriggerEstateTeleport(string name, string? world) => estateTeleportService.TryTriggerEstateTeleport(name, world);

    internal bool IsValidCommand(string command) =>
        commandManager.Commands.ContainsKey(command) || commandValidator.IsKnownNativeCommand(command);

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IChatGui chatGui,
        IPartyList partyList,
        ITargetManager targetManager,
        IPlayerState playerState,
        IObjectTable objectTable,
        IKeyState keyState,
        IDataManager dataManager,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IGameInteropProvider gameInteropProvider,
        IFramework framework,
        IClientState clientState,
        ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.partyList = partyList;
        this.targetManager = targetManager;
        this.playerState = playerState;
        this.objectTable = objectTable;
        this.keyState = keyState;
        this.framework = framework;
        this.dataManager = dataManager;
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.condition = condition;
        TextureProvider = textureProvider;
        this.chatGui = chatGui;
        this.log = log;
        chatFontManager = new ChatFontManager(this.pluginInterface);

        Loc.SetLanguage(this.pluginInterface.UiLanguage);

        Configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Web logins used to be a plain token set without any expiry - carry them over once, as if
        // just used, so nobody gets logged out by the update itself.
        if (Configuration.WebClientAuthStore.Count > 0)
        {
            foreach (var token in Configuration.WebClientAuthStore)
            {
                Configuration.WebClientSessions.TryAdd(token, DateTime.UtcNow);
            }

            Configuration.WebClientAuthStore.Clear();
        }

        // TabStripBackgroundOpacity split off ChromeBackgroundOpacity as its own adjustable slider -
        // -1 is a sentinel meaning "never migrated yet" (distinct from a user explicitly choosing
        // 0/off), so this only ever copies the old combined value across once, the first time an
        // existing config loads after the split. The footer itself no longer has its own opacity
        // slider at all (always fully transparent), so there's nothing left to migrate for it.
        if (Configuration.TabStripBackgroundOpacity < 0f)
        {
            Configuration.TabStripBackgroundOpacity = Configuration.ChromeBackgroundOpacity;
        }

        // ChatWindowLockPosition/ChatWindowLockSize split the old single "lock everything" toggle
        // into two independent ones - migrate a user who already had the old combined lock on into
        // both new ones being on (preserving their existing fully-locked window), then clear the old
        // field so this doesn't refire and re-force both back on if they later unlock just one.
        if (Configuration.ChatWindowLocked)
        {
            Configuration.ChatWindowLockPosition = true;
            Configuration.ChatWindowLockSize = true;
            Configuration.ChatWindowLocked = false;
        }

        if (Configuration.ChatTabs.Count == 0)
        {
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultGeneralTab());
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultChatTab());
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultSystemTab());
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultLootTab());
            SaveConfiguration();
        }
        else
        {
            var defaultTab = Configuration.ChatTabs.Find(t => !t.Removable);
            if (defaultTab != null)
            {
                var addedAny = false;
                foreach (var category in ChannelDisplay.LegacyDefaultCategories)
                {
                    addedAny |= defaultTab.IncludedChannels.Add(category);
                }

                foreach (var category in ChannelDisplay.CoreAnnouncementDefaultCategories)
                {
                    addedAny |= defaultTab.IncludedChannels.Add(category);
                }

                if (defaultTab.IncludedChannels.Contains(ChatCategory.SystemError))
                {
                    addedAny |= defaultTab.IncludedChannels.Add(ChatCategory.AmbientBattle);
                }

                if (addedAny)
                {
                    SaveConfiguration();
                }
            }
        }

        sender = new MessageQueueSender(framework, clientState, condition, log);
        var whisperFailureDetector = new WhisperFailureDetector(dataManager);
        ChatLog = new ChatLogService(chatGui, Configuration, this.pluginInterface, log, gameInteropProvider, playerState, whisperFailureDetector);
        chatActivationWatcher = new ChatActivationWatcher(gameInteropProvider, log);
        chatActivationWatcher.TextToInsert += text => PendingChatPrefillText = text;
        estateTeleportService = new EstateTeleportService(dataManager, log);

        EnsureCommandValidatorLoaded();

        chatWindow = new ChatWindow(this);
        settingsWindow = new SettingsWindow(this);
        previewWindow = new PreviewWindow(this);
        bookmarksWindow = new BookmarksWindow(this);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(bookmarksWindow);
        windowSystem.AddWindow(previewWindow);

        // ChatWindow is registered (and therefore drawn) last on purpose - a click inside
        // PreviewWindow briefly makes ImGui treat PreviewWindow as the frontmost window, and with
        // ChatWindow drawn (and calling SetKeyboardFocusHere for the compose box) after that every
        // single frame, it reliably reclaims keyboard focus instead of losing it back to PreviewWindow
        // on the very next frame the way it did when ChatWindow was drawn first.
        windowSystem.AddWindow(chatWindow);

        if (Configuration.SecondaryChatTabIds.Count > 0 || Configuration.SecondaryChatWhisperTargets.Count > 0)
        {
            EnsureSecondaryWindow();
        }

        var webRootDir = Path.Combine(this.pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Web");
        webServerService = new WebServerService(this, log, webRootDir);

        if (Configuration.WebClientEnabled)
        {
            webServerService.Start();
        }

        openCommandInfo = new CommandInfo(OnOpenCommand) { HelpMessage = Loc.T("Command.Help.Open") };
        openLogCommandInfo = new CommandInfo(OnOpenCommand) { HelpMessage = Loc.T("Command.Help.OpenLog") };
        this.commandManager.AddHandler(CommandName, openCommandInfo);
        this.commandManager.AddHandler(LogCommandName, openLogCommandInfo);

        // Dalamud auto-hides plugin UI during cutscenes/GPose on its own, entirely separate from (and
        // invisible to) ApplyGameStateVisibility's own ShowChatDuringCutscenes/IsGPosing handling
        // below - without opting out here, the chat window still vanished during a cutscene even with
        // that setting on, since Dalamud's own hide wins regardless of what chatWindow.IsOpen is set
        // to. DisableGposeUiHide was already set for the same reason on the GPose side; missing its
        // cutscene counterpart was the actual gap (confirmed by live testing: not caused by this
        // plugin's own suppression logic, whose own diagnostic never once fired despite the window
        // visibly disappearing - and other chat plugins like Chat2 stay visible through cutscenes by
        // opting out of Dalamud's automatic hide the same way).
        this.pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        this.pluginInterface.UiBuilder.DisableGposeUiHide = true;

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleChatWindow;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        this.pluginInterface.LanguageChanged += OnLanguageChanged;

        this.framework.Update += OnFrameworkUpdate;
        ChatLog.EntryAdded += OnEntryAddedForPlugin;
    }

    /// <summary>
    /// Marks the configuration as changed. It's written to disk shortly afterwards (see
    /// FlushConfigurationIfDue), so this is cheap enough to call on every edit.
    /// </summary>
    public void SaveConfiguration()
    {
        var now = Environment.TickCount64;
        if (configDirtySince < 0)
        {
            configDirtySince = now;
        }

        lastConfigChange = now;
    }

    private void SaveConfigurationNow()
    {
        configDirtySince = -1;
        try
        {
            pluginInterface.SavePluginConfig(Configuration);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[CelinesChat] Konnte die Konfiguration nicht speichern.");
        }
    }

    private void FlushConfigurationIfDue()
    {
        if (configDirtySince < 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - lastConfigChange >= ConfigSaveDebounceMs || now - configDirtySince >= ConfigSaveMaxDelayMs)
        {
            SaveConfigurationNow();
        }
    }

    internal bool IsLoggedIn => clientState.IsLoggedIn;

    public void ToggleSettingsWindow() => settingsWindow.Toggle();

    internal void ToggleBookmarksWindow() => bookmarksWindow.Toggle();

    internal string ConfigDirectory => pluginInterface.GetPluginConfigDirectory();

    internal AwayTracker Away { get; } = new();

    private long nextAwayCheckTicks;
    private long nextRelationRefreshTicks;
    private int relationVersion;

    // OnlineStatus sheet row for "Away from Keyboard" (/afk).
    private const uint AfkOnlineStatusId = 17;

    // Away/back detection and the periodic refresh of friend/FC highlighting - a couple of cheap
    // checks, done a few times per second rather than every frame.
    private void UpdateAwayAndRelations()
    {
        var now = Environment.TickCount64;
        if (now >= nextAwayCheckTicks)
        {
            nextAwayCheckTicks = now + 500;
            var afk = objectTable.LocalPlayer is { } player && player.OnlineStatus.RowId == AfkOnlineStatusId;
            var awayNow = Configuration.AwaySummaryEnabled && playerState.IsLoaded && (afk || !TaskbarFlasher.IsGameInForeground());
            Away.Update(awayNow, Configuration.AwaySummaryMinutes);
        }

        // The friend list can change at any time - re-check every message's highlight now and then.
        if (now >= nextRelationRefreshTicks)
        {
            nextRelationRefreshTicks = now + 30_000;
            relationVersion++;
        }
    }

    private void OnEntryAddedForPlugin(ChatLogEntry entry)
    {
        Away.OnEntry(entry);

        // Anyone writing in the FC chat is an FC member - remembered per character for highlighting.
        if (entry.ChatType == XivChatType.FreeCompany
            && !string.IsNullOrEmpty(entry.Sender)
            && !string.Equals(entry.Sender, OwnCharacterName, StringComparison.OrdinalIgnoreCase)
            && playerState.IsLoaded)
        {
            var state = GetCharacterState();
            if (state.KnownFcMembers.Count < 2000 && state.KnownFcMembers.Add(entry.SenderKey))
            {
                relationVersion++;
                SaveConfiguration();
            }
        }
    }

    internal const int RelationFriend = 1;
    internal const int RelationFcMember = 2;
    internal const int RelationFavorite = 3;

    internal bool IsFavorite(ChatLogEntry entry) =>
        !string.IsNullOrEmpty(entry.Sender)
        && (Configuration.FavoritePlayers.Contains(entry.SenderKey) || Configuration.FavoritePlayers.Contains(entry.Sender));

    internal void ToggleFavorite(ChatLogEntry entry)
    {
        if (IsFavorite(entry))
        {
            Configuration.FavoritePlayers.Remove(entry.SenderKey);
            Configuration.FavoritePlayers.Remove(entry.Sender);
        }
        else
        {
            Configuration.FavoritePlayers.Add(entry.SenderKey);
        }

        OnRelationSettingsChanged();
    }

    internal void RemoveFavorite(string key)
    {
        if (Configuration.FavoritePlayers.Remove(key))
        {
            OnRelationSettingsChanged();
        }
    }

    /// <summary>0 = nobody special, 1 = on the friend list, 2 = known FC member, 3 = favourite (wins over the others). Cached per message.</summary>
    internal int GetRelation(ChatLogEntry entry)
    {
        if (entry.RelationVersion == relationVersion)
        {
            return entry.Relation;
        }

        var relation = 0;
        if (!string.IsNullOrEmpty(entry.Sender)
            && entry.ChatType != XivChatType.TellOutgoing
            && !string.Equals(entry.Sender, OwnCharacterName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Configuration.HighlightFavorites && IsFavorite(entry))
                {
                    relation = RelationFavorite;
                }
                else if (Configuration.HighlightFriends && IsFriend(entry.Sender, entry.SenderWorld))
                {
                    relation = RelationFriend;
                }
                else if (Configuration.HighlightFcMembers && playerState.IsLoaded)
                {
                    var fc = GetCharacterState().KnownFcMembers;
                    if (fc.Contains(entry.SenderKey) || fc.Contains(entry.Sender))
                    {
                        relation = RelationFcMember;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "[CelinesChat] Freund/FC-Erkennung fehlgeschlagen.");
            }
        }

        entry.Relation = relation;
        entry.RelationVersion = relationVersion;
        return relation;
    }

    /// <summary>Call after changing the friend/FC highlight settings.</summary>
    internal void OnRelationSettingsChanged()
    {
        relationVersion++;
        SaveConfiguration();
    }

    internal WebServerService WebServer => webServerService;

    internal void SetWebClientEnabled(bool enabled)
    {
        Configuration.WebClientEnabled = enabled;
        if (enabled && string.IsNullOrEmpty(Configuration.WebClientAuthCode))
        {
            Configuration.WebClientAuthCode = WebAuth.GenerateAuthCode();
        }

        SaveConfiguration();

        if (enabled)
        {
            webServerService.Start();
        }
        else
        {
            webServerService.Stop();
        }
    }

    internal void RegenerateWebClientAuthCode() => SetWebClientAuthCode(WebAuth.GenerateAuthCode());

    // Shared by the "generate a random one" and "type your own" paths (WebClientPage) - clearing
    // WebClientSessions on any code change (not just a random regeneration) is deliberate: a
    // session token is independent of the code itself, but treating "the code changed" as "log
    // everyone out" consistently, regardless of *why* it changed, is simpler to reason about than a
    // rule that only applies to one of the two ways of changing it.
    internal void SetWebClientAuthCode(string code)
    {
        Configuration.WebClientAuthCode = code;
        Configuration.WebClientSessions.Clear();
        SaveConfiguration();
    }

    internal void HideChatWindow()
    {
        chatManuallyHidden = true;
        chatWindow.IsOpen = false;
        IsChatInputActive = false;
    }

    private bool chatManuallyHidden;

    private long lastEnterActivationTick;

    internal bool IsGPosing => clientState.IsGPosing;

    // Set when Enter opens the chat while gposing (see OnFrameworkUpdate), cleared on leaving GPose
    // or via CloseGposeChatWindow - deliberately NOT tied to IsChatInputActive/focus at all, unlike
    // the grace-period check below. It used to be: ApplyGameStateVisibility's GPose suppression only
    // stayed bypassed while the compose box had keyboard focus (or briefly after, via the grace
    // period), so clicking anything else in the window - a tab, a toolbar button - dropped that
    // focus and the very next frame's suppression check slammed the window shut again ("man kann in
    // gpose keine tabs wechseln, sobald man es versucht geht das fenster nur zu"). A persistent flag
    // keeps it open regardless of *why* the compose box isn't focused right now, and ChatWindow.
    // TrySend's empty-input branch calls CloseGposeChatWindow explicitly to still support the
    // "press Enter on nothing to dismiss" toggle the user wants kept - just as a deliberate action
    // instead of an accidental side effect of losing focus.
    private bool chatOpenedDuringGpose;

    internal void CloseGposeChatWindow()
    {
        chatOpenedDuringGpose = false;
        chatWindow.IsOpen = false;

        // Without this, IsChatInputActive stays stuck at whatever it was the instant the window
        // closed (Draw() never runs again to naturally refresh it while IsOpen is false) - and
        // OnFrameworkUpdate's Enter handler treats IsChatInputActive == true as "already open and
        // focused, just consume the key," permanently swallowing every later Enter press meant to
        // reopen the window. Matches HideChatWindow's own reset for the same reason.
        IsChatInputActive = false;
    }

    /// Single on/off switch for the message preview, regardless of which PreviewPosition mode is
    /// active - backed by previewWindow.IsOpen either way. In "Window" mode that's already what
    /// governs the floating window's own visibility; in "Inside" mode there's no separate window to
    /// toggle, so ChatWindow.DrawEmbeddedPreview checks this same flag before drawing.
    internal bool IsPreviewVisible => previewWindow.IsOpen;

    public void TogglePreviewWindow() => previewWindow.Toggle();

    private SecondaryChatWindow? secondaryChatWindow;

    internal bool HasSecondaryWindow => secondaryChatWindow != null;

    internal bool IsTabInSecondaryWindow(Guid tabId) => Configuration.SecondaryChatTabIds.Contains(tabId);

    internal bool IsWhisperInSecondaryWindow(string target) => Configuration.SecondaryChatWhisperTargets.Contains(target);

    internal void MoveTabToSecondaryWindow(Guid tabId, Vector2? dropScreenPos = null)
    {
        EnsureSecondaryWindow(dropScreenPos);
        Configuration.SecondaryChatTabIds.Add(tabId);
        SaveConfiguration();
    }

    internal void MoveWhisperToSecondaryWindow(string target, Vector2? dropScreenPos = null)
    {
        EnsureSecondaryWindow(dropScreenPos);
        Configuration.SecondaryChatWhisperTargets.Add(target);
        SaveConfiguration();
    }

    internal void MoveTabToMainWindow(Guid tabId)
    {
        Configuration.SecondaryChatTabIds.Remove(tabId);
        SaveConfiguration();
    }

    internal void MoveWhisperToMainWindow(string target)
    {
        Configuration.SecondaryChatWhisperTargets.Remove(target);
        SaveConfiguration();
    }

    internal void ReturnAllTabsFromSecondaryWindow()
    {
        Configuration.SecondaryChatTabIds.Clear();
        Configuration.SecondaryChatWhisperTargets.Clear();
        SaveConfiguration();
    }

    internal void SaveSecondaryWindowBounds(Vector2 position, Vector2 size)
    {
        Configuration.SecondaryChatWindowPosition = position;
        Configuration.SecondaryChatWindowSize = size;
        SaveConfiguration();
    }

    private void EnsureSecondaryWindow(Vector2? dropScreenPos = null)
    {
        if (secondaryChatWindow != null)
        {
            return;
        }

        secondaryChatWindow = new SecondaryChatWindow(this, () =>
        {
            if (secondaryChatWindow != null)
            {
                windowSystem.RemoveWindow(secondaryChatWindow);
                secondaryChatWindow = null;
            }
        });

        if (dropScreenPos is { } dropPos)
        {
            secondaryChatWindow.Position = dropPos;
            secondaryChatWindow.PositionCondition = ImGuiCond.Appearing;
        }
        else if (Configuration.SecondaryChatWindowPosition is { } savedPos)
        {
            secondaryChatWindow.Position = savedPos;
            secondaryChatWindow.PositionCondition = ImGuiCond.FirstUseEver;
        }

        if (Configuration.SecondaryChatWindowSize is { } savedSize)
        {
            secondaryChatWindow.Size = savedSize;
            secondaryChatWindow.SizeCondition = ImGuiCond.FirstUseEver;
        }

        windowSystem.AddWindow(secondaryChatWindow);
        secondaryChatWindow.IsOpen = true;
    }

    public void CancelSending() => sender.Cancel();

    // Exposed separately from GetCharacterState() (rather than having callers infer "did the
    // character change" from the CharacterState reference itself) so ChatWindow can detect a real
    // login/character switch on its own, using its own locally-tracked "last seen" value - restoring
    // that character's last active tab and forcing a scroll-to-bottom needs to happen exactly once
    // per switch, not on every one of the several GetCharacterState() calls a single frame makes.
    internal ulong CurrentCharacterKey => playerState.IsLoaded ? playerState.ContentId : 0UL;

    internal CharacterState GetCharacterState()
    {
        var key = CurrentCharacterKey;
        if (!Configuration.Characters.TryGetValue(key, out var state))
        {
            state = new CharacterState();
            Configuration.Characters[key] = state;
        }

        if (!state.SnippetsMigrated)
        {
            state.Snippets.AddRange(Configuration.Snippets.Select(s => new Snippet { Name = s.Name, Text = s.Text }));
            state.SnippetsMigrated = true;
            SaveConfiguration();
        }

        return state;
    }

    internal string OwnCharacterName => playerState.IsLoaded && !string.IsNullOrEmpty(playerState.CharacterName)
        ? playerState.CharacterName
        : Loc.T("ChatLog.You");

    internal string MentionFirstName
    {
        get
        {
            if (!playerState.IsLoaded || string.IsNullOrEmpty(playerState.CharacterName))
            {
                return string.Empty;
            }

            var spaceIndex = playerState.CharacterName.IndexOf(' ');
            return spaceIndex > 0 ? playerState.CharacterName[..spaceIndex] : playerState.CharacterName;
        }
    }

    private string mentionTermsCache = string.Empty;
    private string? mentionTermsFirstName;
    private int mentionTermsKeywordsVersion = -1;
    private int mentionKeywordsVersion;

    /// <summary>
    /// First name plus Configuration.MentionKeywords, newline-separated (see MentionMatcher) - what the
    /// renderer highlights as a mention. Cached, since it's asked for on every frame.
    /// </summary>
    internal string MentionTerms
    {
        get
        {
            var firstName = MentionFirstName;
            if (firstName != mentionTermsFirstName || mentionKeywordsVersion != mentionTermsKeywordsVersion)
            {
                mentionTermsCache = MentionMatcher.BuildTerms(firstName, Configuration.MentionKeywords);
                mentionTermsFirstName = firstName;
                mentionTermsKeywordsVersion = mentionKeywordsVersion;
            }

            return mentionTermsCache;
        }
    }

    /// <summary>Call after editing Configuration.MentionKeywords.</summary>
    internal void OnMentionKeywordsChanged()
    {
        mentionKeywordsVersion++;
        SaveConfiguration();
    }

    private int filterVersion;

    /// <summary>Hidden player or word filter - cached per entry until a filter changes (see OnFiltersChanged).</summary>
    internal bool IsFilteredOut(ChatLogEntry entry)
    {
        var version = filterVersion;
        if (entry.FilterVersion == version)
        {
            return entry.FilteredOut;
        }

        var result = MessageFilter.IsFilteredOut(entry, Configuration, OwnCharacterName);
        entry.FilteredOut = result;
        entry.FilterVersion = version;
        return result;
    }

    /// <summary>Call after editing Configuration.WordFilters or HiddenPlayers.</summary>
    internal void OnFiltersChanged()
    {
        filterVersion++;
        SaveConfiguration();
    }

    /// <summary>The note saved for this message's sender ("Name@World" first, then plain "Name"), or null.</summary>
    internal string? GetPlayerNote(ChatLogEntry entry)
    {
        var notes = Configuration.PlayerNotes;
        if (notes.Count == 0 || string.IsNullOrEmpty(entry.Sender))
        {
            return null;
        }

        return notes.TryGetValue(entry.SenderKey, out var note) || notes.TryGetValue(entry.Sender, out note) ? note : null;
    }

    internal void SetPlayerNote(string key, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            Configuration.PlayerNotes.Remove(key);
        }
        else
        {
            // Not trimmed - the settings page saves while typing, and trimming there ate every space
            // typed at the end of the note.
            Configuration.PlayerNotes[key] = note;
        }

        SaveConfiguration();
    }

    private HashSet<string>? bookmarkKeys;

    internal bool IsBookmarked(ChatLogEntry entry)
    {
        if (Configuration.Bookmarks.Count == 0)
        {
            return false;
        }

        // Also called from the web client's threads - build into a local and only then publish it,
        // and treat a list changed mid-copy as "not bookmarked" for that one call.
        var keys = bookmarkKeys;
        if (keys == null)
        {
            try
            {
                keys = new HashSet<string>(Configuration.Bookmarks.Select(b => b.Key));
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            bookmarkKeys = keys;
        }

        return keys.Contains(entry.BookmarkKey);
    }

    internal void ToggleBookmark(ChatLogEntry entry)
    {
        var key = entry.BookmarkKey;
        var index = Configuration.Bookmarks.FindIndex(b => b.Key == key);
        if (index >= 0)
        {
            Configuration.Bookmarks.RemoveAt(index);
        }
        else
        {
            Configuration.Bookmarks.Add(new ChatBookmark
            {
                Timestamp = entry.Timestamp,
                ChatType = entry.ChatType,
                Sender = entry.Sender,
                SenderWorld = entry.SenderWorld,
                Text = entry.Text,
            });
        }

        bookmarkKeys = null;
        SaveConfiguration();
    }

    internal void RemoveBookmark(ChatBookmark bookmark)
    {
        if (Configuration.Bookmarks.Remove(bookmark))
        {
            bookmarkKeys = null;
            SaveConfiguration();
        }
    }

    /// <summary>After Configuration was replaced from a backup file - drop every cache built from the old one.</summary>
    internal void OnConfigurationImported()
    {
        Configuration.RestoreComparers();
        bookmarkKeys = null;
        filterVersion++;
        relationVersion++;
        mentionKeywordsVersion++;
        LayoutVersion++;
        SaveConfiguration();
    }

    /// <summary>Bumped whenever every message's rendered height changes (e.g. a tab's timestamps toggled), so the chat windows drop their cached heights.</summary>
    internal int LayoutVersion { get; private set; }

    internal void InvalidateChatLayout() => LayoutVersion++;

    internal void HidePlayer(ChatLogEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Sender) && Configuration.HiddenPlayers.Add(entry.SenderKey))
        {
            OnFiltersChanged();
        }
    }

    internal void UnhidePlayer(string key)
    {
        if (Configuration.HiddenPlayers.Remove(key))
        {
            OnFiltersChanged();
        }
    }

    internal bool IsOwnCharacterTarget(string target)
    {
        if (!playerState.IsLoaded || string.IsNullOrEmpty(playerState.CharacterName))
        {
            return false;
        }

        var atIndex = target.IndexOf('@');
        var targetName = atIndex >= 0 ? target[..atIndex] : target;
        if (!string.Equals(targetName, playerState.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (atIndex < 0)
        {
            return true;
        }

        var targetWorld = target[(atIndex + 1)..];
        var ownWorld = playerState.HomeWorld.ValueNullable?.Name.ExtractText();
        return ownWorld == null || string.Equals(targetWorld, ownWorld, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The targeted player as "Name@HomeWorld". Without the world, /tell only reaches players from
    /// your current world - visitors from other worlds never got the message.
    /// </summary>
    internal string? GetCurrentTargetPlayerName()
    {
        return targetManager.Target is IPlayerCharacter player
            ? WithWorld(player.Name.TextValue, player.HomeWorld.ValueNullable?.Name.ExtractText())
            : null;
    }

    // "Name@HomeWorld" for the same reason as GetCurrentTargetPlayerName - cross-world parties are
    // common. Skips the player themselves.
    internal List<string> GetPartyMemberNames()
    {
        var names = new List<string>();
        foreach (var member in partyList)
        {
            var name = member.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var qualified = WithWorld(name, member.World.ValueNullable?.Name.ExtractText());
            if (!IsOwnCharacterTarget(qualified) && !names.Contains(qualified))
            {
                names.Add(qualified);
            }
        }

        return names;
    }

    private static string WithWorld(string name, string? world) =>
        string.IsNullOrEmpty(world) ? name : $"{name}@{world}";

    private IPlayerCharacter? FindNearbyPlayer(string name, string? world)
    {
        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter player || !string.Equals(player.Name.TextValue, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (world != null && !string.Equals(player.HomeWorld.ValueNullable?.Name.ExtractText(), world, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return player;
        }

        return null;
    }

    internal void TargetPlayer(string name, string? world)
    {
        if (FindNearbyPlayer(name, world) is { } player)
        {
            targetManager.Target = player;
        }
        else
        {
            log.Warning($"[CelinesChat] Ziel setzen: Spieler '{name}' nicht in der Naehe gefunden.");
        }
    }

    internal unsafe void InviteToParty(string name, string? world, ulong contentId)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[CelinesChat] Gruppeneinladung: Spieler '{name}' nicht in der Naehe gefunden.");
            return;
        }

        var targetWorldId = (ushort)player.HomeWorld.RowId;
        var isCrossWorld = playerState.IsLoaded && playerState.HomeWorld.RowId != 0 && playerState.HomeWorld.RowId != targetWorldId;

        if (isCrossWorld && contentId != 0)
        {
            InfoProxyPartyInvite.Instance()->InviteToPartyContentId(contentId, targetWorldId);
        }
        else
        {
            InfoProxyPartyInvite.Instance()->InviteToParty(contentId, player.Name.TextValue, targetWorldId);
        }
    }

    internal unsafe void AddToMuteList(string name, string? world, ulong contentId, ulong accountId)
    {
        if (accountId == 0)
        {
            log.Warning($"[CelinesChat] Mute-Liste: Account-ID fuer '{name}' noch nicht bekannt - bitte kurz warten und erneut versuchen.");
            return;
        }

        var worldId = FindNearbyPlayer(name, world)?.HomeWorld.RowId;
        AgentMutelist.Instance()->Add(accountId, contentId, name, worldId.HasValue ? (short)worldId.Value : (short)-1);
    }

    internal unsafe void InviteToNoviceNetwork(string name, string? world)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[CelinesChat] Novice Network: Spieler '{name}' nicht in der Naehe gefunden.");
            return;
        }

        InfoProxyNoviceNetwork.Instance()->InviteToNoviceNetwork(0, 0, (ushort)player.HomeWorld.RowId, player.Name.TextValue);
    }

    internal unsafe void OpenAdventurerPlate(string name, string? world, ulong contentId)
    {
        var agent = AgentCharaCard.Instance();
        if (agent == null)
        {
            log.Warning("[CelinesChat] AgentCharaCard nicht verfuegbar.");
            return;
        }

        if (contentId != 0)
        {
            agent->OpenCharaCard(contentId);
            return;
        }

        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[CelinesChat] Abenteurerkarte: Spieler '{name}' nicht in der Naehe gefunden und keine Content-ID vorhanden.");
            return;
        }

        agent->OpenCharaCard((GameObject*)player.Address);
    }

    internal void AddToBlacklist(string name, string? world)
    {
        SendRawCommand(world != null ? $"/blist add {name}@{world}" : $"/blist add {name}");
    }

    private bool pendingChatActivation;
    private int? pendingPreviewCursorTarget;

    /// Routes a preview-click cursor jump through the exact same activation trigger the physical
    /// Enter key uses (pendingChatActivation, detected in OnFrameworkUpdate) instead of setting
    /// ChatWindow's own fields directly from PreviewWindow's Draw - Enter reliably grabs true keyboard
    /// input while the direct approach never did, and the one difference is that OnFrameworkUpdate
    /// runs before ImGui draws any window for that frame, while a click handled inside
    /// PreviewWindow.Draw only takes effect after PreviewWindow (the window the user just genuinely
    /// clicked) has already run its own frame - going through the same pre-draw trigger point Enter
    /// already uses avoids that ordering disadvantage entirely.
    internal void RequestComposeCursorActivation(int sourceCharIndex)
    {
        pendingPreviewCursorTarget = sourceCharIndex;
        pendingChatActivation = true;
        chatManuallyHidden = false;
        chatWindow.IsOpen = true;
    }

    internal int? ConsumePendingPreviewCursorTarget()
    {
        var target = pendingPreviewCursorTarget;
        pendingPreviewCursorTarget = null;
        return target;
    }

    internal bool ConsumePendingChatActivation()
    {
        if (!pendingChatActivation)
        {
            return false;
        }

        pendingChatActivation = false;
        return true;
    }

    internal void ReplyInMessageChannel(XivChatType chatType)
    {
        var state = GetCharacterState();

        if (ChannelDisplay.LinkshellIndex(chatType) is { } lsIndex)
        {
            state.LastChannel = ChatChannel.Linkshell;
            state.LinkshellNumber = lsIndex;
        }
        else if (ChannelDisplay.CrossWorldLinkshellIndex(chatType) is { } cwlsIndex)
        {
            state.LastChannel = ChatChannel.CrossWorldLinkshell;
            state.CrossWorldLinkshellNumber = cwlsIndex;
        }
        else
        {
            var mapped = chatType switch
            {
                XivChatType.Say => ChatChannel.Say,
                XivChatType.Party => ChatChannel.Party,
                XivChatType.Yell => ChatChannel.Yell,
                XivChatType.Shout => ChatChannel.Shout,
                XivChatType.FreeCompany => ChatChannel.FreeCompany,
                XivChatType.Alliance => ChatChannel.Alliance,
                XivChatType.PvPTeam => ChatChannel.PvpTeam,
                XivChatType.NoviceNetwork => ChatChannel.NoviceNetwork,
                _ => (ChatChannel?)null,
            };

            if (mapped == null)
            {
                return;
            }

            state.LastChannel = mapped.Value;
        }

        SaveConfiguration();
        chatManuallyHidden = false;
        chatWindow.IsOpen = true;
    }

    internal bool HasCurrentMapFlag() => MapFlagWatcher.HasFlag();

    internal readonly record struct StatusIconInfo(Dalamud.Bindings.ImGui.ImTextureID TextureHandle, Vector2 Uv0, Vector2 Uv1, Vector2 Size);

    // Resolved once and kept alive for the plugin's whole lifetime, rather than calling
    // TextureProvider.GetFromGame fresh on every GetStatusIconInfo call (i.e. once per status icon
    // per message, every single frame). A freshly-requested ISharedImmediateTexture that's never
    // held onto between calls never gets the chance to stay "warm" in Dalamud's own shared-texture
    // cache, so GetWrapOrDefault() could intermittently return null right after the plugin
    // considered it loaded - flipping a message's status icon on/off between frames, which shifts
    // that whole line's word-wrap prefix width right along with it. Holding one shared reference
    // here keeps it consistently resolved instead.
    private ISharedImmediateTexture? fontIconTexture;

    internal StatusIconInfo? GetStatusIconInfo(BitmapFontIcon icon, float targetHeight)
    {
        if (icon == BitmapFontIcon.None || !GfdIconAtlas.TryGetEntry(dataManager, (uint)icon, out var gfdEntry))
        {
            return null;
        }

        fontIconTexture ??= TextureProvider.GetFromGame("common/font/fonticon_ps5.tex");
        var texture = fontIconTexture.GetWrapOrDefault();
        if (texture == null)
        {
            return null;
        }

        var textureSize = new Vector2(texture.Width, texture.Height);
        var sizeRatio = targetHeight / gfdEntry.Height;
        var size = new Vector2(gfdEntry.Width, gfdEntry.Height) * sizeRatio;
        var uv0 = new Vector2(gfdEntry.Left, gfdEntry.Top + 170) * 2 / textureSize;
        var uv1 = new Vector2(gfdEntry.Left + gfdEntry.Width, gfdEntry.Top + gfdEntry.Height + 170) * 2 / textureSize;

        return new StatusIconInfo(texture.Handle, uv0, uv1, size);
    }

    // Same reasoning as fontIconTexture above - held for the plugin's lifetime so the texture stays
    // resolved instead of flickering in and out between frames. Keyed by file name (one per C4T variant).
    private readonly Dictionary<string, ISharedImmediateTexture> c4tLogoTextures = new();

    /// <summary>The C4T logo from the plugin's Assets folder, or null until it's loaded (or if it's missing).</summary>
    internal Dalamud.Bindings.ImGui.ImTextureID? GetC4tLogo(string fileName)
    {
        if (!c4tLogoTextures.TryGetValue(fileName, out var texture))
        {
            var path = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Assets", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            texture = TextureProvider.GetFromFile(new FileInfo(path));
            c4tLogoTextures[fileName] = texture;
        }

        return texture.GetWrapOrDefault()?.Handle;
    }

    private ItemPayload? hoveredItemThisFrame;

    private uint? shownItemTooltipId;

    internal void OnChatLinkHovered(Payload payload)
    {
        if (payload is ItemPayload item)
        {
            hoveredItemThisFrame = item;
        }
    }

    private unsafe void ReconcileItemTooltip()
    {
        var wanted = hoveredItemThisFrame;
        hoveredItemThisFrame = null;

        if (wanted?.RawItemId == shownItemTooltipId)
        {
            return;
        }

        if (wanted != null)
        {
            var agent = AgentItemDetail.Instance();
            var raptureAtkModule = RaptureAtkModule.Instance();
            var addon = raptureAtkModule != null ? raptureAtkModule->RaptureAtkUnitManager.GetAddonByName("ItemDetail") : null;
            var atkStage = AtkStage.Instance();
            if (agent == null || addon == null || atkStage == null)
            {
                return;
            }

            agent->DetailKind = wanted.Kind == ItemKind.EventItem
                ? FFXIVClientStructs.FFXIV.Client.Enums.DetailKind.KeyItem
                : FFXIVClientStructs.FFXIV.Client.Enums.DetailKind.Item;
            agent->TypeOrId = wanted.RawItemId;
            agent->Index = 0;
            agent->Flag1 &= 0xEF;
            agent->ItemId = wanted.RawItemId;
            agent->Flag2 = 1;
            agent->Flag3 = 0;
            agent->AddonId = addon->Id;

            atkStage->TooltipManager.TooltipType |= 2;
            addon->Show(false, 15);

            addon->Focus();
        }
        else
        {
            var raptureAtkModule = RaptureAtkModule.Instance();
            var addon = raptureAtkModule != null ? raptureAtkModule->RaptureAtkUnitManager.GetAddonByName("ItemDetail") : null;
            if (addon != null)
            {
                addon->Hide(true, false, 0);
            }

            var agent = AgentItemDetail.Instance();
            if (agent != null)
            {
                var eventData = stackalloc AtkValue[1];
                var atkValues = stackalloc AtkValue[1];
                atkValues->Type = AtkValueType.Int;
                atkValues->Int = -1;
                agent->ReceiveEvent(eventData, atkValues, 1, 1);
            }
        }

        shownItemTooltipId = wanted?.RawItemId;
    }

    internal void HandleChatLinkClicked(Payload payload, List<Payload> messagePayloads)
    {
        switch (payload)
        {
            case MapLinkPayload map:
                gameGui.OpenMapWithMapLink(map);
                break;
            case ItemPayload item:
                unsafe
                {
                    var itemFinder = ItemFinderModule.Instance();
                    if (itemFinder != null)
                    {
                        itemFinder->SearchForItem(item.RawItemId, false);
                    }
                }

                break;
            case DalamudLinkPayload link:
                InvokeDalamudLink(link, messagePayloads);
                break;
        }
    }

    private void InvokeDalamudLink(DalamudLinkPayload link, List<Payload> messagePayloads)
    {
        var start = messagePayloads.IndexOf(link);
        var end = messagePayloads.IndexOf(RawPayload.LinkTerminator, start < 0 ? 0 : start);
        if (start < 0 || end < 0)
        {
            return;
        }

        var linkPayloads = messagePayloads.Skip(start).Take(end - start + 1).ToList();
        if (!chatGui.RegisteredLinkHandlers.TryGetValue((link.Plugin, link.CommandId), out var handler))
        {
            log.Warning($"[CelinesChat] Kein registrierter Link-Handler fuer Plugin '{link.Plugin}' gefunden.");
            return;
        }

        // The try/catch has to live inside the deferred callback - wrapped around RunOnTick itself it
        // only covered scheduling, and an exception thrown by the other plugin's handler went unseen.
        framework.RunOnTick(() =>
        {
            try
            {
                handler.Invoke(link.CommandId, new SeString(linkPayloads));
            }
            catch (Exception ex)
            {
                log.Error(ex, "[CelinesChat] Fehler beim Ausfuehren eines Dalamud-Link-Handlers.");
            }
        });
    }

    internal void OpenWhisperTab(string target)
    {
        var state = GetCharacterState();

        var resolvedTarget = RememberWhisperTarget(state, target);

        PendingWhisperTarget = resolvedTarget;
        SaveConfiguration();
        chatManuallyHidden = false;
        chatWindow.IsOpen = true;
    }

    /// <summary>
    /// Sends a slash command as typed. Returns false (and sends nothing) if it's longer than the
    /// game allows for one chat line - unlike normal messages, a command can't be split up.
    /// </summary>
    public bool SendRawCommand(string command)
    {
        var encoded = MessageMarkerEncoder.Encode(command);
        if (encoded.Length > MessageSplitter.MaxChatLineBytes)
        {
            return false;
        }

        sender.Enqueue(new[] { encoded }, Configuration.DelayMs);
        RecordSentHistory(command);
        return true;
    }

    /// <summary>
    /// Sends text to an explicit channel without touching the main compose box's channel selection -
    /// used by the torn-off window's own input. Returns an error text, or null when it was queued.
    /// </summary>
    internal string? SendToChannel(ChatChannel channel, int linkshellNumber, int crossWorldLinkshellNumber, string whisperTarget, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!IsLoggedIn)
        {
            return Loc.T("Compose.NotLoggedIn");
        }

        if (trimmed.StartsWith('/'))
        {
            return SendRawCommand(trimmed) ? null : Loc.T("Compose.CommandTooLong", MessageSplitter.MaxChatLineBytes);
        }

        if (channel == ChatChannel.Whisper && (string.IsNullOrWhiteSpace(whisperTarget) || IsOwnCharacterTarget(whisperTarget)))
        {
            return Loc.T("Compose.WhisperTargetMissing");
        }

        var prefix = GetChannelPrefix(channel, linkshellNumber, crossWorldLinkshellNumber, whisperTarget);
        var chunks = MessageSplitter.BuildMessages(text, Configuration.MaxMessageLength, System.Text.Encoding.UTF8.GetByteCount(prefix));
        var commands = new List<byte[]>(chunks.Count);
        foreach (var chunk in chunks)
        {
            commands.Add(MessageMarkerEncoder.Encode(prefix + chunk));
        }

        var delayMs = channel == ChatChannel.Whisper ? Math.Max(Configuration.DelayMs, MinWhisperDelayMs) : Configuration.DelayMs;
        sender.Enqueue(commands, delayMs);
        RecordSentHistory(text);
        return null;
    }

    public void SendChunks(List<string> chunks, string originalDraft)
    {
        var state = GetCharacterState();
        var prefix = GetChannelPrefix(state);
        var commands = new List<byte[]>(chunks.Count);
        foreach (var chunk in chunks)
        {
            commands.Add(MessageMarkerEncoder.Encode(prefix + chunk));
        }

        var delayMs = state.LastChannel == ChatChannel.Whisper
            ? Math.Max(Configuration.DelayMs, MinWhisperDelayMs)
            : Configuration.DelayMs;
        sender.Enqueue(commands, delayMs);

        if (state.LastChannel == ChatChannel.Whisper && !string.IsNullOrWhiteSpace(state.LastWhisperTarget))
        {
            RememberWhisperTarget(state, state.LastWhisperTarget);
        }

        RecordSentHistory(originalDraft);
    }

    private void RecordSentHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        sentHistory.Remove(text);
        sentHistory.Insert(0, text);
        while (sentHistory.Count > MaxSentHistory)
        {
            sentHistory.RemoveAt(sentHistory.Count - 1);
        }
    }

    internal string RememberWhisperTarget(CharacterState state, string target)
    {
        if (state.RecentWhisperTargets.Contains(target))
        {
            return target;
        }

        var nameOnly = target.Split('@')[0];
        foreach (var existing in state.RecentWhisperTargets)
        {
            if (string.Equals(existing.Split('@')[0], nameOnly, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        state.RecentWhisperTargets.Add(target);
        SaveConfiguration();
        return target;
    }

    internal void RenameWhisperTarget(string oldTarget, string newTarget)
    {
        if (string.IsNullOrWhiteSpace(oldTarget) || string.IsNullOrWhiteSpace(newTarget)
            || string.Equals(oldTarget, newTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = GetCharacterState();

        var index = state.RecentWhisperTargets.FindIndex(t => string.Equals(t, oldTarget, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (state.RecentWhisperTargets.Any(t => string.Equals(t, newTarget, StringComparison.OrdinalIgnoreCase)))
            {
                state.RecentWhisperTargets.RemoveAt(index);
            }
            else
            {
                state.RecentWhisperTargets[index] = newTarget;
            }
        }

        MoveSetEntry(state.LockedWhisperTargets, oldTarget, newTarget);
        MoveSetEntry(state.MutedWhisperTargets, oldTarget, newTarget);

        if (string.Equals(state.LastWhisperTarget, oldTarget, StringComparison.OrdinalIgnoreCase))
        {
            state.LastWhisperTarget = newTarget;
        }

        MoveDictEntry(Configuration.WhisperColours, oldTarget, newTarget);
        MoveDictEntry(Configuration.WhisperTabBackgroundColours, oldTarget, newTarget);
        MoveSetEntry(Configuration.SecondaryChatWhisperTargets, oldTarget, newTarget);

        ChatLog.RenameWhisperLog(oldTarget, newTarget);

        SaveConfiguration();

        chatWindow.RenameWhisperTargetInMemory(oldTarget, newTarget);
    }

    private static void MoveSetEntry(HashSet<string> set, string oldKey, string newKey)
    {
        if (set.Remove(oldKey))
        {
            set.Add(newKey);
        }
    }

    private static void MoveDictEntry(Dictionary<string, Vector4> dict, string oldKey, string newKey)
    {
        if (dict.TryGetValue(oldKey, out var value) && !dict.ContainsKey(newKey))
        {
            dict.Remove(oldKey);
            dict[newKey] = value;
        }
    }

    /// <summary>UTF-8 size of the channel command each message goes out with - MessageSplitter needs it to keep every whole line within the game's limit.</summary>
    internal int GetChannelPrefixBytes(CharacterState state) => System.Text.Encoding.UTF8.GetByteCount(GetChannelPrefix(state));

    internal string GetChannelPrefix(CharacterState state) =>
        GetChannelPrefix(state.LastChannel, state.LinkshellNumber, state.CrossWorldLinkshellNumber, state.LastWhisperTarget);

    internal static string GetChannelPrefix(ChatChannel channel, int linkshellNumber, int crossWorldLinkshellNumber, string whisperTarget) => channel switch
    {
        ChatChannel.Say => "/say ",
        ChatChannel.Party => "/p ",
        ChatChannel.Whisper => $"/tell {whisperTarget.Trim()} ",
        ChatChannel.Yell => "/yell ",
        ChatChannel.Shout => "/shout ",
        ChatChannel.FreeCompany => "/fc ",
        ChatChannel.Linkshell => $"/linkshell{Math.Clamp(linkshellNumber, 1, 8)} ",
        ChatChannel.Alliance => "/alliance ",
        ChatChannel.PvpTeam => "/pvpteam ",
        ChatChannel.NoviceNetwork => "/novice ",
        ChatChannel.CrossWorldLinkshell => $"/cwlinkshell{Math.Clamp(crossWorldLinkshellNumber, 1, 8)} ",
        _ => "/say ",
    };

    private bool mainChatInputActive;

    // Set by the main window's compose box (setter) - the torn-off window's own input reports via
    // IsSecondaryChatInputActive. Either one being focused means Enter belongs to that input, not to
    // "open the chat" (see OnFrameworkUpdate).
    internal bool IsChatInputActive
    {
        private get => mainChatInputActive || IsSecondaryChatInputActive;
        set => mainChatInputActive = value;
    }

    internal bool IsSecondaryChatInputActive { private get; set; }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        FlushConfigurationIfDue();
        UpdateAwayAndRelations();
        SetNativeChatVisible(!Configuration.HideNativeChat);
        ApplyGameStateVisibility();

        if (!keyState[VirtualKey.RETURN])
        {
            return;
        }

        if (IsChatInputActive)
        {
            keyState[VirtualKey.RETURN] = false;
            return;
        }

        var raptureAtkModule = RaptureAtkModule.Instance();
        if (raptureAtkModule != null && raptureAtkModule->AtkModule.IsTextInputActive())
        {
            return;
        }

        keyState[VirtualKey.RETURN] = false;
        chatManuallyHidden = false;
        chatWindow.IsOpen = true;
        chatOpenedDuringGpose = clientState.IsGPosing;
        pendingChatActivation = true;
        lastEnterActivationTick = Environment.TickCount64;
    }

    private void ApplyGameStateVisibility()
    {
        // Kept in sync regardless of the early-return below, so a *later* gpose session always
        // starts fresh (requiring its own Enter press) instead of inheriting a stale "already opened
        // this session" flag from whenever gposing last ended.
        if (!clientState.IsGPosing)
        {
            chatOpenedDuringGpose = false;
        }

        if (IsChatInputActive || Environment.TickCount64 - lastEnterActivationTick < EnterActivationGraceMs)
        {
            return;
        }

        // OccupiedInEvent covers scripted sequences that aren't flagged as a "cutscene" by the two
        // WatchingCutscene* flags at all - a duty's own entry sequence (e.g. walking into a dungeon)
        // reportedly still hid the chat window with both ShowChatDuringCutscenes and
        // ShowChatDuringLoadingScreens on, which only this flag being unaccounted-for explains.
        var inCutscene = condition[ConditionFlag.WatchingCutscene]
            || condition[ConditionFlag.WatchingCutscene78]
            || condition[ConditionFlag.OccupiedInCutSceneEvent]
            || condition[ConditionFlag.OccupiedInEvent];
        var inLoadingScreen = condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];

        var suppressedByGameState = !clientState.IsLoggedIn
            || (inCutscene && !Configuration.ShowChatDuringCutscenes)
            || (inLoadingScreen && !Configuration.ShowChatDuringLoadingScreens)
            || (clientState.IsGPosing && !chatOpenedDuringGpose);

        chatWindow.IsOpen = !chatManuallyHidden && !suppressedByGameState;

        // The secondary window has no manual show/hide toggle of its own (chatManuallyHidden doesn't
        // apply here), but it should still honour the same ShowChatDuringCutscenes/
        // ShowChatDuringLoadingScreens/GPose rules the main window does - without this it just sat at
        // whatever IsOpen its own OnClose/empty-check last left it at, ignoring those settings
        // entirely. IsSecondaryWindowHiddenByGameState is checked by SecondaryChatWindow.OnClose so
        // this programmatic hide is never mistaken for the user actually closing the window (which
        // would otherwise dump every tab in it back onto the main window every time a cutscene starts).
        if (secondaryChatWindow != null)
        {
            IsSecondaryWindowHiddenByGameState = suppressedByGameState;
            secondaryChatWindow.IsOpen = !suppressedByGameState;
        }
    }

    internal bool IsSecondaryWindowHiddenByGameState { get; private set; }

    private static unsafe void SetNativeChatVisible(bool visible)
    {
        var raptureAtkModule = RaptureAtkModule.Instance();
        if (raptureAtkModule == null)
        {
            return;
        }

        // Runs every frame - the names are fixed strings rather than rebuilt (and allocated) each time.
        foreach (var name in NativeChatAddonNames)
        {
            SetAddonVisible(raptureAtkModule, name, visible);
        }
    }

    private static unsafe void SetAddonVisible(RaptureAtkModule* raptureAtkModule, string name, bool visible)
    {
        var addon = raptureAtkModule->RaptureAtkUnitManager.GetAddonByName(name);
        if (addon != null && addon->IsReady)
        {
            addon->IsVisible = visible;
        }
    }

    private void OnOpenCommand(string command, string args) => ToggleChatWindow();

    private void OnLanguageChanged(string langCode)
    {
        Loc.SetLanguage(langCode);
        openCommandInfo.HelpMessage = Loc.T("Command.Help.Open");
        openLogCommandInfo.HelpMessage = Loc.T("Command.Help.OpenLog");

        chatWindow.WindowName = WindowTitles.Chat;
        settingsWindow.WindowName = WindowTitles.Settings;
        previewWindow.WindowName = WindowTitles.Preview;
    }

    private void DrawUi()
    {
        windowSystem.Draw();
        ReconcileItemTooltip();
    }

    private void ToggleChatWindow()
    {
        chatWindow.Toggle();
        chatManuallyHidden = !chatWindow.IsOpen;

        if (!chatWindow.IsOpen)
        {
            // Same reset HideChatWindow() already does - ChatWindow.Draw() (the only other place that
            // keeps IsChatInputActive in sync) never runs again while the window is closed. If the
            // compose box happened to be focused right when /celineschat (or the OpenMainUi toggle)
            // closed it, IsChatInputActive would otherwise stay stuck true forever, silently
            // swallowing every later Enter press meant to reopen the window - OnFrameworkUpdate's own
            // Enter handler bails out immediately whenever IsChatInputActive is true.
            IsChatInputActive = false;
        }
    }

    // Set for the duration of Dispose() so SecondaryChatWindow.OnClose can tell a genuine, user-
    // clicked close (which should return its tabs to the main window) apart from any close Dalamud's
    // own teardown triggers while the plugin is unloading/reloading - without this, tearing a tab out
    // never survived a reload: OnClose ran during shutdown just like a real close would, wiping
    // SecondaryChatTabIds/SecondaryChatWhisperTargets right before that now-empty config got saved.
    internal bool IsDisposing { get; private set; }

    public void Dispose()
    {
        IsDisposing = true;

        chatWindow.FlushPendingDraftSave();

        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleChatWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        pluginInterface.LanguageChanged -= OnLanguageChanged;
        framework.Update -= OnFrameworkUpdate;
        ChatLog.EntryAdded -= OnEntryAddedForPlugin;
        SetNativeChatVisible(true);

        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(LogCommandName);

        webServerService.Dispose();
        ChatLog.Dispose();
        chatActivationWatcher.Dispose();
        chatFontManager.Dispose();
        sender.Dispose();

        // Last, so it also catches anything the teardown above still changed.
        if (configDirtySince >= 0)
        {
            SaveConfigurationNow();
        }
    }
}
