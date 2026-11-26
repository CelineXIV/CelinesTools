using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal sealed class ChatWindow : Window
{
    private const string GenericDraftKey = "*generic*";
    private const string NoviceNetworkDraftKey = "*nn*";
    private const string TabDraftKeyPrefix = "*tab*";
    private const float SplitterHeight = 6f;
    private const float MaxComposeAreaHeight = 400f;

    private const float EmbeddedPreviewHeight = 160f;

    private const float FooterBottomPaddingPx = 6f;

    private const float BottomToleranceForNewMessagePx = 12f;
    private static readonly Vector4 SearchHighlightColor = new(1f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private readonly Plugin plugin;

    private string inputText = string.Empty;
    private string currentDraftKey = string.Empty;
    private string validationMessage = string.Empty;
    private string newSnippetName = string.Empty;
    private string snippetSearchFilter = string.Empty;
    private string channelPickerWhisperInput = string.Empty;
    private string activeWhisperTabTarget = string.Empty;

    private ulong lastRestoredCharacterKey = ulong.MaxValue;
    private bool forceScrollToBottomOnNextViewChange;

    private bool noviceNetworkTabActive;

    private int noviceNetworkUnread;
    private string? pendingTabForceSelect;

    private Guid? pendingFixedTabForceSelect;
    private int selectionStart;
    private int selectionEnd;
    private int cursorPos;

    private int? pendingCursorCharIndex;
    private int? stashedCursorCharIndex;
    private int pinToBottomFramesRemaining;
    private string cachedInput = string.Empty;
    private int cachedMaxLength = -1;
    private int cachedPrefixBytes = -1;
    private List<string> cachedChunks = new();
    private List<MappedChunk> cachedMappedChunks = new();

    private int historyBrowseIndex = -1;

    private bool pendingSnippetExpand;

    private readonly Dictionary<Guid, int> fixedTabMentionCounts = new();

    private readonly PlayerCard playerCard;

    internal void OpenPlayerCard(ChatLogEntry entry) => playerCard.Open(entry);

    private string newSnippetShortcut = string.Empty;

    private ChatLogService.SearchProgress? fullTextSearchProgress;
    private System.Threading.CancellationTokenSource? fullTextSearchCancel;
    private string historyBrowseOriginalDraft = string.Empty;
    private bool splitterDragging;

    private float measuredFooterHeight = 60f;
    private float measuredEmbeddedPreviewHeight;
    private bool pendingSendFromEnter;
    private bool pendingFocusInput;
    private bool composeBoxActive;

    private const long DraftSaveDebounceMs = 800;
    private bool draftSavePending;
    private long draftSaveDueTick;
    private bool pendingAutoTranslatePopup;
    private string autoTranslateSearch = string.Empty;
    private uint? autoTranslateBrowseGroup;

    private Guid activeTabId;

    private long lastSeenSequence;

    private readonly Dictionary<string, long> lastSeenSequenceByView = new();
    private long initialSeenSequence;
    private long nextPreloadSequence = -1;

    private bool lastKnownFontAvailable = true;

    private readonly Dictionary<long, float> entryHeightCache = new();
    private readonly HashSet<long> provisionalHeightSequences = new();

    private readonly Dictionary<long, byte> entryVariantCache = new();

    private readonly List<ChatLogEntry> visibleEntriesBuffer = new();

    private string? noteEditorKey;
    private string noteEditorText = string.Empty;
    private bool openNoteEditor;

    private string? pendingScrollBookmarkKey;

    private long? pendingScrollSequence;
    private int pendingScrollFramesLeft;
    private bool awaySummaryExpanded;

    internal Guid ActiveTabId => activeTabId;

    internal string ActiveWhisperTabTarget => activeWhisperTabTarget;

    internal event Action? ViewChanged;

    private float tabStripScrollX;

    private float tabStripScrollRawTarget;
    private float tabStripContentWidth;

    private readonly List<float> tabStripTabStartOffsets = new();

    private readonly List<ChatLogEntry> viewMatchingLiveEntriesBuffer = new();

    private readonly List<ChatLogEntry> liveEntriesBuffer = new();

    private List<string> historyPopupDates = new();
    private List<string> historyPopupWhisperTargets = new();

    private readonly List<long> previousViewEntrySequences = new();
    private bool unreadTabScrolledLeft;
    private bool unreadTabScrolledRight;
    private List<ChatLogEntry>? loadedHistoryEntries;
    private string? loadedHistoryLabel;
    private string logSearchFilter = string.Empty;
    private string historySearchFilter = string.Empty;
    private string lastViewKey = string.Empty;
    private readonly Dictionary<string, float> viewScrollPositions = new();
    private readonly Dictionary<string, int> whisperUnreadCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> fixedTabUnreadCounts = new();

    private readonly HashSet<string> whisperHistoryShownThisSession = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<ChatLogEntry>> whisperPreloadedHistory = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Task<List<ChatLogEntry>>> pendingWhisperHistoryLoads = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, long> whisperCloseCutoff = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Guid, long> fixedTabClearCutoff = new();

    private long? noviceNetworkClearCutoff;

    private bool rightClickedOnName;

    private ImGuiWindowFlags baseFlags;

    private int c4tColorsPushed;
    private float currentOpacity = 1f;

    private long? newMessagesDividerSequence;

    private readonly Dictionary<string, long> bottomSeenSequenceByView = new();
    private int newBelowCountLastFrame;
    private bool showJumpButtonLastFrame;

    private readonly List<ChatLogEntry> currentViewEntries = new();
    private string currentViewLabel = string.Empty;

    private string fullTextSearchInput = string.Empty;
    private Task<List<ChatLogEntry>>? fullTextSearchTask;
    private string fullTextSearchTerm = string.Empty;
    private string? exportMessage;

    private int seenLayoutVersion = -1;

    public ChatWindow(Plugin plugin) : base(WindowTitles.Chat)
    {
        this.plugin = plugin;
        playerCard = new PlayerCard(plugin, OpenWhisperTabWithHistory, e =>
        {
            noteEditorKey = e.SenderKey;
            noteEditorText = plugin.GetPlayerNote(e) ?? string.Empty;
            openNoteEditor = true;
        });
        Position = new Vector2(18, 868);
        PositionCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(658, 511);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar;

        baseFlags = Flags;

        RespectCloseHotkey = false;

        InhibitAtkCollision = false;

        IsOpen = true;

        var existingEntries = plugin.ChatLog.Snapshot();
        if (existingEntries.Count > 0)
        {
            lastSeenSequence = existingEntries[^1].Sequence;

            initialSeenSequence = lastSeenSequence;
        }
    }

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
        currentOpacity = opacity;
        c4tColorsPushed = C4tTheme.PushColors(plugin.Configuration);

        var flags = baseFlags;
        if (plugin.Configuration.ChatWindowLockPosition)
        {
            flags |= ImGuiWindowFlags.NoMove;
        }

        if (plugin.Configuration.ChatWindowLockSize)
        {
            flags |= ImGuiWindowFlags.NoResize;
        }

        Flags = flags;
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(c4tColorsPushed);
        c4tColorsPushed = 0;
    }

    public override void Draw()
    {
        if (!plugin.Configuration.ShowPerformanceOverlay)
        {
            DrawCore();
            return;
        }

        var start = PerfStats.Start();
        try
        {
            DrawCore();
        }
        finally
        {
            plugin.Perf.ChatWindow.Add(PerfStats.ElapsedMs(start));
            plugin.Perf.VisibleEntries = visibleEntriesBuffer.Count;
            plugin.Perf.CachedHeights = entryHeightCache.Count;
        }
    }

    internal void NotifyViewChanged() => ViewChanged?.Invoke();

    internal void RequestComposeFocus() => pendingFocusInput = true;

    private void DrawCore()
    {
        C4tTheme.DrawDecorations(plugin.Configuration, currentOpacity);

        if (plugin.LayoutVersion != seenLayoutVersion)
        {
            seenLayoutVersion = plugin.LayoutVersion;
            entryHeightCache.Clear();
            entryVariantCache.Clear();
            provisionalHeightSequences.Clear();
        }

        ApplyCompletedWhisperHistoryLoads();
        ApplyCompletedFullTextSearch();

        var justActivatedThisFrame = plugin.ConsumePendingChatActivation();
        if (justActivatedThisFrame)
        {
            pendingFocusInput = true;

            if (plugin.ConsumePendingPreviewCursorTarget() is { } previewCursorTarget)
            {
                stashedCursorCharIndex = previewCursorTarget;
            }
        }

        using var chatFontScope = plugin.PushChatFont();

        var fontAvailableNow = plugin.IsChatFontAvailable;
        if (fontAvailableNow != lastKnownFontAvailable)
        {
            lastKnownFontAvailable = fontAvailableNow;

            if (fontAvailableNow && provisionalHeightSequences.Count > 0)
            {
                foreach (var seq in provisionalHeightSequences)
                {
                    entryHeightCache.Remove(seq);
                }

                provisionalHeightSequences.Clear();
            }
        }

        var config = plugin.Configuration;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f)
            .Push(ImGuiStyleVar.FrameRounding, 4f)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f))
            .Push(ImGuiStyleVar.ChildRounding, 6f)
            .Push(ImGuiStyleVar.PopupRounding, 6f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 6f)
            .Push(ImGuiStyleVar.GrabRounding, 4f)
            .Push(ImGuiStyleVar.TabRounding, 4f);
        var state = plugin.GetCharacterState();

        if (plugin.CurrentCharacterKey != lastRestoredCharacterKey)
        {
            lastRestoredCharacterKey = plugin.CurrentCharacterKey;
            RestoreLastActiveView(config, state);
        }

        if (plugin.PendingWhisperTarget is { } pendingTarget)
        {
            EnterWhisperView(config, state, pendingTarget);
            plugin.PendingWhisperTarget = null;
        }

        SyncDraft(state);

        if (config.ReplyHotkeyEnabled
            && ImGui.GetIO().KeyCtrl
            && ImGui.IsKeyPressed(ImGuiKey.R, false)
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (plugin.ReplyToLastWhisper())
            {
                pendingFocusInput = true;
            }
        }

        if (plugin.PendingChatPrefillText is { } prefillText)
        {
            plugin.PendingChatPrefillText = null;
            InsertChatPrefillText(state, prefillText);
        }

        if (!string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            state.LastChannel = ChatChannel.Whisper;
            state.LastWhisperTarget = activeWhisperTabTarget;

            whisperUnreadCounts.Remove(activeWhisperTabTarget);
        }
        else if (!noviceNetworkTabActive)
        {
            fixedTabUnreadCounts.Remove(activeTabId);
            fixedTabMentionCounts.Remove(activeTabId);
        }

        if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            DrawWhisperTarget(state);
        }

        DrawLogTabBar(config, state);

        if (config.ChatToolbarExpanded)
        {
            DrawLogToolbar(config, state);
        }

        var mappedChunks = GetMappedChunks(inputText, config.MaxMessageLength, plugin.GetChannelPrefixBytes(state));
        var chunks = cachedChunks;
        plugin.CurrentPreviewChunks = chunks;
        plugin.CurrentPreviewMappedChunks = mappedChunks;

        var minInputBoxHeight = ImGui.GetFrameHeight();

        var previewReserve = config.PreviewPosition == PreviewPosition.Inside ? measuredEmbeddedPreviewHeight : 0f;
        var minComposeAreaHeight = minInputBoxHeight + measuredFooterHeight + previewReserve;

        var composeAreaHeight = Math.Clamp(config.ComposeAreaHeight, minComposeAreaHeight, MathF.Max(minComposeAreaHeight, MaxComposeAreaHeight));
        var inputBoxHeight = Math.Max(minInputBoxHeight, composeAreaHeight - measuredFooterHeight - previewReserve);

        DrawAwaySummary(config);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, C4tTheme.LogBackground(config)))
        using (ImRaii.Child("##chatLogScroll", new Vector2(-1, -(composeAreaHeight + SplitterHeight)), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            ImGui.SetScrollX(0f);
            C4tTheme.DrawWatermark(plugin);

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, config.LineSpacing), config.LineSpacing >= 0f))
            {
                DrawChatLogEntries(config, state, composeAreaHeight, inputBoxHeight);
            }
        }

        DrawPlayerNoteEditor();
        playerCard.Draw();

        DrawSplitter(config, minComposeAreaHeight);

        if (config.PreviewPosition == PreviewPosition.Inside && plugin.IsPreviewVisible)
        {
            var previewTop = ImGui.GetCursorScreenPos().Y;
            DrawEmbeddedPreview(config, mappedChunks);
            measuredEmbeddedPreviewHeight = MathF.Ceiling(Math.Max(0f, ImGui.GetCursorScreenPos().Y - previewTop));
        }
        else
        {
            measuredEmbeddedPreviewHeight = 0f;
        }

        if (composeBoxActive && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGuiP.ClearActiveID();
        }

        DrawChannelPickerButton(config, state);
        ImGui.SameLine();

        if (pendingFocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            pendingFocusInput = false;
        }

        var sendingMultiMessage = plugin.IsSending && plugin.SendTotal > 1;

        ImGui.SetNextItemWidth(-1);
        const ImGuiInputTextFlags composeFlags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackCharFilter | ImGuiInputTextFlags.AllowTabInput;
        var commandTint = GetCommandTint(config);
        float inputBoxBottom;
        using (ImRaii.Disabled(sendingMultiMessage))
        using (ImRaii.PushColor(ImGuiCol.Text, commandTint))
        {
            if (ImGui.InputTextMultiline("##composeText", ref inputText, 4000, new Vector2(-1, inputBoxHeight), composeFlags, TrackTextState))
            {
                state.Drafts[currentDraftKey] = inputText;
                draftSavePending = true;
                draftSaveDueTick = Environment.TickCount64 + DraftSaveDebounceMs;
            }

            inputBoxBottom = ImGui.GetItemRectMax().Y;
        }

        if (draftSavePending && (ImGui.IsItemDeactivated() || Environment.TickCount64 >= draftSaveDueTick))
        {
            FlushPendingDraftSave();
        }

        composeBoxActive = ImGui.IsItemActive();
        plugin.IsChatInputActive = composeBoxActive;

        if (composeBoxActive && stashedCursorCharIndex is { } stashedIndex)
        {
            stashedCursorCharIndex = null;
            pendingCursorCharIndex = stashedIndex;
        }

        if (pendingAutoTranslatePopup)
        {
            pendingAutoTranslatePopup = false;
            plugin.EnsureAutoTranslateLoaded();

            autoTranslateSearch = GetWordAtCursor();
            autoTranslateBrowseGroup = null;
            ImGui.OpenPopup("##autoTranslatePopup");
        }

        DrawAutoTranslatePopup(state);

        if (pendingSendFromEnter)
        {
            pendingSendFromEnter = false;

            if (!justActivatedThisFrame)
            {
                TrySend(state);
            }
        }

        HandleSendHotkey(state, config);

        DrawComposeStatusRow(config, state, chunks);

        if (state.LastChannel == ChatChannel.Whisper && !string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            DrawActiveWhisperIndicator(config, activeWhisperTabTarget, chunks.Count);
        }

        if (!string.IsNullOrEmpty(validationMessage))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), validationMessage);
        }

        measuredFooterHeight = MathF.Ceiling(Math.Max(0f, ImGui.GetCursorScreenPos().Y - inputBoxBottom) + FooterBottomPaddingPx);

        DrawSendingOverlay();
    }

    private void DrawSplitter(Configuration config, float minComposeAreaHeight)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.06f))
                   .Push(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.16f))
                   .Push(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.26f)))
        {
            ImGui.Button("##chatSplitter", new Vector2(-1, SplitterHeight));
        }

        if (ImGui.IsItemActive())
        {
            splitterDragging = true;
            config.ComposeAreaHeight = Math.Clamp(config.ComposeAreaHeight - ImGui.GetIO().MouseDelta.Y, minComposeAreaHeight, MathF.Max(minComposeAreaHeight, MaxComposeAreaHeight));
        }
        else if (splitterDragging)
        {
            splitterDragging = false;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }
    }

    private void DrawChannelPickerButton(Configuration config, CharacterState state)
    {
        if (DrawToolbarIconButton(FontAwesomeIcon.Comment))
        {
            ImGui.OpenPopup("##channelPicker");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(CurrentChannelLabel(state));
        }

        using var popup = ImRaii.Popup("##channelPicker");
        if (popup.Success)
        {
            DrawWhisperChannelMenu(state);
            DrawChannelOption(config, state, ChatChannel.Say, Loc.T("Channel.Say"));
            DrawChannelOption(config, state, ChatChannel.Party, Loc.T("Channel.Party"));
            DrawChannelOption(config, state, ChatChannel.Alliance, Loc.T("Channel.Alliance"));
            DrawChannelOption(config, state, ChatChannel.Yell, Loc.T("Channel.Yell"));
            DrawChannelOption(config, state, ChatChannel.Shout, Loc.T("Channel.Shout"));
            DrawChannelOption(config, state, ChatChannel.FreeCompany, Loc.T("Channel.FreeCompany"));
            DrawChannelOption(config, state, ChatChannel.PvpTeam, Loc.T("Channel.PvpTeam"));
            DrawChannelOption(config, state, ChatChannel.NoviceNetwork, Loc.T("Channel.NoviceNetwork"));

            var cwls = NativeChannels.GetExistingCrossWorldLinkshells();
            if (cwls.Count > 0)
            {
                ImGui.Separator();
                foreach (var (number, name) in cwls)
                {
                    DrawNumberedChannelOption(config, state, ChatChannel.CrossWorldLinkshell, number, $"{Loc.T("Channel.CrossWorldLinkshell")} [{number}]: {name}");
                }
            }

            var linkshells = NativeChannels.GetExistingLinkshells();
            if (linkshells.Count > 0)
            {
                ImGui.Separator();
                foreach (var (number, name) in linkshells)
                {
                    DrawNumberedChannelOption(config, state, ChatChannel.Linkshell, number, $"{Loc.T("Channel.Linkshell")} [{number}]: {name}");
                }
            }
        }
    }

    private void DrawWhisperChannelMenu(CharacterState state)
    {
        using var menu = ImRaii.Menu(Loc.T("Channel.Whisper"));
        if (!menu.Success)
        {
            return;
        }

        ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##channelPickerWhisperTarget", Loc.T("Whisper.TargetHint"), ref channelPickerWhisperInput, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            TryOpenWhisperFromChannelPicker();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Whisper.Open")))
        {
            TryOpenWhisperFromChannelPicker();
        }

        var partyMembers = plugin.GetPartyMemberNames();
        if (partyMembers.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled(Loc.T("Whisper.SuggestionsParty"));
            foreach (var name in partyMembers)
            {
                if (ImGui.Selectable(name + "##channelPickerWhisperParty"))
                {
                    plugin.OpenWhisperTab(name);
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        var recents = state.RecentWhisperTargets;
        if (recents.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled(Loc.T("Whisper.SuggestionsRecent"));
            foreach (var name in recents)
            {
                if (ImGui.Selectable(name + "##channelPickerWhisperRecent"))
                {
                    plugin.OpenWhisperTab(name);
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    private void TryOpenWhisperFromChannelPicker()
    {
        var target = channelPickerWhisperInput.Trim();
        if (target.Length == 0)
        {
            return;
        }

        plugin.OpenWhisperTab(target);
        channelPickerWhisperInput = string.Empty;
        ImGui.CloseCurrentPopup();
    }

    private void DrawChannelOption(Configuration config, CharacterState state, ChatChannel channel, string label)
    {
        if (ImGui.Selectable(label + "##chan" + channel, state.LastChannel == channel))
        {
            SelectChannel(config, state, channel);
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawNumberedChannelOption(Configuration config, CharacterState state, ChatChannel channel, int number, string label)
    {
        var isSelected = state.LastChannel == channel
            && (channel == ChatChannel.Linkshell ? state.LinkshellNumber : state.CrossWorldLinkshellNumber) == number;
        if (ImGui.Selectable(label + "##chan" + channel + number, isSelected))
        {
            if (channel == ChatChannel.Linkshell)
            {
                state.LinkshellNumber = number;
            }
            else
            {
                state.CrossWorldLinkshellNumber = number;
            }

            SelectChannel(config, state, channel);
            ImGui.CloseCurrentPopup();
        }
    }

    internal void SelectChannel(Configuration config, CharacterState state, ChatChannel channel)
    {
        if (channel == ChatChannel.Whisper && !string.IsNullOrEmpty(state.LastWhisperTarget))
        {
            EnterWhisperView(config, state, state.LastWhisperTarget);
        }
        else
        {
            state.LastChannel = channel;
        }

        plugin.SaveConfiguration();
        ViewChanged?.Invoke();
    }

    private bool DrawToolbarIconButton(FontAwesomeIcon icon, Vector4? colorOverride = null)
    {
        var config = plugin.Configuration;
        if (config.ToolbarIconPack == ToolbarIconPack.Classic)
        {
            if (colorOverride is { } classicColor)
            {
                using var color = ImRaii.PushColor(ImGuiCol.Text, classicColor);
                return ImGuiComponents.IconButton(icon);
            }

            return ImGuiComponents.IconButton(icon);
        }

        var iconColor = colorOverride
            ?? (config.ToolbarUseCustomIconColor ? config.ToolbarIconColor : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);

        return GlassIconButton.Draw(icon, config.ToolbarIconPack, config.ToolbarIconBackgroundColor, iconColor);
    }

    private void DrawLogToolbar(Configuration config, CharacterState state)
    {
        var hasActiveSearch = logSearchFilter.Length > 0;
        DrawToolbarIconButton(FontAwesomeIcon.Search, hasActiveSearch ? SearchHighlightColor : null);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.SearchButtonTooltip"));
        }

        using (var searchPopup = ImRaii.ContextPopupItem("##logSearchPopup"))
        {
            if (searchPopup.Success)
            {
                ImGui.SetNextItemWidth(200);
                ImGui.InputTextWithHint("##chatLogSearch", Loc.T("ChatLog.SearchHint"), ref logSearchFilter, 200);
                DrawFullTextSearchAndExport();
            }
        }

        var showingWhisperForHistory = !string.IsNullOrEmpty(activeWhisperTabTarget);

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.CalendarAlt))
        {
            if (showingWhisperForHistory)
            {
                RefreshWhisperHistoryForTarget(activeWhisperTabTarget);
            }
            else
            {
                historyPopupDates = plugin.ChatLog.GetAvailableLogDates();
                historyPopupWhisperTargets = plugin.ChatLog.GetWhisperLogTargets();
                ImGui.OpenPopup("##historyDatesPopup");
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(showingWhisperForHistory ? Loc.T("ChatLog.LoadWhisperHistory") : Loc.T("ChatLog.LoadHistory"));
        }

        DrawHistoryDatesPopup();

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.Bookmark))
        {
            plugin.ToggleBookmarksWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Bookmarks.Title"));
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.FolderOpen))
        {
            try
            {
                System.IO.Directory.CreateDirectory(plugin.ChatLog.LogsFolderPath);
                Process.Start(new ProcessStartInfo(plugin.ChatLog.LogsFolderPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                plugin.Log.Warning(ex, "[CelinesChat] Konnte den Log-Ordner nicht oeffnen.");
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.OpenFolder"));
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.Eraser))
        {
            ImGui.OpenPopup("##confirmClearTab");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Clear"));
        }

        DrawClearTabConfirmationPopup(config);

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.EyeSlash))
        {
            plugin.HideChatWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.HideChat"));
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.Cog))
        {
            plugin.ToggleSettingsWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Settings"));
        }

        ImGui.SameLine();
        var lockPosition = plugin.Configuration.ChatWindowLockPosition;
        var lockSize = plugin.Configuration.ChatWindowLockSize;
        var anyLocked = lockPosition || lockSize;
        if (DrawToolbarIconButton(anyLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen))
        {
            ImGui.OpenPopup("##windowLockMenu");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.WindowLock"));
        }

        using (var lockPopup = ImRaii.Popup("##windowLockMenu"))
        {
            if (lockPopup.Success)
            {
                if (ImGui.Checkbox(Loc.T("Compose.WindowLockPosition"), ref lockPosition))
                {
                    plugin.Configuration.ChatWindowLockPosition = lockPosition;
                    plugin.SaveConfiguration();
                }

                if (ImGui.Checkbox(Loc.T("Compose.WindowLockSize"), ref lockSize))
                {
                    plugin.Configuration.ChatWindowLockSize = lockSize;
                    plugin.SaveConfiguration();
                }
            }
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.WindowRestore))
        {
            plugin.TogglePreviewWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.OpenPreviewWindow"));
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.Wifi))
        {
            ImGui.OpenPopup("##noviceNetworkMenu");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("NoviceNetwork.MenuTooltip"));
        }

        using (var noviceNetworkPopup = ImRaii.Popup("##noviceNetworkMenu"))
        {
            if (noviceNetworkPopup.Success)
            {
                if (ImGui.Selectable(Loc.T("NoviceNetwork.Join")))
                {
                    NoviceNetworkService.Connect(plugin.Log);
                }

                if (ImGui.Selectable(Loc.T("NoviceNetwork.Leave")))
                {
                    NoviceNetworkService.Disconnect(plugin.Log);
                }
            }
        }

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.Users))
        {
            NoviceNetworkService.OpenNativeMemberList(plugin.Log);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("NoviceNetwork.OpenList"));
        }

        ImGui.SameLine();
        DrawRpActionsMenu(state);

        if (loadedHistoryEntries != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({loadedHistoryLabel})");
            ImGui.SameLine();
            if (DrawToolbarIconButton(FontAwesomeIcon.Bolt))
            {
                loadedHistoryEntries = null;
                loadedHistoryLabel = null;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.BackToLive"));
            }
        }
    }

    private void DrawFullTextSearchAndExport()
    {
        ImGui.Separator();
        ImGui.TextDisabled(Loc.T("ChatLog.FullTextSearchHeader"));
        ImGui.SetNextItemWidth(200);
        var submitted = ImGui.InputTextWithHint("##fullTextSearch", Loc.T("ChatLog.FullTextSearchHint"), ref fullTextSearchInput, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var searching = fullTextSearchTask != null;
        using (ImRaii.Disabled(searching || fullTextSearchInput.Trim().Length < 2))
        {
            if (ImGui.Button(Loc.T("ChatLog.FullTextSearchButton")) || (submitted && !searching && fullTextSearchInput.Trim().Length >= 2))
            {
                StartFullTextSearch(fullTextSearchInput.Trim());
            }
        }

        if (searching)
        {
            var progress = fullTextSearchProgress;
            ImGui.TextDisabled(progress is { FilesTotal: > 0 }
                ? Loc.T("ChatLog.FullTextSearchProgress", progress.FilesDone, progress.FilesTotal, progress.Hits)
                : Loc.T("ChatLog.FullTextSearchRunning"));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Common.Cancel") + "##cancelSearch"))
            {
                fullTextSearchCancel?.Cancel();
            }
        }

        ImGui.Separator();
        if (ImGui.Button(Loc.T("ChatLog.ExportView")))
        {
            exportMessage = ExportCurrentView();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.ExportViewTooltip"));
        }

        if (exportMessage != null)
        {
            ImGui.TextDisabled(exportMessage);
        }
    }

    private void StartFullTextSearch(string term)
    {
        fullTextSearchTerm = term;
        fullTextSearchCancel?.Dispose();
        fullTextSearchCancel = new System.Threading.CancellationTokenSource();
        var progress = new ChatLogService.SearchProgress();
        fullTextSearchProgress = progress;
        var token = fullTextSearchCancel.Token;
        fullTextSearchTask = Task.Run(() => plugin.ChatLog.SearchAllLogs(term, 2000, progress, token));
    }

    private void ApplyCompletedFullTextSearch()
    {
        if (fullTextSearchTask is not { IsCompleted: true } task)
        {
            return;
        }

        fullTextSearchTask = null;
        if (!task.IsCompletedSuccessfully)
        {
            plugin.Log.Warning(task.Exception, "[CelinesChat] Suche in den Log-Dateien fehlgeschlagen.");
            return;
        }

        foreach (var entry in task.Result)
        {
            entry.Sequence = nextPreloadSequence--;
        }

        loadedHistoryEntries = task.Result;
        loadedHistoryLabel = Loc.T("ChatLog.FullTextSearchLabel", fullTextSearchTerm, task.Result.Count);
    }

    private string ExportCurrentView()
    {
        try
        {
            if (currentViewEntries.Count == 0)
            {
                return Loc.T("ChatLog.ExportEmpty");
            }

            var folder = System.IO.Path.Combine(plugin.ChatLog.LogsFolderPath, "Exports");
            System.IO.Directory.CreateDirectory(folder);

            var safeLabel = currentViewLabel;
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                safeLabel = safeLabel.Replace(invalid, '_');
            }

            var path = System.IO.Path.Combine(folder, $"{safeLabel}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            var builder = new StringBuilder();
            foreach (var entry in currentViewEntries)
            {
                var sender = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
                var direction = entry.ChatType == XivChatType.TellOutgoing ? ">> " : string.Empty;
                builder.Append('[').Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .Append(ChannelDisplay.Tag(entry.ChatType)).Append(' ')
                    .Append(direction).Append(sender).Append(": ")
                    .Append(entry.Text.Replace('\r', ' ').Replace('\n', ' '))
                    .AppendLine();
            }

            System.IO.File.WriteAllText(path, builder.ToString());
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return Loc.T("ChatLog.ExportDone", currentViewEntries.Count);
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[CelinesChat] Export fehlgeschlagen.");
            return Loc.T("ChatLog.ExportFailed");
        }
    }

    private void DrawHistoryDatesPopup()
    {
        using var popup = ImRaii.Popup("##historyDatesPopup");
        if (!popup.Success)
        {
            return;
        }

        var dates = historyPopupDates;
        var whisperTargets = historyPopupWhisperTargets;
        if (dates.Count == 0 && whisperTargets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("ChatLog.NoHistoryFiles"));
            return;
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##historyDateSearch", Loc.T("ChatLog.HistorySearchHint"), ref historySearchFilter, 40);

        using var dateList = ImRaii.Child("##historyDateList", new Vector2(220, 260), true);

        var anyDateShown = false;
        foreach (var date in dates)
        {
            if (historySearchFilter.Length > 0 && date.IndexOf(historySearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!anyDateShown)
            {
                ImGui.TextDisabled(Loc.T("ChatLog.HistoryDatesHeader"));
                anyDateShown = true;
            }

            if (ImGui.Selectable(date))
            {
                var dayEntries = plugin.ChatLog.LoadHistoryFile(date);

                foreach (var dayEntry in dayEntries)
                {
                    dayEntry.Sequence = nextPreloadSequence--;
                }

                loadedHistoryEntries = dayEntries;
                loadedHistoryLabel = date;
                ImGui.CloseCurrentPopup();
            }
        }

        var anyWhisperShown = false;
        foreach (var target in whisperTargets)
        {
            if (historySearchFilter.Length > 0 && target.IndexOf(historySearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!anyWhisperShown)
            {
                if (anyDateShown)
                {
                    ImGui.Separator();
                }

                ImGui.TextDisabled(Loc.T("ChatLog.HistoryWhispersHeader"));
                anyWhisperShown = true;
            }

            if (ImGui.Selectable(target + "##histWhisper" + target))
            {
                OpenWhisperTabWithHistory(target);
                ImGui.CloseCurrentPopup();
            }
        }

        if (!anyDateShown && !anyWhisperShown)
        {
            ImGui.TextDisabled(Loc.T("ChatLog.NoHistoryFiles"));
        }
    }

    private void DrawClearTabConfirmationPopup(Configuration config)
    {
        using var modal = ImRaii.PopupModal("##confirmClearTab", ImGuiWindowFlags.AlwaysAutoResize);
        if (!modal.Success)
        {
            return;
        }

        var showingWhisper = !string.IsNullOrEmpty(activeWhisperTabTarget);
        var activeTab = showingWhisper || noviceNetworkTabActive ? null : GetActiveFixedTab(config);
        var tabLabel = showingWhisper
            ? activeWhisperTabTarget
            : noviceNetworkTabActive
                ? Loc.T("Channel.NoviceNetwork")
                : activeTab?.Name ?? string.Empty;

        ImGui.TextUnformatted(Loc.T("ChatLog.ClearTabConfirm", tabLabel));

        if (ImGui.Button(Loc.T("ChatLog.ClearTabConfirmYes")))
        {
            var entries = plugin.ChatLog.Snapshot();
            var cutoff = entries.Count > 0 ? entries[^1].Sequence : 0;

            if (showingWhisper)
            {
                whisperCloseCutoff[activeWhisperTabTarget] = cutoff;
            }
            else if (noviceNetworkTabActive)
            {
                noviceNetworkClearCutoff = cutoff;
            }
            else if (activeTab != null)
            {
                fixedTabClearCutoff[activeTab.Id] = cutoff;
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ChatLog.ClearTabConfirmCancel")))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawChatLogEntries(Configuration config, CharacterState state, float composeAreaHeight, float inputBoxHeight)
    {
        var viewingHistory = loadedHistoryEntries != null;

        plugin.ChatLog.CopyEntriesTo(liveEntriesBuffer);
        var liveEntries = liveEntriesBuffer;

        List<ChatLogEntry>? whisperPreload = null;
        if (!viewingHistory && !string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            whisperPreloadedHistory.TryGetValue(activeWhisperTabTarget, out whisperPreload);
        }

        var viewKey = viewingHistory
            ? $"H:{loadedHistoryLabel}"
            : !string.IsNullOrEmpty(activeWhisperTabTarget)
                ? $"W:{activeWhisperTabTarget}"
                : noviceNetworkTabActive
                    ? "NN"
                    : $"T:{activeTabId}";
        var viewChanged = viewKey != lastViewKey;
        if (viewChanged && lastViewKey.Length > 0)
        {
            viewScrollPositions[lastViewKey] = ImGui.GetScrollY();
        }

        lastViewKey = viewKey;

        if (viewChanged)
        {
            newMessagesDividerSequence = config.ShowNewMessagesDivider && !viewingHistory
                ? lastSeenSequenceByView.GetValueOrDefault(viewKey, initialSeenSequence)
                : null;
            showJumpButtonLastFrame = false;
            newBelowCountLastFrame = 0;
        }

        if (pendingScrollSequence != null && --pendingScrollFramesLeft <= 0)
        {
            pendingScrollSequence = null;
        }

        currentViewEntries.Clear();
        currentViewLabel = viewingHistory
            ? loadedHistoryLabel ?? "history"
            : !string.IsNullOrEmpty(activeWhisperTabTarget)
                ? activeWhisperTabTarget
                : noviceNetworkTabActive ? "NoviceNetwork" : GetActiveFixedTab(config)?.Name ?? "chat";

        var nearBottomBeforeDraw = ImGui.GetScrollMaxY() - ImGui.GetScrollY() <= BottomToleranceForNewMessagePx;
        var mentionTerm = plugin.MentionTerms;

        var availableWidth = ImGui.GetContentRegionAvail().X;

        var showingWhisper = !string.IsNullOrEmpty(activeWhisperTabTarget);
        var showingNoviceNetwork = !showingWhisper && noviceNetworkTabActive;
        var activeTab = showingWhisper || showingNoviceNetwork ? null : GetActiveFixedTab(config);

        var discord = showingWhisper ? config.DiscordModeWhispers : activeTab?.DiscordMode == true;
        var grouping = config.GroupConsecutiveMessages || discord;

        var hasNewEntries = false;
        var hasNewOwnMessageInView = false;

        var viewSeenBefore = lastSeenSequenceByView.GetValueOrDefault(viewKey, initialSeenSequence);
        var viewSeenMax = viewSeenBefore;

        bool MatchesActiveView(ChatLogEntry entry)
        {
            if (plugin.IsFilteredOut(entry))
            {
                return false;
            }

            if (showingWhisper)
            {
                return MatchesWhisperTarget(entry, activeWhisperTabTarget);
            }

            if (showingNoviceNetwork)
            {
                return IsNoviceNetworkMessage(entry.ChatType)
                    && (noviceNetworkClearCutoff is not { } nnClearCutoff || entry.Sequence > nnClearCutoff);
            }

            if (activeTab != null)
            {
                return activeTab.Matches(entry.ChatType)
                    && (!fixedTabClearCutoff.TryGetValue(activeTab.Id, out var clearCutoff) || entry.Sequence > clearCutoff);
            }

            return false;
        }

        if (!viewingHistory)
        {
            for (var i = liveEntries.Count - 1; i >= 0; i--)
            {
                var entry = liveEntries[i];
                if (entry.Sequence <= lastSeenSequence)
                {
                    break;
                }

                TrackUnreadForNewEntry(config, state, entry, showingWhisper, showingNoviceNetwork, activeTab?.Id);
            }
        }

        const int maxLiveEntriesConsidered = 600;
        viewMatchingLiveEntriesBuffer.Clear();
        for (var i = liveEntries.Count - 1; i >= 0 && viewMatchingLiveEntriesBuffer.Count < maxLiveEntriesConsidered; i--)
        {
            var entry = liveEntries[i];
            if (MatchesActiveView(entry))
            {
                viewMatchingLiveEntriesBuffer.Add(entry);
            }
        }

        viewMatchingLiveEntriesBuffer.Reverse();

        var heightLostFromViewShrink = 0f;
        var trackViewShrink = !viewChanged && whisperPreload == null && loadedHistoryEntries == null;
        if (trackViewShrink && previousViewEntrySequences.Count > 0)
        {
            var currentFirstSequence = viewMatchingLiveEntriesBuffer.Count > 0 ? viewMatchingLiveEntriesBuffer[0].Sequence : long.MaxValue;
            foreach (var droppedSequence in previousViewEntrySequences)
            {
                if (droppedSequence >= currentFirstSequence)
                {
                    break;
                }

                if (entryHeightCache.TryGetValue(droppedSequence, out var lostHeight))
                {
                    heightLostFromViewShrink += lostHeight;
                }
                else
                {
                    plugin.Log.Verbose("[CelinesChat][JitterDiag] view={View} droppedSeq={Seq} had NO cached height (undercounts compensation)", viewKey, droppedSequence);
                }
            }

            if (heightLostFromViewShrink > 0.5f)
            {
                plugin.Log.Verbose(
                    "[CelinesChat][JitterDiag] view={View} heightLost={HeightLost:F1} nearBottom={NearBottom} scrollY={ScrollY:F1} scrollMaxY={ScrollMaxY:F1} prevFirstSeq={PrevFirst} newFirstSeq={NewFirst}",
                    viewKey,
                    heightLostFromViewShrink,
                    nearBottomBeforeDraw,
                    ImGui.GetScrollY(),
                    ImGui.GetScrollMaxY(),
                    previousViewEntrySequences[0],
                    viewMatchingLiveEntriesBuffer.Count > 0 ? viewMatchingLiveEntriesBuffer[0].Sequence : -1);
            }
        }

        previousViewEntrySequences.Clear();
        if (trackViewShrink)
        {
            for (var i = 0; i < viewMatchingLiveEntriesBuffer.Count; i++)
            {
                previousViewEntrySequences.Add(viewMatchingLiveEntriesBuffer[i].Sequence);
            }
        }

        IReadOnlyList<ChatLogEntry> entries = loadedHistoryEntries ?? viewMatchingLiveEntriesBuffer;

        var showTimestamps = activeTab?.ShowTimestamps ?? true;
        var bottomSeen = bottomSeenSequenceByView.GetValueOrDefault(viewKey, viewSeenBefore);
        var newBelowCount = 0;
        var dividerDrawn = false;
        DateTime? previousDate = null;

        var jumpButtonClicked = false;
        var jumpButtonMin = Vector2.Zero;
        var jumpButtonMax = Vector2.Zero;
        var jumpButtonLabel = string.Empty;
        if (showJumpButtonLastFrame)
        {
            jumpButtonLabel = newBelowCountLastFrame > 0
                ? Loc.T("ChatLog.JumpToNewMessages", newBelowCountLastFrame)
                : Loc.T("ChatLog.JumpToBottom");
            var labelSize = ImGui.CalcTextSize(jumpButtonLabel);
            var buttonSize = labelSize + new Vector2(24f, 10f);
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            jumpButtonMin = new Vector2(windowPos.X + ((windowSize.X - buttonSize.X) / 2f), windowPos.Y + windowSize.Y - buttonSize.Y - 10f);
            jumpButtonMax = jumpButtonMin + buttonSize;

            var restore = ImGui.GetCursorPos();
            ImGui.SetCursorScreenPos(jumpButtonMin);
            jumpButtonClicked = ImGui.InvisibleButton("##jumpToBottom", buttonSize);
            ImGui.SetCursorPos(restore);
        }

        ChatLogEntry? lastShownEntry = null;

        bool PassesFilters(ChatLogEntry entry)
        {
            if (plugin.IsFilteredOut(entry))
            {
                return false;
            }

            return logSearchFilter.Length == 0
                || entry.Text.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.Sender.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.SearchTimestamp.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void TrackEntry(ChatLogEntry entry)
        {
            currentViewEntries.Add(entry);

            if (!viewingHistory && entry.Sequence > bottomSeen)
            {
                newBelowCount++;
            }

            if (!viewingHistory && entry.Sequence > viewSeenMax)
            {
                viewSeenMax = entry.Sequence;
            }

            if (!viewingHistory && entry.Sequence > viewSeenBefore)
            {
                hasNewEntries = true;

                if (IsSelfAuthored(entry))
                {
                    hasNewOwnMessageInView = true;
                }
            }
        }

        void ProcessEntry(ChatLogEntry entry, ChatLogEntry runFirst, int repeatCount, bool trailingSpacing)
        {
            TrackEntry(entry);

            var markerDrawn = false;

            var date = entry.Timestamp.Date;
            if (config.ShowDateSeparators && (previousDate is { } previous ? previous != date : date != DateTime.Today))
            {
                DrawCenteredMarker(date.ToString(Loc.T("ChatLog.DateSeparatorFormat"), Loc.Culture), ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], availableWidth);
                markerDrawn = true;
            }

            previousDate = date;

            if (!dividerDrawn && newMessagesDividerSequence is { } dividerSeq && entry.Sequence > dividerSeq && entry.Sequence > 0 && !IsSelfAuthored(entry))
            {
                DrawCenteredMarker(Loc.T("ChatLog.NewMessagesDivider"), C4tTheme.Active(config)?.Second ?? new Vector4(1f, 0.4f, 0.4f, 1f), availableWidth);
                dividerDrawn = true;
                markerDrawn = true;
            }

            var continuation = grouping
                && !markerDrawn
                && lastShownEntry != null
                && MessageGrouping.Continues(lastShownEntry, runFirst);
            lastShownEntry = entry;
            var variant = MessageGrouping.Variant(continuation, trailingSpacing, repeatCount, discord);

            if (pendingScrollSequence == entry.Sequence && !viewChanged)
            {
                ImGui.SetScrollHereY(0.3f);
                pendingScrollSequence = null;
                pinToBottomFramesRemaining = 0;
            }

            if (pendingScrollBookmarkKey != null && !viewChanged && entry.BookmarkKey == pendingScrollBookmarkKey)
            {
                ImGui.SetScrollHereY(0.3f);
                pendingScrollBookmarkKey = null;
                pinToBottomFramesRemaining = 0;
            }

            var hadCache = entryHeightCache.TryGetValue(entry.Sequence, out var cachedHeight)
                && entryVariantCache.TryGetValue(entry.Sequence, out var cachedVariant)
                && cachedVariant == variant;
            if (hadCache)
            {
                var beforeDummy = ImGui.GetCursorPos();
                ImGui.Dummy(new Vector2(availableWidth, cachedHeight));

                if (!ImGui.IsItemVisible())
                {
                    return;
                }

                ImGui.SetCursorPos(beforeDummy);
            }

            var entryTop = ImGui.GetCursorPosY();
            DrawLogEntry(entry, config, mentionTerm, availableWidth, showingWhisper, showTimestamps, repeatCount, continuation, trailingSpacing, discord);
            var newHeight = ImGui.GetCursorPosY() - entryTop;

            if (hadCache && MathF.Abs(newHeight - cachedHeight) > 0.5f)
            {
                plugin.Log.Verbose(
                    "[CelinesChat][JitterDiag] view={View} seq={Seq} height changed while visible: {OldHeight:F1} -> {NewHeight:F1} fontAvailable={FontAvailable} availableWidth={Width:F1}",
                    viewKey,
                    entry.Sequence,
                    cachedHeight,
                    newHeight,
                    plugin.IsChatFontAvailable,
                    availableWidth);
            }

            if (!plugin.IsChatFontAvailable)
            {
                provisionalHeightSequences.Add(entry.Sequence);

                if (hadCache)
                {
                    return;
                }
            }

            entryHeightCache[entry.Sequence] = newHeight;
            entryVariantCache[entry.Sequence] = variant;
        }

        visibleEntriesBuffer.Clear();
        if (whisperPreload != null)
        {
            foreach (var entry in MergeByTimestamp(whisperPreload, entries))
            {
                if (PassesFilters(entry))
                {
                    visibleEntriesBuffer.Add(entry);
                }
            }
        }
        else
        {
            foreach (var entry in entries)
            {
                if (PassesFilters(entry))
                {
                    visibleEntriesBuffer.Add(entry);
                }
            }
        }

        var visibleCount = visibleEntriesBuffer.Count;
        for (var i = 0; i < visibleCount; i++)
        {
            var last = i;
            if (config.CollapseRepeatedMessages)
            {
                while (last + 1 < visibleCount && MessageGrouping.IsRepeat(visibleEntriesBuffer[last], visibleEntriesBuffer[last + 1]))
                {
                    last++;
                }
            }

            for (var k = i; k < last; k++)
            {
                TrackEntry(visibleEntriesBuffer[k]);
            }

            var shown = visibleEntriesBuffer[last];

            if (pendingScrollSequence is { } wanted && !viewChanged)
            {
                for (var k = i; k <= last; k++)
                {
                    if (visibleEntriesBuffer[k].Sequence == wanted)
                    {
                        pendingScrollSequence = shown.Sequence;
                        break;
                    }
                }
            }

            var groupsWithNext = grouping
                && last + 1 < visibleCount
                && MessageGrouping.Continues(shown, visibleEntriesBuffer[last + 1]);
            ProcessEntry(shown, visibleEntriesBuffer[i], last - i + 1, !groupsWithNext);
            i = last;
        }

        if (entryVariantCache.Count > 20000)
        {
            entryVariantCache.Clear();
        }

        if (viewChanged)
        {
            if (forceScrollToBottomOnNextViewChange || config.ScrollToBottomOnTabSwitch || !viewScrollPositions.TryGetValue(viewKey, out var savedScrollY))
            {
                forceScrollToBottomOnNextViewChange = false;
                ImGui.SetScrollHereY(1f);

                pinToBottomFramesRemaining = 5;
            }
            else
            {
                ImGui.SetScrollY(savedScrollY);
                pinToBottomFramesRemaining = 0;
            }
        }
        else if (pinToBottomFramesRemaining > 0)
        {
            pinToBottomFramesRemaining--;
            ImGui.SetScrollHereY(1f);
        }
        else if (heightLostFromViewShrink > 0f && !nearBottomBeforeDraw)
        {
            ImGui.SetScrollY(MathF.Max(0f, ImGui.GetScrollY() - heightLostFromViewShrink));
        }
        else if (hasNewOwnMessageInView || (!viewingHistory && hasNewEntries && nearBottomBeforeDraw))
        {
            ImGui.SetScrollHereY(1f);
        }

        if (!viewingHistory && liveEntries.Count > 0)
        {
            lastSeenSequence = liveEntries[^1].Sequence;
        }

        if (!viewingHistory)
        {
            lastSeenSequenceByView[viewKey] = viewSeenMax;
        }

        var distanceFromBottom = ImGui.GetScrollMaxY() - ImGui.GetScrollY();
        if (viewingHistory || nearBottomBeforeDraw)
        {
            bottomSeenSequenceByView[viewKey] = viewSeenMax;
            newBelowCount = 0;
        }

        if (jumpButtonClicked)
        {
            ImGui.SetScrollY(ImGui.GetScrollMaxY());
            pinToBottomFramesRemaining = 3;
        }

        if (showJumpButtonLastFrame)
        {
            DrawJumpButton(jumpButtonMin, jumpButtonMax, jumpButtonLabel, config);
        }

        showJumpButtonLastFrame = !jumpButtonClicked && distanceFromBottom > ImGui.GetTextLineHeightWithSpacing() * 4f;
        newBelowCountLastFrame = newBelowCount;
    }

    private static void DrawJumpButton(Vector2 min, Vector2 max, string label, Configuration config)
    {
        var accent = C4tTheme.Active(config)?.Accent ?? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
        var hovered = ImGui.IsMouseHoveringRect(min, max);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), false);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, hovered ? 1f : 0.85f)), (max.Y - min.Y) / 2f);
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(min + ((max - min - textSize) / 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);
        drawList.PopClipRect();

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    internal bool OpenBookmark(ChatBookmark bookmark)
    {
        var date = bookmark.Timestamp.ToString("yyyy-MM-dd");
        var dayEntries = plugin.ChatLog.LoadHistoryFile(date);
        var key = bookmark.Key;
        if (!dayEntries.Exists(e => e.BookmarkKey == key))
        {
            return false;
        }

        foreach (var dayEntry in dayEntries)
        {
            dayEntry.Sequence = nextPreloadSequence--;
        }

        loadedHistoryEntries = dayEntries;
        loadedHistoryLabel = date;
        logSearchFilter = string.Empty;
        pendingScrollBookmarkKey = key;
        IsOpen = true;
        return true;
    }

    internal static Vector4? RelationNameColor(int relation, ChatLogEntry entry, string displayName, Configuration config)
    {
        if (relation == 0 || (entry.ChatType == XivChatType.TellIncoming && config.WhisperColours.ContainsKey(displayName)))
        {
            return null;
        }

        return RelationColor(relation, config);
    }

    private static Vector4 RelationColor(int relation, Configuration config) => relation switch
    {
        Plugin.RelationFavorite => config.FavoriteNameColor,
        Plugin.RelationFriend => config.FriendNameColor,
        _ => config.FcMemberNameColor,
    };

    internal static float DrawRelationIcon(int relation, Configuration config)
    {
        var icon = relation switch
        {
            Plugin.RelationFavorite => FontAwesomeIcon.Heart,
            Plugin.RelationFriend => FontAwesomeIcon.UserFriends,
            _ => FontAwesomeIcon.ShieldAlt,
        };
        var text = icon.ToIconString();
        var lineHeight = ImGui.GetTextLineHeight();
        var iconSize = MathF.Round(lineHeight * 0.65f);

        Vector2 measured;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            measured = ImGui.CalcTextSize(text) * (iconSize / ImGui.GetFontSize());
        }

        var pos = ImGui.GetCursorScreenPos();
        var boxSize = new Vector2(measured.X, lineHeight);
        ImGui.Dummy(boxSize);
        var hovered = ImGui.IsItemHovered();
        ImGui.GetWindowDrawList().AddText(
            UiBuilder.IconFont,
            iconSize,
            new Vector2(pos.X, pos.Y + ((lineHeight - measured.Y) / 2f)),
            ImGui.GetColorU32(RelationColor(relation, config)),
            text);

        if (hovered)
        {
            ImGui.SetTooltip(Loc.T(relation switch
            {
                Plugin.RelationFavorite => "Relation.Favorite",
                Plugin.RelationFriend => "Relation.Friend",
                _ => "Relation.FcMember",
            }));
        }

        ImGui.SameLine(0, 4f);
        return boxSize.X + 4f;
    }

    private const float DiscordAvatarGap = 10f;

    private static float DiscordAvatarSize() => MathF.Round((ImGui.GetTextLineHeight() * 2f) + ImGui.GetStyle().ItemSpacing.Y);

    private void DrawDiscordEntryBody(
        ChatLogEntry entry,
        Configuration config,
        string mentionTerm,
        float availableWidth,
        bool showTimestamp,
        int repeatCount,
        bool continuation,
        Vector4 channelColor,
        bool allowRpFormatting,
        out Vector4 senderColor,
        out bool nameHovered,
        out string whisperTarget,
        out Vector2 nameRectMin,
        out Vector2 nameRectMax)
    {
        nameHovered = false;
        whisperTarget = string.Empty;
        nameRectMin = Vector2.Zero;
        nameRectMax = Vector2.Zero;

        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        var self = IsSelfAuthored(entry);
        var hasSender = !string.IsNullOrEmpty(entry.Sender);
        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var relation = hasSender && !isOutgoingTell ? plugin.GetRelation(entry) : 0;

        senderColor = isOutgoingTell
            ? config.SendAccentColor
            : logSearchFilter.Length > 0 && entry.Sender.Contains(logSearchFilter, StringComparison.OrdinalIgnoreCase)
                ? SearchHighlightColor
                : RelationNameColor(relation, entry, displayName, config)
                    ?? (entry.ChatType == XivChatType.TellIncoming
                        ? ChannelDisplay.WhisperColor(displayName, config)
                        : channelColor);

        var nickname = hasSender && !isOutgoingTell ? plugin.GetNickname(entry) : null;
        var authorName = isOutgoingTell
            ? plugin.OwnCharacterName
            : hasSender
                ? nickname ?? entry.Sender
                : ChannelDisplay.CategoryOf(entry.ChatType) is { } category
                    ? ChannelDisplay.CategoryName(category)
                    : ChannelDisplay.DisplayTag(entry.ChatType).Trim();

        var avatarSize = DiscordAvatarSize();
        var gutter = avatarSize + (DiscordAvatarGap * ImGuiHelpers.GlobalScale);
        var rowStart = ImGui.GetCursorScreenPos();

        if (!continuation)
        {
            DrawDiscordAvatar(entry, config, rowStart, avatarSize, self, hasSender, senderColor, authorName);
        }

        ImGui.Indent(gutter);
        var initialWidthUsed = 0f;

        if (!continuation)
        {
            if (relation != 0 && config.ShowRelationIcons)
            {
                DrawRelationIcon(relation, config);
            }

            ImGui.TextColored(senderColor, authorName);
            if (!self && entry.IsRealPlayer && hasSender)
            {
                nameHovered = ImGui.IsItemHovered();
                var nameClicked = ImGui.IsItemClicked();
                nameRectMin = ImGui.GetItemRectMin();
                nameRectMax = ImGui.GetItemRectMax();
                whisperTarget = displayName;

                if (nameHovered && nameClicked && ImGui.GetIO().KeyCtrl)
                {
                    OpenWhisperTabWithHistory(whisperTarget);
                }
                else if (nameHovered && nameClicked && config.PlayerCardOnClick)
                {
                    playerCard.Open(entry);
                }
            }

            if (!isOutgoingTell && entry.SenderWorld != null && nickname == null)
            {
                ImGui.SameLine(0, 2f);
                ImGui.TextDisabled("@" + entry.SenderWorld);
            }

            ImGui.SameLine(0, 8f * ImGuiHelpers.GlobalScale);
            DrawChannelBadge(ChannelDisplay.DisplayTag(entry.ChatType).Trim().Trim('[', ']'), channelColor);

            if (showTimestamp)
            {
                ImGui.SameLine(0, 8f * ImGuiHelpers.GlobalScale);
                ImGui.TextDisabled(DiscordTimeLabel(entry.Timestamp));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(entry.Timestamp.ToString(Loc.T("ChatLog.TimestampTooltipFormat"), Loc.Culture));
                }
            }

            if (repeatCount > 1)
            {
                ImGui.SameLine(0, 8f * ImGuiHelpers.GlobalScale);
                ImGui.TextDisabled("\u00d7" + repeatCount);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("ChatLog.RepeatedTooltip", repeatCount));
                }
            }
        }
        else if (repeatCount > 1)
        {
            var badge = "\u00d7" + repeatCount + " ";
            ImGui.TextDisabled(badge);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.RepeatedTooltip", repeatCount));
            }

            ImGui.SameLine(0, 0);
            initialWidthUsed = ImGui.CalcTextSize(badge).X;
        }

        ColoredTextRenderer.DrawForEntry(
            entry,
            channelColor,
            config.EmoteTextColor,
            config.OocTextColor,
            config.MentionColor,
            LinkColor,
            MathF.Max(60f, availableWidth - gutter),
            mentionTerm,
            initialWidthUsed,
            logSearchFilter,
            SearchHighlightColor,
            target =>
            {
                if (target.Url != null)
                {
                    OpenUrl(target.Url, plugin);
                }
                else if (target.Payload != null && entry.Payloads != null)
                {
                    plugin.HandleChatLinkClicked(target.Payload, entry.Payloads);
                }
            },
            target =>
            {
                if (target.Payload != null)
                {
                    plugin.OnChatLinkHovered(target.Payload);
                }
            },
            allowRpFormatting: allowRpFormatting);

        ImGui.Unindent(gutter);

        if (!continuation)
        {
            var used = ImGui.GetCursorScreenPos().Y - rowStart.Y;
            if (used < avatarSize)
            {
                ImGui.Dummy(new Vector2(1f, avatarSize - used));
            }
        }
    }

    private void DrawDiscordAvatar(ChatLogEntry entry, Configuration config, Vector2 pos, float size, bool self, bool hasSender, Vector4 color, string authorName)
    {
        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(size, size);
        var center = pos + new Vector2(size / 2f, size / 2f);
        var isPlayer = self || (hasSender && entry.IsRealPlayer);

        ImTextureID? texture = null;
        if (isPlayer)
        {
            var name = self ? plugin.OwnCharacterName : entry.Sender;
            var world = self ? plugin.OwnHomeWorld : plugin.ResolveWorld(entry);
            var key = world != null ? name + "@" + world : name;
            config.CustomAvatars.TryGetValue(key, out var customFile);
            texture = plugin.Avatars.Get(key, customFile, config.LodestoneAvatars, name, world, out _);
        }

        if (texture is { } handle)
        {
            drawList.AddImageRounded(handle, pos, max, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), size / 2f);
        }
        else
        {
            drawList.AddCircleFilled(center, size / 2f, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.28f)));
            if (isPlayer)
            {
                var initial = authorName.Length > 0 ? authorName[..1].ToUpperInvariant() : "?";
                var textSize = ImGui.CalcTextSize(initial);
                drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(color), initial);
            }
            else
            {
                var icon = FontAwesomeIcon.InfoCircle.ToIconString();
                var iconSize = size * 0.5f;
                Vector2 measured;
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    measured = ImGui.CalcTextSize(icon) * (iconSize / ImGui.GetFontSize());
                }

                drawList.AddText(UiBuilder.IconFont, iconSize, center - (measured / 2f), ImGui.GetColorU32(color), icon);
            }
        }

        if (!self && isPlayer && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, max))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                playerCard.Open(entry);
            }
        }
    }

    private static void DrawChannelBadge(string text, Vector4 color)
    {
        if (text.Length == 0)
        {
            return;
        }

        var padding = new Vector2(5f, 1f) * ImGuiHelpers.GlobalScale;
        var textSize = ImGui.CalcTextSize(text);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(textSize.X + (padding.X * 2f), textSize.Y));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            new Vector2(pos.X, pos.Y - padding.Y),
            new Vector2(pos.X + textSize.X + (padding.X * 2f), pos.Y + textSize.Y + padding.Y),
            ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.18f)),
            4f * ImGuiHelpers.GlobalScale);
        drawList.AddText(new Vector2(pos.X + padding.X, pos.Y), ImGui.GetColorU32(color), text);
    }

    private static string DiscordTimeLabel(DateTime timestamp)
    {
        var date = timestamp.Date;
        if (date == DateTime.Today)
        {
            return timestamp.ToString("HH:mm");
        }

        return date == DateTime.Today.AddDays(-1)
            ? Loc.T("ChatLog.YesterdayAt", timestamp.ToString("HH:mm"))
            : timestamp.ToString(Loc.T("ChatLog.DiscordDateFormat"), Loc.Culture);
    }

    private void DrawPlayerMenuItems(ChatLogEntry entry, Configuration config)
    {
        if (ImGui.Selectable(Loc.T("ChatLog.InvitePartyAction")))
        {
            plugin.InviteToParty(entry.Sender, entry.SenderWorld, entry.ContentId);
        }

        if (ImGui.Selectable(Loc.T("ChatLog.TargetAction")))
        {
            plugin.TargetPlayer(entry.Sender, entry.SenderWorld);
        }

        if (ImGui.Selectable(Loc.T("ChatLog.AdventurerPlateAction")))
        {
            plugin.OpenAdventurerPlate(entry.Sender, entry.SenderWorld, entry.ContentId);
        }

        if (config.EstateTeleportContextMenuEnabled && plugin.IsFriend(entry.Sender, entry.SenderWorld)
            && ImGui.Selectable(Loc.T("ChatLog.EstateTeleportAction")))
        {
            plugin.TriggerEstateTeleport(entry.Sender, entry.SenderWorld);
        }

        if (ImGui.Selectable(Loc.T("ChatLog.InviteNoviceNetworkAction")))
        {
            plugin.InviteToNoviceNetwork(entry.Sender, entry.SenderWorld);
        }

        ImGui.Separator();

        if (ImGui.Selectable(Loc.T(plugin.IsFavorite(entry) ? "ChatLog.RemoveFavorite" : "ChatLog.AddFavorite")))
        {
            plugin.ToggleFavorite(entry);
        }

        if (ImGui.Selectable(Loc.T(plugin.GetPlayerNote(entry) != null ? "ChatLog.EditNoteAction" : "ChatLog.AddNoteAction")))
        {
            noteEditorKey = entry.SenderKey;
            noteEditorText = plugin.GetPlayerNote(entry) ?? string.Empty;
            openNoteEditor = true;
        }
    }

    private void DrawBlockMenuItems(ChatLogEntry entry)
    {
        if (ImGui.Selectable(Loc.T("ChatLog.HideInChatAction")))
        {
            plugin.HidePlayer(entry);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.HideInChatTooltip"));
        }

        if (ImGui.Selectable(Loc.T("ChatLog.AddToBlacklistAction")))
        {
            plugin.AddToBlacklist(entry.Sender, entry.SenderWorld);
        }

        using (ImRaii.Disabled(entry.AccountId == 0))
        {
            if (ImGui.Selectable(Loc.T("ChatLog.AddToMuteListAction")))
            {
                plugin.AddToMuteList(entry.Sender, entry.SenderWorld, entry.ContentId, entry.AccountId);
            }
        }

        if (entry.AccountId == 0 && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.AddToMuteListUnavailable"));
        }
    }

    private void DrawMessageMenuItems(ChatLogEntry entry, bool includeReply)
    {
        if (includeReply && Plugin.CanReplyIn(entry.ChatType) && ImGui.Selectable(Loc.T("ChatLog.ReplyAction")))
        {
            plugin.ReplyTo(entry);
        }

        if (!string.IsNullOrWhiteSpace(entry.Text) && ImGui.Selectable(Loc.T("ChatLog.QuoteAction")))
        {
            QuoteMessage(entry);
        }

        if (ImGui.Selectable(Loc.T("ChatLog.CopyMessage")))
        {
            ImGui.SetClipboardText($"{entry.Sender}: {entry.Text}");
        }

        if (ImGui.Selectable(Loc.T(plugin.IsBookmarked(entry) ? "ChatLog.RemoveBookmark" : "ChatLog.AddBookmark")))
        {
            plugin.ToggleBookmark(entry);
        }

        if (ImGui.Selectable(Loc.T("ChatLog.ShowBookmarks")))
        {
            plugin.ToggleBookmarksWindow();
        }

        DrawItemLinkMenus(entry);
    }

    private void DrawItemLinkMenus(ChatLogEntry entry)
    {
        if (entry.Payloads == null)
        {
            return;
        }

        var shown = 0;
        var seen = new HashSet<uint>();
        for (var i = 0; i < entry.Payloads.Count && shown < 3; i++)
        {
            if (entry.Payloads[i] is not Dalamud.Game.Text.SeStringHandling.Payloads.ItemPayload item || !seen.Add(item.ItemId))
            {
                continue;
            }

            var name = ItemLinkName(entry.Payloads, i, item);
            shown++;
            if (shown == 1)
            {
                ImGui.Separator();
            }

            using var itemMenu = ImRaii.Menu(Loc.T("ChatLog.ItemMenu", SafeLabel(name)) + "##item" + i);
            if (!itemMenu.Success)
            {
                continue;
            }

            if (ImGui.MenuItem(Loc.T("ChatLog.ItemFind")))
            {
                plugin.HandleChatLinkClicked(item, entry.Payloads);
            }

            if (ImGui.MenuItem(Loc.T("ChatLog.ItemUniversalis")))
            {
                OpenUrl("https://universalis.app/market/" + item.ItemId, plugin);
            }

            if (ImGui.MenuItem(Loc.T("ChatLog.ItemGarland")))
            {
                OpenUrl("https://www.garlandtools.org/db/#item/" + item.ItemId, plugin);
            }

            if (ImGui.MenuItem(Loc.T("ChatLog.ItemCopyName")))
            {
                ImGui.SetClipboardText(name);
            }
        }
    }

    private static string ItemLinkName(List<Dalamud.Game.Text.SeStringHandling.Payload> payloads, int index, Dalamud.Game.Text.SeStringHandling.Payloads.ItemPayload item)
    {
        if (!string.IsNullOrWhiteSpace(item.DisplayName))
        {
            return CleanItemName(item.DisplayName);
        }

        var builder = new StringBuilder();
        for (var j = index + 1; j < payloads.Count; j++)
        {
            if (payloads[j] is Dalamud.Game.Text.SeStringHandling.Payloads.RawPayload raw && raw.Equals(Dalamud.Game.Text.SeStringHandling.Payloads.RawPayload.LinkTerminator))
            {
                break;
            }

            if (payloads[j] is Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload text)
            {
                builder.Append(text.Text);
            }
        }

        var name = CleanItemName(builder.ToString());
        return name.Length > 0 ? name : "#" + item.ItemId;
    }

    private static string CleanItemName(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is < '\uE000' or > '\uF8FF')
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }

    private void QuoteMessage(ChatLogEntry entry)
    {
        var name = string.IsNullOrEmpty(entry.Sender) ? string.Empty : FirstName(entry.Sender) + ": ";
        InsertTextAtCursor($"\u00bb {name}\"{PreviewLine(entry.Text, 60)}\" ");
    }

    internal void JumpToEntry(ChatLogEntry entry)
    {
        var config = plugin.Configuration;
        var state = plugin.GetCharacterState();
        loadedHistoryEntries = null;
        loadedHistoryLabel = null;
        logSearchFilter = string.Empty;
        IsOpen = true;

        if (entry.ChatType is XivChatType.TellIncoming or XivChatType.TellOutgoing)
        {
            OpenWhisperTabWithHistory(entry.SenderKey);
        }
        else
        {
            var tab = config.ChatTabs.Find(t => !plugin.IsTabInSecondaryWindow(t.Id) && t.Matches(entry.ChatType));
            if (tab == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(activeWhisperTabTarget) || noviceNetworkTabActive || activeTabId != tab.Id)
            {
                SwitchToFixedTab(config, state, tab);
            }
        }

        pendingScrollSequence = entry.Sequence;
        pendingScrollFramesLeft = 30;
    }

    private void DrawAwaySummary(Configuration config)
    {
        if (plugin.Away.Summary is not { } summary)
        {
            return;
        }

        var whispers = 0;
        var mentions = 0;
        foreach (var entry in summary.Entries)
        {
            if (entry.NotifyKind == 1)
            {
                whispers++;
            }
            else
            {
                mentions++;
            }
        }

        var accent = C4tTheme.Active(config)?.Second ?? new Vector4(1f, 0.8f, 0.3f, 1f);
        var minutes = Math.Max(1, (int)Math.Round((summary.End - summary.Start).TotalMinutes));

        using var id = ImRaii.PushId("awaySummary");
        if (ImGuiComponents.IconButton(awaySummaryExpanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown))
        {
            awaySummaryExpanded = !awaySummaryExpanded;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(accent, Loc.T("Away.Summary", minutes, whispers, mentions));
        if (ImGui.IsItemClicked())
        {
            awaySummaryExpanded = !awaySummaryExpanded;
        }

        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight()));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
        {
            plugin.Away.Dismiss();
            awaySummaryExpanded = false;
            return;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Away.Dismiss"));
        }

        if (!awaySummaryExpanded)
        {
            return;
        }

        var listHeight = MathF.Min(summary.Entries.Count, 8) * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2f;
        using var child = ImRaii.Child("##awayList", new Vector2(-1, listHeight), true);
        if (!child.Success)
        {
            return;
        }

        var seenWhisperSenders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = summary.Entries.Count - 1; i >= 0; i--)
        {
            var entry = summary.Entries[i];
            string label;
            if (entry.NotifyKind == 1)
            {
                if (!seenWhisperSenders.Add(entry.SenderKey))
                {
                    continue;
                }

                var count = summary.Entries.FindAll(e => e.NotifyKind == 1 && string.Equals(e.SenderKey, entry.SenderKey, StringComparison.OrdinalIgnoreCase)).Count;
                label = Loc.T("Away.WhisperLine", entry.Timestamp.ToString("HH:mm"), FirstName(entry.Sender), count, PreviewLine(entry.Text, 50));
            }
            else
            {
                label = Loc.T("Away.MentionLine", entry.Timestamp.ToString("HH:mm"), ChannelDisplay.DisplayTag(entry.ChatType), FirstName(entry.Sender), PreviewLine(entry.Text, 50));
            }

            if (ImGui.Selectable(SafeLabel(label) + "##away" + i))
            {
                JumpToEntry(entry);
            }
        }
    }

    internal static void AddMessageGap(Configuration config)
    {
        if (config.MessageSpacing < 0f)
        {
            ImGui.Spacing();
        }
        else if (config.MessageSpacing > 0f)
        {
            ImGui.Dummy(new Vector2(0f, config.MessageSpacing));
        }
    }

    private void DrawPlayerNoteEditor()
    {
        if (openNoteEditor)
        {
            ImGui.OpenPopup("##playerNoteEditor");
            openNoteEditor = false;
        }

        using var popup = ImRaii.Popup("##playerNoteEditor");
        if (!popup.Success || noteEditorKey == null)
        {
            return;
        }

        ImGui.TextUnformatted(Loc.T("ChatLog.NoteFor", noteEditorKey));
        ImGui.InputTextMultiline("##playerNoteText", ref noteEditorText, 500, new Vector2(320f, 90f));

        if (ImGui.Button(Loc.T("Common.Save")))
        {
            plugin.SetPlayerNote(noteEditorKey, noteEditorText);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ChatLog.DeleteNote")))
        {
            plugin.SetPlayerNote(noteEditorKey, string.Empty);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Common.Cancel")))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    internal static void DrawCenteredMarker(string text, Vector4 color, float availableWidth)
    {
        var textSize = ImGui.CalcTextSize(text);
        var pos = ImGui.GetCursorScreenPos();
        var width = MathF.Max(textSize.X, availableWidth - ImGui.GetStyle().ScrollbarSize - 6f);
        var y = pos.Y + (textSize.Y / 2f);
        var textX = pos.X + ((width - textSize.X) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var lineColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, color.W * 0.5f));
        drawList.AddLine(new Vector2(pos.X, y), new Vector2(textX - 8f, y), lineColor);
        drawList.AddLine(new Vector2(textX + textSize.X + 8f, y), new Vector2(pos.X + width, y), lineColor);
        drawList.AddText(new Vector2(textX, pos.Y), ImGui.GetColorU32(color), text);
        ImGui.Dummy(new Vector2(width, textSize.Y));
    }

    private bool IsSelfAuthored(ChatLogEntry entry) =>
        entry.ChatType == XivChatType.TellOutgoing
        || string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase);

    private void TrackUnreadForNewEntry(Configuration config, CharacterState state, ChatLogEntry entry, bool showingWhisper, bool showingNoviceNetwork, Guid? activeFixedTabId)
    {
        if (IsSelfAuthored(entry) || plugin.IsFilteredOut(entry))
        {
            return;
        }

        foreach (var tab in config.ChatTabs)
        {
            if (!showingWhisper && tab.Id == activeFixedTabId)
            {
                continue;
            }

            if (!tab.Matches(entry.ChatType) || tab.NotifyMode == TabNotifyMode.Never)
            {
                continue;
            }

            var important = entry.NotifyKind != 0;
            if (tab.NotifyMode == TabNotifyMode.MentionsOnly && !important)
            {
                continue;
            }

            fixedTabUnreadCounts[tab.Id] = fixedTabUnreadCounts.GetValueOrDefault(tab.Id) + 1;
            if (important)
            {
                fixedTabMentionCounts[tab.Id] = fixedTabMentionCounts.GetValueOrDefault(tab.Id) + 1;
            }
        }

        if (!showingNoviceNetwork && IsNoviceNetworkMessage(entry.ChatType))
        {
            noviceNetworkUnread++;
        }

        if (entry.ChatType == XivChatType.TellIncoming)
        {
            TrackIncomingWhisper(state, entry);
        }
    }

    private static bool IsNoviceNetworkMessage(XivChatType type) =>
        type is XivChatType.NoviceNetwork or XivChatType.NoviceNetworkSystem;

    private void TrackIncomingWhisper(CharacterState state, ChatLogEntry entry)
    {
        var senderIdentity = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

        if (plugin.IsFilteredOut(entry))
        {
            return;
        }

        var tabKey = plugin.RememberWhisperTarget(state, senderIdentity);

        if (!IsViewingWhisperTarget(tabKey, entry.Sender) && !state.MutedWhisperTargets.Contains(tabKey))
        {
            whisperUnreadCounts[tabKey] = whisperUnreadCounts.GetValueOrDefault(tabKey) + 1;
        }
    }

    private bool IsViewingWhisperTarget(string tabKey, string senderNameOnly)
    {
        if (string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            return false;
        }

        if (string.Equals(activeWhisperTabTarget, tabKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(activeWhisperTabTarget.Split('@')[0], senderNameOnly, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawLogEntry(ChatLogEntry entry, Configuration config, string mentionTerm, float availableWidth, bool showingWhisper, bool showTimestamp = true, int repeatCount = 1, bool continuation = false, bool trailingSpacing = true, bool discord = false)
    {
        using var id = ImRaii.PushId((int)entry.Sequence);

        var prefixSize = Vector2.Zero;
        var channelColor = ChannelDisplay.Color(entry.ChatType, config);
        var allowRpFormatting = config.AllowsRpFormatting(entry.ChatType);
        var nameHovered = false;
        var whisperTarget = string.Empty;
        var nameRectMin = Vector2.Zero;
        var nameRectMax = Vector2.Zero;
        Vector4 senderColor;
        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;

        using (ImRaii.Group())
        {
            if (discord)
            {
                DrawDiscordEntryBody(entry, config, mentionTerm, availableWidth, showTimestamp, repeatCount, continuation, channelColor, allowRpFormatting, out senderColor, out nameHovered, out whisperTarget, out nameRectMin, out nameRectMax);
            }
            else
            {
                if (showTimestamp)
                {
                    var timestamp = entry.Timestamp.ToString("HH:mm") + " ";
                    ImGui.TextColored(config.TimestampColor, timestamp);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(entry.Timestamp.ToString(Loc.T("ChatLog.TimestampTooltipFormat"), Loc.Culture));
                    }

                    ImGui.SameLine(0, 0);
                    prefixSize += ImGui.CalcTextSize(timestamp);
                }

                var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

                if (continuation)
                {
                    var indent = ImGui.GetFontSize() * 1.2f;
                    ImGui.Dummy(new Vector2(indent, 1f));
                    ImGui.SameLine(0, 0);
                    prefixSize.X += indent;
                }
                else
                {
                    var tag = ChannelDisplay.DisplayTag(entry.ChatType) + " ";
                    ImGui.TextColored(channelColor, tag);
                    ImGui.SameLine(0, 0);
                    prefixSize += ImGui.CalcTextSize(tag);

                    if (!isOutgoingTell && config.ShowSenderStatusIcons)
                    {
                        prefixSize.X += StatusIconRenderer.Draw(plugin, entry);
                    }
                }

                if (repeatCount > 1)
                {
                    var badge = "\u00d7" + repeatCount + " ";
                    ImGui.TextColored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], badge);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(Loc.T("ChatLog.RepeatedTooltip", repeatCount));
                    }

                    ImGui.SameLine(0, 0);
                    prefixSize += ImGui.CalcTextSize(badge);
                }

                var rawName = isOutgoingTell && showingWhisper
                    ? plugin.OwnCharacterName
                    : (!isOutgoingTell ? plugin.GetNickname(entry) : null) ?? displayName;

                var namePart = continuation || string.IsNullOrEmpty(rawName) ? string.Empty : rawName + ": ";

                var relation = namePart.Length > 0 ? plugin.GetRelation(entry) : 0;
                senderColor = isOutgoingTell
                    ? config.SendAccentColor
                    : logSearchFilter.Length > 0 && entry.Sender.Contains(logSearchFilter, StringComparison.OrdinalIgnoreCase)
                        ? SearchHighlightColor
                        : RelationNameColor(relation, entry, displayName, config)
                            ?? (entry.ChatType == XivChatType.TellIncoming
                                ? ChannelDisplay.WhisperColor(displayName, config)
                                : channelColor);
                if (relation != 0 && config.ShowRelationIcons)
                {
                    prefixSize.X += DrawRelationIcon(relation, config);
                }

                if (namePart.Length > 0)
                {
                    ImGui.TextColored(senderColor, namePart);
                }

                if (!IsSelfAuthored(entry) && entry.IsRealPlayer && namePart.Length > 0)
                {
                    nameHovered = ImGui.IsItemHovered();
                    var nameClicked = ImGui.IsItemClicked();
                    nameRectMin = ImGui.GetItemRectMin();
                    nameRectMax = ImGui.GetItemRectMax();
                    whisperTarget = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

                    if (nameHovered && nameClicked && ImGui.GetIO().KeyCtrl)
                    {
                        OpenWhisperTabWithHistory(whisperTarget);
                    }
                    else if (nameHovered && nameClicked && config.PlayerCardOnClick)
                    {
                        playerCard.Open(entry);
                    }
                }

                var namePartSize = ImGui.CalcTextSize(namePart);

                ImGui.SameLine(0, 0);
                ColoredTextRenderer.DrawForEntry(
                    entry,
                    channelColor,
                    config.EmoteTextColor,
                    config.OocTextColor,
                    config.MentionColor,
                    LinkColor,
                    availableWidth,
                    mentionTerm,
                    prefixSize.X + namePartSize.X,
                    logSearchFilter,
                    SearchHighlightColor,
                    target =>
                    {
                        if (target.Url != null)
                        {
                            OpenUrl(target.Url, plugin);
                        }
                        else if (target.Payload != null && entry.Payloads != null)
                        {
                            plugin.HandleChatLinkClicked(target.Payload, entry.Payloads);
                        }
                    },
                    target =>
                    {
                        if (target.Payload != null)
                        {
                            plugin.OnChatLinkHovered(target.Payload);
                        }
                    },
                    allowRpFormatting: allowRpFormatting);
            }

            if (trailingSpacing)
            {
                AddMessageGap(config);
                if (discord)
                {
                    ImGui.Dummy(new Vector2(1f, 4f * ImGuiHelpers.GlobalScale));
                }
            }
        }

        if (plugin.IsBookmarked(entry))
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var accent = C4tTheme.Active(config)?.Second ?? new Vector4(1f, 0.8f, 0.25f, 1f);
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(min.X - 5f, min.Y), new Vector2(min.X - 2f, max.Y), ImGui.GetColorU32(accent), 1.5f);
        }

        if (ImGui.IsItemHovered())
        {
            if (discord)
            {
                var rowMin = ImGui.GetItemRectMin();
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(rowMin - new Vector2(4f, 1f), ImGui.GetItemRectMax() + new Vector2(4f, 1f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), 4f);
                if (continuation && showTimestamp)
                {
                    drawList.AddText(rowMin + new Vector2(1f, 0f), ImGui.GetColorU32(ImGuiCol.TextDisabled), entry.Timestamp.ToString("HH:mm"));
                }
            }
            else
            {
                var min = ImGui.GetItemRectMin() - new Vector2(4f, 2f);
                var max = ImGui.GetItemRectMax() + new Vector2(4f, 2f);
                ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 3f, ImDrawFlags.None, 1.3f);
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            rightClickedOnName = nameHovered || (continuation && !IsSelfAuthored(entry) && entry.IsRealPlayer);
            if (continuation)
            {
                whisperTarget = displayNameForMenu(entry);
            }
        }

        static string displayNameForMenu(ChatLogEntry e) => e.SenderWorld != null ? $"{e.Sender}@{e.SenderWorld}" : e.Sender;

        using (var entryContextPopup = ImRaii.ContextPopupItem("##logEntryContext"))
        {
            if (entryContextPopup.Success)
            {
            if (!isOutgoingTell && rightClickedOnName)
            {
                ImGui.TextDisabled(plugin.GetNickname(entry) is { } nick ? $"{nick} ({whisperTarget})" : whisperTarget);
                ImGui.Separator();

                if (ImGui.Selectable(Loc.T("ChatLog.WhisperAction")))
                {
                    OpenWhisperTabWithHistory(whisperTarget);
                }

                if (Plugin.CanReplyIn(entry.ChatType) && ImGui.Selectable(Loc.T("ChatLog.ReplyAction")))
                {
                    plugin.ReplyTo(entry);
                }

                if (ImGui.Selectable(Loc.T("ChatLog.PlayerCardAction")))
                {
                    playerCard.Open(entry);
                }

                using (var playerMenu = ImRaii.Menu(Loc.T("ChatLog.PlayerMenu")))
                {
                    if (playerMenu.Success)
                    {
                        DrawPlayerMenuItems(entry, config);
                    }
                }

                using (var blockMenu = ImRaii.Menu(Loc.T("ChatLog.BlockFunctionsMenu")))
                {
                    if (blockMenu.Success)
                    {
                        DrawBlockMenuItems(entry);
                    }
                }

                ImGui.Separator();
                using (var messageMenu = ImRaii.Menu(Loc.T("ChatLog.MessageMenu")))
                {
                    if (messageMenu.Success)
                    {
                        DrawMessageMenuItems(entry, includeReply: false);
                    }
                }
            }
            else
            {
                DrawMessageMenuItems(entry, includeReply: true);
            }
            }
        }

        if (nameHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var note = plugin.GetPlayerNote(entry);
            var hint = Loc.T(config.PlayerCardOnClick ? "ChatLog.ClickForCard" : "ChatLog.ClickToWhisper");
            var tooltip = note != null ? note + "\n\n" + hint : hint;
            if (plugin.GetNickname(entry) != null)
            {
                tooltip = (entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender) + "\n\n" + tooltip;
            }

            ImGui.SetTooltip(tooltip);

            var underlineY = nameRectMax.Y - 1f;
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(nameRectMin.X, underlineY),
                new Vector2(nameRectMax.X, underlineY),
                ImGui.GetColorU32(senderColor));
        }
    }

    internal void FlushPendingDraftSave()
    {
        if (!draftSavePending)
        {
            return;
        }

        draftSavePending = false;
        plugin.SaveConfiguration();
    }

    private void SyncDraft(CharacterState state)
    {
        var key = !string.IsNullOrEmpty(activeWhisperTabTarget)
            ? state.LastChannel == ChatChannel.Whisper ? activeWhisperTabTarget : GenericDraftKey
            : noviceNetworkTabActive
                ? NoviceNetworkDraftKey
                : activeTabId != Guid.Empty ? TabDraftKeyPrefix + activeTabId : GenericDraftKey;

        if (key == currentDraftKey)
        {
            return;
        }

        if (!string.IsNullOrEmpty(currentDraftKey))
        {
            state.Drafts[currentDraftKey] = inputText;
        }

        inputText = state.Drafts.TryGetValue(key, out var draft) ? draft : string.Empty;
        currentDraftKey = key;

        cursorPos = selectionStart = selectionEnd = inputText.Length;

        historyBrowseIndex = -1;
    }

    private Vector4? GetCommandTint(Configuration config)
    {
        var trimmed = inputText.TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return null;
        }

        var command = trimmed.Split(' ', '\n')[0];
        return plugin.IsValidCommand(command) ? config.ValidCommandColor : config.InvalidCommandColor;
    }

    private int TrackTextState(ref ImGuiInputTextCallbackData data)
    {
        try
        {
            return TrackTextStateUnsafe(ref data);
        }
        catch (Exception ex)
        {
            plugin.Log.Error(ex, "[CelinesChat] Fehler im Eingabefeld-Callback.");
            return 0;
        }
    }

    private unsafe int TrackTextStateUnsafe(ref ImGuiInputTextCallbackData data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCharFilter)
        {
            if (data.EventChar == '\n'
                && plugin.Configuration.SendOnEnter
                && !ImGui.GetIO().KeyShift
                && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)))
            {
                pendingSendFromEnter = true;
                return 1;
            }

            if (data.EventChar == '\t')
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Tab))
                {
                    pendingAutoTranslatePopup = true;
                    return 1;
                }

                data.EventChar = ' ';
            }

            if (data.EventChar == ' ' && plugin.Configuration.SnippetShortcutsEnabled)
            {
                pendingSnippetExpand = true;
            }

            return 0;
        }

        if (pendingSnippetExpand)
        {
            pendingSnippetExpand = false;
            TryExpandSnippetBeforeCursor(ref data, FindSnippetByShortcut);
        }

        if (data.CursorPos == 0 && ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
            HandleHistoryRecall(ref data, up: true);
            return 0;
        }

        if (data.CursorPos == data.BufTextLen && ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            HandleHistoryRecall(ref data, up: false);
            return 0;
        }

        if (pendingCursorCharIndex is { } targetCharIndex)
        {
            pendingCursorCharIndex = null;
            var clampedIndex = Math.Clamp(targetCharIndex, 0, inputText.Length);
            var byteOffset = Encoding.UTF8.GetByteCount(inputText[..clampedIndex]);
            data.CursorPos = byteOffset;
            data.SelectionStart = byteOffset;
            data.SelectionEnd = byteOffset;
            cursorPos = clampedIndex;
            selectionStart = clampedIndex;
            selectionEnd = clampedIndex;
            return 0;
        }

        selectionStart = ByteOffsetToCharIndex(data, data.SelectionStart);
        selectionEnd = ByteOffsetToCharIndex(data, data.SelectionEnd);
        cursorPos = ByteOffsetToCharIndex(data, data.CursorPos);
        return 0;
    }

    private List<string> RecallHistory()
    {
        var result = new List<string>();
        var perChannel = plugin.Configuration.SentHistoryPerChannel;
        var current = Plugin.ChannelKey(plugin.GetCharacterState());
        foreach (var item in plugin.SentHistory)
        {
            if (perChannel && item.Channel.Length > 0 && item.Channel != current)
            {
                continue;
            }

            if (!result.Contains(item.Text))
            {
                result.Add(item.Text);
            }
        }

        return result;
    }

    private unsafe void HandleHistoryRecall(ref ImGuiInputTextCallbackData data, bool up)
    {
        var history = RecallHistory();
        if (history.Count == 0)
        {
            return;
        }

        if (up)
        {
            if (historyBrowseIndex == -1)
            {
                historyBrowseOriginalDraft = inputText;
            }

            if (historyBrowseIndex < history.Count - 1)
            {
                historyBrowseIndex++;
            }
        }
        else
        {
            if (historyBrowseIndex == -1)
            {
                return;
            }

            historyBrowseIndex--;
        }

        var replacement = historyBrowseIndex == -1 ? historyBrowseOriginalDraft : history[historyBrowseIndex];

        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, replacement);
    }

    private static unsafe int ByteOffsetToCharIndex(ImGuiInputTextCallbackData data, int byteOffset)
    {
        var length = Math.Clamp(byteOffset, 0, data.BufTextLen);
        return Encoding.UTF8.GetCharCount(data.Buf, length);
    }

    private void DrawAutoTranslatePopup(CharacterState state)
    {
        using var popup = ImRaii.Popup("##autoTranslatePopup");
        if (!popup.Success)
        {
            return;
        }

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##autoTranslateSearch", Loc.T("Compose.AutoTranslateSearchHint"), ref autoTranslateSearch, 100);

        ImGui.Separator();
        using var list = ImRaii.Child("##autoTranslateList", new Vector2(300, 300));

        if (autoTranslateSearch.Length > 0)
        {
            var results = plugin.AutoTranslate.Search(autoTranslateSearch);
            if (results.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("Compose.AutoTranslateNoResults"));
            }

            foreach (var entry in results)
            {
                if (ImGui.Selectable(entry.Text + "##at" + entry.Group + "_" + entry.Key))
                {
                    InsertAutoTranslateEntry(state, entry);
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        else if (autoTranslateBrowseGroup is { } browseGroup)
        {
            if (ImGui.Selectable(Loc.T("Compose.AutoTranslateBack")))
            {
                autoTranslateBrowseGroup = null;
            }

            ImGui.Separator();

            foreach (var entry in plugin.AutoTranslate.GetEntries(browseGroup))
            {
                if (ImGui.Selectable(entry.Text + "##at" + entry.Group + "_" + entry.Key))
                {
                    InsertAutoTranslateEntry(state, entry);
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        else if (plugin.AutoTranslate.LoadError is { } loadError)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Compose.AutoTranslateLoadError"));
            ImGui.TextWrapped(loadError);
        }
        else if (!plugin.AutoTranslate.IsLoaded)
        {
            ImGui.TextDisabled(Loc.T("Compose.AutoTranslateLoading"));
        }
        else
        {
            foreach (var group in plugin.AutoTranslate.GetGroups())
            {
                if (ImGui.Selectable(plugin.AutoTranslate.GetGroupTitle(group) + "##atg" + group))
                {
                    autoTranslateBrowseGroup = group;
                }
            }
        }
    }

    private void InsertAutoTranslateEntry(CharacterState state, AutoTranslateService.Entry entry)
    {
        InsertMarkerAtCursor(state, MessageMarkerEncoder.BuildAutoTranslateMarker(entry));
    }

    private void InsertChatPrefillText(CharacterState state, string text)
    {
        if (text.StartsWith('/'))
        {
            inputText = text;

            if (TryParseTellTarget(text, out var tellTarget))
            {
                var resolvedTarget = plugin.RememberWhisperTarget(state, tellTarget);
                plugin.OpenWhisperTab(resolvedTarget);
                currentDraftKey = resolvedTarget;
            }
        }
        else if (!inputText.Contains(text, StringComparison.Ordinal))
        {
            inputText += text;
        }

        state.Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();
    }

    private static bool TryParseTellTarget(string text, out string target)
    {
        target = string.Empty;
        const string prefix = "/tell ";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = text[prefix.Length..].TrimStart();
        if (rest.Length == 0)
        {
            return false;
        }

        var atIndex = rest.IndexOf('@');
        if (atIndex < 0)
        {
            return false;
        }

        var endIndex = rest.IndexOf(' ', atIndex);
        target = endIndex > 0 ? rest[..endIndex] : rest.TrimEnd();

        return target.Contains('@');
    }

    private void InsertMarkerAtCursor(CharacterState state, string marker)
    {
        var pos = Math.Clamp(cursorPos, 0, inputText.Length);
        inputText = inputText[..pos] + marker + inputText[pos..];

        state.Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();
    }

    private void HandleSendHotkey(CharacterState state, Configuration config)
    {
        if (config.SendOnEnter)
        {
            return;
        }

        if (!ImGui.IsItemFocused())
        {
            return;
        }

        var ctrlEnter = ImGui.GetIO().KeyCtrl && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
        if (ctrlEnter)
        {
            TrySend(state);
        }
    }

    private List<string> GetChunks(string text, int maxLength, int prefixBytes)
    {
        GetMappedChunks(text, maxLength, prefixBytes);
        return cachedChunks;
    }

    private List<MappedChunk> GetMappedChunks(string text, int maxLength, int prefixBytes)
    {
        if (text != cachedInput || maxLength != cachedMaxLength || prefixBytes != cachedPrefixBytes)
        {
            cachedInput = text;
            cachedMaxLength = maxLength;
            cachedPrefixBytes = prefixBytes;
            cachedMappedChunks = MessageSplitter.BuildMappedMessages(text, maxLength, prefixBytes);
            cachedChunks = cachedMappedChunks.Select(c => c.Text).ToList();
        }

        return cachedMappedChunks;
    }

    private void TrySend(CharacterState state)
    {
        inputText = ExpandSnippetShortcuts(inputText);
        var trimmedInput = inputText.TrimStart();
        if (trimmedInput.StartsWith('/'))
        {
            if (!plugin.IsLoggedIn)
            {
                validationMessage = Loc.T("Compose.NotLoggedIn");
                return;
            }

            if (!plugin.SendRawCommand(trimmedInput))
            {
                validationMessage = Loc.T("Compose.CommandTooLong", MessageSplitter.MaxChatLineBytes);
                return;
            }

            ClearComposeAfterSend(state);
            return;
        }

        var chunks = GetChunks(inputText, plugin.Configuration.MaxMessageLength, plugin.GetChannelPrefixBytes(state));

        if (chunks.Count > 0 && !plugin.IsLoggedIn)
        {
            validationMessage = Loc.T("Compose.NotLoggedIn");
            return;
        }

        if (chunks.Count == 0)
        {
            validationMessage = string.Empty;
            ImGuiP.ClearActiveID();

            if (plugin.IsGPosing)
            {
                plugin.CloseGposeChatWindow();
            }

            return;
        }

        if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrWhiteSpace(state.LastWhisperTarget))
        {
            validationMessage = Loc.T("Compose.WhisperTargetMissing");
            return;
        }

        if (state.LastChannel == ChatChannel.Whisper && plugin.IsOwnCharacterTarget(state.LastWhisperTarget))
        {
            validationMessage = Loc.T("Compose.WhisperTargetSelf");
            return;
        }

        validationMessage = string.Empty;
        plugin.SendChunks(chunks, inputText);
        ClearComposeAfterSend(state);
    }

    private void ClearComposeAfterSend(CharacterState state)
    {
        validationMessage = string.Empty;
        inputText = string.Empty;
        state.Drafts[currentDraftKey] = string.Empty;
        plugin.SaveConfiguration();
        historyBrowseIndex = -1;
        cursorPos = selectionStart = selectionEnd = 0;

        ImGuiP.ClearActiveID();

        if (plugin.Configuration.KeepInputFocusAfterSend)
        {
            pendingFocusInput = true;
        }
    }

    private void DrawComposeStatusRow(Configuration config, CharacterState state, List<string> chunks)
    {
        if (!string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            return;
        }

        var channelColor = GetActiveChannelColor(config, state);
        ImGui.TextColored(channelColor, CurrentChannelLabel(state));
        ImGui.SameLine();

        var countText = Loc.T("Compose.MessageCount", chunks.Count);
        var countWidth = ImGui.CalcTextSize(countText).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > countWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - countWidth);
        }

        ImGui.TextDisabled(countText);
    }

    private void DrawSendingOverlay()
    {
        if (!(plugin.IsSending && plugin.SendTotal > 1))
        {
            return;
        }

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var drawList = ImGui.GetForegroundDrawList();
        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)));

        var done = plugin.SendTotal - plugin.SendRemaining;
        var fraction = Math.Clamp(plugin.SendTotal > 0 ? (float)done / plugin.SendTotal : 0f, 0f, 1f);
        var progressText = Loc.T("Compose.Sending", done, plugin.SendTotal);
        var cancelLabel = Loc.T("Compose.Cancel");
        var textColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

        var barWidth = Math.Clamp(windowSize.X - 80f, 80f, 280f);
        var barHeight = ImGui.GetFrameHeight();
        var cancelTextSize = ImGui.CalcTextSize(cancelLabel);
        var cancelSize = new Vector2(cancelTextSize.X + 28f, barHeight);
        const float rowSpacing = 10f;
        var blockSize = new Vector2(MathF.Max(barWidth, cancelSize.X), barHeight + rowSpacing + cancelSize.Y);
        var blockPos = windowPos + (windowSize - blockSize) * 0.5f;

        var barPos = new Vector2(blockPos.X + (blockSize.X - barWidth) * 0.5f, blockPos.Y);
        drawList.AddRectFilled(barPos, barPos + new Vector2(barWidth, barHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), 4f);
        if (fraction > 0f)
        {
            drawList.AddRectFilled(barPos, barPos + new Vector2(barWidth * fraction, barHeight), ImGui.GetColorU32(new Vector4(0.16f, 0.45f, 0.24f, 1f)), 4f);
        }

        var progressTextSize = ImGui.CalcTextSize(progressText);
        drawList.AddText(barPos + (new Vector2(barWidth, barHeight) - progressTextSize) * 0.5f, textColor, progressText);

        var cancelPos = new Vector2(blockPos.X + (blockSize.X - cancelSize.X) * 0.5f, barPos.Y + barHeight + rowSpacing);
        var cancelHovered = ImGui.IsMouseHoveringRect(cancelPos, cancelPos + cancelSize);
        var cancelColor = cancelHovered
            ? (ImGui.IsMouseDown(ImGuiMouseButton.Left) ? new Vector4(0.78f, 0.26f, 0.26f, 1f) : new Vector4(0.68f, 0.22f, 0.22f, 1f))
            : new Vector4(0.55f, 0.18f, 0.18f, 1f);
        drawList.AddRectFilled(cancelPos, cancelPos + cancelSize, ImGui.GetColorU32(cancelColor), 4f);
        drawList.AddText(cancelPos + (cancelSize - cancelTextSize) * 0.5f, textColor, cancelLabel);

        if (cancelHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                plugin.CancelSending();
            }
        }
    }

    private static Vector4 GetActiveChannelColor(Configuration config, CharacterState state) => state.LastChannel switch
    {
        ChatChannel.Linkshell => ChannelDisplay.LinkshellColor(state.LinkshellNumber, config),
        ChatChannel.CrossWorldLinkshell => ChannelDisplay.CrossWorldLinkshellColor(state.CrossWorldLinkshellNumber, config),
        _ => ChannelDisplay.Color(CategoryForChannel(state.LastChannel), config),
    };

    private void DrawEmbeddedPreview(Configuration config, List<MappedChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        ImGui.Separator();

        using var scrollArea = ImRaii.Child("##embeddedPreviewScroll", new Vector2(-1, EmbeddedPreviewHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < chunks.Count; i++)
        {
            PreviewRenderer.DrawMappedChunk(chunks[i], config, availableWidth, sourceIndex =>
            {
                pendingCursorCharIndex = sourceIndex;
                pendingFocusInput = true;
            }, i * 10000);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Text.Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }

    private void DrawRpActionsMenu(CharacterState state)
    {
        if (DrawToolbarIconButton(FontAwesomeIcon.Magic))
        {
            ImGui.OpenPopup("##rpActionsMenu");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.RpActionsTooltip"));
        }

        using var popup = ImRaii.Popup("##rpActionsMenu");
        if (!popup.Success)
        {
            return;
        }

        if (ImGui.Selectable(Loc.T("Compose.WrapEmote")))
        {
            WrapSelection("*", "*");
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Selectable(Loc.T("Compose.WrapOoc")))
        {
            WrapSelection("((", "))");
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Selectable(Loc.T("Compose.AutoTranslate")))
        {
            pendingAutoTranslatePopup = true;
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Selectable(Loc.T("Compose.InsertMapFlag")))
        {
            if (plugin.HasCurrentMapFlag())
            {
                InsertChatPrefillText(state, "<flag>");
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();

        using (var snippetsMenu = ImRaii.Menu(Loc.T("Compose.Snippets")))
        {
            if (snippetsMenu.Success)
            {
                DrawSnippetsMenuContent();
            }
        }

        using (var historyMenu = ImRaii.Menu(Loc.T("Compose.History")))
        {
            if (historyMenu.Success)
            {
                DrawHistoryMenuContent();
            }
        }

        ImGui.Separator();

        if (ImGui.Selectable(Loc.T("Compose.Copy")))
        {
            ImGui.SetClipboardText(inputText);
            ImGui.CloseCurrentPopup();
        }
    }

    private string GetWordAtCursor()
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, inputText.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, inputText.Length);

        if (start != end)
        {
            return inputText.Substring(start, end - start);
        }

        var pos = Math.Clamp(cursorPos, 0, inputText.Length);

        var wordStart = pos;
        while (wordStart > 0 && !char.IsWhiteSpace(inputText[wordStart - 1]))
        {
            wordStart--;
        }

        var wordEnd = pos;
        while (wordEnd < inputText.Length && !char.IsWhiteSpace(inputText[wordEnd]))
        {
            wordEnd++;
        }

        return wordStart < wordEnd ? inputText[wordStart..wordEnd] : string.Empty;
    }

    private void WrapSelection(string open, string close)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, inputText.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, inputText.Length);

        if (start != end)
        {
            var selected = inputText.Substring(start, end - start);
            inputText = inputText[..start] + open + selected + close + inputText[end..];
            pendingCursorCharIndex = start + open.Length + selected.Length + close.Length;
        }
        else
        {
            var pos = Math.Clamp(cursorPos, 0, inputText.Length);
            inputText = inputText[..pos] + open + close + inputText[pos..];
            pendingCursorCharIndex = pos + open.Length;
        }

        pendingFocusInput = true;

        plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();
    }

    private void DrawSnippetsMenuContent()
    {
        var snippets = plugin.GetCharacterState().Snippets;

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##snippetSearch", Loc.T("Compose.SnippetSearchHint"), ref snippetSearchFilter, 100);

        if (snippets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoSnippets"));
        }

        var removeIndex = -1;
        Snippet? picked = null;
        for (var i = 0; i < snippets.Count; i++)
        {
            if (snippetSearchFilter.Length > 0 && snippets[i].Name.IndexOf(snippetSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            using var id = ImRaii.PushId(i);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            var shortcut = snippets[i].Shortcut;
            if (ImGui.InputTextWithHint("##shortcut", Loc.T("Compose.SnippetShortcutHint"), ref shortcut, 20))
            {
                snippets[i].Shortcut = NormalizeShortcut(shortcut);
                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Compose.SnippetShortcutTooltip"));
            }

            ImGui.SameLine();
            if (ImGui.Selectable(SafeLabel(snippets[i].Name)))
            {
                picked = snippets[i];
            }
        }

        if (removeIndex >= 0)
        {
            snippets.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }
        else if (picked != null)
        {
            InsertTextAtCursor(picked.Text);
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(150);
        ImGui.InputText(Loc.T("Compose.SnippetNameLabel"), ref newSnippetName, 60);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70);
        ImGui.InputTextWithHint("##newShortcut", Loc.T("Compose.SnippetShortcutHint"), ref newSnippetShortcut, 20);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.SnippetShortcutTooltip"));
        }

        ImGui.SameLine();
        var canSave = !string.IsNullOrWhiteSpace(newSnippetName) && !string.IsNullOrWhiteSpace(inputText);
        using (ImRaii.Disabled(!canSave))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, Loc.T("Compose.SaveSnippet")))
            {
                snippets.Add(new Snippet { Name = newSnippetName, Text = inputText, Shortcut = NormalizeShortcut(newSnippetShortcut) });
                plugin.SaveConfiguration();
                newSnippetName = string.Empty;
                newSnippetShortcut = string.Empty;
            }
        }

        var enabled = plugin.Configuration.SnippetShortcutsEnabled;
        if (ImGui.Checkbox(Loc.T("Compose.SnippetShortcutsEnabled"), ref enabled))
        {
            plugin.Configuration.SnippetShortcutsEnabled = enabled;
            plugin.SaveConfiguration();
        }
    }

    private static string NormalizeShortcut(string value) => value.Trim().TrimStart(':').Replace(" ", string.Empty);

    private Snippet? FindSnippetByShortcut(string code)
    {
        if (code.Length == 0)
        {
            return null;
        }

        foreach (var snippet in plugin.GetCharacterState().Snippets)
        {
            if (snippet.Shortcut.Length > 0 && string.Equals(snippet.Shortcut, code, StringComparison.OrdinalIgnoreCase))
            {
                return snippet;
            }
        }

        return null;
    }

    private string ExpandSnippetShortcuts(string text)
    {
        if (!plugin.Configuration.SnippetShortcutsEnabled || !text.Contains(':'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var atWordStart = i == 0 || char.IsWhiteSpace(text[i - 1]);
            if (atWordStart && text[i] == ':')
            {
                var end = i + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                if (FindSnippetByShortcut(text[(i + 1)..end]) is { } snippet)
                {
                    builder.Append(snippet.Text);
                    i = end;
                    continue;
                }
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString();
    }

    private static unsafe void TryExpandSnippetBeforeCursor(ref ImGuiInputTextCallbackData data, Func<string, Snippet?> find)
    {
        var cursorBytes = Math.Clamp(data.CursorPos, 0, data.BufTextLen);
        if (cursorBytes < 3)
        {
            return;
        }

        var before = Encoding.UTF8.GetString(data.Buf, cursorBytes);
        if (before.Length < 3 || before[^1] != ' ')
        {
            return;
        }

        var start = before.Length - 2;
        while (start > 0 && !char.IsWhiteSpace(before[start - 1]))
        {
            start--;
        }

        var word = before[start..^1];
        if (word.Length < 2 || word[0] != ':' || find(word[1..]) is not { } snippet)
        {
            return;
        }

        var byteStart = Encoding.UTF8.GetByteCount(before[..start]);
        var byteLength = Encoding.UTF8.GetByteCount(word) + 1;
        data.DeleteChars(byteStart, byteLength);
        data.InsertChars(byteStart, snippet.Text + " ");
        data.SelectionStart = data.CursorPos;
        data.SelectionEnd = data.CursorPos;
    }

    private void DrawHistoryMenuContent()
    {
        var config = plugin.Configuration;
        var perChannel = config.SentHistoryPerChannel;
        if (ImGui.Checkbox(Loc.T("Compose.HistoryPerChannel"), ref perChannel))
        {
            config.SentHistoryPerChannel = perChannel;
            historyBrowseIndex = -1;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.HistoryPerChannelTooltip"));
        }

        ImGui.Separator();

        var history = plugin.SentHistory;
        if (history.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoHistory"));
        }

        var current = Plugin.ChannelKey(plugin.GetCharacterState());
        for (var i = 0; i < history.Count; i++)
        {
            var item = history[i];
            var text = item.Text;
            if (perChannel && item.Channel.Length > 0 && item.Channel != current)
            {
                continue;
            }

            using var id = ImRaii.PushId(i);
            var channelLabel = item.Channel.Length == 0 ? string.Empty : "[" + HistoryChannelLabel(item.Channel) + "] ";
            if (ImGui.Selectable(SafeLabel(channelLabel + PreviewLine(text, 50))))
            {
                inputText = text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                cursorPos = selectionStart = selectionEnd = inputText.Length;
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private static string HistoryChannelLabel(string channelKey)
    {
        var colon = channelKey.IndexOf(':');
        if (colon < 0)
        {
            return channelKey;
        }

        var kind = channelKey[..colon];
        var rest = channelKey[(colon + 1)..];
        return kind switch
        {
            "Linkshell" => "LS" + rest,
            "CrossWorldLinkshell" => "CWLS" + rest,
            "Whisper" => FirstName(rest),
            _ => channelKey,
        };
    }

    private void InsertTextAtCursor(string text)
    {
        var pos = Math.Clamp(cursorPos, 0, inputText.Length);
        var before = inputText[..pos];
        var after = inputText[pos..];

        var insert = text;
        if (before.Length > 0 && !char.IsWhiteSpace(before[^1]))
        {
            insert = " " + insert;
        }

        if (after.Length > 0 && !char.IsWhiteSpace(after[0]))
        {
            insert += " ";
        }

        inputText = before + insert + after;
        plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();

        pendingCursorCharIndex = pos + insert.Length;
        pendingFocusInput = true;
    }

    private static string PreviewLine(string text, int maxChars)
    {
        var line = text.Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
        if (line.Length <= maxChars)
        {
            return line;
        }

        var cut = char.IsHighSurrogate(line[maxChars - 1]) ? maxChars - 1 : maxChars;
        return line[..cut] + "...";
    }

    private static string SafeLabel(string text)
    {
        while (text.Contains("##", StringComparison.Ordinal))
        {
            text = text.Replace("##", "# #", StringComparison.Ordinal);
        }

        return text;
    }

    private static void DrawActiveWhisperIndicator(Configuration config, string target, int messageCount)
    {
        var color = ChannelDisplay.WhisperColor(target, config);

        var lineHeight = MathF.Max(ImGui.GetTextLineHeight(), ImGui.GetFrameHeight()) + 2f;
        using var indicatorColor = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(color.X, color.Y, color.Z, 0.16f))
            .Push(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.75f));
        using var indicatorRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 4f);
        using var indicatorChild = ImRaii.Child("##activeWhisperIndicator", new Vector2(-1, lineHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var contentHeight = ImGui.GetContentRegionAvail().Y;
        var rowStartY = ImGui.GetCursorPosY();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var iconText = FontAwesomeIcon.EnvelopeOpen.ToIconString();
            var iconHeight = ImGui.CalcTextSize(iconText).Y;
            ImGui.SetCursorPosY(rowStartY + (contentHeight - iconHeight) / 2f);
            ImGui.TextColored(color, iconText);
        }

        ImGui.SameLine();

        var countText = Loc.T("Compose.MessageCount", messageCount);
        var countWidth = ImGui.CalcTextSize(countText).X;
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - countWidth - ImGui.GetStyle().ItemSpacing.X);
        var displayTarget = target;
        var fullText = Loc.T("Whisper.ActiveIndicator", displayTarget);
        if (ImGui.CalcTextSize(fullText).X > availableWidth)
        {
            for (var len = target.Length - 1; len > 0; len--)
            {
                var candidate = target[..len] + "...";
                var candidateText = Loc.T("Whisper.ActiveIndicator", candidate);
                if (ImGui.CalcTextSize(candidateText).X <= availableWidth)
                {
                    displayTarget = candidate;
                    fullText = candidateText;
                    break;
                }
            }
        }

        var textHeight = ImGui.CalcTextSize(fullText).Y;
        ImGui.SetCursorPosY(rowStartY + (contentHeight - textHeight) / 2f);
        ImGui.TextColored(color, fullText);
        if (displayTarget != target && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.ActiveIndicator", target));
        }

        ImGui.SameLine();
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > countWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - countWidth);
        }

        ImGui.SetCursorPosY(rowStartY + (contentHeight - ImGui.CalcTextSize(countText).Y) / 2f);
        ImGui.TextDisabled(countText);
    }

    private void DrawWhisperTarget(CharacterState state)
    {
        var currentTargetName = plugin.GetCurrentTargetPlayerName();
        using (ImRaii.Disabled(currentTargetName == null))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs) && currentTargetName != null)
            {
                plugin.OpenWhisperTab(currentTargetName);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(Loc.T("Whisper.UseTarget"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Users))
        {
            ImGui.OpenPopup("##whisperSuggestions");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.Suggestions"));
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var target = state.LastWhisperTarget;
        if (ImGui.InputTextWithHint("##whisperTargetInput", Loc.T("Whisper.TargetHint"), ref target, 100))
        {
            state.LastWhisperTarget = target;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.TargetLabel"));
        }

        using var suggestionsPopup = ImRaii.Popup("##whisperSuggestions");
        if (suggestionsPopup.Success)
        {
            var partyMembers = plugin.GetPartyMemberNames();
            var recents = state.RecentWhisperTargets;

            if (partyMembers.Count == 0 && recents.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.NoSuggestions"));
            }

            string? picked = null;
            if (partyMembers.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsParty"));
                using var partyId = ImRaii.PushId("party");
                foreach (var name in partyMembers)
                {
                    if (ImGui.Selectable(name))
                    {
                        picked = name;
                    }
                }
            }

            if (recents.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsRecent"));
                using var recentId = ImRaii.PushId("recent");
                foreach (var name in recents)
                {
                    if (ImGui.Selectable(name))
                    {
                        picked = name;
                    }
                }
            }

            if (picked != null)
            {
                plugin.OpenWhisperTab(picked);
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private static Vector4 BlinkColor()
    {
        var pulse = (MathF.Sin(Environment.TickCount64 / 250f) + 1f) * 0.5f;
        var normal = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        return Vector4.Lerp(normal, SearchHighlightColor, pulse);
    }

    private const float TabStripWheelStepPx = 60f;

    private float SnapScrollToTabBoundary(float value)
    {
        var snapped = 0f;
        foreach (var offset in tabStripTabStartOffsets)
        {
            if (offset > value + 0.5f)
            {
                break;
            }

            snapped = offset;
        }

        return snapped;
    }

    private float NextTabBoundaryAfter(float value)
    {
        foreach (var offset in tabStripTabStartOffsets)
        {
            if (offset > value + 0.5f)
            {
                return offset;
            }
        }

        return value;
    }

    private float PrevTabBoundaryBefore(float value)
    {
        var result = 0f;
        foreach (var offset in tabStripTabStartOffsets)
        {
            if (offset >= value - 0.5f)
            {
                break;
            }

            result = offset;
        }

        return result;
    }

    private void DrawLogTabBar(Configuration config, CharacterState state)
    {
        var rowHeight = ImGui.GetFrameHeight();
        var rowOrigin = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            rowOrigin,
            rowOrigin + new Vector2(ImGui.GetContentRegionAvail().X, rowHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, config.TabStripBackgroundOpacity)));

        C4tTheme.DrawLogoItem(plugin);

        var toolbarExpanded = config.ChatToolbarExpanded;
        if (DrawToolbarIconButton(toolbarExpanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown))
        {
            config.ChatToolbarExpanded = !toolbarExpanded;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(toolbarExpanded ? Loc.T("ChatLog.CollapseToolbar") : Loc.T("ChatLog.ExpandToolbar"));
        }

        ImGui.SameLine();

        var arrowSize = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        var stripWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - (arrowSize + spacing) * 2f - spacing);

        var lastTabStart = tabStripTabStartOffsets.Count > 0 ? tabStripTabStartOffsets[^1] : 0f;
        var maxScroll = MathF.Max(MathF.Max(0f, tabStripContentWidth - stripWidth), lastTabStart);
        tabStripScrollRawTarget = Math.Clamp(tabStripScrollRawTarget, 0f, maxScroll);
        tabStripScrollX = SnapScrollToTabBoundary(tabStripScrollRawTarget);

        using (ImRaii.Disabled(tabStripScrollX <= 0f))
        {
            if (DrawToolbarIconButton(FontAwesomeIcon.ChevronLeft, unreadTabScrolledLeft ? SearchHighlightColor : null))
            {
                tabStripScrollX = PrevTabBoundaryBefore(tabStripScrollX);
                tabStripScrollRawTarget = tabStripScrollX;
            }

            if (unreadTabScrolledLeft && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.UnreadTabScrolledOffScreen"));
            }
        }

        ImGui.SameLine();

        var tabsStripChild = ImRaii.Child("##logTabsStrip", new Vector2(stripWidth, arrowSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (TabDragHelper.DrawDropZone(stripWidth, arrowSize) is { } droppedOnMain)
        {
            if (Guid.TryParse(droppedOnMain, out var droppedTabId))
            {
                plugin.MoveTabToMainWindow(droppedTabId);
            }
            else
            {
                plugin.MoveWhisperToMainWindow(droppedOnMain);
            }
        }

        var tabsStripTextAlign = ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

        var basePos = ImGui.GetCursorPos();
        var runningX = 0f;
        string? closedTarget = null;
        float? forceScrollTabStart = null;
        float? forceScrollTabEnd = null;

        tabStripTabStartOffsets.Clear();
        var unreadOffScreenLeft = false;
        var unreadOffScreenRight = false;

        foreach (var tab in config.ChatTabs.ToArray())
        {
            if (plugin.IsTabInSecondaryWindow(tab.Id))
            {
                continue;
            }

            var tabWidth = ImGui.CalcTextSize(tab.Name).X + ImGui.GetStyle().FramePadding.X * 2f;
            tabStripTabStartOffsets.Add(runningX);
            var fixedUnread = fixedTabUnreadCounts.GetValueOrDefault(tab.Id);

            if (tab.Id == pendingFixedTabForceSelect)
            {
                forceScrollTabStart = runningX;
                forceScrollTabEnd = runningX + tabWidth;
            }

            if (fixedUnread > 0)
            {
                if (runningX + tabWidth <= tabStripScrollX + 0.5f)
                {
                    unreadOffScreenLeft = true;
                }
                else if (runningX >= tabStripScrollX + stripWidth - 0.5f)
                {
                    unreadOffScreenRight = true;
                }
            }

            if (runningX >= tabStripScrollX - 0.5f && runningX + tabWidth <= tabStripScrollX + stripWidth + 0.5f)
            {
                var isSelected = string.IsNullOrEmpty(activeWhisperTabTarget) && !noviceNetworkTabActive && activeTabId == tab.Id;
                var tabSize = new Vector2(tabWidth, arrowSize);

                ImGui.SetCursorPos(new Vector2(basePos.X + runningX - tabStripScrollX, basePos.Y));
                DrawTabBackgroundFill(tab.BackgroundColor ?? (config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null), tabSize);
                var clicked = ImGui.Selectable(tab.Name + "##tab" + tab.Id, isSelected, ImGuiSelectableFlags.None, tabSize);
                DrawActiveTabUnderline(isSelected, config);

                DrawUnreadBadge(fixedUnread, fixedTabMentionCounts.GetValueOrDefault(tab.Id) > 0);

                if (!tab.PositionLocked)
                {
                    TabDragHelper.HandleHoverAndTearOff(tab.Id.ToString(), tab.Name, dropPos =>
                    {
                        if (!plugin.HasSecondaryWindow)
                        {
                            plugin.MoveTabToSecondaryWindow(tab.Id, dropPos);
                        }
                    });
                    if (TabDragHelper.DrawReorderTarget(tab.Id.ToString(), out var insertBefore) is { } droppedTabIdStr
                        && Guid.TryParse(droppedTabIdStr, out var droppedTabId))
                    {
                        plugin.MoveTabToMainWindow(droppedTabId);

                        var draggedIndex = config.ChatTabs.FindIndex(t => t.Id == droppedTabId);
                        if (draggedIndex >= 0)
                        {
                            var draggedTab = config.ChatTabs[draggedIndex];
                            config.ChatTabs.RemoveAt(draggedIndex);
                            var targetIndex = config.ChatTabs.IndexOf(tab);
                            config.ChatTabs.Insert(insertBefore ? targetIndex : targetIndex + 1, draggedTab);
                            plugin.SaveConfiguration();
                        }
                    }
                }

                if (clicked && !isSelected)
                {
                    SwitchToFixedTab(config, state, tab);
                    fixedTabUnreadCounts.Remove(tab.Id);
                    fixedTabMentionCounts.Remove(tab.Id);
                }

                DrawFixedTabQuickEdit(tab);
            }

            runningX += tabWidth + spacing;
        }

        if (NoviceNetworkService.IsConnected)
        {
            var noviceLabel = Loc.T("Channel.NoviceNetwork");
            var noviceTabWidth = ImGui.CalcTextSize(noviceLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
            tabStripTabStartOffsets.Add(runningX);

            if (noviceNetworkUnread > 0)
            {
                if (runningX + noviceTabWidth <= tabStripScrollX + 0.5f)
                {
                    unreadOffScreenLeft = true;
                }
                else if (runningX >= tabStripScrollX + stripWidth - 0.5f)
                {
                    unreadOffScreenRight = true;
                }
            }

            if (runningX >= tabStripScrollX - 0.5f && runningX + noviceTabWidth <= tabStripScrollX + stripWidth + 0.5f)
            {
                var isSelected = noviceNetworkTabActive;
                var tabSize = new Vector2(noviceTabWidth, arrowSize);

                ImGui.SetCursorPos(new Vector2(basePos.X + runningX - tabStripScrollX, basePos.Y));
                var clicked = ImGui.Selectable(noviceLabel + "##noviceNetworkTab", isSelected, ImGuiSelectableFlags.None, tabSize);
                DrawActiveTabUnderline(isSelected, config);

                DrawUnreadBadge(noviceNetworkUnread);

                if (clicked && !isSelected)
                {
                    EnterNoviceNetworkView(state);
                    noviceNetworkUnread = 0;
                }
            }

            runningX += noviceTabWidth + spacing;
        }
        else if (noviceNetworkTabActive)
        {
            noviceNetworkTabActive = false;
            noviceNetworkUnread = 0;
            ViewChanged?.Invoke();
        }

        foreach (var targetName in state.RecentWhisperTargets.ToArray())
        {
            if (plugin.IsWhisperInSecondaryWindow(targetName))
            {
                continue;
            }

            var whisperText = FirstName(targetName);
            var tabWidth = ImGui.CalcTextSize(whisperText).X + ImGui.GetStyle().FramePadding.X * 2f;
            tabStripTabStartOffsets.Add(runningX);
            var whisperUnread = whisperUnreadCounts.GetValueOrDefault(targetName);

            if (targetName == pendingTabForceSelect)
            {
                forceScrollTabStart = runningX;
                forceScrollTabEnd = runningX + tabWidth;
            }

            if (whisperUnread > 0)
            {
                if (runningX + tabWidth <= tabStripScrollX + 0.5f)
                {
                    unreadOffScreenLeft = true;
                }
                else if (runningX >= tabStripScrollX + stripWidth - 0.5f)
                {
                    unreadOffScreenRight = true;
                }
            }

            if (runningX >= tabStripScrollX - 0.5f && runningX + tabWidth <= tabStripScrollX + stripWidth + 0.5f)
            {
                var isSelected = activeWhisperTabTarget == targetName;
                var tabSize = new Vector2(tabWidth, arrowSize);

                ImGui.SetCursorPos(new Vector2(basePos.X + runningX - tabStripScrollX, basePos.Y));
                var whisperBg = config.WhisperTabBackgroundColours.TryGetValue(targetName, out var customBg)
                    ? customBg
                    : config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null;
                DrawTabBackgroundFill(whisperBg, tabSize);

                var blinking = whisperUnread > 0 && config.BlinkUnreadWhisperTabs;
                bool clicked;
                using (ImRaii.PushColor(ImGuiCol.Text, BlinkColor(), blinking))
                {
                    clicked = ImGui.Selectable(whisperText + "##whisper" + targetName, isSelected, ImGuiSelectableFlags.None, tabSize);
                }

                DrawActiveTabUnderline(isSelected, config);
                DrawUnreadBadge(whisperUnread);

                var locked = state.LockedWhisperTargets.Contains(targetName);
                if (!locked)
                {
                    TabDragHelper.HandleHoverAndTearOff(targetName, whisperText, dropPos =>
                    {
                        if (!plugin.HasSecondaryWindow)
                        {
                            plugin.MoveWhisperToSecondaryWindow(targetName, dropPos);
                        }
                    });
                    if (TabDragHelper.DrawReorderTarget(targetName, out var insertBefore) is { } droppedTargetName
                        && !Guid.TryParse(droppedTargetName, out _))
                    {
                        plugin.MoveWhisperToMainWindow(droppedTargetName);

                        var draggedIndex = state.RecentWhisperTargets.IndexOf(droppedTargetName);
                        if (draggedIndex >= 0)
                        {
                            state.RecentWhisperTargets.RemoveAt(draggedIndex);
                            var targetIndex = state.RecentWhisperTargets.IndexOf(targetName);
                            state.RecentWhisperTargets.Insert(insertBefore ? targetIndex : targetIndex + 1, droppedTargetName);
                            plugin.SaveConfiguration();
                        }
                    }
                }

                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    closedTarget = targetName;
                }

                if (ImGui.IsItemHovered() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ImGui.SetTooltip(targetName);
                }

                if (clicked && !isSelected)
                {
                    EnterWhisperView(config, state, targetName);
                    whisperUnreadCounts.Remove(targetName);
                    plugin.SaveConfiguration();
                }

                if (DrawWhisperTabQuickEdit(state, targetName))
                {
                    closedTarget = targetName;
                }
            }

            runningX += tabWidth + spacing;
        }

        if (forceScrollTabStart is { } scrollStart && forceScrollTabEnd is { } scrollEnd
            && (scrollStart < tabStripScrollX || scrollEnd > tabStripScrollX + stripWidth))
        {
            tabStripScrollX = scrollStart;
            tabStripScrollRawTarget = tabStripScrollX;
        }

        tabStripContentWidth = runningX;
        unreadTabScrolledLeft = unreadOffScreenLeft;
        unreadTabScrolledRight = unreadOffScreenRight;

        var wheelMaxScroll = MathF.Max(MathF.Max(0f, tabStripContentWidth - stripWidth), tabStripTabStartOffsets.Count > 0 ? tabStripTabStartOffsets[^1] : 0f);

        if (config.TabStripWheelScrollEnabled && ImGui.IsWindowHovered())
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
            {
                tabStripScrollRawTarget = Math.Clamp(tabStripScrollRawTarget - wheel * TabStripWheelStepPx, 0f, wheelMaxScroll);
                tabStripScrollX = SnapScrollToTabBoundary(tabStripScrollRawTarget);
            }
        }

        tabsStripTextAlign.Dispose();
        tabsStripChild.Dispose();

        ImGui.SameLine();
        using (ImRaii.Disabled(tabStripScrollX >= wheelMaxScroll))
        {
            if (DrawToolbarIconButton(FontAwesomeIcon.ChevronRight, unreadTabScrolledRight ? SearchHighlightColor : null))
            {
                tabStripScrollX = MathF.Min(wheelMaxScroll, NextTabBoundaryAfter(tabStripScrollX));
                tabStripScrollRawTarget = tabStripScrollX;
            }

            if (unreadTabScrolledRight && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.UnreadTabScrolledOffScreen"));
            }
        }

        pendingTabForceSelect = null;
        pendingFixedTabForceSelect = null;

        if (closedTarget != null)
        {
            CloseWhisperTab(config, state, closedTarget);
        }
    }

    internal void CloseWhisperTab(Configuration config, CharacterState state, string target)
    {
        state.RecentWhisperTargets.Remove(target);
        whisperUnreadCounts.Remove(target);

        if (activeWhisperTabTarget == target)
        {
            if (config.ChatTabs.Count > 0)
            {
                SwitchToFixedTab(config, state, config.ChatTabs[0]);
            }
            else
            {
                activeWhisperTabTarget = string.Empty;
                activeTabId = Guid.Empty;
            }
        }

        plugin.SaveConfiguration();
        ViewChanged?.Invoke();
    }

    internal static void DrawTabBackgroundFill(Vector4? color, Vector2 size)
    {
        if (color is not { } fill)
        {
            return;
        }

        var min = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(min, min + size, ImGui.GetColorU32(fill), ImGui.GetStyle().FrameRounding);
    }

    private const float ActiveTabUnderlineThickness = 2.5f;

    internal static void DrawActiveTabUnderline(bool isSelected, Configuration config)
    {
        if (!isSelected)
        {
            return;
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(min.X, max.Y - ActiveTabUnderlineThickness),
            new Vector2(max.X, max.Y),
            ImGui.GetColorU32(C4tTheme.ActiveMarkerColor(config)));
    }

    internal static void OpenUrl(string url, Plugin plugin)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, $"[CelinesChat] Konnte Link nicht oeffnen: {url}");
        }
    }

    internal void SwitchToFixedTab(Configuration config, CharacterState state, ChatTab newTab)
    {
        if (string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            SaveChannelSelection(config.ChatTabs.Find(t => t.Id == activeTabId), state);
        }

        activeTabId = newTab.Id;
        activeWhisperTabTarget = string.Empty;

        noviceNetworkTabActive = false;

        LoadChannelSelection(newTab, state);
        SaveLastActiveView(state, tabId: newTab.Id);
        ViewChanged?.Invoke();
    }

    private void SaveLastActiveView(CharacterState state, Guid? tabId = null, string? whisperTarget = null, bool noviceNetwork = false)
    {
        state.LastActiveTabId = tabId;
        state.LastActiveWhisperTarget = whisperTarget;
        state.LastActiveNoviceNetwork = noviceNetwork;
        plugin.SaveConfiguration();
    }

    private void RestoreLastActiveView(Configuration config, CharacterState state)
    {
        forceScrollToBottomOnNextViewChange = true;

        lastViewKey = string.Empty;

        if (!string.IsNullOrEmpty(state.LastActiveWhisperTarget))
        {
            var target = plugin.RememberWhisperTarget(state, state.LastActiveWhisperTarget);
            activeTabId = Guid.Empty;
            activeWhisperTabTarget = target;
            pendingTabForceSelect = target;
            noviceNetworkTabActive = false;
            PreloadWhisperHistoryForFirstView(target);
            ViewChanged?.Invoke();
            return;
        }

        if (state.LastActiveNoviceNetwork && NoviceNetworkService.IsConnected)
        {
            activeTabId = Guid.Empty;
            activeWhisperTabTarget = string.Empty;
            noviceNetworkTabActive = true;
            ViewChanged?.Invoke();
            return;
        }

        if (state.LastActiveTabId is { } tabId && config.ChatTabs.Find(t => t.Id == tabId) is { } tab)
        {
            activeTabId = tab.Id;
            activeWhisperTabTarget = string.Empty;
            noviceNetworkTabActive = false;
            pendingFixedTabForceSelect = tab.Id;
            LoadChannelSelection(tab, state);
            ViewChanged?.Invoke();
        }
    }

    private static void SaveChannelSelection(ChatTab? tab, CharacterState state)
    {
        if (tab == null)
        {
            return;
        }

        tab.LastChannel = state.LastChannel;
        tab.LinkshellNumber = state.LinkshellNumber;
        tab.CrossWorldLinkshellNumber = state.CrossWorldLinkshellNumber;
        tab.LastWhisperTarget = state.LastWhisperTarget;
    }

    private static void LoadChannelSelection(ChatTab tab, CharacterState state)
    {
        state.LastChannel = tab.LastChannel;
        state.LinkshellNumber = tab.LinkshellNumber;
        state.CrossWorldLinkshellNumber = tab.CrossWorldLinkshellNumber;
        state.LastWhisperTarget = tab.LastWhisperTarget;
    }

    internal void EnterWhisperView(Configuration config, CharacterState state, string target)
    {
        if (string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            SaveChannelSelection(config.ChatTabs.Find(t => t.Id == activeTabId), state);
        }

        var resolvedTarget = plugin.RememberWhisperTarget(state, target);

        state.LastWhisperTarget = resolvedTarget;
        state.LastChannel = ChatChannel.Whisper;
        activeWhisperTabTarget = resolvedTarget;
        pendingTabForceSelect = resolvedTarget;

        PreloadWhisperHistoryForFirstView(resolvedTarget);

        noviceNetworkTabActive = false;

        SaveLastActiveView(state, whisperTarget: resolvedTarget);
        ViewChanged?.Invoke();
    }

    internal void EnterNoviceNetworkView(CharacterState state)
    {
        activeWhisperTabTarget = string.Empty;
        noviceNetworkTabActive = true;
        SaveLastActiveView(state, noviceNetwork: true);
        ViewChanged?.Invoke();
    }

    private void DrawFixedTabQuickEdit(ChatTab tab)
    {
        using var popup = ImRaii.ContextPopupItem("##tabQuickEdit" + tab.Id);
        if (!popup.Success)
        {
            return;
        }

        ImGui.SetNextItemWidth(160);
        var name = tab.Name;
        if (ImGui.InputText(Loc.T("Tabs.NameLabel") + "##rename" + tab.Id, ref name, 60))
        {
            tab.Name = name;
            plugin.SaveConfiguration();
        }

        var locked = tab.PositionLocked;
        if (ImGui.Checkbox(Loc.T("Tabs.PositionLocked") + "##lock" + tab.Id, ref locked))
        {
            tab.PositionLocked = locked;
            plugin.SaveConfiguration();
        }

        DrawTabNotifyModeCombo(tab);

        DrawTabChannelMenu(tab);

        var showTimestamps = tab.ShowTimestamps;
        if (ImGui.Checkbox(Loc.T("Tabs.ShowTimestamps") + "##timestamps" + tab.Id, ref showTimestamps))
        {
            tab.ShowTimestamps = showTimestamps;
            plugin.InvalidateChatLayout();
            plugin.SaveConfiguration();
        }

        var discordMode = tab.DiscordMode;
        if (ImGui.Checkbox(Loc.T("Tabs.DiscordMode") + "##discord" + tab.Id, ref discordMode))
        {
            tab.DiscordMode = discordMode;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.DiscordModeTooltip"));
        }

        var group = plugin.Configuration.GroupConsecutiveMessages;
        if (ImGui.Checkbox(Loc.T("Tabs.GroupMessages") + "##group" + tab.Id, ref group))
        {
            plugin.Configuration.GroupConsecutiveMessages = group;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.GroupMessagesTooltip"));
        }

        var tabBg = tab.BackgroundColor ?? plugin.Configuration.TabStripBackgroundColor;
        ImGui.SetNextItemWidth(160);
        if (ImGui.ColorEdit4(Loc.T("Tabs.BackgroundColorLabel") + "##tabBg" + tab.Id, ref tabBg))
        {
            tab.BackgroundColor = tabBg;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.UndoAlt))
        {
            tab.BackgroundColor = null;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
        }
    }

    internal void DrawTabNotifyModeCombo(ChatTab tab)
    {
        ImGui.SetNextItemWidth(160);
        using (var combo = ImRaii.Combo(Loc.T("Tabs.NotifyMode") + "##notify" + tab.Id, NotifyModeName(tab.NotifyMode)))
        {
            if (combo.Success)
            {
                foreach (var mode in new[] { TabNotifyMode.All, TabNotifyMode.MentionsOnly, TabNotifyMode.Never })
                {
                    if (ImGui.Selectable(NotifyModeName(mode), tab.NotifyMode == mode))
                    {
                        tab.NotifyMode = mode;
                        fixedTabUnreadCounts.Remove(tab.Id);
                        fixedTabMentionCounts.Remove(tab.Id);
                        plugin.SaveConfiguration();
                    }
                }
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.NotifyModeTooltip"));
        }
    }

    internal static string NotifyModeName(TabNotifyMode mode) => mode switch
    {
        TabNotifyMode.MentionsOnly => Loc.T("Tabs.NotifyMentions"),
        TabNotifyMode.Never => Loc.T("Tabs.NotifyNever"),
        _ => Loc.T("Tabs.NotifyAll"),
    };

    private void DrawTabChannelMenu(ChatTab tab)
    {
        using var menu = ImRaii.Menu(Loc.T("Tabs.ChannelsMenu", tab.IncludedChannels.Count) + "##channels" + tab.Id);
        if (!menu.Success)
        {
            return;
        }

        foreach (var group in ChannelDisplay.AllGroups)
        {
            using var groupMenu = ImRaii.Menu(ChannelDisplay.GroupName(group) + "##group" + group);
            if (!groupMenu.Success)
            {
                continue;
            }

            foreach (var category in ChannelDisplay.AllCategories)
            {
                if (ChannelDisplay.GroupOf(category) != group)
                {
                    continue;
                }

                var included = tab.IncludedChannels.Contains(category);
                bool toggled;
                using (ImRaii.PushColor(ImGuiCol.Text, ChannelDisplay.Color(category, plugin.Configuration)))
                {
                    toggled = ImGui.MenuItem(ChannelDisplay.CategoryName(category) + "##menuCat" + category, string.Empty, included);
                }

                if (toggled)
                {
                    if (included)
                    {
                        tab.IncludedChannels.Remove(category);
                    }
                    else
                    {
                        tab.IncludedChannels.Add(category);
                    }

                    plugin.SaveConfiguration();
                }

                if (category == ChatCategory.Linkshell && tab.IncludedChannels.Contains(category))
                {
                    DrawLinkshellMenuItems(NativeChannels.AllSlots(NativeChannels.GetExistingLinkshells()), tab.IncludedLinkshellNumbers, "LS");
                }
                else if (category == ChatCategory.CrossWorldLinkshell && tab.IncludedChannels.Contains(category))
                {
                    DrawLinkshellMenuItems(NativeChannels.AllSlots(NativeChannels.GetExistingCrossWorldLinkshells()), tab.IncludedCrossWorldLinkshellNumbers, "CW");
                }
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem(Loc.T("Tabs.ChannelsMoreSettings")))
        {
            plugin.OpenSettingsPage(SettingsPage.Tabs);
        }
    }

    private void DrawLinkshellMenuItems(List<(int Number, string Name)> slots, HashSet<int> included, string prefix)
    {
        using var indent = ImRaii.PushIndent();
        foreach (var (number, name) in slots)
        {
            var label = name.Length > 0 ? $"{prefix}{number}: {name}" : $"{prefix}{number}";
            var isChecked = included.Count == 0 || included.Contains(number);
            if (ImGui.MenuItem(label + "##menu" + prefix + number, string.Empty, isChecked))
            {
                if (included.Count == 0)
                {
                    foreach (var (existing, _) in slots)
                    {
                        included.Add(existing);
                    }
                }

                if (isChecked)
                {
                    included.Remove(number);
                }
                else
                {
                    included.Add(number);
                }

                plugin.SaveConfiguration();
            }
        }
    }

    private bool DrawWhisperTabQuickEdit(CharacterState state, string targetName)
    {
        using var popup = ImRaii.ContextPopupItem("##whisperTabQuickEdit" + targetName);
        if (!popup.Success)
        {
            return false;
        }

        var closeRequested = false;
        if (ImGui.Selectable(Loc.T("Tabs.CloseConversation") + "##closeWhisper" + targetName))
        {
            closeRequested = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();

        var locked = state.LockedWhisperTargets.Contains(targetName);
        if (ImGui.Checkbox(Loc.T("Tabs.PositionLocked") + "##lockWhisper" + targetName, ref locked))
        {
            if (locked)
            {
                state.LockedWhisperTargets.Add(targetName);
            }
            else
            {
                state.LockedWhisperTargets.Remove(targetName);
            }

            plugin.SaveConfiguration();
        }

        var muted = state.MutedWhisperTargets.Contains(targetName);
        if (ImGui.Checkbox(Loc.T("Tabs.Muted") + "##muteWhisper" + targetName, ref muted))
        {
            if (muted)
            {
                state.MutedWhisperTargets.Add(targetName);
            }
            else
            {
                state.MutedWhisperTargets.Remove(targetName);
            }

            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.MutedTooltip"));
        }

        var config = plugin.Configuration;

        var discordWhispers = config.DiscordModeWhispers;
        if (ImGui.Checkbox(Loc.T("Tabs.DiscordModeWhispers") + "##discordWhisper" + targetName, ref discordWhispers))
        {
            config.DiscordModeWhispers = discordWhispers;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.DiscordModeTooltip"));
        }

        var color = ChannelDisplay.WhisperColor(targetName, config);
        ImGui.SetNextItemWidth(160);
        if (ImGui.ColorEdit4(Loc.T("Tabs.WhisperColorLabel") + "##whisperColor" + targetName, ref color))
        {
            config.WhisperColours[targetName] = color;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("resetWhisperColor" + targetName, FontAwesomeIcon.UndoAlt))
        {
            config.WhisperColours.Remove(targetName);
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
        }

        var tabBg = config.WhisperTabBackgroundColours.TryGetValue(targetName, out var customBg) ? customBg : config.TabStripBackgroundColor;
        ImGui.SetNextItemWidth(160);
        if (ImGui.ColorEdit4(Loc.T("Tabs.BackgroundColorLabel") + "##whisperTabBg" + targetName, ref tabBg))
        {
            config.WhisperTabBackgroundColours[targetName] = tabBg;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("resetWhisperTabBg" + targetName, FontAwesomeIcon.UndoAlt))
        {
            config.WhisperTabBackgroundColours.Remove(targetName);
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
        }

        return closeRequested;
    }

    internal static void DrawUnreadBadge(int count, bool important = false)
    {
        if (count <= 0)
        {
            return;
        }

        var label = (important ? "@" : string.Empty) + (count > 99 ? "99+" : count.ToString());
        var textSize = ImGui.CalcTextSize(label);
        var radius = MathF.Max(7f, (textSize.X / 2f) + 3f);

        var max = ImGui.GetItemRectMax();
        var min = ImGui.GetItemRectMin();

        var center = new Vector2(max.X - radius * 0.6f, min.Y + radius);

        var drawList = ImGui.GetWindowDrawList();
        var fill = important ? new Vector4(0.95f, 0.68f, 0.12f, 1f) : new Vector4(0.85f, 0.15f, 0.15f, 1f);
        var textColor = important ? new Vector4(0.12f, 0.08f, 0f, 1f) : new Vector4(1f, 1f, 1f, 1f);
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill));
        drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(textColor), label);
    }

    internal ChatTab? GetActiveFixedTab(Configuration config)
    {
        if (config.ChatTabs.Count == 0)
        {
            return null;
        }

        return config.ChatTabs.Find(t => t.Id == activeTabId) ?? config.ChatTabs[0];
    }

    private void RefreshWhisperHistoryForTarget(string target)
    {
        whisperHistoryShownThisSession.Remove(target);
        PreloadWhisperHistoryForFirstView(target);
    }

    private void PreloadWhisperHistoryForFirstView(string target)
    {
        if (!whisperHistoryShownThisSession.Add(target))
        {
            return;
        }

        pendingWhisperHistoryLoads[target] = Task.Run(() => plugin.ChatLog.LoadWhisperHistory(target));
    }

    private void ApplyCompletedWhisperHistoryLoads()
    {
        if (pendingWhisperHistoryLoads.Count == 0)
        {
            return;
        }

        List<string>? finished = null;
        foreach (var (target, task) in pendingWhisperHistoryLoads)
        {
            if (!task.IsCompleted)
            {
                continue;
            }

            finished ??= new List<string>();
            finished.Add(target);

            if (task.IsCompletedSuccessfully)
            {
                ApplyWhisperHistoryLoadResult(target, task.Result);
            }
            else
            {
                plugin.Log.Warning(task.Exception, $"[CelinesChat] Konnte Whisper-Historie fuer '{target}' nicht im Hintergrund laden.");
            }
        }

        if (finished == null)
        {
            return;
        }

        foreach (var target in finished)
        {
            pendingWhisperHistoryLoads.Remove(target);
        }
    }

    private void ApplyWhisperHistoryLoadResult(string target, List<ChatLogEntry> fileEntries)
    {
        if (fileEntries.Count == 0)
        {
            return;
        }

        var alreadyLive = new HashSet<(XivChatType, string, string?, string, DateTime)>();
        foreach (var live in plugin.ChatLog.Snapshot())
        {
            alreadyLive.Add((live.ChatType, live.Sender, live.SenderWorld, live.Text, TruncateToSeconds(live.Timestamp)));
        }

        var deduped = new List<ChatLogEntry>();
        foreach (var fileEntry in fileEntries)
        {
            if (!alreadyLive.Contains((fileEntry.ChatType, fileEntry.Sender, fileEntry.SenderWorld, fileEntry.Text, TruncateToSeconds(fileEntry.Timestamp))))
            {
                fileEntry.Sequence = nextPreloadSequence--;
                deduped.Add(fileEntry);
            }
        }

        if (deduped.Count > 0)
        {
            whisperPreloadedHistory[target] = deduped;
        }
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

    private static IEnumerable<ChatLogEntry> MergeByTimestamp(IReadOnlyList<ChatLogEntry> a, IReadOnlyList<ChatLogEntry> b)
    {
        var i = 0;
        var j = 0;
        while (i < a.Count && j < b.Count)
        {
            yield return a[i].Timestamp <= b[j].Timestamp ? a[i++] : b[j++];
        }

        while (i < a.Count)
        {
            yield return a[i++];
        }

        while (j < b.Count)
        {
            yield return b[j++];
        }
    }

    private void OpenWhisperTabWithHistory(string target)
    {
        plugin.OpenWhisperTab(target);
        RefreshWhisperHistoryForTarget(target);
    }

    internal void RenameWhisperTargetInMemory(string oldTarget, string newTarget)
    {
        if (whisperUnreadCounts.Remove(oldTarget, out var unread))
        {
            whisperUnreadCounts[newTarget] = unread;
        }

        if (whisperCloseCutoff.Remove(oldTarget, out var cutoff))
        {
            whisperCloseCutoff[newTarget] = cutoff;
        }

        if (whisperHistoryShownThisSession.Remove(oldTarget))
        {
            whisperHistoryShownThisSession.Add(newTarget);
        }

        if (whisperPreloadedHistory.Remove(oldTarget, out var preloaded))
        {
            whisperPreloadedHistory[newTarget] = preloaded;
        }

        if (pendingWhisperHistoryLoads.Remove(oldTarget, out var pendingLoad))
        {
            pendingWhisperHistoryLoads[newTarget] = pendingLoad;
        }

        if (string.Equals(activeWhisperTabTarget, oldTarget, StringComparison.OrdinalIgnoreCase))
        {
            activeWhisperTabTarget = newTarget;
        }

        if (loadedHistoryEntries != null && string.Equals(loadedHistoryLabel, oldTarget, StringComparison.OrdinalIgnoreCase))
        {
            loadedHistoryEntries = null;
            loadedHistoryLabel = null;
        }
    }

    internal bool MatchesWhisperTarget(ChatLogEntry entry, string target)
    {
        if (!MatchesWhisperTargetIgnoringCutoff(entry, target))
        {
            return false;
        }

        if (whisperCloseCutoff.TryGetValue(target, out var cutoff) && entry.Sequence <= cutoff)
        {
            return false;
        }

        return true;
    }

    internal static bool MatchesWhisperTargetIgnoringCutoff(ChatLogEntry entry, string target)
    {
        var atIndex = target.IndexOf('@');
        var targetNameOnly = atIndex >= 0 ? target.AsSpan(0, atIndex) : target.AsSpan();

        if (entry.ChatType == XivChatType.ErrorMessage)
        {
            return entry.RelatedWhisperTarget != null
                && targetNameOnly.Equals(entry.RelatedWhisperTarget, StringComparison.OrdinalIgnoreCase);
        }

        return entry.ChatType is XivChatType.TellIncoming or XivChatType.TellOutgoing
            && targetNameOnly.Equals(entry.Sender, StringComparison.OrdinalIgnoreCase);
    }

    private static string CurrentChannelLabel(CharacterState state) => state.LastChannel switch
    {
        ChatChannel.Say => Loc.T("Channel.Say"),
        ChatChannel.Party => Loc.T("Channel.Party"),
        ChatChannel.Whisper => string.IsNullOrEmpty(state.LastWhisperTarget)
            ? Loc.T("Channel.Whisper")
            : Loc.T("Channel.WhisperWithTarget", FirstName(state.LastWhisperTarget)),
        ChatChannel.Yell => Loc.T("Channel.Yell"),
        ChatChannel.Shout => Loc.T("Channel.Shout"),
        ChatChannel.FreeCompany => Loc.T("Channel.FreeCompany"),
        ChatChannel.Alliance => Loc.T("Channel.Alliance"),
        ChatChannel.PvpTeam => Loc.T("Channel.PvpTeam"),
        ChatChannel.NoviceNetwork => Loc.T("Channel.NoviceNetwork"),
        ChatChannel.Linkshell => FormatNumberedChannelLabel(Loc.T("Channel.Linkshell"), state.LinkshellNumber, NativeChannels.GetExistingLinkshells()),
        ChatChannel.CrossWorldLinkshell => FormatNumberedChannelLabel(Loc.T("Channel.CrossWorldLinkshell"), state.CrossWorldLinkshellNumber, NativeChannels.GetExistingCrossWorldLinkshells()),
        _ => state.LastChannel.ToString(),
    };

    private static string FormatNumberedChannelLabel(string baseName, int number, List<(int Number, string Name)> existing)
    {
        var match = existing.Find(e => e.Number == number);
        return match.Name != null ? $"{baseName} [{number}]: {match.Name}" : $"{baseName} [{number}]";
    }

    internal static string FirstName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return targetName;
        }

        var atIndex = targetName.IndexOf('@');
        var namePart = atIndex >= 0 ? targetName[..atIndex] : targetName;
        var spaceIndex = namePart.IndexOf(' ');
        return spaceIndex >= 0 ? namePart[..spaceIndex] : namePart;
    }

    private static ChatCategory CategoryForChannel(ChatChannel channel) => channel switch
    {
        ChatChannel.Say => ChatCategory.Say,
        ChatChannel.Party => ChatCategory.Party,
        ChatChannel.Whisper => ChatCategory.Whisper,
        ChatChannel.Yell => ChatCategory.Yell,
        ChatChannel.Shout => ChatCategory.Shout,
        ChatChannel.FreeCompany => ChatCategory.FreeCompany,
        ChatChannel.Linkshell => ChatCategory.Linkshell,
        ChatChannel.CrossWorldLinkshell => ChatCategory.CrossWorldLinkshell,
        ChatChannel.Alliance => ChatCategory.Alliance,
        ChatChannel.PvpTeam => ChatCategory.PvpTeam,
        ChatChannel.NoviceNetwork => ChatCategory.NoviceNetwork,
        _ => ChatCategory.Say,
    };
}
