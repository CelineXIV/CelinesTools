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
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using C4TChat.Services;
using C4TChat.Services.Web;
using C4TChat.Windows;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace C4TChat;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/c4tchat";
    private const string LogCommandName = "/c4tchatlog";
    private const string LegacyCommandName = "/celineschat";
    private const string LegacyLogCommandName = "/celineschatlog";

    private const int MaxSentHistory = 50;

    private const long EnterActivationGraceMs = 500;

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
    private readonly WindowSystem windowSystem = new("C4TChat");
    private readonly WebServerService webServerService;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly BookmarksWindow bookmarksWindow;
    private readonly PreviewWindow previewWindow;
    private readonly CommandInfo openCommandInfo;
    private readonly CommandInfo openLogCommandInfo;
    private readonly EstateTeleportService estateTeleportService;
    private readonly PerfOverlayWindow perfWindow;
    private readonly SocialWindow socialWindow;
    private readonly AvatarCropWindow avatarCropWindow;

    internal void OpenAvatarCrop(string storedFileName, string forWhom) => avatarCropWindow.Open(storedFileName, forWhom);

    internal (Vector2 Uv0, Vector2 Uv1) AvatarUv(string key, string? customFile) =>
        Avatars.UvFor(key, customFile, Configuration.CustomAvatarCrops);

    public Configuration Configuration { get; }

    internal ChatLogService ChatLog { get; }

    internal IPluginLog Log => log;

    internal IDataManager DataManager => dataManager;

    internal ChatWindow ChatWindowInstance => chatWindow;

    internal IFramework Framework => framework;

    internal bool IsSending => sender.IsSending;

    internal int SendTotal => sender.Total;

    internal int SendRemaining => sender.Remaining;

    internal IReadOnlyList<SentHistoryItem> SentHistory => GetCharacterState().SentHistory;

    internal PerfStats Perf { get; } = new();

    internal AvatarService Avatars { get; private set; } = null!;

    internal string? OwnHomeWorld => playerState.IsLoaded ? playerState.HomeWorld.ValueNullable?.Name.ExtractText() : null;

    internal string? ResolveWorld(ChatLogEntry entry) =>
        entry.SenderWorld
        ?? (playerState.IsLoaded ? playerState.CurrentWorld.ValueNullable?.Name.ExtractText() : null);

    internal string? LastIncomingWhisperFrom { get; private set; }

    internal static string ChannelKey(ChatChannel channel, int linkshellNumber, int crossWorldLinkshellNumber, string whisperTarget) => channel switch
    {
        ChatChannel.Linkshell => "Linkshell:" + linkshellNumber,
        ChatChannel.CrossWorldLinkshell => "CrossWorldLinkshell:" + crossWorldLinkshellNumber,
        ChatChannel.Whisper => "Whisper:" + whisperTarget,
        _ => channel.ToString(),
    };

    internal static string ChannelKey(CharacterState state) =>
        ChannelKey(state.LastChannel, state.LinkshellNumber, state.CrossWorldLinkshellNumber, state.LastWhisperTarget);

    internal string? PendingWhisperTarget { get; set; }

    internal string? PendingChatPrefillText { get; set; }

    internal List<string> CurrentPreviewChunks { get; set; } = new();

    internal List<MappedChunk> CurrentPreviewMappedChunks { get; set; } = new();

    internal AutoTranslateService AutoTranslate { get; } = new();

    internal void EnsureAutoTranslateLoaded() => AutoTranslate.EnsureLoaded(dataManager, log);

    private readonly CommandValidator commandValidator = new();

    internal void EnsureCommandValidatorLoaded() => commandValidator.EnsureLoaded(dataManager, log);

    private readonly ChatFontManager chatFontManager;

    internal bool IsChatFontAvailable => chatFontManager.IsAvailable;

    internal IDisposable? PushChatFont()
    {
        chatFontManager.EnsureFont(Configuration.ChatFont, Configuration.ChatFontSizePx);
        return chatFontManager.IsAvailable ? chatFontManager.Push() : null;
    }

    internal bool IsFriend(string name, string? world)
    {
        world ??= playerState.IsLoaded ? playerState.CurrentWorld.ValueNullable?.Name.ExtractText() : null;
        if (estateTeleportService.IsFriend(name, world))
        {
            return true;
        }

        return world != null && SavedFriendKeys().Contains(name + "@" + world);
    }

    private List<CachedFriend>? savedFriendSource;
    private int savedFriendSourceCount = -1;
    private HashSet<string> savedFriendKeys = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> SavedFriendKeys()
    {
        if (!playerState.IsLoaded)
        {
            return savedFriendKeys;
        }

        var cache = GetCharacterState().FriendCache;
        if (!ReferenceEquals(cache, savedFriendSource) || cache.Count != savedFriendSourceCount)
        {
            savedFriendSource = cache;
            savedFriendSourceCount = cache.Count;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var worlds = dataManager.GetExcelSheet<World>();
            foreach (var friend in cache)
            {
                if (worlds.GetRowOrDefault(friend.HomeWorld) is { } row)
                {
                    keys.Add(friend.Name + "@" + row.Name.ExtractText());
                }
            }

            savedFriendKeys = keys;
        }

        return savedFriendKeys;
    }

    internal List<int> RelationPriorityOrder()
    {
        var order = Configuration.RelationPriority;
        if (order.Count != 3 || !order.Contains(RelationFavorite) || !order.Contains(RelationFriend) || !order.Contains(RelationFcMember))
        {
            Configuration.RelationPriority = order = new List<int> { RelationFavorite, RelationFriend, RelationFcMember };
        }

        return order;
    }

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
        ITextureReadbackProvider textureReadbackProvider,
        IGameInteropProvider gameInteropProvider,
        IFramework framework,
        IClientState clientState,
        ICondition condition,
        IContextMenu contextMenu)
    {
        this.contextMenu = contextMenu;
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

        migratedFromLegacy = LegacyMigration.Run(this.pluginInterface, log);
        Configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (!Configuration.ToolbarFlatIntroduced)
        {
            Configuration.ToolbarFlatIntroduced = true;
            Configuration.ToolbarIconPack = ToolbarIconPack.Flat;
        }

        if (Configuration.WebClientAuthStore.Count > 0)
        {
            foreach (var token in Configuration.WebClientAuthStore)
            {
                Configuration.WebClientSessions.TryAdd(token, DateTime.UtcNow);
            }

            Configuration.WebClientAuthStore.Clear();
        }

        if (Configuration.TabStripBackgroundOpacity < 0f)
        {
            Configuration.TabStripBackgroundOpacity = Configuration.ChromeBackgroundOpacity;
        }

        if (Configuration.ChatWindowLocked)
        {
            Configuration.ChatWindowLockPosition = true;
            Configuration.ChatWindowLockSize = true;
            Configuration.ChatWindowLocked = false;
        }

        foreach (var tab in Configuration.ChatTabs)
        {
            if (tab.Muted)
            {
                tab.NotifyMode = TabNotifyMode.Never;
                tab.Muted = false;
            }
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
        perfWindow = new PerfOverlayWindow(this) { IsOpen = Configuration.ShowPerformanceOverlay };
        socialWindow = new SocialWindow(this);
        avatarCropWindow = new AvatarCropWindow(this);
        windowSystem.AddWindow(avatarCropWindow);
        windowSystem.AddWindow(socialWindow);
        Avatars = new AvatarService(textureProvider, textureReadbackProvider, log, this.pluginInterface.GetPluginConfigDirectory());
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(bookmarksWindow);
        windowSystem.AddWindow(perfWindow);
        windowSystem.AddWindow(previewWindow);

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
        if (LegacyMigration.Check(this.pluginInterface) != LegacyMigration.LegacyState.RunningOld)
        {
            this.commandManager.AddHandler(LegacyCommandName, new CommandInfo(OnOpenCommand) { ShowInHelp = false });
            this.commandManager.AddHandler(LegacyLogCommandName, new CommandInfo(OnOpenCommand) { ShowInHelp = false });
            legacyCommandsRegistered = true;
        }

        this.pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        this.pluginInterface.UiBuilder.DisableGposeUiHide = true;

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleChatWindow;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        this.pluginInterface.LanguageChanged += OnLanguageChanged;

        this.framework.Update += OnFrameworkUpdate;
        ChatLog.EntryAdded += OnEntryAddedForPlugin;
        ChatLog.SenderIdentified += OnSenderIdentified;
        this.contextMenu.OnMenuOpened += OnGameContextMenuOpened;
    }

    private readonly IContextMenu contextMenu;

    private static readonly HashSet<string> PlayerMenuAddons = new(StringComparer.Ordinal)
    {
        "FriendList",
        "SocialList",
        "PartyMemberList",
        "_PartyList",
        "FreeCompany",
        "LinkShell",
        "CrossWorldLinkshell",
        "ContentMemberList",
        "BeginnerChatList",
        "BlackList",
        "LookingForGroup",
    };

    private void OnGameContextMenuOpened(IMenuOpenedArgs args)
    {
        if (!Configuration.PlayerCardInGameMenus
            || args.MenuType != ContextMenuType.Default
            || (args.AddonName != null && !PlayerMenuAddons.Contains(args.AddonName))
            || args.Target is not MenuTargetDefault target
            || string.IsNullOrWhiteSpace(target.TargetName))
        {
            return;
        }

        if (args.AddonName == null && target.TargetObject is not IPlayerCharacter)
        {
            return;
        }

        var name = target.TargetName;
        var worldId = target.TargetHomeWorld.RowId;
        var world = worldId != 0 ? target.TargetHomeWorld.ValueNullable?.Name.ExtractText() : null;
        var contentId = target.TargetContentId;

        if (string.Equals(name, OwnCharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = Loc.T("ChatLog.PlayerCardAction"),
            PrefixChar = 'C',
            PrefixColor = 538,
            OnClicked = _ => OpenPlayerCard(name, string.IsNullOrEmpty(world) ? null : world, contentId),
        });
    }

    internal void OpenPlayerCard(string name, string? world, ulong contentId)
    {
        var currentWorld = playerState.IsLoaded ? playerState.CurrentWorld.ValueNullable?.Name.ExtractText() : null;
        var entry = new ChatLogEntry
        {
            Sender = name,
            SenderWorld = world != null && !string.Equals(world, currentWorld, StringComparison.OrdinalIgnoreCase) ? world : null,
            ChatType = XivChatType.Say,
            ContentId = contentId,
            ContentIdKnown = contentId != 0,
            Timestamp = DateTime.Now,
        };

        if (contentId != 0)
        {
            ObserveCharacter(contentId, name, world, fromFriendList: false);
        }

        chatWindow.OpenPlayerCard(entry);
    }

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
            log.Error(ex, "[C4TChat] Konnte die Konfiguration nicht speichern.");
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

    internal void OpenSettingsPage(SettingsPage page) => settingsWindow.ShowPage(page);

    internal void ToggleBookmarksWindow() => bookmarksWindow.Toggle();

    internal string ConfigDirectory => pluginInterface.GetPluginConfigDirectory();

    internal AwayTracker Away { get; } = new();

    private long nextAwayCheckTicks;
    private long nextRelationRefreshTicks;
    private int relationVersion;

    private const uint AfkOnlineStatusId = 17;

    private bool migratedFromLegacy;
    private bool legacyCommandsRegistered;
    private bool legacyNoticeShown;

    private void ShowLegacyNoticeOnce()
    {
        if (legacyNoticeShown || !playerState.IsLoaded)
        {
            return;
        }

        legacyNoticeShown = true;
        var state = LegacyMigration.Check(pluginInterface);
        string? text = null;
        if (migratedFromLegacy)
        {
            text = Loc.T("Rebrand.Migrated");
        }

        if (state == LegacyMigration.LegacyState.RunningOld)
        {
            text = (text != null ? text + " " : string.Empty) + Loc.T("Rebrand.OldStillRunning");
        }
        else if (state != LegacyMigration.LegacyState.None)
        {
            text = (text != null ? text + " " : string.Empty) + Loc.T("Rebrand.UninstallOld");
        }

        if (text != null)
        {
            chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = "[C4TChat] " + text });
        }
    }

    private void UpdateAwayAndRelations()
    {
        ShowLegacyNoticeOnce();
        PrintPendingNameChangeNotices();
        var now = Environment.TickCount64;
        if (now >= nextAwayCheckTicks)
        {
            nextAwayCheckTicks = now + 500;
            var afk = objectTable.LocalPlayer is { } player && player.OnlineStatus.RowId == AfkOnlineStatusId;
            var awayNow = Configuration.AwaySummaryEnabled && playerState.IsLoaded && (afk || !TaskbarFlasher.IsGameInForeground());
            Away.Update(awayNow, Configuration.AwaySummaryMinutes);
        }

        if (now >= nextRelationRefreshTicks)
        {
            nextRelationRefreshTicks = now + 30_000;
            relationVersion++;
            CheckFriendListForNameChanges();
        }

        if (nextLogMaintenanceTicks == 0)
        {
            nextLogMaintenanceTicks = now + 60_000;
        }
        else if (now >= nextLogMaintenanceTicks)
        {
            nextLogMaintenanceTicks = now + (6 * 60 * 60 * 1000L);
            RunLogMaintenance();
        }
    }

    private long nextLogMaintenanceTicks;
    private System.Threading.Tasks.Task? logMaintenanceTask;

    internal string? LastLogMaintenanceResult { get; private set; }

    internal bool IsLogMaintenanceRunning => logMaintenanceTask is { IsCompleted: false };

    internal void RunLogMaintenance(bool evenIfOff = false)
    {
        var archiveDays = Configuration.LogArchiveAfterDays;
        var deleteDays = Configuration.LogDeleteAfterDays;
        if (IsLogMaintenanceRunning || (!evenIfOff && archiveDays <= 0 && deleteDays <= 0))
        {
            return;
        }

        var folder = ChatLog.LogsFolderPath;
        logMaintenanceTask = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var result = LogArchive.Run(folder, archiveDays, deleteDays);
                LastLogMaintenanceResult = Loc.T("FileLog.MaintenanceResult", DateTime.Now.ToString("HH:mm"), result.Archived, result.Deleted);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[C4TChat] Aufräumen der Log-Dateien fehlgeschlagen.");
                LastLogMaintenanceResult = Loc.T("FileLog.MaintenanceFailed");
            }
        });
    }

    private void OnEntryAddedForPlugin(ChatLogEntry entry)
    {
        Away.OnEntry(entry);

        if (entry.ChatType == XivChatType.TellIncoming && !string.IsNullOrEmpty(entry.Sender) && !IsFilteredOut(entry))
        {
            LastIncomingWhisperFrom = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        }

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

    private int nicknameVersion;

    internal string? GetNickname(ChatLogEntry entry)
    {
        if (!Configuration.ShowNicknames || Configuration.PlayerNicknames.Count == 0 || string.IsNullOrEmpty(entry.Sender)
            || entry.ChatType == XivChatType.TellOutgoing)
        {
            return null;
        }

        if (entry.NicknameVersion != nicknameVersion)
        {
            entry.Nickname = FindNickname(entry.SenderKey, entry.Sender, ResolveWorld(entry));
            entry.NicknameVersion = nicknameVersion;
        }

        return entry.Nickname;
    }

    internal string? FindNickname(string senderKey, string name, string? world)
    {
        var nicknames = Configuration.PlayerNicknames;
        return nicknames.TryGetValue(senderKey, out var nick)
            || (world != null && nicknames.TryGetValue(name + "@" + world, out nick))
            || nicknames.TryGetValue(name, out nick)
            ? nick
            : null;
    }

    internal void SetNickname(string key, string name, string nickname)
    {
        var nicknames = Configuration.PlayerNicknames;
        nicknames.Remove(name);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            nicknames.Remove(key);
        }
        else
        {
            nicknames[key] = nickname.Trim();
        }

        nicknameVersion++;
        InvalidateChatLayout();
        SaveConfiguration();
    }

    private const int MaxKnownCharacters = 5000;
    private bool knownCharactersDirty;

    private readonly Dictionary<ulong, (string Name, int Count)> pendingChatRenames = new();

    private void OnSenderIdentified(ChatLogEntry entry)
    {
        if (entry.ChatType == XivChatType.TellOutgoing || !entry.IsRealPlayer)
        {
            return;
        }

        ObserveCharacter(entry.ContentId, entry.Sender, ResolveWorld(entry), fromFriendList: false);
    }

    private void CheckFriendListForNameChanges()
    {
        if (!Configuration.DetectNameChanges || !playerState.IsLoaded)
        {
            return;
        }

        try
        {
            foreach (var (contentId, name, world) in estateTeleportService.GetFriends())
            {
                ObserveCharacter(contentId, name, world, fromFriendList: true);
            }
        }
        catch (Exception ex)
        {
            Log.Verbose(ex, "[C4TChat] Freundesliste konnte nicht gelesen werden.");
        }

        if (knownCharactersDirty)
        {
            knownCharactersDirty = false;
            SaveConfiguration();
        }
    }

    private void ObserveCharacter(ulong contentId, string name, string? world, bool fromFriendList)
    {
        if (!Configuration.DetectNameChanges || contentId == 0 || string.IsNullOrWhiteSpace(name)
            || contentId == CurrentCharacterKey)
        {
            return;
        }

        var known = Configuration.KnownCharacters;
        if (!known.TryGetValue(contentId, out var character))
        {
            if ((fromFriendList || HasPersonalData(name, world)) && known.Count < MaxKnownCharacters)
            {
                known[contentId] = new KnownCharacter { Name = name, World = world };
                knownCharactersDirty = true;
            }

            return;
        }

        var nameChanged = !string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase);

        var worldChanged = fromFriendList && world != null && character.World != null
            && !string.Equals(character.World, world, StringComparison.OrdinalIgnoreCase);

        if (!nameChanged && !worldChanged)
        {
            pendingChatRenames.Remove(contentId);
            if (character.World == null && world != null)
            {
                character.World = world;
                knownCharactersDirty = true;
            }

            return;
        }

        if (!fromFriendList)
        {
            var count = pendingChatRenames.TryGetValue(contentId, out var pending)
                && string.Equals(pending.Name, name, StringComparison.OrdinalIgnoreCase)
                ? pending.Count + 1
                : 1;
            if (count < 2)
            {
                pendingChatRenames[contentId] = (name, count);
                return;
            }
        }

        pendingChatRenames.Remove(contentId);

        var oldName = character.Name;
        var oldWorld = character.World;
        var newWorld = worldChanged || character.World == null ? world : character.World;

        var oldLabel = oldWorld != null ? oldName + "@" + oldWorld : oldName;
        character.PreviousNames.RemoveAll(n => string.Equals(n, oldLabel, StringComparison.OrdinalIgnoreCase));
        character.PreviousNames.Insert(0, oldLabel);
        if (character.PreviousNames.Count > 5)
        {
            character.PreviousNames.RemoveRange(5, character.PreviousNames.Count - 5);
        }

        character.Name = name;
        character.World = newWorld;
        character.ChangedAt = DateTime.Now;

        var moved = MovePlayerData(oldName, oldWorld, name, newWorld);
        SaveConfiguration();

        var newLabel = newWorld != null ? name + "@" + newWorld : name;
        var text = nameChanged
            ? Loc.T("NameChange.Renamed", oldLabel, newLabel)
            : Loc.T("NameChange.Transferred", name, newWorld ?? "?");
        if (moved)
        {
            text += " " + Loc.T("NameChange.DataMoved");
        }

        log.Info($"[C4TChat] Namenswechsel erkannt: {oldLabel} -> {newLabel}");

        pendingNameChangeNotices.Add("[C4TChat] " + text);
    }

    private readonly List<string> pendingNameChangeNotices = new();

    private void PrintPendingNameChangeNotices()
    {
        if (pendingNameChangeNotices.Count == 0)
        {
            return;
        }

        var notices = pendingNameChangeNotices.ToArray();
        pendingNameChangeNotices.Clear();
        foreach (var notice in notices)
        {
            try
            {
                chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = notice });
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "[C4TChat] Hinweis zum Namenswechsel konnte nicht angezeigt werden.");
            }
        }
    }

    internal IReadOnlyList<string>? GetPreviousNames(ChatLogEntry entry)
    {
        var known = Configuration.KnownCharacters;
        if (known.Count == 0)
        {
            return null;
        }

        if (entry.ContentIdKnown && entry.ContentId != 0)
        {
            return known.TryGetValue(entry.ContentId, out var byId) && byId.PreviousNames.Count > 0 ? byId.PreviousNames : null;
        }

        var world = ResolveWorld(entry);
        foreach (var character in known.Values)
        {
            if (character.PreviousNames.Count > 0
                && string.Equals(character.Name, entry.Sender, StringComparison.OrdinalIgnoreCase)
                && (world == null || character.World == null || string.Equals(character.World, world, StringComparison.OrdinalIgnoreCase)))
            {
                return character.PreviousNames;
            }
        }

        return null;
    }

    private bool HasPersonalData(string name, string? world)
    {
        var config = Configuration;
        var key = world != null ? name + "@" + world : name;
        bool has(Func<string, bool> contains) => contains(key) || contains(name);

        return has(config.PlayerNotes.ContainsKey)
            || has(config.FavoritePlayers.Contains)
            || has(config.CustomAvatars.ContainsKey)
            || has(config.PlayerNicknames.ContainsKey)
            || has(config.HiddenPlayers.Contains)
            || (playerState.IsLoaded && GetCharacterState().RecentWhisperTargets.Exists(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase)));
    }

    private bool MovePlayerData(string oldName, string? oldWorld, string newName, string? newWorld)
    {
        var config = Configuration;
        var oldKey = oldWorld != null ? oldName + "@" + oldWorld : oldName;
        var newKey = newWorld != null ? newName + "@" + newWorld : newName;
        var moved = false;

        void moveDict(Dictionary<string, string> dict)
        {
            foreach (var (from, to) in new[] { (oldKey, newKey), (oldName, newName) })
            {
                if (dict.Remove(from, out var value))
                {
                    dict.TryAdd(to, value);
                    moved = true;
                }
            }
        }

        void moveSet(HashSet<string> set)
        {
            foreach (var (from, to) in new[] { (oldKey, newKey), (oldName, newName) })
            {
                if (set.Remove(from))
                {
                    set.Add(to);
                    moved = true;
                }
            }
        }

        moveDict(config.PlayerNotes);
        moveDict(config.PlayerNicknames);
        moveDict(config.CustomAvatars);
        moveSet(config.FavoritePlayers);
        moveSet(config.HiddenPlayers);
        if (playerState.IsLoaded)
        {
            moveSet(GetCharacterState().KnownFcMembers);
        }

        if (oldWorld != null && newWorld != null)
        {
            var hadConversation = playerState.IsLoaded
                && GetCharacterState().RecentWhisperTargets.Exists(t => string.Equals(t, oldKey, StringComparison.OrdinalIgnoreCase));
            RenameWhisperTarget(oldKey, newKey);
            moved |= hadConversation;
        }

        relationVersion++;
        nicknameVersion++;
        filterVersion++;
        InvalidateChatLayout();
        return moved;
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
                foreach (var candidate in RelationPriorityOrder())
                {
                    var applies = candidate switch
                    {
                        RelationFavorite => Configuration.HighlightFavorites && IsFavorite(entry),
                        RelationFriend => Configuration.HighlightFriends && IsFriend(entry.Sender, entry.SenderWorld),
                        _ => Configuration.HighlightFcMembers && playerState.IsLoaded
                            && (GetCharacterState().KnownFcMembers.Contains(entry.SenderKey) || GetCharacterState().KnownFcMembers.Contains(entry.Sender)),
                    };

                    if (applies)
                    {
                        relation = candidate;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Verbose(ex, "[C4TChat] Freund/FC-Erkennung fehlgeschlagen.");
            }
        }

        entry.Relation = relation;
        entry.RelationVersion = relationVersion;
        return relation;
    }

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

    private bool chatOpenedDuringGpose;

    internal void CloseGposeChatWindow()
    {
        chatOpenedDuringGpose = false;
        chatWindow.IsOpen = false;

        IsChatInputActive = false;
    }

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

    internal ulong CurrentCharacterKey => playerState.IsLoaded ? playerState.ContentId : 0UL;

    internal ushort CurrentWorldId => playerState.IsLoaded ? (ushort)playerState.CurrentWorld.RowId : (ushort)0;

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

    internal void OnMentionKeywordsChanged()
    {
        mentionKeywordsVersion++;
        SaveConfiguration();
    }

    private int filterVersion;

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

    internal void OnFiltersChanged()
    {
        filterVersion++;
        SaveConfiguration();
    }

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

    internal string? GetCurrentTargetPlayerName()
    {
        return targetManager.Target is IPlayerCharacter player
            ? WithWorld(player.Name.TextValue, player.HomeWorld.ValueNullable?.Name.ExtractText())
            : null;
    }

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
            log.Warning($"[C4TChat] Ziel setzen: Spieler '{name}' nicht in der Naehe gefunden.");
        }
    }

    internal unsafe void InviteToParty(string name, string? world, ulong contentId)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[C4TChat] Gruppeneinladung: Spieler '{name}' nicht in der Naehe gefunden.");
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

    internal unsafe void InviteByContentId(string name, ushort homeWorldId, ulong contentId)
    {
        var proxy = InfoProxyPartyInvite.Instance();
        if (proxy == null || contentId == 0)
        {
            return;
        }

        var isCrossWorld = playerState.IsLoaded && playerState.HomeWorld.RowId != 0 && playerState.HomeWorld.RowId != homeWorldId;
        if (isCrossWorld)
        {
            proxy->InviteToPartyContentId(contentId, homeWorldId);
        }
        else
        {
            proxy->InviteToParty(contentId, name, homeWorldId);
        }
    }

    private static readonly string[] SocialAddonNames = { "Social", "FriendList", "PartyMemberList", "SocialList" };
    private bool socialBypass;
    private bool socialBypassSeen;
    private long socialBypassSince;

    internal unsafe void OpenNativeSocial(SocialWindow.SocialTab tab, bool openSearchConditions = false)
    {
        if (openSearchConditions)
        {
            StartHiddenSearch();
            return;
        }

        pendingSearchConditions = false;

        reopenSocialTab = socialWindow.IsOpen ? tab : null;
        socialWindow.IsOpen = false;

        socialBypass = true;
        socialBypassSeen = false;
        socialBypassSince = Environment.TickCount64;

        var agentModule = AgentModule.Instance();
        if (agentModule == null)
        {
            return;
        }

        agentModule->GetAgentByInternalId(AgentId.Social)->Show();
        pendingNativeSocialTab = tab;
    }

    private SocialWindow.SocialTab? pendingNativeSocialTab;

    private enum HiddenSearchStage
    {
        None,
        Opening,
        WaitingForDialog,
        DialogOpen,
        Closing,
    }

    private HiddenSearchStage hiddenSearch;
    private long hiddenSearchSince;
    private bool hiddenSearchTabRequested;

    internal const uint FriendMenuSearchInfo = 51;
    internal const uint FriendMenuRemove = 65;
    internal const uint FriendMenuAssignGroup = 1678;
    internal const uint FriendMenuRespond = 63;
    internal const uint FriendMenuAccept = 460;
    internal const uint FriendMenuDecline = 461;

    private enum FriendActionStage
    {
        Opening,
        MenuRequested,
        SubMenuRequested,
        Selected,
    }

    private sealed class FriendAction
    {
        public ulong ContentId;
        public string Name = string.Empty;
        public uint MenuText;
        public uint SubMenuText;
        public bool LoggedMissing;
        public FriendActionStage Stage;
        public long Since;
        public bool TabRequested;
        public bool ResultSeen;
        public long QuietSince;
        public bool ReopenWindow;
        public HashSet<string> Centered = new();
        public int LastCount = -1;
        public long CountStableSince;
        public long NextTry;
    }

    private FriendAction? friendAction;

    private static readonly string[] FriendActionResultAddons = { "SocialDetailB", "SelectYesno", "ContextMenu", "AddonContextSub", "FriendGroupEdit" };

    internal string FriendMenuLabel(uint addonRow) =>
        dataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>().GetRowOrDefault(addonRow)?.Text.ExtractText() ?? string.Empty;

    internal unsafe void RunFriendMenuItem(ulong contentId, string name, uint addonRow, uint subMenuRow = 0)
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null || contentId == 0 || hiddenSearch != HiddenSearchStage.None)
        {
            return;
        }

        if (friendAction != null)
        {
            if (Environment.TickCount64 - friendAction.Since <= 10_000)
            {
                return;
            }

            log.Info("[C4TChat][FriendMenu] Alte Aktion hing fest - wird verworfen.");
            friendAction = null;
        }

        friendAction = new FriendAction
        {
            ContentId = contentId,
            Name = name,
            MenuText = addonRow,
            SubMenuText = subMenuRow,
            Stage = FriendActionStage.Opening,
            Since = Environment.TickCount64,
        };
        agentModule->GetAgentByInternalId(AgentId.Social)->Show();
    }

    private unsafe void RunFriendAction(RaptureAtkUnitManager* manager)
    {
        var action = friendAction!;
        var now = Environment.TickCount64;

        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null && addon->IsVisible)
            {
                addon->IsVisible = false;
            }
        }

        switch (action.Stage)
        {
            case FriendActionStage.Opening:
            {
                var social = manager->GetAddonByName("Social");
                var list = manager->GetAddonByName("FriendList");
                if (list != null && list->IsReady)
                {
                    var proxyCount = (int)((InfoProxyCommonList*)InfoProxyFriendList.Instance())->GetEntryCount();
                    if (proxyCount != action.LastCount)
                    {
                        action.LastCount = proxyCount;
                        action.CountStableSince = now;
                    }

                    var settled = (now - action.CountStableSince >= 400 || now - action.Since > 4000) && now >= action.NextTry;
                    if (settled)
                    {
                        action.NextTry = now + 250;
                    }

                    if (settled && OpenFriendContextMenu(action, quiet: now - action.Since < 5000))
                    {
                        action.Stage = FriendActionStage.MenuRequested;
                        action.Since = now;
                    }
                    else if (now - action.Since > 6000)
                    {
                        FailFriendAction(manager);
                    }
                }
                else if (social != null && social->IsReady && !action.TabRequested)
                {
                    action.TabRequested = true;
                    AgentModule.Instance()->GetAgentByInternalId(AgentId.Friendlist)->Show();
                }
                else if (now - action.Since > 3000)
                {
                    log.Info($"[C4TChat][FriendMenu] Abbruch: FriendList nicht bereit (Social={(social != null ? social->IsReady.ToString() : "null")}, List={(list != null ? "da" : "null")})");
                    FailFriendAction(manager);
                }

                break;
            }

            case FriendActionStage.MenuRequested:
            {
                var menu = manager->GetAddonByName("ContextMenu");
                if (menu != null && menu->IsVisible)
                {
                    menu->IsVisible = false;
                }

                var context = AgentContext.Instance();
                if (context != null && context->TargetContentId == action.ContentId
                    && TrySelectContextItem(context, &context->MainContextMenu, FriendMenuLabel(action.MenuText), action))
                {
                    action.Since = now;
                    if (action.SubMenuText != 0)
                    {
                        action.Stage = FriendActionStage.SubMenuRequested;
                        action.LoggedMissing = false;
                    }
                    else
                    {
                        action.Stage = FriendActionStage.Selected;
                        action.QuietSince = 0;
                        action.ReopenWindow = socialWindow.IsOpen;
                        socialWindow.IsOpen = false;
                    }
                }
                else if (now - action.Since > 2000)
                {
                    log.Info($"[C4TChat][FriendMenu] Abbruch: Menü-Ziel={(context != null ? context->TargetContentId.ToString("X") : "null")} erwartet={action.ContentId:X} name={(context != null ? context->TargetName.ToString() : "")} menüSichtbar={(menu != null ? "da" : "null")}");
                    if (context != null)
                    {
                        context->ClearMenu();
                    }

                    if (menu != null)
                    {
                        menu->Close(true);
                    }

                    FailFriendAction(manager);
                }

                break;
            }

            case FriendActionStage.SubMenuRequested:
            {
                var yesNo = manager->GetAddonByName("SelectYesno");
                if (yesNo != null && yesNo->IsVisible && yesNo->IsReady)
                {
                    yesNo->FireCallbackInt(action.SubMenuText == FriendMenuDecline ? 1 : 0);
                    action.Stage = FriendActionStage.Selected;
                    action.Since = now;
                    action.QuietSince = 0;
                }
                else if (now - action.Since > 3000)
                {
                    log.Info($"[C4TChat][FriendMenu] Keine Ja/Nein-Abfrage nach \"{FriendMenuLabel(action.MenuText)}\".");
                    FailFriendAction(manager);
                }

                break;
            }

            case FriendActionStage.Selected:
            {
                var open = false;
                foreach (var name in FriendActionResultAddons)
                {
                    var addon = manager->GetAddonByName(name);
                    var visible = addon != null && addon->IsVisible;
                    open |= visible;
                    if (visible && addon->IsReady && action.Centered.Add(name))
                    {
                        CenterAddon(addon);
                    }
                }

                if (open)
                {
                    action.ResultSeen = true;
                    action.QuietSince = 0;
                }
                else if (action.QuietSince == 0)
                {
                    action.QuietSince = now;
                }

                if ((action.ResultSeen && action.QuietSince != 0 && now - action.QuietSince > 400) || now - action.Since > (action.ResultSeen ? 120_000 : 3000))
                {
                    FinishFriendAction(manager);
                }

                break;
            }
        }
    }

    private unsafe bool OpenFriendContextMenu(FriendAction action, bool quiet = false)
    {
        var proxy = InfoProxyFriendList.Instance();
        var agent = AgentFriendlist.Instance();
        var context = AgentContext.Instance();
        if (proxy == null || agent == null || context == null)
        {
            return false;
        }

        var candidates = new List<int>();
        var common = (InfoProxyCommonList*)proxy;
        var count = (int)common->GetEntryCount();
        var raw = -1;
        for (var i = 0; i < count; i++)
        {
            var entry = common->GetEntry((uint)i);
            if (entry != null && entry->ContentId == action.ContentId)
            {
                raw = i;
                break;
            }
        }

        if (raw >= 0)
        {
            candidates.Add(raw);
            for (var offset = 1; offset <= 3; offset++)
            {
                if (raw - offset >= 0)
                {
                    candidates.Add(raw - offset);
                }

                if (raw + offset < count)
                {
                    candidates.Add(raw + offset);
                }
            }
        }

        var displayed = DisplayedFriendIndex(proxy, action.Name);
        if (displayed >= 0 && !candidates.Contains(displayed))
        {
            candidates.Add(displayed);
        }

        foreach (var index in candidates)
        {
            SendFriendListContextEvent(agent, index, action.Name);
            if (context->TargetContentId == action.ContentId)
            {
                return true;
            }
        }

        if (!quiet)
        {
            log.Info($"[C4TChat][FriendMenu] Kein passendes Menü für {action.Name}: versucht [{string.Join(", ", candidates)}], zuletzt Ziel {context->TargetName}");
            DumpFriendListArrays(proxy);
        }
        context->ClearMenu();
        var menu = RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName("ContextMenu");
        if (menu != null)
        {
            menu->Close(true);
        }

        return false;
    }

    private unsafe void DumpFriendListArrays(InfoProxyFriendList* proxy)
    {
        var common = (InfoProxyCommonList*)proxy;
        var holder = &RaptureAtkModule.Instance()->AtkArrayDataHolder;
        var strings = holder->GetStringArrayData(common->StringArrayIndex);
        var numbers = holder->GetNumberArrayData(common->NumberArrayIndex);
        log.Info($"[C4TChat][FriendMenu] Arrays: string#{common->StringArrayIndex} size={(strings != null ? strings->Size : -1)}, number#{common->NumberArrayIndex} size={(numbers != null ? numbers->Size : -1)}");
        if (strings != null)
        {
            var parts = new List<string>();
            var span = strings->Span;
            for (var i = 0; i < Math.Min(span.Length, 60); i++)
            {
                parts.Add(i + ":" + (span[i].Value == null ? "-" : Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)span[i].Value).TextValue));
            }

            log.Info("[C4TChat][FriendMenu] S: " + string.Join(" | ", parts));
        }

        if (numbers != null)
        {
            var parts = new List<string>();
            var span = numbers->Span;
            for (var i = 0; i < Math.Min(span.Length, 120); i++)
            {
                parts.Add(i + ":" + span[i].ToString("X"));
            }

            log.Info("[C4TChat][FriendMenu] N: " + string.Join(" ", parts));
        }

        var entries = new List<string>();
        var count = common->GetEntryCount();
        for (uint i = 0; i < Math.Min(count, 12u); i++)
        {
            var entry = common->GetEntry(i);
            if (entry != null)
            {
                entries.Add($"{entry->NameString}={entry->ContentId:X}");
            }
        }

        log.Info("[C4TChat][FriendMenu] E: " + string.Join(" | ", entries));
    }

    private static unsafe void CenterAddon(AtkUnitBase* addon)
    {
        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (device == null)
        {
            return;
        }

        var width = addon->GetScaledWidth(true);
        var height = addon->GetScaledHeight(true);
        var x = (short)Math.Max(0, ((int)device->Width - width) / 2);
        var y = (short)Math.Max(0, ((int)device->Height - height) / 2);
        addon->SetPosition(x, y);
    }

    private static unsafe void SendFriendListContextEvent(AgentFriendlist* agent, int index, string name)
    {
        var values = stackalloc AtkValue[3];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 1;
        values[1].Type = AtkValueType.Int;
        values[1].Int = index;
        values[2].Type = AtkValueType.Undefined;
        values[2].SetManagedString(name);
        var result = stackalloc AtkValue[1];
        agent->ReceiveEvent(result, values, 3, 0);
        values[2].ReleaseManagedMemory();
    }

    private unsafe int DisplayedFriendIndex(InfoProxyFriendList* proxy, string name)
    {
        var common = (InfoProxyCommonList*)proxy;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = common->GetEntryCount();
        for (uint i = 0; i < count; i++)
        {
            var entry = common->GetEntry(i);
            if (entry != null && entry->NameString is { Length: > 0 } n)
            {
                names.Add(n);
            }
        }

        var holder = &RaptureAtkModule.Instance()->AtkArrayDataHolder;
        var strings = holder->GetStringArrayData(common->StringArrayIndex);
        if (strings == null)
        {
            return -1;
        }

        var rank = 0;
        var span = strings->Span;
        for (var i = 0; i < span.Length; i++)
        {
            var ptr = span[i];
            if (ptr.Value == null)
            {
                continue;
            }

            var text = Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)ptr.Value).TextValue.Trim();
            if (!names.Contains(text))
            {
                continue;
            }

            if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
            {
                return rank;
            }

            rank++;
        }

        return -1;
    }

    private static unsafe List<string> MenuTexts(ContextMenu* menu)
    {
        var texts = new List<string>();
        foreach (ref readonly var param in menu->EventParams)
        {
            if (param.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString)
            {
                texts.Add(param.GetValueAsString());
            }
        }

        return texts;
    }

    private unsafe bool TrySelectContextItem(AgentContext* context, ContextMenu* menu, string label, FriendAction action, bool logMissing = true)
    {
        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        var texts = new List<string>();
        foreach (ref readonly var param in menu->EventParams)
        {
            if (param.Type is AtkValueType.String
                or AtkValueType.ManagedString
                or AtkValueType.ConstString)
            {
                texts.Add(param.GetValueAsString());
            }
        }

        var index = texts.FindIndex(t => t.Contains(label, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            if (logMissing && !action.LoggedMissing)
            {
                action.LoggedMissing = true;
                log.Info($"[C4TChat] Freundesmenü: \"{label}\" nicht gefunden in [{string.Join(" | ", texts)}]");
            }

            return false;
        }

        var values = stackalloc AtkValue[5];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 0;
        values[1].Type = AtkValueType.Int;
        values[1].Int = index;
        values[2].Type = AtkValueType.UInt;
        values[2].UInt = 0;
        values[3].Type = AtkValueType.Undefined;
        values[4].Type = AtkValueType.Undefined;
        var result = stackalloc AtkValue[1];
        context->ReceiveEvent(result, values, 5, 0);
        return true;
    }

    private unsafe void CloseHiddenSocial(RaptureAtkUnitManager* manager)
    {
        var agentModule = AgentModule.Instance();
        if (agentModule != null)
        {
            agentModule->GetAgentByInternalId(AgentId.Social)->Hide();
        }

        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null)
            {
                addon->Close(true);
            }
        }
    }

    private unsafe void FinishFriendAction(RaptureAtkUnitManager* manager)
    {
        var reopen = friendAction?.ReopenWindow ?? false;
        friendAction = null;
        CloseHiddenSocial(manager);
        if (reopen)
        {
            socialWindow.Show(SocialWindow.SocialTab.Friends, toggle: false);
        }
    }

    private unsafe void FailFriendAction(RaptureAtkUnitManager* manager)
    {
        friendAction = null;
        CloseHiddenSocial(manager);
        chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = "[C4TChat] " + Loc.T("Social.MenuFailed") });
    }

    private unsafe void StartHiddenSearch()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
        {
            return;
        }

        socialWindow.IsOpen = false;
        hiddenSearch = HiddenSearchStage.Opening;
        hiddenSearchSince = Environment.TickCount64;
        hiddenSearchTabRequested = false;
        agentModule->GetAgentByInternalId(AgentId.Social)->Show();
    }

    private unsafe void RunHiddenSearch(RaptureAtkUnitManager* manager)
    {
        var now = Environment.TickCount64;

        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null && addon->IsVisible)
            {
                addon->IsVisible = false;
            }
        }

        var social = manager->GetAddonByName("Social");
        var list = manager->GetAddonByName("SocialList");
        var dialog = manager->GetAddonByName("PcSearchDetail");
        var dialogOpen = dialog != null && dialog->IsVisible;

        switch (hiddenSearch)
        {
            case HiddenSearchStage.Opening:
                if (list != null && list->IsReady && ClickAddonButton(list, SearchConditionsButtonParam))
                {
                    hiddenSearch = HiddenSearchStage.WaitingForDialog;
                    hiddenSearchSince = now;
                }
                else if (social != null && social->IsReady && !hiddenSearchTabRequested)
                {
                    hiddenSearchTabRequested = true;
                    var agents = AgentModule.Instance();
                    if (agents != null)
                    {
                        agents->GetAgentByInternalId(AgentId.Search)->Show();
                    }
                }
                else if (now - hiddenSearchSince > 3000)
                {
                    FinishHiddenSearch(manager);
                }

                break;

            case HiddenSearchStage.WaitingForDialog:
                if (dialogOpen)
                {
                    hiddenSearch = HiddenSearchStage.DialogOpen;
                }
                else if (now - hiddenSearchSince > 3000)
                {
                    FinishHiddenSearch(manager);
                }

                break;

            case HiddenSearchStage.DialogOpen:
                if (!dialogOpen)
                {
                    hiddenSearch = HiddenSearchStage.Closing;
                    hiddenSearchSince = now;
                    socialWindow.Show(SocialWindow.SocialTab.Search, toggle: false);
                }

                break;

            case HiddenSearchStage.Closing:
                if (now - hiddenSearchSince > 1500)
                {
                    FinishHiddenSearch(manager);
                }

                break;
        }
    }

    private unsafe void FinishHiddenSearch(RaptureAtkUnitManager* manager)
    {
        hiddenSearch = HiddenSearchStage.None;

        var agentModule = AgentModule.Instance();
        if (agentModule != null)
        {
            agentModule->GetAgentByInternalId(AgentId.Social)->Hide();
        }

        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null)
            {
                addon->Close(true);
            }
        }

        if (!socialWindow.IsOpen)
        {
            socialWindow.Show(SocialWindow.SocialTab.Search, toggle: false);
        }
    }
    private bool pendingSearchConditions;
    private SocialWindow.SocialTab? reopenSocialTab;

    private const int SearchConditionsButtonParam = 1;

    private static unsafe bool ClickAddonButton(AtkUnitBase* addon, int param)
    {
        if (addon == null || !addon->IsReady)
        {
            return false;
        }

        var manager = &addon->UldManager;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
            {
                continue;
            }

            for (var evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
            {
                if (evt->State.EventType == AtkEventType.ButtonClick && evt->Param == param && evt->Listener != null)
                {
                    var data = default(AtkEventData);
                    evt->Listener->ReceiveEvent(AtkEventType.ButtonClick, param, evt, &data);
                    return true;
                }
            }
        }

        return false;
    }

    private unsafe void ReplaceNativeSocialWindow()
    {
        if (!Configuration.ReplaceSocialWindow && hiddenSearch == HiddenSearchStage.None && friendAction == null)
        {
            socialBypass = false;
            return;
        }

        var raptureAtkModule = RaptureAtkModule.Instance();
        if (raptureAtkModule == null)
        {
            return;
        }

        var manager = &raptureAtkModule->RaptureAtkUnitManager;
        if (friendAction != null)
        {
            RunFriendAction(manager);
            return;
        }

        if (hiddenSearch != HiddenSearchStage.None)
        {
            RunHiddenSearch(manager);
            return;
        }

        var anyOpen = false;
        SocialWindow.SocialTab? tab = null;
        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon == null || !addon->IsVisible)
            {
                continue;
            }

            anyOpen = true;
            tab ??= name switch
            {
                "PartyMemberList" => SocialWindow.SocialTab.Party,
                "FriendList" => SocialWindow.SocialTab.Friends,
                "SocialList" => SocialWindow.SocialTab.Search,
                _ => null,
            };
        }

        if (socialBypass)
        {
            var conditions = manager->GetAddonByName("PcSearchDetail");
            anyOpen |= conditions != null && conditions->IsVisible;

            if (anyOpen)
            {
                socialBypassSeen = true;
                if (pendingNativeSocialTab is { } pendingTab)
                {
                    pendingNativeSocialTab = null;
                    var tabAgent = pendingTab switch
                    {
                        SocialWindow.SocialTab.Party => AgentId.PartyMember,
                        SocialWindow.SocialTab.Search => AgentId.Search,
                        _ => AgentId.Friendlist,
                    };
                    var agents = AgentModule.Instance();
                    if (agents != null && tab != pendingTab)
                    {
                        agents->GetAgentByInternalId(tabAgent)->Show();
                    }
                }
            }
            else if (socialBypassSeen || Environment.TickCount64 - socialBypassSince > 3000)
            {
                socialBypass = false;
                pendingSearchConditions = false;
                if (reopenSocialTab is { } reopen)
                {
                    reopenSocialTab = null;
                    socialWindow.Show(reopen, toggle: false);
                }
            }

            if (pendingSearchConditions && pendingNativeSocialTab == null)
            {
                var list = manager->GetAddonByName("SocialList");
                if (list != null && list->IsVisible && ClickAddonButton(list, SearchConditionsButtonParam))
                {
                    pendingSearchConditions = false;
                }
                else if (Environment.TickCount64 - socialBypassSince > 3000)
                {
                    pendingSearchConditions = false;
                }
            }

            return;
        }

        if (!anyOpen)
        {
            CloseLeftoverSocial(manager);
            return;
        }

        var agentModule = AgentModule.Instance();
        if (agentModule != null)
        {
            agentModule->GetAgentByInternalId(AgentId.Social)->Hide();
        }

        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null && addon->IsVisible)
            {
                addon->IsVisible = false;
                addon->Close(true);
            }
        }

        socialWindow.Show(tab ?? SocialWindow.SocialTab.Friends, toggle: true);
    }

    private long nextLeftoverSocialCheck;

    private unsafe void CloseLeftoverSocial(RaptureAtkUnitManager* manager)
    {
        var now = Environment.TickCount64;
        if (now < nextLeftoverSocialCheck)
        {
            return;
        }

        nextLeftoverSocialCheck = now + 1000;
        foreach (var name in SocialAddonNames)
        {
            var addon = manager->GetAddonByName(name);
            if (addon != null && addon->IsReady && !addon->IsVisible)
            {
                log.Debug($"[C4TChat] Unsichtbares {name}-Fenster geschlossen.");
                addon->Close(true);
            }
        }
    }

    internal unsafe void AddToMuteList(string name, string? world, ulong contentId, ulong accountId)
    {
        if (accountId == 0)
        {
            log.Warning($"[C4TChat] Mute-Liste: Account-ID fuer '{name}' noch nicht bekannt - bitte kurz warten und erneut versuchen.");
            return;
        }

        var worldId = FindNearbyPlayer(name, world)?.HomeWorld.RowId;
        AgentMutelist.Instance()->Add(accountId, contentId, name, worldId.HasValue ? (short)worldId.Value : (short)-1);
    }

    internal unsafe void InviteToNoviceNetwork(string name, string? world)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[C4TChat] Novice Network: Spieler '{name}' nicht in der Naehe gefunden.");
            return;
        }

        InfoProxyNoviceNetwork.Instance()->InviteToNoviceNetwork(0, 0, (ushort)player.HomeWorld.RowId, player.Name.TextValue);
    }

    internal unsafe void OpenAdventurerPlate(string name, string? world, ulong contentId)
    {
        var agent = AgentCharaCard.Instance();
        if (agent == null)
        {
            log.Warning("[C4TChat] AgentCharaCard nicht verfuegbar.");
            return;
        }

        if (contentId != 0)
        {
            agent->OpenCharaCard(contentId);
            return;
        }

        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[C4TChat] Abenteurerkarte: Spieler '{name}' nicht in der Naehe gefunden und keine Content-ID vorhanden.");
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
        chatWindow.NotifyViewChanged();
    }

    internal bool ReplyTo(ChatLogEntry entry)
    {
        if (entry.ChatType is XivChatType.TellIncoming or XivChatType.TellOutgoing)
        {
            if (string.IsNullOrEmpty(entry.Sender))
            {
                return false;
            }

            OpenWhisperTab(entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender);
            return true;
        }

        if (!CanReplyIn(entry.ChatType))
        {
            return false;
        }

        ReplyInMessageChannel(entry.ChatType);
        RequestComposeFocus();
        return true;
    }

    internal static bool CanReplyIn(XivChatType type) =>
        type is XivChatType.TellIncoming or XivChatType.TellOutgoing or XivChatType.Say or XivChatType.Party
            or XivChatType.Yell or XivChatType.Shout or XivChatType.FreeCompany or XivChatType.Alliance
            or XivChatType.PvPTeam or XivChatType.NoviceNetwork
        || ChannelDisplay.LinkshellIndex(type) != null
        || ChannelDisplay.CrossWorldLinkshellIndex(type) != null;

    internal void RequestComposeFocus() => chatWindow.RequestComposeFocus();

    internal bool ReplyToLastWhisper()
    {
        if (string.IsNullOrEmpty(LastIncomingWhisperFrom))
        {
            return false;
        }

        OpenWhisperTab(LastIncomingWhisperFrom);
        return true;
    }

    internal bool HasCurrentMapFlag() => MapFlagWatcher.HasFlag();

    internal readonly record struct StatusIconInfo(Dalamud.Bindings.ImGui.ImTextureID TextureHandle, Vector2 Uv0, Vector2 Uv1, Vector2 Size);

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

    private readonly Dictionary<string, ISharedImmediateTexture> c4tLogoTextures = new();

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
            log.Warning($"[C4TChat] Kein registrierter Link-Handler fuer Plugin '{link.Plugin}' gefunden.");
            return;
        }

        framework.RunOnTick(() =>
        {
            try
            {
                handler.Invoke(link.CommandId, new SeString(linkPayloads));
            }
            catch (Exception ex)
            {
                log.Error(ex, "[C4TChat] Fehler beim Ausfuehren eines Dalamud-Link-Handlers.");
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

    public bool SendRawCommand(string command)
    {
        var encoded = MessageMarkerEncoder.Encode(command);
        if (encoded.Length > MessageSplitter.MaxChatLineBytes)
        {
            return false;
        }

        sender.Enqueue(new[] { encoded }, Configuration.DelayMs);
        RecordSentHistory(command, string.Empty);
        return true;
    }

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
        RecordSentHistory(text, ChannelKey(channel, linkshellNumber, crossWorldLinkshellNumber, whisperTarget));
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

        RecordSentHistory(originalDraft, ChannelKey(state));
    }

    private void RecordSentHistory(string text, string channelKey)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var history = GetCharacterState().SentHistory;
        history.RemoveAll(item => item.Text == text && item.Channel == channelKey);
        history.Insert(0, new SentHistoryItem { Text = text, Channel = text.StartsWith('/') ? string.Empty : channelKey });
        while (history.Count > MaxSentHistory)
        {
            history.RemoveAt(history.Count - 1);
        }

        SaveConfiguration();
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

    internal bool IsChatInputActive
    {
        private get => mainChatInputActive || IsSecondaryChatInputActive;
        set => mainChatInputActive = value;
    }

    internal bool IsSecondaryChatInputActive { private get; set; }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!Configuration.ShowPerformanceOverlay)
        {
            OnFrameworkUpdateCore();
            return;
        }

        var start = PerfStats.Start();
        try
        {
            OnFrameworkUpdateCore();
        }
        finally
        {
            Perf.FrameworkUpdate.Add(PerfStats.ElapsedMs(start));
        }
    }

    private unsafe void OnFrameworkUpdateCore()
    {
        FlushConfigurationIfDue();
        UpdateAwayAndRelations();
        ReplaceNativeSocialWindow();

        var typingMinigame = InTypingMinigame();
        if (!typingMinigame)
        {
            SetNativeChatVisible(!Configuration.HideNativeChat);
        }

        ApplyGameStateVisibility(typingMinigame);

        if (typingMinigame || !keyState[VirtualKey.RETURN])
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

    private static readonly HashSet<uint> TypingMinigameTerritories = new() { 1359 };
    private static readonly HashSet<uint> TypingMinigameContent = new() { 1095 };

    private unsafe bool InTypingMinigame()
    {
        if (TypingMinigameTerritories.Contains(clientState.TerritoryType))
        {
            return true;
        }

        var gameMain = FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance();
        return gameMain != null && TypingMinigameContent.Contains(gameMain->CurrentContentFinderConditionId);
    }

    private void ApplyGameStateVisibility(bool typingMinigame)
    {
        if (!clientState.IsGPosing)
        {
            chatOpenedDuringGpose = false;
        }

        if (typingMinigame)
        {
            IsChatInputActive = false;
            chatWindow.IsOpen = false;
            if (secondaryChatWindow != null)
            {
                IsSecondaryWindowHiddenByGameState = true;
                secondaryChatWindow.IsOpen = false;
            }

            return;
        }

        if (IsChatInputActive || Environment.TickCount64 - lastEnterActivationTick < EnterActivationGraceMs)
        {
            return;
        }

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

    private void OnOpenCommand(string command, string args)
    {
        if (string.Equals(args.Trim(), "social", StringComparison.OrdinalIgnoreCase))
        {
            socialWindow.Show(socialWindow.ActiveTab, toggle: true);
            return;
        }

        if (string.Equals(args.Trim(), "perf", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.ShowPerformanceOverlay = !Configuration.ShowPerformanceOverlay;
            SaveConfiguration();
            return;
        }

        ToggleChatWindow();
    }

    private void OnLanguageChanged(string langCode)
    {
        Loc.SetLanguage(langCode);
        openCommandInfo.HelpMessage = Loc.T("Command.Help.Open");
        openLogCommandInfo.HelpMessage = Loc.T("Command.Help.OpenLog");

        chatWindow.WindowName = WindowTitles.Chat;
        settingsWindow.WindowName = WindowTitles.Settings;
        previewWindow.WindowName = WindowTitles.Preview;
        socialWindow.WindowName = Loc.T("Social.Title") + "###celineschatSocial";
    }

    private void DrawUi()
    {
        if (perfWindow.IsOpen != Configuration.ShowPerformanceOverlay)
        {
            perfWindow.IsOpen = Configuration.ShowPerformanceOverlay;
        }

        windowSystem.Draw();
        ReconcileItemTooltip();
    }

    private void ToggleChatWindow()
    {
        chatWindow.Toggle();
        chatManuallyHidden = !chatWindow.IsOpen;

        if (!chatWindow.IsOpen)
        {
            IsChatInputActive = false;
        }
    }

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
        ChatLog.SenderIdentified -= OnSenderIdentified;
        contextMenu.OnMenuOpened -= OnGameContextMenuOpened;
        SetNativeChatVisible(true);

        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(LogCommandName);
        if (legacyCommandsRegistered)
        {
            commandManager.RemoveHandler(LegacyCommandName);
            commandManager.RemoveHandler(LegacyLogCommandName);
        }

        webServerService.Dispose();
        Avatars.Dispose();
        ChatLog.Dispose();
        chatActivationWatcher.Dispose();
        chatFontManager.Dispose();
        sender.Dispose();

        if (configDirtySince >= 0)
        {
            SaveConfigurationNow();
        }
    }
}
