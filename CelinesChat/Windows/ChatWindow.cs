using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal sealed class ChatWindow : Window
{
    private const string GenericDraftKey = "*generic*";
    private const float SplitterHeight = 6f;
    private const float MaxComposeAreaHeight = 400f;

    private const float FooterBottomPaddingPx = 6f;

    // See nearBottomBeforeDraw's own remarks - deliberately small, well under one rendered line's
    // height, so it absorbs SetScrollHereY(1f)'s own few-pixel settling gap without reintroducing the
    // old wide-tolerance manual-scroll-hijack bug.
    private const float BottomToleranceForNewMessagePx = 12f;
    private static readonly Vector4 SearchHighlightColor = new(1f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private readonly Plugin plugin;

    private string inputText = string.Empty;
    private string currentDraftKey = string.Empty;
    private string validationMessage = string.Empty;
    private string newSnippetName = string.Empty;
    private string snippetSearchFilter = string.Empty;
    private string activeWhisperTabTarget = string.Empty;

    private bool noviceNetworkTabActive;

    private int noviceNetworkUnread;
    private string? pendingTabForceSelect;
    private int selectionStart;
    private int selectionEnd;
    private int cursorPos;

    private int? pendingCursorCharIndex;
    private string cachedInput = string.Empty;
    private int cachedMaxLength = -1;
    private List<string> cachedChunks = new();

    private int historyBrowseIndex = -1;
    private string historyBrowseOriginalDraft = string.Empty;
    private bool splitterDragging;

    private float measuredFooterHeight = 60f;
    private bool pendingSendFromEnter;
    private bool pendingFocusInput;
    private bool composeBoxActive;
    private bool pendingAutoTranslatePopup;
    private string autoTranslateSearch = string.Empty;
    private uint? autoTranslateBrowseGroup;

    private Guid activeTabId;

    // Global watermark, advanced to the newest entry across *all* channels every frame - used only
    // to decide, exactly once per entry, whether TrackUnreadForNewEntry still needs to route it to
    // an unread badge somewhere. Deliberately global: unread badges live on tabs/whispers that may
    // not be the active view right now, so "is this entry new" for badge purposes has to mean "new
    // since I last looked at anything", not "new to whatever's on screen this frame".
    private long lastSeenSequence;

    // "Highest sequence this specific view has actually drawn", keyed by viewKey (same key space as
    // viewScrollPositions) - separate from lastSeenSequence above. The auto-scroll-to-bottom trigger
    // used to key off that same global watermark, which meant a busy fixed tab's own traffic (or any
    // other channel's) could race a low-traffic whisper tab's watermark past a whisper message before
    // that view ever got credit for it as "new" - the message still rendered fine, just without the
    // scroll, since by the time the whisper tab was actually drawn the global watermark had already
    // moved past it. Tracking per view means each view's own "have I auto-scrolled for this yet"
    // watermark only advances based on entries that actually matched *its own* filter.
    private readonly Dictionary<string, long> lastSeenSequenceByView = new();
    private long initialSeenSequence;
    private long nextPreloadSequence = -1;

    private bool lastKnownFontAvailable = true;

    // Last-measured render height per entry (keyed by ChatLogEntry.Sequence, stable across frames
    // and buffer eviction - see PreloadWhisperHistoryForFirstView/DrawHistoryDatesPopup for why
    // file-loaded entries get their own negative Sequence range so they can't collide with each
    // other or with live entries here). Lets DrawChatLogEntries skip the real per-word ImGui work
    // for an entry sitting well outside the visible scroll area and draw a plain placeholder of its
    // last-known height instead - see its own remarks for why this matters once a tab holds enough
    // messages for the *volume* of full per-frame redraw work itself to become the bottleneck.
    private readonly Dictionary<long, float> entryHeightCache = new();
    private readonly HashSet<long> provisionalHeightSequences = new();

    internal Guid ActiveTabId => activeTabId;

    internal string ActiveWhisperTabTarget => activeWhisperTabTarget;

    internal event Action? ViewChanged;

    private float tabStripScrollX;

    private float tabStripScrollRawTarget;
    private float tabStripContentWidth;

    private readonly List<float> tabStripTabStartOffsets = new();

    // Reused every frame (Clear() keeps the backing array, no reallocation once warmed up) for the
    // active view's own capped, filtered entry list - see DrawChatLogEntries' matching remarks.
    private readonly List<ChatLogEntry> viewMatchingLiveEntriesBuffer = new();
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

    private readonly Dictionary<string, long> whisperCloseCutoff = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Guid, long> fixedTabClearCutoff = new();

    private long? noviceNetworkClearCutoff;

    private bool rightClickedOnName;

    private ImGuiWindowFlags baseFlags;

    public ChatWindow(Plugin plugin) : base(WindowTitles.Chat)
    {
        this.plugin = plugin;
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

        var initialState = plugin.GetCharacterState();
        if (initialState.LastChannel == ChatChannel.Whisper && !string.IsNullOrEmpty(initialState.LastWhisperTarget))
        {
            activeWhisperTabTarget = plugin.RememberWhisperTarget(initialState, initialState.LastWhisperTarget);
            pendingTabForceSelect = activeWhisperTabTarget;

            PreloadWhisperHistoryForFirstView(activeWhisperTabTarget);
        }

        var existingEntries = plugin.ChatLog.Snapshot();
        if (existingEntries.Count > 0)
        {
            lastSeenSequence = existingEntries[^1].Sequence;

            // Seeds every view's own watermark the first time it's actually visited (see
            // lastSeenSequenceByView's own remarks) - nothing that already existed before this
            // window was created should ever be flagged "new" for any view.
            initialSeenSequence = lastSeenSequence;
        }
    }

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);

        Flags = plugin.Configuration.ChatWindowLocked
            ? baseFlags | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            : baseFlags;
    }

    public override void Draw()
    {
        var justActivatedThisFrame = plugin.ConsumePendingChatActivation();
        if (justActivatedThisFrame)
        {
            pendingFocusInput = true;
            ImGui.SetWindowFocus();
        }

        // Null specifically means the font isn't ready yet this frame (see Plugin.PushChatFont's
        // own remarks) - drawing still proceeds (skipping the whole frame turned out to be worse:
        // if the font flips available/unavailable often, that made the entire window blink in and
        // out rather than just occasionally mismeasuring text). What DrawChatLogEntries does do is
        // skip caching any height measured while the font isn't the real chat font, so a bad
        // measurement never lingers in entryHeightCache waiting to snap once the real font is back.
        using var chatFontScope = plugin.PushChatFont();

        var fontAvailableNow = plugin.IsChatFontAvailable;
        if (fontAvailableNow != lastKnownFontAvailable)
        {
            lastKnownFontAvailable = fontAvailableNow;

            if (fontAvailableNow && provisionalHeightSequences.Count > 0)
            {
                // Any entry first measured while the font wasn't ready got cached with the wrong
                // (fallback) font's metrics. Previously that bad height just sat there until the
                // entry happened to scroll back into view later, at which point it got re-measured
                // and snapped to the correct height - a visible, delayed "text jumps" glitch with no
                // obvious trigger. Purging them the instant the real font comes back forces an
                // immediate, correct re-measurement this frame instead of an unpredictable one later.
                foreach (var seq in provisionalHeightSequences)
                {
                    entryHeightCache.Remove(seq);
                }

                provisionalHeightSequences.Clear();
            }
        }

        var config = plugin.Configuration;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        var state = plugin.GetCharacterState();

        if (plugin.PendingWhisperTarget is { } pendingTarget)
        {
            EnterWhisperView(config, state, pendingTarget);
            plugin.PendingWhisperTarget = null;
        }

        SyncDraft(state);

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
        }

        if (state.LastChannel == ChatChannel.Whisper)
        {
            if (string.IsNullOrEmpty(activeWhisperTabTarget))
            {
                DrawWhisperTarget(state);
            }
            else
            {
                DrawActiveWhisperIndicator(config, activeWhisperTabTarget);
            }
        }

        DrawLogTabBar(config, state);

        if (config.ChatToolbarExpanded)
        {
            DrawLogToolbar(config, state);
        }

        var chunks = GetChunks(inputText, config.MaxMessageLength);
        plugin.CurrentPreviewChunks = chunks;

        var minInputBoxHeight = ImGui.GetFrameHeight();

        var minComposeAreaHeight = minInputBoxHeight + measuredFooterHeight;
        var composeAreaHeight = Math.Clamp(config.ComposeAreaHeight, minComposeAreaHeight, MaxComposeAreaHeight);
        var inputBoxHeight = Math.Max(minInputBoxHeight, composeAreaHeight - measuredFooterHeight);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, config.ChatLogBackgroundOpacity));

        ImGui.BeginChild("##chatLogScroll", new Vector2(-1, -(composeAreaHeight + SplitterHeight)), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        ImGui.SetScrollX(0f);

        DrawChatLogEntries(config, state, composeAreaHeight, inputBoxHeight);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        DrawSplitter(config, minComposeAreaHeight);

        if (composeBoxActive && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGuiP.ClearActiveID();
        }

        if (pendingFocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            pendingFocusInput = false;
        }

        ImGui.SetNextItemWidth(-1);
        const ImGuiInputTextFlags composeFlags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackCharFilter | ImGuiInputTextFlags.AllowTabInput;
        var commandTint = GetCommandTint(config);
        if (commandTint is { } tint)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, tint);
        }

        if (ImGui.InputTextMultiline("##composeText", ref inputText, 4000, new Vector2(-1, inputBoxHeight), composeFlags, TrackTextState))
        {
            state.Drafts[currentDraftKey] = inputText;
            plugin.SaveConfiguration();
        }

        var inputBoxBottom = ImGui.GetItemRectMax().Y;

        if (commandTint != null)
        {
            ImGui.PopStyleColor();
        }

        composeBoxActive = ImGui.IsItemActive();
        plugin.IsChatInputActive = composeBoxActive;

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

        DrawPreviewToggle(config, state, chunks);

        if (string.IsNullOrEmpty(validationMessage))
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight()));
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), validationMessage);
        }

        // Rounded up to a whole pixel - this feeds into composeAreaHeight below, and with the
        // default ComposeAreaHeight (70f) sitting under the natural minComposeAreaHeight for most
        // font sizes, composeAreaHeight sits clamped at minComposeAreaHeight for most users, which
        // tracks this value 1:1. Any sub-pixel noise in the raw measurement (ImGui's own text/frame
        // layout math isn't guaranteed to land on an exact whole pixel every frame) would otherwise
        // feed straight into ##chatLogScroll's own height every single frame - invisible on its own,
        // but while pinned to the bottom (see DrawChatLogEntries' scroll-follow logic), a viewport
        // that grows or shrinks by a fraction of a pixel shifts which whole pixel row the last line of
        // text rounds to, which is exactly a visible one-pixel "jitter" at the bottom and nowhere
        // else. Ceiling (not Round) so this never under-reserves space by rounding down.
        measuredFooterHeight = MathF.Ceiling(Math.Max(0f, ImGui.GetCursorScreenPos().Y - inputBoxBottom) + FooterBottomPaddingPx);

        ImGui.PopStyleVar(8);
    }

    private void DrawSplitter(Configuration config, float minComposeAreaHeight)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.26f));
        ImGui.Button("##chatSplitter", new Vector2(-1, SplitterHeight));
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemActive())
        {
            splitterDragging = true;
            config.ComposeAreaHeight = Math.Clamp(config.ComposeAreaHeight - ImGui.GetIO().MouseDelta.Y, minComposeAreaHeight, MaxComposeAreaHeight);
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

        ImGui.SameLine();
        var channelColor = state.LastChannel switch
        {
            ChatChannel.Linkshell => ChannelDisplay.LinkshellColor(state.LinkshellNumber, config),
            ChatChannel.CrossWorldLinkshell => ChannelDisplay.CrossWorldLinkshellColor(state.CrossWorldLinkshellNumber, config),
            _ => ChannelDisplay.Color(CategoryForChannel(state.LastChannel), config),
        };
        ImGui.TextColored(channelColor, CurrentChannelLabel(state));

        if (ImGui.BeginPopup("##channelPicker"))
        {
            DrawChannelOption(config, state, ChatChannel.Whisper, Loc.T("Channel.Whisper"));
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

            ImGui.EndPopup();
        }
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
                ImGui.PushStyleColor(ImGuiCol.Text, classicColor);
                var clicked = ImGuiComponents.IconButton(icon);
                ImGui.PopStyleColor();
                return clicked;
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

        if (ImGui.BeginPopupContextItem("##logSearchPopup"))
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##chatLogSearch", Loc.T("ChatLog.SearchHint"), ref logSearchFilter, 200);
            ImGui.EndPopup();
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
                ImGui.OpenPopup("##historyDatesPopup");
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(showingWhisperForHistory ? Loc.T("ChatLog.LoadWhisperHistory") : Loc.T("ChatLog.LoadHistory"));
        }

        DrawHistoryDatesPopup();

        ImGui.SameLine();
        if (DrawToolbarIconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start(new ProcessStartInfo(plugin.ChatLog.LogsFolderPath) { UseShellExecute = true });
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
        var locked = plugin.Configuration.ChatWindowLocked;
        if (DrawToolbarIconButton(locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen))
        {
            plugin.Configuration.ChatWindowLocked = !locked;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(locked ? Loc.T("Compose.WindowUnlock") : Loc.T("Compose.WindowLock"));
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

        if (ImGui.BeginPopup("##noviceNetworkMenu"))
        {
            if (ImGui.Selectable(Loc.T("NoviceNetwork.Join")))
            {
                NoviceNetworkService.Connect(plugin.Log);
            }

            if (ImGui.Selectable(Loc.T("NoviceNetwork.Leave")))
            {
                NoviceNetworkService.Disconnect(plugin.Log);
            }

            ImGui.EndPopup();
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

    private void DrawHistoryDatesPopup()
    {
        if (!ImGui.BeginPopup("##historyDatesPopup"))
        {
            return;
        }

        var dates = plugin.ChatLog.GetAvailableLogDates();
        var whisperTargets = plugin.ChatLog.GetWhisperLogTargets();
        if (dates.Count == 0 && whisperTargets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("ChatLog.NoHistoryFiles"));
            ImGui.EndPopup();
            return;
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##historyDateSearch", Loc.T("ChatLog.HistorySearchHint"), ref historySearchFilter, 40);

        ImGui.BeginChild("##historyDateList", new Vector2(220, 260), true);

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

        ImGui.EndChild();

        ImGui.EndPopup();
    }

    private void DrawClearTabConfirmationPopup(Configuration config)
    {
        if (!ImGui.BeginPopupModal("##confirmClearTab", ImGuiWindowFlags.AlwaysAutoResize))
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

        ImGui.EndPopup();
    }

    private void DrawChatLogEntries(Configuration config, CharacterState state, float composeAreaHeight, float inputBoxHeight)
    {
        var viewingHistory = loadedHistoryEntries != null;

        // Snapshot(), not the live Entries property directly - Entries hands back the service's
        // own internal list with no copy and no lock, while writes to it (a new message arriving,
        // eviction once the buffer's full) DO take entriesLock. Iterating that same live list here
        // with a plain foreach had no protection against a message landing mid-iteration - rare,
        // but exactly the kind of one-frame-only glitch that's near-impossible to catch on camera
        // (a single bad frame surrounded by good ones on either side) yet still very real.
        var liveEntries = plugin.ChatLog.Snapshot();

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

        // Chat2's own condition for this, verified against its actual source (ChatLog.Window.cs) -
        // was exact equality with no tolerance window at all for a while, on the theory that once
        // GetScrollY() equals GetScrollMaxY() both advance together every frame content grows, so
        // there's nothing to paper over with a band. That held up for busy tabs, but a live repro on
        // a whisper tab showed SetScrollHereY(1f) itself can settle a handful of pixels (4-8px, well
        // under one line height) short of the true GetScrollMaxY() it was aiming for - close enough
        // to look and feel like the bottom, but exact equality never fires again afterward, so every
        // later incoming message permanently stops auto-scrolling too (this check is self-reinforcing
        // once it goes false). BottomToleranceForNewMessagePx is deliberately small - far below a
        // single line's height - specifically so it can't reintroduce the old wide-band bug (a manual
        // scroll gesture passing through a several-line-tall "near enough" zone getting yanked the
        // rest of the way regardless of whether anything new had happened).
        var nearBottomBeforeDraw = ImGui.GetScrollMaxY() - ImGui.GetScrollY() <= BottomToleranceForNewMessagePx;
        var mentionTerm = plugin.MentionFirstName;

        var availableWidth = ImGui.GetContentRegionAvail().X;

        var showingWhisper = !string.IsNullOrEmpty(activeWhisperTabTarget);
        var showingNoviceNetwork = !showingWhisper && noviceNetworkTabActive;
        var activeTab = showingWhisper || showingNoviceNetwork ? null : GetActiveFixedTab(config);

        var hasNewEntries = false;
        var hasNewOwnMessageInView = false;

        var viewSeenBefore = lastSeenSequenceByView.GetValueOrDefault(viewKey, initialSeenSequence);
        var viewSeenMax = viewSeenBefore;

        bool MatchesActiveView(ChatLogEntry entry)
        {
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

        // Badges (unread counts on tabs/whispers other than the active one) need to see every
        // channel's traffic, not just what matches the active view, so this can't share the
        // view-specific cap below - but it doesn't need a full forward scan of up to 3000 entries
        // either: liveEntries is in ascending Sequence order, lastSeenSequence only ever advances,
        // so walking backward from the newest and stopping at the first already-seen entry visits
        // exactly the (usually single-digit) handful that are actually new, not the whole buffer.
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

        // Filter to the active view *before* capping, not after - capping the raw global stream
        // first (as this used to) meant a busy, unrelated channel's volume alone could push a lower-
        // traffic view's own recent messages (e.g. a whisper conversation) out of the "last 600"
        // window entirely, while its *older*, file-preloaded history (via whisperPreload below,
        // which isn't subject to this cap) kept showing - reported as "the newest messages vanish
        // but old ones stay". Filtering first means each view's cap is judged against its own
        // traffic, not everyone else's.
        //
        // Walking liveEntries backward and stopping once maxLiveEntriesConsidered matches are found
        // (rather than collecting every match forward and trimming afterward) means a busy tab that
        // matches most of the buffer only ever visits its own last 600, not the full ~3000 - and
        // reusing viewMatchingLiveEntriesBuffer across frames (Clear(), not a fresh List) avoids
        // repeated backing-array growth from allocating a new list every single frame, which was
        // producing enough GC pressure to show up as an actual UI hitch.
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

        IReadOnlyList<ChatLogEntry> entries = loadedHistoryEntries ?? viewMatchingLiveEntriesBuffer;

        void ProcessEntry(ChatLogEntry entry)
        {
            if (logSearchFilter.Length > 0
                && entry.Text.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Sender.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss").IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
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

            // Same technique as Chat2's own DrawMessages (verified against its actual source) -
            // ImGui.IsItemVisible() is ImGui's own authoritative visibility check against its real,
            // current clip rect, not a hand-rolled approximation. An earlier version compared
            // entryTop against a scrollY/viewHeight/cullMarginPx captured once at the top of this
            // whole method - close enough most of the time, but never exactly what ImGui itself
            // considers visible, and that gap was a real, ongoing source of position drift.
            var hadCache = entryHeightCache.TryGetValue(entry.Sequence, out var cachedHeight);
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
            DrawLogEntry(entry, config, mentionTerm, availableWidth, showingWhisper);
            var newHeight = ImGui.GetCursorPosY() - entryTop;

            if (!plugin.IsChatFontAvailable)
            {
                // Track this entry as provisional so Draw's own font-availability-restored check
                // can purge and correctly re-measure it the instant the real font is back, instead
                // of leaving a wrong height to linger until the entry happens to scroll back into
                // view naturally (which is what showed up as a delayed, seemingly random "text
                // jumps" glitch).
                provisionalHeightSequences.Add(entry.Sequence);

                if (hadCache)
                {
                    // Measured with whatever font ImGui had active, not the real chat font (see
                    // Draw's own remarks) - don't let this overwrite an entry that's already got a
                    // real, correctly-measured cached height. This check only applies when a cached
                    // height already exists, though - an entry with no cache yet still needs
                    // *something* written so culling has a height to work with at all; skipping
                    // that unconditionally meant every uncached entry (e.g. all 3000 right after a
                    // plugin reload) got fully redrawn every single frame for as long as the font
                    // stayed unavailable, with culling never getting the chance to kick in - a
                    // real, severe performance regression, not just an occasional bad measurement.
                    return;
                }
            }

            entryHeightCache[entry.Sequence] = newHeight;
        }

        // whisperPreload (per-whisper-target file history, loaded once) used to always draw entirely
        // before entries (the live/daily-log-sourced list), on the assumption that preloaded history
        // is always older than everything live. That's not actually guaranteed - the preload file
        // and the live list are two independent sources with their own coverage/date ranges/caps, so
        // the preload file can (and did, in a live report) contain messages *newer* than some of
        // what's in the live list, putting a whole newer conversation above older live messages.
        // Both lists are individually sorted ascending by Timestamp already (LoadWhisperHistory's own
        // stable sort; entries built ascending above), so a proper merge - not two sequential loops -
        // is what actually produces correct chronological order either way.
        if (whisperPreload != null)
        {
            foreach (var entry in MergeByTimestamp(whisperPreload, entries))
            {
                ProcessEntry(entry);
            }
        }
        else
        {
            foreach (var entry in entries)
            {
                ProcessEntry(entry);
            }
        }

        if (viewChanged)
        {
            if (config.ScrollToBottomOnTabSwitch || !viewScrollPositions.TryGetValue(viewKey, out var savedScrollY))
            {
                ImGui.SetScrollHereY(1f);
            }
            else
            {
                ImGui.SetScrollY(savedScrollY);
            }
        }
        else if (hasNewOwnMessageInView || (!viewingHistory && hasNewEntries && nearBottomBeforeDraw))
        {
            // Only snaps when something actually new arrived while already exactly at the bottom -
            // matches Chat2's own condition (switchedTab || GetScrollY() >= GetScrollMaxY()), not a
            // tolerance window around it. A wide "near enough" band was only ever there to paper
            // over the old visibility check's own imprecision (see ProcessEntry's remarks) - with
            // that fixed at its actual source, a tolerance band just meant a manual scroll crossing
            // into that zone (mouse wheel almost never lands on the exact pixel maximum) got
            // hijacked and yanked the rest of the way regardless of whether anything new had
            // happened, reported as "scrolling up and back down starts the jitter".
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
    }

    private bool IsSelfAuthored(ChatLogEntry entry) =>
        entry.ChatType == XivChatType.TellOutgoing
        || string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase);

    private void TrackUnreadForNewEntry(Configuration config, CharacterState state, ChatLogEntry entry, bool showingWhisper, bool showingNoviceNetwork, Guid? activeFixedTabId)
    {
        if (IsSelfAuthored(entry))
        {
            return;
        }

        foreach (var tab in config.ChatTabs)
        {
            if (!showingWhisper && tab.Id == activeFixedTabId)
            {
                continue;
            }

            if (!tab.Matches(entry.ChatType) || tab.Muted)
            {
                continue;
            }

            fixedTabUnreadCounts[tab.Id] = fixedTabUnreadCounts.GetValueOrDefault(tab.Id) + 1;
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

    private void DrawLogEntry(ChatLogEntry entry, Configuration config, string mentionTerm, float availableWidth, bool showingWhisper)
    {
        ImGui.PushID((int)entry.Sequence);
        ImGui.BeginGroup();

        var prefixSize = Vector2.Zero;
        var channelColor = ChannelDisplay.Color(entry.ChatType, config);

        var timestamp = entry.Timestamp.ToString("HH:mm") + " ";
        ImGui.TextColored(config.TimestampColor, timestamp);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(timestamp);

        var tag = ChannelDisplay.DisplayTag(entry.ChatType) + " ";
        ImGui.TextColored(channelColor, tag);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(tag);

        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        if (!isOutgoingTell && config.ShowSenderStatusIcons)
        {
            prefixSize.X += StatusIconRenderer.Draw(plugin, entry.SenderPayloads);
        }

        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

        var rawName = isOutgoingTell && showingWhisper ? plugin.OwnCharacterName : displayName;

        var namePart = string.IsNullOrEmpty(rawName) ? string.Empty : rawName + ": ";

        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : logSearchFilter.Length > 0 && entry.Sender.Contains(logSearchFilter, StringComparison.OrdinalIgnoreCase)
                ? SearchHighlightColor
                : entry.ChatType == XivChatType.TellIncoming
                    ? ChannelDisplay.WhisperColor(displayName, config)
                    : channelColor;
        ImGui.TextColored(senderColor, namePart);

        var nameHovered = false;
        var whisperTarget = string.Empty;
        var nameRectMin = Vector2.Zero;
        var nameRectMax = Vector2.Zero;

        if (!IsSelfAuthored(entry))
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
            });

        ImGui.Spacing();

        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            var min = ImGui.GetItemRectMin() - new Vector2(4f, 2f);
            var max = ImGui.GetItemRectMax() + new Vector2(4f, 2f);
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 3f, ImDrawFlags.None, 1.3f);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            rightClickedOnName = nameHovered;
        }

        if (ImGui.BeginPopupContextItem("##logEntryContext"))
        {
            if (!isOutgoingTell && rightClickedOnName)
            {
                if (ImGui.Selectable(Loc.T("ChatLog.WhisperAction")))
                {
                    OpenWhisperTabWithHistory(whisperTarget);
                }

                if (ImGui.Selectable(Loc.T("ChatLog.InvitePartyAction")))
                {
                    plugin.InviteToParty(entry.Sender, entry.SenderWorld, entry.ContentId);
                }

                if (ImGui.BeginMenu(Loc.T("ChatLog.BlockFunctionsMenu")))
                {
                    if (ImGui.Selectable(Loc.T("ChatLog.AddToBlacklistAction")))
                    {
                        plugin.AddToBlacklist(entry.Sender, entry.SenderWorld);
                    }

                    ImGui.BeginDisabled(entry.AccountId == 0);
                    if (ImGui.Selectable(Loc.T("ChatLog.AddToMuteListAction")))
                    {
                        plugin.AddToMuteList(entry.Sender, entry.SenderWorld, entry.ContentId, entry.AccountId);
                    }

                    ImGui.EndDisabled();
                    if (entry.AccountId == 0 && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(Loc.T("ChatLog.AddToMuteListUnavailable"));
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.Selectable(Loc.T("ChatLog.InviteNoviceNetworkAction")))
                {
                    plugin.InviteToNoviceNetwork(entry.Sender, entry.SenderWorld);
                }

                if (ImGui.Selectable(Loc.T("ChatLog.ReplyInModeAction")))
                {
                    plugin.ReplyInMessageChannel(entry.ChatType);
                }

                if (ImGui.Selectable(Loc.T("ChatLog.TargetAction")))
                {
                    plugin.TargetPlayer(entry.Sender, entry.SenderWorld);
                }

                if (ImGui.Selectable(Loc.T("ChatLog.AdventurerPlateAction")))
                {
                    plugin.OpenAdventurerPlate(entry.Sender, entry.SenderWorld, entry.ContentId);
                }

                ImGui.Separator();
            }

            if (ImGui.Selectable(Loc.T("ChatLog.CopyMessage")))
            {
                ImGui.SetClipboardText($"{entry.Sender}: {entry.Text}");
            }

            ImGui.EndPopup();
        }

        if (nameHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("ChatLog.ClickToWhisper"));

            var underlineY = nameRectMax.Y - 1f;
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(nameRectMin.X, underlineY),
                new Vector2(nameRectMax.X, underlineY),
                ImGui.GetColorU32(senderColor));
        }

        ImGui.PopID();
    }

    private void SyncDraft(CharacterState state)
    {
        var key = state.LastChannel == ChatChannel.Whisper && !string.IsNullOrEmpty(activeWhisperTabTarget)
            ? activeWhisperTabTarget
            : GenericDraftKey;

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

    private unsafe int TrackTextState(ref ImGuiInputTextCallbackData data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCharFilter)
        {
            if (data.EventChar == '\n' && plugin.Configuration.SendOnEnter && !ImGui.GetIO().KeyShift)
            {
                pendingSendFromEnter = true;
                return 1;
            }

            if (data.EventChar == '\t')
            {
                pendingAutoTranslatePopup = true;
                return 1;
            }

            return 0;
        }

        // ImGuiInputTextFlags.CallbackHistory can't be combined with Multiline - ImGui itself
        // asserts against that combination (surfaced as "CelinesChat is creating Errors" at
        // startup, an ImGui assertion fired from InputTextMultiline every time this box drew), since
        // up/down are needed for moving the cursor between lines in a real multiline box, not just
        // for history recall. Reimplemented here instead, on top of the CallbackAlways invocation
        // this box already gets every frame regardless: only treat up/down as "recall history" when
        // the cursor is already at the very start/end of the whole buffer - otherwise they move the
        // cursor between lines as normal multiline editing expects.
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

    private unsafe void HandleHistoryRecall(ref ImGuiInputTextCallbackData data, bool up)
    {
        var history = plugin.SentHistory;
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
        if (!ImGui.BeginPopup("##autoTranslatePopup"))
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
        ImGui.BeginChild("##autoTranslateList", new Vector2(300, 300));

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

        ImGui.EndChild();
        ImGui.EndPopup();
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
                plugin.OpenWhisperTab(tellTarget);
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

    private List<string> GetChunks(string text, int maxLength)
    {
        if (text != cachedInput || maxLength != cachedMaxLength)
        {
            cachedInput = text;
            cachedMaxLength = maxLength;
            cachedChunks = MessageSplitter.BuildMessages(text, maxLength);
        }

        return cachedChunks;
    }

    private void TrySend(CharacterState state)
    {
        var trimmedInput = inputText.TrimStart();
        if (trimmedInput.StartsWith('/'))
        {
            plugin.SendRawCommand(trimmedInput);
            ClearComposeAfterSend(state);
            return;
        }

        var chunks = GetChunks(inputText, plugin.Configuration.MaxMessageLength);

        if (chunks.Count == 0)
        {
            validationMessage = string.Empty;
            ImGuiP.ClearActiveID();
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

        ImGuiP.ClearActiveID();

        if (plugin.Configuration.KeepInputFocusAfterSend)
        {
            pendingFocusInput = true;
        }
    }

    private void DrawPreviewToggle(Configuration config, CharacterState state, List<string> chunks)
    {
        DrawChannelPickerButton(config, state);

        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Compose.MessageCount", chunks.Count));

        if (plugin.IsSending)
        {
            var done = plugin.SendTotal - plugin.SendRemaining;
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T("Compose.Sending", done, plugin.SendTotal));
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Ban, new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.68f, 0.22f, 0.22f, 1f)))
            {
                plugin.CancelSending();
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

        if (!ImGui.BeginPopup("##rpActionsMenu"))
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

        if (ImGui.BeginMenu(Loc.T("Compose.Snippets")))
        {
            DrawSnippetsMenuContent();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(Loc.T("Compose.History")))
        {
            DrawHistoryMenuContent();
            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.Selectable(Loc.T("Compose.Copy")))
        {
            ImGui.SetClipboardText(inputText);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
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
        for (var i = 0; i < snippets.Count; i++)
        {
            if (snippetSearchFilter.Length > 0 && snippets[i].Name.IndexOf(snippetSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            ImGui.PushID(i);
            if (ImGui.Selectable(snippets[i].Name))
            {
                inputText = string.IsNullOrEmpty(inputText) ? snippets[i].Text : inputText + " " + snippets[i].Text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            snippets.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(150);
        ImGui.InputText(Loc.T("Compose.SnippetNameLabel"), ref newSnippetName, 60);
        ImGui.SameLine();
        var canSave = !string.IsNullOrWhiteSpace(newSnippetName) && !string.IsNullOrWhiteSpace(inputText);
        ImGui.BeginDisabled(!canSave);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, Loc.T("Compose.SaveSnippet")))
        {
            snippets.Add(new Snippet { Name = newSnippetName, Text = inputText });
            plugin.SaveConfiguration();
            newSnippetName = string.Empty;
        }

        ImGui.EndDisabled();
    }

    private void DrawHistoryMenuContent()
    {
        var history = plugin.SentHistory;
        if (history.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoHistory"));
        }

        foreach (var text in history)
        {
            var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            if (ImGui.Selectable(preview))
            {
                inputText = text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private static void DrawActiveWhisperIndicator(Configuration config, string target)
    {
        var color = ChannelDisplay.WhisperColor(target, config);

        var lineHeight = MathF.Max(ImGui.GetTextLineHeight(), ImGui.GetFrameHeight()) + 2f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(color.X, color.Y, color.Z, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.75f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.BeginChild("##activeWhisperIndicator", new Vector2(-1, lineHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // Centered against the child's own actually-measured content height, not
        // AlignTextToFramePadding() - that assumes the surrounding frame height derives from the
        // current FramePadding style value, which held up fine normally, but a UI theme plugin
        // (e.g. FrostUI) that changes FramePadding independently of font/line metrics broke that
        // assumption and the text stopped lining up with lineHeight above. Measuring each piece's
        // own height and centering it directly works regardless of what any theme changes.
        var contentHeight = ImGui.GetContentRegionAvail().Y;
        var rowStartY = ImGui.GetCursorPosY();

        ImGui.PushFont(UiBuilder.IconFont);
        var iconText = FontAwesomeIcon.EnvelopeOpen.ToIconString();
        var iconHeight = ImGui.CalcTextSize(iconText).Y;
        ImGui.SetCursorPosY(rowStartY + (contentHeight - iconHeight) / 2f);
        ImGui.TextColored(color, iconText);
        ImGui.PopFont();
        ImGui.SameLine();

        var availableWidth = ImGui.GetContentRegionAvail().X;
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

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private void DrawWhisperTarget(CharacterState state)
    {
        ImGui.SetNextItemWidth(-90);
        var target = state.LastWhisperTarget;
        if (ImGui.InputText(Loc.T("Whisper.TargetLabel"), ref target, 100))
        {
            state.LastWhisperTarget = target;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var currentTargetName = plugin.GetCurrentTargetPlayerName();
        ImGui.BeginDisabled(currentTargetName == null);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs))
        {
            plugin.OpenWhisperTab(currentTargetName!);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.UseTarget"));
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Users))
        {
            ImGui.OpenPopup("##whisperSuggestions");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.Suggestions"));
        }

        if (ImGui.BeginPopup("##whisperSuggestions"))
        {
            var partyMembers = plugin.GetPartyMemberNames();
            var recents = state.RecentWhisperTargets;

            if (partyMembers.Count == 0 && recents.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.NoSuggestions"));
            }

            if (partyMembers.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsParty"));
                foreach (var name in partyMembers)
                {
                    if (ImGui.Selectable(name))
                    {
                        plugin.OpenWhisperTab(name);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            if (recents.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsRecent"));
                foreach (var name in recents)
                {
                    if (ImGui.Selectable(name))
                    {
                        plugin.OpenWhisperTab(name);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            ImGui.EndPopup();
        }
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

        var stripWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - (arrowSize + spacing) * 2f);

        var lastTabStart = tabStripTabStartOffsets.Count > 0 ? tabStripTabStartOffsets[^1] : 0f;
        var maxScroll = MathF.Max(MathF.Max(0f, tabStripContentWidth - stripWidth), lastTabStart);
        tabStripScrollRawTarget = Math.Clamp(tabStripScrollRawTarget, 0f, maxScroll);
        tabStripScrollX = SnapScrollToTabBoundary(tabStripScrollRawTarget);

        ImGui.BeginDisabled(tabStripScrollX <= 0f);
        if (DrawToolbarIconButton(FontAwesomeIcon.ChevronLeft, unreadTabScrolledLeft ? SearchHighlightColor : null))
        {
            tabStripScrollX = PrevTabBoundaryBefore(tabStripScrollX);
            tabStripScrollRawTarget = tabStripScrollX;
        }

        if (unreadTabScrolledLeft && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.UnreadTabScrolledOffScreen"));
        }

        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginChild("##logTabsStrip", new Vector2(stripWidth, arrowSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

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

        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

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

                DrawUnreadBadge(fixedUnread);

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

                DrawUnreadBadge(noviceNetworkUnread);

                if (clicked && !isSelected)
                {
                    EnterNoviceNetworkView();
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
                if (blinking)
                {
                    var pulse = (MathF.Sin(Environment.TickCount64 / 250f) + 1f) * 0.5f;
                    var normal = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
                    ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Lerp(normal, SearchHighlightColor, pulse));
                }

                var clicked = ImGui.Selectable(whisperText + "##whisper" + targetName, isSelected, ImGuiSelectableFlags.None, tabSize);

                if (blinking)
                {
                    ImGui.PopStyleColor();
                }

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

        ImGui.PopStyleVar();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginDisabled(tabStripScrollX >= wheelMaxScroll);
        if (DrawToolbarIconButton(FontAwesomeIcon.ChevronRight, unreadTabScrolledRight ? SearchHighlightColor : null))
        {
            tabStripScrollX = MathF.Min(wheelMaxScroll, NextTabBoundaryAfter(tabStripScrollX));
            tabStripScrollRawTarget = tabStripScrollX;
        }

        if (unreadTabScrolledRight && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.UnreadTabScrolledOffScreen"));
        }

        ImGui.EndDisabled();

        pendingTabForceSelect = null;

        if (closedTarget != null)
        {
            state.RecentWhisperTargets.Remove(closedTarget);
            whisperUnreadCounts.Remove(closedTarget);

            if (activeWhisperTabTarget == closedTarget)
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
        }
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
        ViewChanged?.Invoke();
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

        ViewChanged?.Invoke();
    }

    internal void EnterNoviceNetworkView()
    {
        activeWhisperTabTarget = string.Empty;
        noviceNetworkTabActive = true;
        ViewChanged?.Invoke();
    }

    private void DrawFixedTabQuickEdit(ChatTab tab)
    {
        if (!ImGui.BeginPopupContextItem("##tabQuickEdit" + tab.Id))
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

        var muted = tab.Muted;
        if (ImGui.Checkbox(Loc.T("Tabs.Muted") + "##mute" + tab.Id, ref muted))
        {
            tab.Muted = muted;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.MutedTooltip"));
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

        ImGui.EndPopup();
    }

    private bool DrawWhisperTabQuickEdit(CharacterState state, string targetName)
    {
        if (!ImGui.BeginPopupContextItem("##whisperTabQuickEdit" + targetName))
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

        ImGui.EndPopup();
        return closeRequested;
    }

    internal static void DrawUnreadBadge(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var label = count > 99 ? "99+" : count.ToString();
        var textSize = ImGui.CalcTextSize(label);
        var radius = MathF.Max(7f, (textSize.X / 2f) + 3f);

        var max = ImGui.GetItemRectMax();
        var min = ImGui.GetItemRectMin();

        var center = new Vector2(max.X - radius * 0.6f, min.Y + radius);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.85f, 0.15f, 0.15f, 1f)));
        drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);
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

        var fileEntries = plugin.ChatLog.LoadWhisperHistory(target);
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

    /// <summary>
    /// Merges two sequences that are each already sorted ascending by Timestamp into one properly
    /// ordered sequence - the classic merge step of a merge sort, not a fresh sort of the combined
    /// set, since both inputs are already individually ordered. See the whisperPreload/entries call
    /// site's own remarks for why a plain "draw all of A, then all of B" isn't safe here.
    /// </summary>
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
        if (entry.ChatType is not (XivChatType.TellIncoming or XivChatType.TellOutgoing))
        {
            return false;
        }

        var entryIdentity = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var targetNameOnly = target.Split('@')[0];
        var isMatch = string.Equals(entryIdentity, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Sender, targetNameOnly, StringComparison.OrdinalIgnoreCase);
        if (!isMatch)
        {
            return false;
        }

        if (whisperCloseCutoff.TryGetValue(target, out var cutoff) && entry.Sequence <= cutoff)
        {
            return false;
        }

        return true;
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
