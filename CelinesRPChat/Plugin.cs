using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CelinesRPChat.Services;
using CelinesRPChat.Windows;

namespace CelinesRPChat;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/celinesrpchat";
    private const string LogCommandName = "/celinesrpchatlog";
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
    private readonly MessageQueueSender sender;
    private readonly WindowSystem windowSystem = new("CelinesRPChat");
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
    /// Set by <see cref="SetWhisperTarget"/> (e.g. clicking a sender's name in the log) so
    /// <see cref="ChatWindow"/> can sync its active whisper tab on the next frame.
    /// </summary>
    internal string? PendingWhisperTarget { get; set; }

    /// <summary>
    /// The currently split draft chunks, updated by <see cref="ChatWindow"/> every frame so
    /// <see cref="PreviewWindow"/> can display them without needing its own copy of the draft.
    /// </summary>
    internal List<string> CurrentPreviewChunks { get; set; } = new();

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IChatGui chatGui,
        IPartyList partyList,
        ITargetManager targetManager,
        IPlayerState playerState,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.partyList = partyList;
        this.targetManager = targetManager;
        this.playerState = playerState;

        Loc.SetLanguage(this.pluginInterface.UiLanguage);

        Configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        sender = new MessageQueueSender(framework, log);
        ChatLog = new ChatLogService(chatGui, Configuration, this.pluginInterface, log);

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
    }

    public void SaveConfiguration() => pluginInterface.SavePluginConfig(Configuration);

    public void ToggleSettingsWindow() => settingsWindow.Toggle();

    public void TogglePreviewWindow() => previewWindow.Toggle();

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
        return target is { ObjectKind: ObjectKind.Pc } ? target.Name.TextValue : null;
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

    internal void SetWhisperTarget(string target)
    {
        var state = GetCharacterState();
        state.LastWhisperTarget = target;
        state.LastChannel = ChatChannel.Whisper;
        PendingWhisperTarget = target;
        SaveConfiguration();
        chatWindow.IsOpen = true;
    }

    public void SendChunks(List<string> chunks, string originalDraft)
    {
        var state = GetCharacterState();
        var prefix = BuildChannelPrefix(state);
        var commands = new List<string>(chunks.Count);
        foreach (var chunk in chunks)
        {
            commands.Add(prefix + chunk);
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

    private void RememberWhisperTarget(CharacterState state, string target)
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
        _ => "/say ",
    };

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

    private void DrawUi() => windowSystem.Draw();

    private void ToggleChatWindow() => chatWindow.Toggle();

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleChatWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        pluginInterface.LanguageChanged -= OnLanguageChanged;

        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(LogCommandName);

        ChatLog.Dispose();
        sender.Dispose();
    }
}
