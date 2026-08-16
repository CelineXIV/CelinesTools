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

    internal const int MinWhisperDelayMs = 1200;

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
    private readonly PreviewWindow previewWindow;
    private readonly CommandInfo openCommandInfo;
    private readonly CommandInfo openLogCommandInfo;
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

        sender = new MessageQueueSender(framework, log);
        ChatLog = new ChatLogService(chatGui, Configuration, this.pluginInterface, log, gameInteropProvider, playerState);
        chatActivationWatcher = new ChatActivationWatcher(gameInteropProvider, log);
        chatActivationWatcher.TextToInsert += text => PendingChatPrefillText = text;

        EnsureCommandValidatorLoaded();

        chatWindow = new ChatWindow(this);
        settingsWindow = new SettingsWindow(this);
        previewWindow = new PreviewWindow(this);
        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(previewWindow);

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

        this.pluginInterface.UiBuilder.DisableGposeUiHide = true;

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleChatWindow;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        this.pluginInterface.LanguageChanged += OnLanguageChanged;

        this.framework.Update += OnFrameworkUpdate;
    }

    public void SaveConfiguration() => pluginInterface.SavePluginConfig(Configuration);

    public void ToggleSettingsWindow() => settingsWindow.Toggle();

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

    internal void RegenerateWebClientAuthCode()
    {
        Configuration.WebClientAuthCode = WebAuth.GenerateAuthCode();
        Configuration.WebClientAuthStore.Clear();
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

    internal CharacterState GetCharacterState()
    {
        var key = playerState.IsLoaded ? playerState.ContentId : 0UL;
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
        var target = targetManager.Target;
        return target is { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc } ? target.Name.TextValue : null;
    }

    internal List<string> GetPartyMemberNames()
    {
        var names = new List<string>();
        foreach (var member in partyList)
        {
            var name = member.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

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

        try
        {
            framework.RunOnTick(() => handler.Invoke(link.CommandId, new SeString(linkPayloads)));
        }
        catch (Exception ex)
        {
            log.Error(ex, "[CelinesChat] Fehler beim Ausfuehren eines Dalamud-Link-Handlers.");
        }
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

    public void SendRawCommand(string command)
    {
        var encoded = MessageMarkerEncoder.Encode(command);
        sender.Enqueue(new[] { encoded }, Configuration.DelayMs);
        RecordSentHistory(command);
    }

    public void SendChunks(List<string> chunks, string originalDraft)
    {
        var state = GetCharacterState();
        var prefix = BuildChannelPrefix(state);
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

    private string BuildChannelPrefix(CharacterState state) => state.LastChannel switch
    {
        ChatChannel.Say => "/say ",
        ChatChannel.Party => "/p ",
        ChatChannel.Whisper => $"/tell {state.LastWhisperTarget} ",
        ChatChannel.Yell => "/yell ",
        ChatChannel.Shout => "/shout ",
        ChatChannel.FreeCompany => "/fc ",
        ChatChannel.Linkshell => $"/linkshell{Math.Clamp(state.LinkshellNumber, 1, 8)} ",
        ChatChannel.Alliance => "/alliance ",
        ChatChannel.PvpTeam => "/pvpteam ",
        ChatChannel.NoviceNetwork => "/novice ",
        ChatChannel.CrossWorldLinkshell => $"/cwlinkshell{Math.Clamp(state.CrossWorldLinkshellNumber, 1, 8)} ",
        _ => "/say ",
    };

    internal bool IsChatInputActive { private get; set; }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
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
        pendingChatActivation = true;
        lastEnterActivationTick = Environment.TickCount64;
    }

    private void ApplyGameStateVisibility()
    {
        if (IsChatInputActive || Environment.TickCount64 - lastEnterActivationTick < EnterActivationGraceMs)
        {
            return;
        }

        var inCutscene = condition[ConditionFlag.WatchingCutscene]
            || condition[ConditionFlag.WatchingCutscene78]
            || condition[ConditionFlag.OccupiedInCutSceneEvent];
        var inLoadingScreen = condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];

        var suppressedByGameState = !clientState.IsLoggedIn
            || (inCutscene && !Configuration.ShowChatDuringCutscenes)
            || (inLoadingScreen && !Configuration.ShowChatDuringLoadingScreens)
            || clientState.IsGPosing;

        chatWindow.IsOpen = !chatManuallyHidden && !suppressedByGameState;
    }

    private static unsafe void SetNativeChatVisible(bool visible)
    {
        var raptureAtkModule = RaptureAtkModule.Instance();
        if (raptureAtkModule == null)
        {
            return;
        }

        SetAddonVisible(raptureAtkModule, "ChatLog", visible);
        for (var i = 0; i < 4; i++)
        {
            SetAddonVisible(raptureAtkModule, $"ChatLogPanel_{i}", visible);
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
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleChatWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        pluginInterface.LanguageChanged -= OnLanguageChanged;
        framework.Update -= OnFrameworkUpdate;
        SetNativeChatVisible(true);

        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(LogCommandName);

        webServerService.Dispose();
        ChatLog.Dispose();
        chatActivationWatcher.Dispose();
        chatFontManager.Dispose();
        sender.Dispose();
    }
}
