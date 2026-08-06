using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using CelinesChat.Services;
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
    private const int MaxRecentWhisperTargets = 6;
    private const int MaxSentHistory = 10;

    // /tell has to be validated against the recipient by the server before it goes out, unlike
    // local channels (say/party/yell/...) which are processed client-side. Sending tells back to
    // back at the same short delay used for local channels can make the server silently drop one
    // of them. This floor only raises the delay for whisper messages, not other channels.
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
    internal ITextureProvider TextureProvider { get; }
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly MessageQueueSender sender;
    private readonly ChatActivationWatcher chatActivationWatcher;
    private readonly WindowSystem windowSystem = new("CelinesChat");
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly PreviewWindow previewWindow;
    private readonly CommandInfo openCommandInfo;
    private readonly CommandInfo openLogCommandInfo;
    private readonly List<string> sentHistory = new();

    public Configuration Configuration { get; }

    internal ChatLogService ChatLog { get; }

    internal bool IsSending => sender.IsSending;

    internal int SendTotal => sender.Total;

    internal int SendRemaining => sender.Remaining;

    internal IReadOnlyList<string> SentHistory => sentHistory;

    /// <summary>
    /// Set by <see cref="OpenWhisperTab"/> (e.g. right-clicking a sender's name in the log) so
    /// <see cref="ChatWindow"/> can sync its active whisper tab on the next frame.
    /// </summary>
    internal string? PendingWhisperTarget { get; set; }

    /// <summary>
    /// Set from <see cref="ChatActivationWatcher"/> whenever the game wants to pre-fill a chat box
    /// with some text (map flag placement, item links, and similar native "insert into whichever
    /// chat is active" actions), consumed by <see cref="ChatWindow"/> the next time it draws (see
    /// <see cref="PendingWhisperTarget"/> for the same pattern).
    /// </summary>
    internal string? PendingChatPrefillText { get; set; }

    /// <summary>
    /// The currently split draft chunks, updated by <see cref="ChatWindow"/> every frame so
    /// <see cref="PreviewWindow"/> can display them without needing its own copy of the draft.
    /// </summary>
    internal List<string> CurrentPreviewChunks { get; set; } = new();

    internal AutoTranslateService AutoTranslate { get; } = new();

    internal void EnsureAutoTranslateLoaded() => AutoTranslate.EnsureLoaded(dataManager, log);

    private readonly CommandValidator commandValidator = new();

    internal void EnsureCommandValidatorLoaded() => commandValidator.EnsureLoaded(dataManager, log);

    private readonly ChatFontManager chatFontManager;

    /// <summary>
    /// Rebuilds (if needed) and pushes the configured chat font - callers must Dispose the
    /// returned value (or use it in a <c>using</c>) to pop it again, same as any IFontHandle.Push.
    /// </summary>
    internal IDisposable? PushChatFont()
    {
        chatFontManager.EnsureFont(Configuration.ChatFont, Configuration.ChatFontSizePx);
        return chatFontManager.Push();
    }

    /// <summary>
    /// Whether the game or some plugin (including our own) actually understands this slash
    /// command - used to tint the compose box (see ChatWindow) so a typo like "/blabliblubb"
    /// looks visibly different from a real command like "/dance". Plugin commands come straight
    /// from Dalamud's own registry (covers every loaded plugin, not just this one); native ones
    /// need <see cref="CommandValidator"/> since Dalamud doesn't expose those at all.
    /// </summary>
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
        IFramework framework)
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
        TextureProvider = textureProvider;
        this.chatGui = chatGui;
        this.log = log;
        chatFontManager = new ChatFontManager(this.pluginInterface);

        Loc.SetLanguage(this.pluginInterface.UiLanguage);

        Configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.ChatTabs.Count == 0)
        {
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultAllTab());
            Configuration.ChatTabs.Add(ChatTab.CreateDefaultBattleLootTab());
            SaveConfiguration();
        }
        else
        {
            // Keep the default "Alle" tab showing every category even after an update adds new
            // ones (e.g. Alliance/PvP Team/Novice Network) - a tab saved to disk before those
            // existed would otherwise silently hide messages in those channels forever.
            var defaultTab = Configuration.ChatTabs.Find(t => !t.Removable);
            if (defaultTab != null)
            {
                var addedAny = false;
                foreach (var category in ChannelDisplay.LegacyDefaultCategories)
                {
                    addedAny |= defaultTab.IncludedChannels.Add(category);
                }

                if (addedAny)
                {
                    SaveConfiguration();
                }
            }
        }

        sender = new MessageQueueSender(framework, log);
        ChatLog = new ChatLogService(chatGui, Configuration, this.pluginInterface, log, gameInteropProvider);
        chatActivationWatcher = new ChatActivationWatcher(gameInteropProvider, log);
        chatActivationWatcher.TextToInsert += text => PendingChatPrefillText = text;

        // Small, cheap sheet (a few hundred rows) - unlike AutoTranslate's Completion sheet, no
        // reason to defer loading it until first use.
        EnsureCommandValidatorLoaded();

        chatWindow = new ChatWindow(this);
        settingsWindow = new SettingsWindow(this);
        previewWindow = new PreviewWindow(this);
        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(previewWindow);

        openCommandInfo = new CommandInfo(OnOpenCommand) { HelpMessage = Loc.T("Command.Help.Open") };
        openLogCommandInfo = new CommandInfo(OnOpenCommand) { HelpMessage = Loc.T("Command.Help.OpenLog") };
        this.commandManager.AddHandler(CommandName, openCommandInfo);
        this.commandManager.AddHandler(LogCommandName, openLogCommandInfo);

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleChatWindow;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        this.pluginInterface.LanguageChanged += OnLanguageChanged;

        // Re-asserted every frame (not just once here) because the game can re-show its own
        // chat log on its own - duty transitions, GPose, etc. - which would otherwise silently
        // undo a one-time hide. Restored in Dispose so a plugin unload/reload never leaves the
        // user without any chat at all.
        this.framework.Update += OnFrameworkUpdate;
    }

    public void SaveConfiguration() => pluginInterface.SavePluginConfig(Configuration);

    public void ToggleSettingsWindow() => settingsWindow.Toggle();

    /// <summary>
    /// Hides the chat window entirely - since the native chat log is always kept hidden too (see
    /// SetNativeChatVisible), this leaves no chat visible at all until the next Enter press, which
    /// re-opens it (see OnFrameworkUpdate) - matching vanilla FFXIV's own "hide chat" behavior,
    /// where pressing Enter afterward brings it back with the input focused.
    /// </summary>
    internal void HideChatWindow()
    {
        chatWindow.IsOpen = false;
        IsChatInputActive = false;
    }

    public void TogglePreviewWindow() => previewWindow.Toggle();

    /// <summary>
    /// Tracks which tabs/whisper conversations currently live in the one secondary chat window
    /// (see <see cref="SecondaryChatWindow"/>) instead of the main one - purely runtime state,
    /// deliberately not persisted, so every plugin load starts fresh with everything back in the
    /// main window.
    /// </summary>
    private readonly HashSet<Guid> secondaryWindowTabIds = new();

    private readonly HashSet<string> secondaryWindowWhisperTargets = new(StringComparer.OrdinalIgnoreCase);
    private SecondaryChatWindow? secondaryChatWindow;

    internal bool IsTabInSecondaryWindow(Guid tabId) => secondaryWindowTabIds.Contains(tabId);

    internal bool IsWhisperInSecondaryWindow(string target) => secondaryWindowWhisperTargets.Contains(target);

    /// <summary>
    /// Moves a tab into the secondary window, opening it first if this is the first tab ever
    /// torn off. There's only ever one secondary window - dragging more tabs out joins them into
    /// its tab bar rather than spawning additional windows, matching "a second chat window".
    /// </summary>
    internal void MoveTabToSecondaryWindow(Guid tabId)
    {
        EnsureSecondaryWindow();
        secondaryWindowTabIds.Add(tabId);
    }

    internal void MoveWhisperToSecondaryWindow(string target)
    {
        EnsureSecondaryWindow();
        secondaryWindowWhisperTargets.Add(target);
    }

    internal void MoveTabToMainWindow(Guid tabId) => secondaryWindowTabIds.Remove(tabId);

    internal void MoveWhisperToMainWindow(string target) => secondaryWindowWhisperTargets.Remove(target);

    internal void ReturnAllTabsFromSecondaryWindow()
    {
        secondaryWindowTabIds.Clear();
        secondaryWindowWhisperTargets.Clear();
    }

    private void EnsureSecondaryWindow()
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

    /// <summary>
    /// Shown as the author of your own outgoing whispers (see DrawLogEntry) - the game reports
    /// the RECIPIENT's name in the sender slot for XivChatType.TellOutgoing, so it has to be
    /// substituted with something at display time regardless.
    /// </summary>
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

    /// <summary>
    /// Scans the object table for a rendered player matching name (+world, if known) - this is
    /// all a chat message realistically gives us to go on, and every one of the native actions
    /// below (aside from blacklisting, which is a plain command) needs the player's actual
    /// in-memory game object rather than just their name. Only finds them if they're still
    /// rendered nearby (same zone/render range) at the moment of the click - confirmed via
    /// ChatTwo's own reference implementation (Ui/Handler/PayloadHandler.cs,
    /// FindCharacterForPayload) that this is the standard, safe way plugins do this.
    /// </summary>
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

    /// <summary>
    /// Same as the game's own "Target" context menu entry - sets ITargetManager.Target directly
    /// to the resolved object rather than going through the "/target Name" command, which avoids
    /// that command's own nearby-name-priority quirks.
    /// </summary>
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

    /// <summary>
    /// Party invite via InfoProxyPartyInvite, the exact native functions the game's own "Invite
    /// to Party" menu item calls - verified against ChatTwo's GameFunctions/Party.cs. Same-world
    /// invites work with content ID 0 (ChatTwo passes 0 there too when it doesn't have one), but
    /// a cross-world invite strictly needs the real content ID from the AddMsgSourceEntry hook
    /// (see ChatLogService) - if that hasn't arrived yet for this message, this falls back to
    /// the same-world call, which will simply fail server-side rather than do the wrong thing.
    /// </summary>
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

    /// <summary>
    /// "Block Functions" -> "Add to Mute List". Needs both the account ID and content ID from
    /// the AddMsgSourceEntry hook (see ChatLogService) - unlike blacklisting or party invites,
    /// there's no name+world fallback for this one, so it's simply unavailable until the hook
    /// has resolved both for this particular message.
    /// </summary>
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

    /// <summary>
    /// Verified against ChatTwo's GameFunctions/Context.cs (InviteToNoviceNetwork), which also
    /// passes 0 for account/content ID - "can specify content id if we have it, but there's no
    /// need" per their own comment.
    /// </summary>
    internal unsafe void InviteToNoviceNetwork(string name, string? world)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[CelinesChat] Novice Network: Spieler '{name}' nicht in der Naehe gefunden.");
            return;
        }

        InfoProxyNoviceNetwork.Instance()->InviteToNoviceNetwork(0, 0, (ushort)player.HomeWorld.RowId, player.Name.TextValue);
    }

    /// <summary>
    /// Opens the Adventurer Plate via the same native AgentCharaCard the game itself uses for
    /// "View Adventurer Plate".
    /// </summary>
    internal unsafe void OpenAdventurerPlate(string name, string? world)
    {
        if (FindNearbyPlayer(name, world) is not { } player)
        {
            log.Warning($"[CelinesChat] Abenteurerkarte: Spieler '{name}' nicht in der Naehe gefunden.");
            return;
        }

        var agent = AgentCharaCard.Instance();
        if (agent == null)
        {
            log.Warning("[CelinesChat] AgentCharaCard nicht verfuegbar.");
            return;
        }

        agent->OpenCharaCard((GameObject*)player.Address);
    }

    /// <summary>
    /// "Block Functions" -> "Add to Blacklist" from the native menu. This one really is just the
    /// documented "/blist add Name@World" command (verified against ChatTwo's
    /// GameFunctions/GameFunctions.cs, AddToBlacklist/ListCommand) - no object lookup needed, so
    /// it also works for names that aren't currently rendered nearby. ChatTwo routes the name
    /// through a macro-placeholder hook instead of embedding it directly in the command text,
    /// likely to robustly handle unusual characters in a name - a real but narrow edge case this
    /// simpler version doesn't cover.
    /// </summary>
    internal void AddToBlacklist(string name, string? world)
    {
        SendRawCommand(world != null ? $"/blist add {name}@{world}" : $"/blist add {name}");
    }

    /// <summary>
    /// Set from <see cref="OnFrameworkUpdate"/>, consumed by <see cref="ChatWindow"/>.Draw the
    /// next time it runs - see that method's remarks for why the detection itself has to happen
    /// here rather than in Draw.
    /// </summary>
    private bool pendingChatActivation;

    /// <summary>
    /// Called once by <see cref="ChatWindow"/>.Draw per frame; returns true (clearing the flag)
    /// exactly once per detected activation.
    /// </summary>
    internal bool ConsumePendingChatActivation()
    {
        if (!pendingChatActivation)
        {
            return false;
        }

        pendingChatActivation = false;
        return true;
    }

    /// <summary>
    /// "Reply in Selected Chat Mode" - switches the compose channel to wherever the clicked
    /// message came from (including the exact linkshell/cross-world linkshell number) rather
    /// than whispering.
    /// </summary>
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
        chatWindow.IsOpen = true;
    }

    /// <summary>
    /// Whether a flag is currently placed - used by the manual "insert current flag" compose menu
    /// entry, for when <see cref="ChatActivationWatcher"/>'s automatic capture missed its moment
    /// (e.g. the flag was placed before the compose box was ever opened this session).
    /// </summary>
    internal bool HasCurrentMapFlag() => MapFlagWatcher.HasFlag();

    /// <summary>
    /// Texture + UV rect for one status icon (Mentor crown, Sprout/New Adventurer, Returner,
    /// Role-Playing, ...), sized to fit a line of text at <c>targetHeight</c> while keeping the
    /// icon's own aspect ratio. Null if the icon is None, isn't in the game's own icon atlas, or
    /// that atlas texture failed to load.
    /// </summary>
    internal readonly record struct StatusIconInfo(Dalamud.Bindings.ImGui.ImTextureID TextureHandle, Vector2 Uv0, Vector2 Uv1, Vector2 Size);

    /// <summary>
    /// Resolves a <see cref="BitmapFontIcon"/> (as found in an IconPayload embedded in a message's
    /// sender field - see ChatLogEntry.SenderPayloads) to its drawable rectangle within the game's
    /// shared icon atlas ("common/font/fonticon_ps5.tex", indexed by "common/font/gfdata.gfd" -
    /// see GfdIconAtlas). The UV math (the "+170" vertical offset and "*2" scale) is copied
    /// verbatim from Chat2's own ChunkHandler.DrawIcon, which has this atlas's specific layout
    /// already figured out - not something to re-derive from scratch.
    /// </summary>
    internal StatusIconInfo? GetStatusIconInfo(BitmapFontIcon icon, float targetHeight)
    {
        if (icon == BitmapFontIcon.None || !GfdIconAtlas.TryGetEntry(dataManager, (uint)icon, out var gfdEntry))
        {
            return null;
        }

        var texture = TextureProvider.GetFromGame("common/font/fonticon_ps5.tex").GetWrapOrDefault();
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

    /// <summary>
    /// Set by <see cref="OnChatLinkHovered"/> while drawing this frame's windows, reconciled
    /// against <see cref="shownItemTooltipId"/> right after in <see cref="DrawUi"/> - the native
    /// ItemDetail addon has to be explicitly opened/closed, there's no "just show while hovered"
    /// convenience for it, so this is what turns "hovered or not this frame" into open/keep/close.
    /// </summary>
    private ItemPayload? hoveredItemThisFrame;

    private uint? shownItemTooltipId;

    /// <summary>
    /// Called by <see cref="ColoredTextRenderer.DrawRich"/>'s hover callback for any inline chat
    /// link currently under the mouse - only item links do anything here.
    /// </summary>
    internal void OnChatLinkHovered(Payload payload)
    {
        if (payload is ItemPayload item)
        {
            hoveredItemThisFrame = item;
        }
    }

    /// <summary>
    /// Opens/keeps/closes the game's own native item tooltip window (the same one vanilla shows
    /// for an item link in chat) based on whether an item link was hovered this frame. Verified
    /// against Chat2's real GameFunctions.OpenItemTooltip/CloseItemTooltip - their version pokes
    /// two raw, unnamed byte offsets into AgentItemDetail with a "TODO: revert whenever CS is
    /// merged" comment; the FFXIVClientStructs version this plugin references already has those
    /// merged as the named Flag2/Flag3 fields below, so this needed no raw offsets at all.
    /// </summary>
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

            // Best-effort attempt at fixing the tooltip rendering behind our own (ImGui) chat
            // window: Chat2's own code (which this whole method is otherwise a verified port of)
            // doesn't need this, presumably because in their case nothing else was contending for
            // front-most native z-order at that moment. Focus() is the closest documented
            // "bring this addon to the front" action AtkUnitBase exposes - not verified against a
            // live game, since the underlying cause (interaction between native addon depth
            // layering and Dalamud's own ImGui compositing order) isn't something inspectable
            // through static analysis alone.
            addon->Focus();
        }
        else
        {
            var raptureAtkModule = RaptureAtkModule.Instance();
            var addon = raptureAtkModule != null ? raptureAtkModule->RaptureAtkUnitManager.GetAddonByName("ItemDetail") : null;
            // Hide the addon first - matches Chat2's own ordering, which notes this avoids the
            // "addon close" sound effect firing.
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

    /// <summary>
    /// Dispatches a click on an inline chat link (see ColoredTextRenderer.DrawRich) to whatever
    /// that link type actually does - mirrors ChatTwo's Ui/Handler/PayloadHandler.cs
    /// LeftClickPayload/ClickLinkPayload, scoped to the link types this plugin renders.
    /// </summary>
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

    /// <summary>
    /// Runs a Dalamud link registered by ANOTHER plugin (e.g. Lifestream's teleport links) -
    /// mirrors ChatTwo's PayloadHandler.ClickLinkPayload exactly (verified against their real
    /// source): the payloads between the link and its RawPayload.LinkTerminator are re-packaged
    /// into a fresh SeString and handed to whatever handler that plugin registered via
    /// IChatGui.AddChatLinkHandler - this plugin has no idea what the link actually does, only
    /// how to find and invoke its owner.
    /// </summary>
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
            // Matches ChatTwo's own comment: running this instantly instead of via RunOnTick can
            // freeze the game, for whatever reason.
            framework.RunOnTick(() => handler.Invoke(link.CommandId, new SeString(linkPayloads)));
        }
        catch (Exception ex)
        {
            log.Error(ex, "[CelinesChat] Fehler beim Ausfuehren eines Dalamud-Link-Handlers.");
        }
    }

    /// <summary>
    /// Opens (creating it if it doesn't exist yet) a whisper tab for the given target and
    /// switches the chat window to it - used by the "Whisper" context menu entry and Ctrl+click
    /// on a player name in the log.
    /// </summary>
    internal void OpenWhisperTab(string target)
    {
        var state = GetCharacterState();
        RememberWhisperTarget(state, target);
        state.LastWhisperTarget = target;
        state.LastChannel = ChatChannel.Whisper;
        PendingWhisperTarget = target;
        SaveConfiguration();
        chatWindow.IsOpen = true;
    }

    /// <summary>
    /// Sends the user's own raw text verbatim through the game's chatbox parser (same
    /// ChatSender.Send/ProcessChatBoxEntry path SendChunks uses), instead of wrapping it in our
    /// own channel prefix and message splitter. Lets native slash commands the user types
    /// directly - /r, /yes, /tell Name text, etc. - work exactly like typing them into the
    /// game's own chat box, rather than being mangled into e.g. "/say /r hello".
    /// </summary>
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

    internal void RememberWhisperTarget(CharacterState state, string target)
    {
        state.RecentWhisperTargets.Remove(target);
        state.RecentWhisperTargets.Insert(0, target);
        while (state.RecentWhisperTargets.Count > MaxRecentWhisperTargets)
        {
            state.RecentWhisperTargets.RemoveAt(state.RecentWhisperTargets.Count - 1);
        }

        SaveConfiguration();
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

    /// <summary>
    /// Set by <see cref="ChatWindow"/> every frame, right after drawing its compose textbox, to
    /// exactly whatever <c>ImGui.IsItemFocused()</c> reported for it that frame - a precise,
    /// widget-level "is our own box the thing with keyboard focus right now" signal, as opposed
    /// to the coarser (and, as discovered below, one-frame-stale-in-the-wrong-direction)
    /// Window.IsFocused.
    /// </summary>
    internal bool IsChatInputActive { private get; set; }

    /// <summary>
    /// Doing the Enter-activation detection here (Framework.Update, which runs as part of the
    /// game's own frame tick) instead of inside ChatWindow.Draw (which runs later, during
    /// Dalamud's UI render pass) is what actually makes consuming the keypress
    /// (<c>keyState[VirtualKey.RETURN] = false</c>) work. Doing it at Draw-time consumed the key
    /// too late - the game had already read the original press and started activating its own
    /// (now-hidden, since SetNativeChatVisible keeps it invisible) chat input, which then ate a
    /// *second* Enter press just to close itself back out before our own activation could ever
    /// see IsTextInputActive() go false. Chat2's KeybindManager relies on this exact same timing.
    ///
    /// The same problem comes back for every Enter press *after* the first one, though, unless
    /// this keeps suppressing it for as long as our own box is the one handling it: once
    /// activated, Window.IsFocused stays true and this method used to just step aside entirely,
    /// letting the game's own (hidden) chat input see that same Enter too and activate right
    /// alongside our box - which then silently ate every keystroke meant for movement until it
    /// was closed again, and whatever landed in it before that got sent to Say on the next Enter.
    /// Suppressing whenever IsChatInputActive is true (not just while activating) keeps the game
    /// from ever seeing it while our box owns it, without touching Dear ImGui's own separate
    /// input queue - that's still how the box itself keeps receiving Enter to type/send normally.
    /// </summary>
    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        SetNativeChatVisible(false);

        // Dalamud's own ImGui overlay always draws on top of native game UI - there's no
        // supported way to make this window render behind, say, the Friend List while it's open.
        // Auto-hiding it instead is the practical equivalent: gets out of the way of whatever
        // native menu the player is actually using, without permanently losing the chat (Enter,
        // handled below, brings it straight back to type - even while that menu stays open).
        // AtkStage.GetFocus() is the game's own concept of "some native UI element currently has
        // interaction focus" (distinct from merely being visible, e.g. the HUD/hotbars don't set
        // it) - the best available general signal for "a menu is actively in use" rather than
        // hardcoding specific addon names like "FriendList". This runs before the Enter handling
        // below on purpose: if Enter is also pressed this same frame, that reopens the window
        // afterwards, overriding the tentative close from this check.
        if (!IsChatInputActive && chatWindow.IsOpen)
        {
            var atkStage = AtkStage.Instance();
            if (atkStage != null && atkStage->GetFocus() != null)
            {
                chatWindow.IsOpen = false;
            }
        }

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
        chatWindow.IsOpen = true;
        pendingChatActivation = true;
    }

    /// <summary>
    /// Hides (or restores) the game's own chat log while this plugin's own chat replaces it -
    /// same technique Chat2 uses: reach the "ChatLog"/"ChatLogPanel_0..3" native addons directly
    /// and flip their AtkUnitBase.IsVisible flag. Silently does nothing for any addon that isn't
    /// loaded/ready yet (e.g. during login) rather than throwing - it'll simply get hidden on a
    /// later frame once it exists.
    /// </summary>
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

    private void OnOpenCommand(string command, string args) => chatWindow.Toggle();

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

    private void ToggleChatWindow() => chatWindow.Toggle();

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

        ChatLog.Dispose();
        chatActivationWatcher.Dispose();
        chatFontManager.Dispose();
        sender.Dispose();
    }
}
