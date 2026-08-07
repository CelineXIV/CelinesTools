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
    private static readonly Vector4 SearchHighlightColor = new(1f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private readonly Plugin plugin;

    // Compose state
    private string inputText = string.Empty;
    private string currentDraftKey = string.Empty;
    private string validationMessage = string.Empty;
    private string newSnippetName = string.Empty;
    private string snippetSearchFilter = string.Empty;
    private string activeWhisperTabTarget = string.Empty;
    private string? pendingTabForceSelect;
    private int selectionStart;
    private int selectionEnd;
    private int cursorPos;
    private string cachedInput = string.Empty;
    private int cachedMaxLength = -1;
    private List<string> cachedChunks = new();
    private bool splitterDragging;

    // How tall the preview-toggle row + action row + (sometimes) the validation message actually
    // rendered last frame, in real screen pixels - measured after drawing them (see Draw()), not
    // guessed from GetFrameHeightWithSpacing() formulas. Estimating that footprint from font
    // metrics went through three different formulas across this file's history and each one was
    // only right for some font/row-count combination, either starving the input box (jittery caret
    // scroll-into-view) or over-reserving space for it (dead margin at the bottom of the window).
    // A real, one-frame-stale measurement is exact for whatever's actually on screen and adapts to
    // any font size or row count automatically. Seeded with a reasonable guess for the very first
    // frame only; every frame after that overwrites it with the real value.
    private float measuredFooterHeight = 60f;
    private bool pendingSendFromEnter;
    private bool pendingFocusInput;
    private bool composeBoxActive;
    private bool pendingAutoTranslatePopup;
    private string autoTranslateSearch = string.Empty;
    private uint? autoTranslateBrowseGroup;

    // Log state
    private Guid activeTabId;
    private long lastSeenSequence;
    private List<ChatLogEntry>? loadedHistoryEntries;
    private string? loadedHistoryLabel;
    private string logSearchFilter = string.Empty;
    private string historySearchFilter = string.Empty;
    private string lastViewKey = string.Empty;
    private readonly Dictionary<string, int> whisperUnreadCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> fixedTabUnreadCounts = new();

    // Recorded when a whisper tab is closed (the newest sequence number at that moment) so that
    // if the same conversation later gets a fresh tab (new incoming/outgoing message), it only
    // shows what's been said since the close - not the entire history still sitting in the
    // shared buffer.
    private readonly Dictionary<string, long> whisperCloseCutoff = new(StringComparer.OrdinalIgnoreCase);

    // Whether the most recent right-click on a log entry's group landed specifically on the
    // sender name (vs. elsewhere in the row) - see DrawLogEntry's merged "##logEntryContext"
    // popup for why this can't just be two separate BeginPopupContextItem calls.
    private bool rightClickedOnName;

    // The always-on window flags set once in the constructor - see PreDraw, which rebuilds Flags
    // from this every frame plus NoMove/NoResize whenever the position-lock toggle is on.
    private ImGuiWindowFlags baseFlags;

    public ChatWindow(Plugin plugin) : base(WindowTitles.Chat)
    {
        this.plugin = plugin;
        // Matches where the user actually keeps this window day to day (read from Dalamud's own
        // saved ImGui window state) - only takes effect the very first time this window is ever
        // shown for a given install, same as any other FirstUseEver default.
        Position = new Vector2(18, 868);
        PositionCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(658, 511);
        SizeCondition = ImGuiCond.FirstUseEver;

        // Only the "##chatLogScroll" child in the middle should ever scroll - the channel row,
        // toolbar and compose area above/below it are meant to stay fixed. Without these flags,
        // even a tiny (few-pixel) mismatch in the compose-area height math turns the whole window
        // scrollable, which drags the top toolbar and bottom action row along with it.
        // NoTitleBar drops the title text, collapse triangle and close X entirely, so only the
        // chat itself remains on screen - the window is toggled via the plugin's own command/
        // hotkey instead of a close button.
        Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar;

        // Remembered separately from Flags itself - PreDraw recomputes Flags every frame from
        // this plus NoMove/NoResize (see the position-lock toggle), so the always-on set here
        // can't get lost or duplicated across frames.
        baseFlags = Flags;

        // Without this, Escape (while this window is focused) closes the whole chat window -
        // Dear ImGui's own text-input widgets already defocus themselves on Escape (reverting
        // any in-progress edit) so the game regains movement input; that's all Escape should do
        // here, not also hide the chat.
        RespectCloseHotkey = false;

        // Dalamud's automatic "avoid overlapping native UI" collision handling can reposition
        // and resize a window on its own as it crosses native UI regions - with no titlebar to
        // judge the window's true edges against, that showed up as the wrap point seeming to
        // shift just from dragging the window around. This plugin's whole compose+log layout
        // manages its own size deliberately, so let it alone.
        InhibitAtkCollision = false;

        // No titlebar/close button (see NoTitleBar above) means this is meant to stay open all
        // the time, like the native chat log - toggled via the plugin's own command instead.
        IsOpen = true;

        var initialState = plugin.GetCharacterState();
        if (initialState.LastChannel == ChatChannel.Whisper)
        {
            activeWhisperTabTarget = initialState.LastWhisperTarget;
            pendingTabForceSelect = activeWhisperTabTarget;
        }

        // ChatLogService pre-loads some recent history from log files on startup so the chat
        // doesn't always open empty - fast-forward past all of it here so none of it gets
        // treated as a brand new message once drawing starts (which would otherwise
        // auto-create/blink whisper tabs and bump unread counts for stuff from hours or days ago).
        var existingEntries = plugin.ChatLog.Entries;
        if (existingEntries.Count > 0)
        {
            lastSeenSequence = existingEntries[^1].Sequence;
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
        // Mirrors vanilla FFXIV: Enter opens the chat box for typing when it's not already
        // focused, as long as some other native textbox isn't already using that Enter press.
        // The actual detection happens in Plugin.OnFrameworkUpdate (see its remarks for why) -
        // this just consumes the flag it sets.
        //
        // justActivatedThisFrame is deliberately a *local*, not a field: it only needs to
        // suppress a same-frame '\n' echo from this exact activation reaching the char filter
        // below (see pendingSendFromEnter) and flashing/closing the box it just opened. An
        // earlier version used a persistent field for this and consuming the keystate in
        // OnFrameworkUpdate turned out to prevent that echo from ever arriving at all, which
        // left the field permanently stuck true - silently swallowing the user's *next* real
        // Enter press (the one actually meant to close an empty box) instead of the phantom one
        // it was written for. A local can't leak past this single Draw call, so it can't cause
        // that regardless of whether the echo shows up or not.
        var justActivatedThisFrame = plugin.ConsumePendingChatActivation();
        if (justActivatedThisFrame)
        {
            pendingFocusInput = true;
            ImGui.SetWindowFocus();
        }

        // Scoped to the whole method (disposed whenever Draw returns, including any early
        // return) rather than manually finding every exit point to pop it - the chosen font
        // applies to the entire window: log, compose box, everything. Sizing is baked into the
        // font itself (see ChatFontManager/FontsPage) rather than a runtime ImGui.SetWindowFontScale
        // multiplier - that scale only stretches already-rasterized glyph bitmaps and was a
        // confirmed source of shimmering/unstable text and caret placement while typing.
        using var chatFontScope = plugin.PushChatFont();

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
            pendingTabForceSelect = pendingTarget;
            plugin.PendingWhisperTarget = null;
        }

        SyncDraft(state);

        if (plugin.PendingChatPrefillText is { } prefillText)
        {
            plugin.PendingChatPrefillText = null;
            InsertChatPrefillText(state, prefillText);
        }

        // Being on a whisper tab means "you're whispering that person," full stop - it shouldn't
        // matter what channel was selected before switching to this tab. Re-asserting this every
        // frame (not just on tab click) means clicking any other channel icon while viewing a
        // whisper tab has no lasting effect, which is what makes the manual target field below
        // unnecessary while an existing conversation tab is open.
        if (!string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            state.LastChannel = ChatChannel.Whisper;
            state.LastWhisperTarget = activeWhisperTabTarget;

            // Whatever tab you're currently viewing obviously can't also be "unread"/blinking.
            whisperUnreadCounts.Remove(activeWhisperTabTarget);
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

        DrawLogToolbar(state);

        DrawLogTabBar(config, state);

        var chunks = GetChunks(inputText, config.MaxMessageLength);
        plugin.CurrentPreviewChunks = chunks;

        // The input box's own minimum has to be at least one full visible line of the *currently
        // active* font, not a flat guessed pixel value - GetFrameHeight() (line height + top/bottom
        // FramePadding, using whatever font PushChatFont put in effect above) is exactly that,
        // live. A flat constant here (previously 30px, chosen without regard to font/size) can end
        // up smaller than a single real line once someone picks a bigger font size, and asking
        // Dear ImGui's multiline InputText to fit less than one full line makes its internal
        // scroll-into-view logic for the caret hunt between two slightly different states frame to
        // frame - visible exactly as "the input field jitters, but only when it's dragged small".
        var minInputBoxHeight = ImGui.GetFrameHeight();

        // The lower bound on composeAreaHeight has to guarantee room for the footer (preview-toggle
        // row + action row + sometimes the validation message) *plus* that minimum usable input
        // box - using measuredFooterHeight (the real height that footer rendered at last frame,
        // see its own remarks) instead of a formula, since every formula tried here so far was
        // only correct for some specific font-size/row-count combination.
        var minComposeAreaHeight = minInputBoxHeight + measuredFooterHeight;
        var composeAreaHeight = Math.Clamp(config.ComposeAreaHeight, minComposeAreaHeight, MaxComposeAreaHeight);
        var inputBoxHeight = Math.Max(minInputBoxHeight, composeAreaHeight - measuredFooterHeight);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, config.ChatLogBackgroundOpacity));
        ImGui.BeginChild("##chatLogScroll", new Vector2(-1, -(composeAreaHeight + SplitterHeight)), true);
        DrawChatLogEntries(config, state);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        DrawSplitter(config, minComposeAreaHeight);

        if (composeBoxActive && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            // Dear ImGui's own Escape handling for InputText reverts the buffer to whatever it
            // held when editing started and only *then* deactivates - clobbering everything
            // typed since, when all Escape should do here is give up keyboard focus (so
            // movement resumes) while leaving the draft alone. Deactivating it ourselves before
            // the widget below runs skips that internal revert entirely: by the time it checks
            // "am I still the active widget" this frame, we've already said no, so it has
            // nothing left to revert and just redraws using inputText as-is.
            ImGuiP.ClearActiveID();
        }

        if (pendingFocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            pendingFocusInput = false;
        }

        ImGui.SetNextItemWidth(-1);
        // AllowTabInput stops Tab from doing its default "move focus to the next widget" thing -
        // needed so it reaches the char filter below as a literal '\t' to intercept for the
        // auto-translate picker, the same way Enter is intercepted for sending.
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

        // Bottom edge of the input box itself, in screen space - the starting point for measuring
        // the footer's real footprint below (see measuredFooterHeight's remarks).
        var inputBoxBottom = ImGui.GetItemRectMax().Y;

        if (commandTint != null)
        {
            ImGui.PopStyleColor();
        }

        // A precise, per-frame "does our own box actually have keyboard focus right now" signal
        // for Plugin.OnFrameworkUpdate - see its remarks for why this has to be widget-level,
        // not just "is this window focused". IsItemActive (tied to ImGui's ActiveId), not
        // IsItemFocused (tied to nav focus, which ClearActiveID does NOT clear) - using
        // IsItemFocused here left this stuck true after the empty-Enter-defocuses-like-Escape
        // path called ClearActiveID, since nav focus stayed pinned to this box until some other
        // widget (e.g. clicking a tab) took it away - blocking every activation in between.
        composeBoxActive = ImGui.IsItemActive();
        plugin.IsChatInputActive = composeBoxActive;

        if (pendingAutoTranslatePopup)
        {
            pendingAutoTranslatePopup = false;
            plugin.EnsureAutoTranslateLoaded();
            autoTranslateSearch = string.Empty;
            autoTranslateBrowseGroup = null;
            ImGui.OpenPopup("##autoTranslatePopup");
        }

        DrawAutoTranslatePopup(state);

        if (pendingSendFromEnter)
        {
            pendingSendFromEnter = false;

            // See justActivatedThisFrame's own comment above: only swallow a '\n' that arrives
            // on the exact same frame this box was just given focus, never anything after.
            if (!justActivatedThisFrame)
            {
                TrySend(state);
            }
        }

        HandleSendHotkey(state, config);

        DrawPreviewToggle(config, state, chunks);

        DrawActionRow();

        if (!string.IsNullOrEmpty(validationMessage))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), validationMessage);
        }

        // The real, exact footprint of everything below the input box this frame - see
        // measuredFooterHeight's remarks. Feeds next frame's inputBoxHeight/minComposeAreaHeight
        // instead of a formula, so it's always right for whatever font size and row count (2 rows
        // normally, 3 whenever the validation message is showing) actually rendered.
        measuredFooterHeight = Math.Max(0f, ImGui.GetCursorScreenPos().Y - inputBoxBottom);

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

    /// <summary>
    /// The speech-bubble icon button that opens a dropdown-style popup listing every send
    /// channel, mirroring the game's own native chat-channel picker (right down to only listing
    /// linkshells/cross-world linkshells that actually exist, with their real names) instead of
    /// a row of one icon button per channel. Sits bottom-left of the compose area, followed
    /// immediately by the current channel's name in its configured color.
    /// </summary>
    private void DrawChannelPickerButton(Configuration config, CharacterState state)
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Comment))
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

    private void SelectChannel(Configuration config, CharacterState state, ChatChannel channel)
    {
        // Only jump the log view when switching TO Whisper *and* there's an actual target to
        // whisper (to your last conversation, same convenience as before) - switching to any
        // other send channel, or to Whisper with nothing to whisper yet, must leave whatever tab
        // the user is currently viewing alone. The log tab bar is always visible now (unlike
        // before, when it only existed while Whisper was selected), so clearing
        // activeWhisperTabTarget here would desync it from what ImGui's tab bar still visually
        // shows as selected.
        if (channel == ChatChannel.Whisper && !string.IsNullOrEmpty(state.LastWhisperTarget))
        {
            EnterWhisperView(config, state, state.LastWhisperTarget);
            pendingTabForceSelect = activeWhisperTabTarget;
        }
        else
        {
            state.LastChannel = channel;
        }

        plugin.SaveConfiguration();
    }

    private void DrawLogToolbar(CharacterState state)
    {
        // No left-click action on purpose - the search box used to sit inline here all the time;
        // now it only shows up in a small popup on right-click, to keep this row compact. The
        // icon still turns orange while a filter is active as a reminder it's not "just empty".
        var hasActiveSearch = logSearchFilter.Length > 0;
        if (hasActiveSearch)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, SearchHighlightColor);
        }

        ImGuiComponents.IconButton(FontAwesomeIcon.Search);

        if (hasActiveSearch)
        {
            ImGui.PopStyleColor();
        }

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

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.CalendarAlt))
        {
            ImGui.OpenPopup("##historyDatesPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.LoadHistory"));
        }

        DrawHistoryDatesPopup();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start(new ProcessStartInfo(plugin.ChatLog.LogsFolderPath) { UseShellExecute = true });
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.OpenFolder"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Eraser))
        {
            plugin.ChatLog.Clear();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Clear"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.EyeSlash))
        {
            plugin.HideChatWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.HideChat"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            plugin.ToggleSettingsWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Settings"));
        }

        ImGui.SameLine();
        var locked = plugin.Configuration.ChatWindowLocked;
        if (ImGuiComponents.IconButton(locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen))
        {
            plugin.Configuration.ChatWindowLocked = !locked;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(locked ? Loc.T("Compose.WindowUnlock") : Loc.T("Compose.WindowLock"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.WindowRestore))
        {
            plugin.TogglePreviewWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.OpenPreviewWindow"));
        }

        ImGui.SameLine();
        DrawRpActionsMenu(state);

        if (loadedHistoryEntries != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({loadedHistoryLabel})");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bolt))
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
        if (dates.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("ChatLog.NoHistoryFiles"));
            ImGui.EndPopup();
            return;
        }

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##historyDateSearch", Loc.T("ChatLog.HistorySearchHint"), ref historySearchFilter, 20);

        ImGui.BeginChild("##historyDateList", new Vector2(180, 220), true);
        foreach (var date in dates)
        {
            if (historySearchFilter.Length > 0 && date.IndexOf(historySearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (ImGui.Selectable(date))
            {
                loadedHistoryEntries = plugin.ChatLog.LoadHistoryFile(date);
                loadedHistoryLabel = date;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndChild();

        ImGui.EndPopup();
    }

    private void DrawChatLogEntries(Configuration config, CharacterState state)
    {
        var viewingHistory = loadedHistoryEntries != null;
        IReadOnlyList<ChatLogEntry> entries = loadedHistoryEntries ?? plugin.ChatLog.Entries;

        // The scroll position of "##chatLogScroll" is a single value tied to that child's ID,
        // shared across every tab (switching tabs doesn't draw a new child, just different
        // content in the same one) - without this, opening a different tab kept whatever scroll
        // offset was left over from the previous one instead of showing the bottom of the new
        // tab's content. Comparing against the view shown last frame catches every way the view
        // can change: clicking a different tab, a whisper tab auto-opening, browsing to a loaded
        // history file, etc.
        var viewKey = viewingHistory
            ? $"H:{loadedHistoryLabel}"
            : !string.IsNullOrEmpty(activeWhisperTabTarget)
                ? $"W:{activeWhisperTabTarget}"
                : $"T:{activeTabId}";
        var viewChanged = viewKey != lastViewKey;
        lastViewKey = viewKey;

        // Was a flat 5px, which turned out too strict - a frame or two of height churn (a new
        // entry wrapping to a slightly different line count, the footer's measured height
        // settling, ...) could leave the scroll position a handful of pixels short of the exact
        // max on the very frame a new message arrives, silently breaking auto-follow until the
        // user nudged the scrollbar themselves. Widened to "within about a line or two" instead
        // of "essentially exact", which still respects "scrolled up on purpose" for anything more
        // than a nudge.
        const float nearBottomThresholdPx = 60f;
        var nearBottomBeforeDraw = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - nearBottomThresholdPx;
        var mentionTerm = plugin.MentionFirstName;

        // Measured once per frame, at the log area's left edge before any entry is drawn, so
        // every entry this frame wraps against the exact same width - see ColoredTextRenderer.
        var availableWidth = ImGui.GetContentRegionAvail().X;

        var showingWhisper = !string.IsNullOrEmpty(activeWhisperTabTarget);
        var activeTab = showingWhisper ? null : GetActiveFixedTab(config);

        var hasNewEntries = false;
        var hasNewOwnMessageInView = false;

        var index = 0;
        foreach (var entry in entries)
        {
            // Sequence-based (not count-based) so this still works once the buffer fills up and
            // starts evicting from the front - at that point the count stops changing at all,
            // which would otherwise permanently break auto-scroll and unread tracking below.
            var isNew = !viewingHistory && entry.Sequence > lastSeenSequence;
            if (isNew)
            {
                hasNewEntries = true;
                TrackUnreadForNewEntry(config, state, entry, showingWhisper, activeTab?.Id);
            }

            if (showingWhisper)
            {
                if (!MatchesWhisperTarget(entry, activeWhisperTabTarget))
                {
                    continue;
                }
            }
            else if (activeTab != null && !activeTab.Matches(entry.ChatType))
            {
                continue;
            }

            if (logSearchFilter.Length > 0
                && entry.Text.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Sender.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // A message you just sent yourself should always scroll into view regardless of where
            // you'd scrolled to - unlike someone else's message, there's no "reading history on
            // purpose" case where you wouldn't want to see your own reply land.
            if (isNew && IsSelfAuthored(entry))
            {
                hasNewOwnMessageInView = true;
            }

            DrawLogEntry(entry, config, mentionTerm, index, availableWidth);
            index++;
        }

        // Opening/switching to a tab always jumps to the bottom, unconditionally. Otherwise, new
        // messages only pull the view down if it was already at (or very near) the bottom - if
        // the user has scrolled up to read something older, a new message doesn't yank them back
        // down; scrolling to the bottom themselves is what re-enables auto-follow again. A new
        // message from yourself is the one exception - see hasNewOwnMessageInView above.
        if (viewChanged || hasNewOwnMessageInView || (!viewingHistory && hasNewEntries && nearBottomBeforeDraw))
        {
            ImGui.SetScrollHereY(1f);
        }

        if (!viewingHistory && entries.Count > 0)
        {
            lastSeenSequence = entries[^1].Sequence;
        }
    }

    private bool IsSelfAuthored(ChatLogEntry entry) =>
        entry.ChatType == XivChatType.TellOutgoing
        || string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bumps the unread badge on every tab a freshly-arrived message would show up in, except
    /// whichever one you're currently looking at. Your own outgoing messages never count as
    /// "missed".
    /// </summary>
    private void TrackUnreadForNewEntry(Configuration config, CharacterState state, ChatLogEntry entry, bool showingWhisper, Guid? activeFixedTabId)
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

            if (!tab.Matches(entry.ChatType))
            {
                continue;
            }

            fixedTabUnreadCounts[tab.Id] = fixedTabUnreadCounts.GetValueOrDefault(tab.Id) + 1;
        }

        if (entry.ChatType == XivChatType.TellIncoming)
        {
            TrackIncomingWhisper(state, entry);
        }
    }

    /// <summary>
    /// Makes sure a newly-arrived whisper has a tab to show up in (conversations used to only
    /// get a tab once you replied to them) and bumps its unread count (which also drives the
    /// blink) if you're not already looking at it.
    /// </summary>
    private void TrackIncomingWhisper(CharacterState state, ChatLogEntry entry)
    {
        var senderIdentity = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var tabKey = ResolveWhisperTabKey(state, senderIdentity, entry.Sender);

        plugin.RememberWhisperTarget(state, tabKey);

        if (!IsViewingWhisperTarget(tabKey, entry.Sender))
        {
            whisperUnreadCounts[tabKey] = whisperUnreadCounts.GetValueOrDefault(tabKey) + 1;
        }
    }

    /// <summary>
    /// A whisper target may already be tracked under a slightly different string (e.g. "Name"
    /// from a manually-typed target vs. "Name@World" from an incoming message's payload) -
    /// reuse whatever form is already there instead of creating a second, duplicate tab for the
    /// same person.
    /// </summary>
    private static string ResolveWhisperTabKey(CharacterState state, string preferredIdentity, string nameOnly)
    {
        foreach (var existing in state.RecentWhisperTargets)
        {
            if (string.Equals(existing.Split('@')[0], nameOnly, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        return preferredIdentity;
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

    private void DrawLogEntry(ChatLogEntry entry, Configuration config, string mentionTerm, int index, float availableWidth)
    {
        ImGui.PushID(index);
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

        // For an outgoing tell, entry.Sender actually holds the RECIPIENT's name (that's how the
        // game itself reports XivChatType.TellOutgoing) - showing it as-is made both sides of a
        // whisper conversation display under the other person's name, with no way to tell who
        // said what. Label it "You"/"Du" instead, in the send-accent color so it's visually
        // distinct from the other party's messages too.
        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        if (!isOutgoingTell)
        {
            prefixSize.X += StatusIconRenderer.Draw(plugin, entry.SenderPayloads);
        }

        // SanitizeSenderName strips the world back out of entry.Sender for a cross-world sender
        // (see its own remarks) - it has to be appended back on here for the visible name, or the
        // sender's world silently disappears from the log entirely instead of just no longer
        // being garbled together with an unrenderable glyph.
        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var namePart = (isOutgoingTell ? plugin.OwnCharacterName : displayName) + ": ";

        // Per-conversation-partner colors (see DrawWhisperTabQuickEdit) only ever tint the
        // partner's own NAME - not the [T</T>] tag or the message text, which stay in the
        // regular whisper color so the rest of the conversation doesn't also change color per
        // person.
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

        if (!isOutgoingTell)
        {
            nameHovered = ImGui.IsItemHovered();
            var nameClicked = ImGui.IsItemClicked();
            nameRectMin = ImGui.GetItemRectMin();
            nameRectMax = ImGui.GetItemRectMax();
            whisperTarget = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

            // Plain left-click intentionally does nothing (it used to set the whisper target,
            // which was too easy to trigger by accident while just reading). Ctrl+click is the
            // fast path to the same "Whisper" action the right-click context menu offers.
            if (nameHovered && nameClicked && ImGui.GetIO().KeyCtrl)
            {
                plugin.OpenWhisperTab(whisperTarget);
            }
        }

        var namePartSize = ImGui.CalcTextSize(namePart);

        ImGui.SameLine(0, 0);
        if (entry.Payloads is { } payloads)
        {
            ColoredTextRenderer.DrawRich(
                payloads,
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
                payload => plugin.HandleChatLinkClicked(payload, payloads),
                plugin.OnChatLinkHovered);
        }
        else
        {
            ColoredTextRenderer.Draw(
                entry.Text,
                channelColor,
                config.EmoteTextColor,
                config.OocTextColor,
                config.MentionColor,
                availableWidth,
                mentionTerm,
                prefixSize.X + namePartSize.X,
                logSearchFilter,
                SearchHighlightColor);
        }

        ImGui.Spacing();

        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            var min = ImGui.GetItemRectMin() - new Vector2(4f, 2f);
            var max = ImGui.GetItemRectMax() + new Vector2(4f, 2f);
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 3f, ImDrawFlags.None, 1.3f);
        }

        // The name's hover rect sits entirely inside this whole-row group, so a right click on
        // the name also counts as a right click on the group - two separate BeginPopupContextItem
        // calls (one on the name, one on the group) would both react to that same click, and the
        // group's (evaluated second) would always win, meaning the name-specific menu could never
        // actually show. Recording which one the click was really on here, then building a single
        // merged popup below, avoids that race instead of trying to win it.
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
                    plugin.OpenWhisperTab(whisperTarget);
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

            // Highlight just the name text itself while hovered, not the whole message row
            // (that's the separate outline drawn around the whole group above).
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
    }

    /// <summary>
    /// Tints the whole compose box while it starts with a slash command - green for one the game
    /// or some loaded plugin actually recognises ("/dance"), red for one that doesn't exist at all
    /// ("/blabliblubb") - matching Chat2's own InputHandler (same whole-box tint approach, verified
    /// against their real source). Null (no tint, normal text color) for plain non-command text.
    /// </summary>
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
            // Enter always reaches this filter as a plain '\n' character (ImGui inserts newlines
            // in multiline inputs through the same char-insertion path as typed characters) -
            // rejecting it here (return 1) stops it from ever being inserted, which is far more
            // reliable than letting it in and trying to strip it back out afterwards. Shift+Enter
            // is left alone so it still inserts a real paragraph break.
            if (data.EventChar == '\n' && plugin.Configuration.SendOnEnter && !ImGui.GetIO().KeyShift)
            {
                pendingSendFromEnter = true;
                return 1;
            }

            // Same trick as Enter above: Tab reaches the char filter as a literal '\t' (allowed
            // through in the first place by ImGuiInputTextFlags.AllowTabInput, which otherwise
            // would've made Tab move focus off the box instead). Rejecting it here and opening
            // the auto-translate picker instead mirrors vanilla FFXIV/Chat2's Tab behavior.
            if (data.EventChar == '\t')
            {
                pendingAutoTranslatePopup = true;
                return 1;
            }

            return 0;
        }

        // data.CursorPos/SelectionStart/SelectionEnd are byte offsets into ImGui's internal
        // UTF8 text buffer, not char indices into the C# (UTF16) inputText string - with any
        // multi-byte character before the cursor (e.g. ae/oe/ue/ss in German text) the two
        // diverge, so every inputText[cursorPos] lookup elsewhere silently reads the wrong
        // character unless it's converted here first.
        selectionStart = ByteOffsetToCharIndex(data, data.SelectionStart);
        selectionEnd = ByteOffsetToCharIndex(data, data.SelectionEnd);
        cursorPos = ByteOffsetToCharIndex(data, data.CursorPos);
        return 0;
    }

    private static unsafe int ByteOffsetToCharIndex(ImGuiInputTextCallbackData data, int byteOffset)
    {
        var length = Math.Clamp(byteOffset, 0, data.BufTextLen);
        return Encoding.UTF8.GetCharCount(data.Buf, length);
    }

    /// <summary>
    /// The Tab-triggered auto-translate picker: a search box, or (with nothing typed) a
    /// browsable list of categories from the game's own "Completion" sheet - same data and
    /// layout as vanilla FFXIV/Chat2's Tab popup. Selecting an entry inserts a
    /// "[[Display Text#group.key]]" marker at the cursor (see MessageMarkerEncoder for how that
    /// becomes the real payload at send time).
    /// </summary>
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
                // ImGui.Selectable uses its label as the widget's own ID - without a unique
                // suffix, two entries that happen to share identical display text (e.g. the
                // same word appearing in different categories, like several "retainer..."
                // entries here) would collide and could report a click on the wrong one.
                // Group+Key together are unique per entry, so they're a safe disambiguator.
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
                // Cleared for next frame - browseGroup (captured above) is still the group to
                // show for the remainder of *this* frame, so the list below doesn't go straight
                // from "back" to a null-group crash in the same click.
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
        // The group/key are embedded directly in the marker (parsed back out by
        // MessageMarkerEncoder at send time) instead of being looked up from a separate
        // dictionary keyed by display text - see MessageMarkerEncoder's own remarks for why
        // that's not safe (two different sheet entries can share identical display text).
        InsertMarkerAtCursor(state, MessageMarkerEncoder.BuildAutoTranslateMarker(entry));
    }

    /// <summary>
    /// Applies text from a native "activate chat with this prefill" event (map flags, item links,
    /// and similar - see ChatActivationWatcher) using the exact same rule Chat2's own Activated()
    /// handler uses: a leading '/' means it's a full command and replaces the whole draft (e.g. a
    /// tell reply target), otherwise it's appended to the end - and only if not already present,
    /// so placing the same flag twice in a row doesn't spam duplicate "&lt;flag&gt;"s into the
    /// draft. This is deliberately NOT inserted at the cursor (unlike auto-translate entries,
    /// which the user explicitly picks mid-typing) - these events can land at any moment,
    /// including while the compose box isn't even focused.
    /// </summary>
    private void InsertChatPrefillText(CharacterState state, string text)
    {
        if (text.StartsWith('/'))
        {
            inputText = text;
        }
        else if (!inputText.Contains(text, StringComparison.Ordinal))
        {
            inputText += text;
        }

        state.Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();
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
        // When SendOnEnter is active, Enter is handled in TrackTextState's CallbackCharFilter
        // branch instead (it has to run there, before the '\n' is ever inserted - see
        // pendingSendFromEnter). This method only covers the legacy Ctrl+Enter-to-send hotkey.
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
        // A message that itself starts with "/" is the user directly typing a game command (/r,
        // /yes, /tell Name text, ...) rather than composing chat text - send it verbatim through
        // the game's own command parser instead of wrapping it in our channel prefix and running
        // it through the message splitter (which would both mangle the command and, for a long
        // one, incorrectly split it into multiple numbered messages).
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
            // Enter on an empty draft behaves like Escape: closes/defocuses the input box
            // instead of complaining there's nothing to send, mirroring the game's own chat box
            // and letting movement resume immediately.
            validationMessage = string.Empty;
            ImGuiP.ClearActiveID();
            return;
        }

        if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrWhiteSpace(state.LastWhisperTarget))
        {
            validationMessage = Loc.T("Compose.WhisperTargetMissing");
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

        // While the multiline input keeps keyboard focus (always true for a keyboard-only send
        // like Enter or Ctrl+Enter, unlike clicking the Send button), ImGui's InputText widget
        // ignores external changes to its bound string and keeps rendering its own internal
        // buffer - clearing inputText above has no visible effect until the widget deactivates
        // and re-reads it. Forcing that here is what actually makes the field clear on screen.
        ImGuiP.ClearActiveID();
    }

    private void DrawPreviewToggle(Configuration config, CharacterState state, List<string> chunks)
    {
        DrawChannelPickerButton(config, state);

        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Compose.MessageCount", chunks.Count));
    }

    private void DrawActionRow()
    {
        if (!plugin.IsSending)
        {
            return;
        }

        var done = plugin.SendTotal - plugin.SendRemaining;
        ImGui.TextUnformatted(Loc.T("Compose.Sending", done, plugin.SendTotal));
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Ban, new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.68f, 0.22f, 0.22f, 1f)))
        {
            plugin.CancelSending();
        }
    }

    /// <summary>
    /// Emote/OOC wrapping, templates, history and copy used to each be their own icon button in
    /// a row - consolidated into one small menu behind a single button in the top toolbar (see
    /// DrawLogToolbar), since Enter (see Plugin.OnFrameworkUpdate/TrackTextState) is now the
    /// only way to send, freeing up the bottom action row entirely.
    /// </summary>
    private void DrawRpActionsMenu(CharacterState state)
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Magic))
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

        // Normally a flag is inserted automatically the moment it's placed (see
        // ChatActivationWatcher/Plugin.PendingChatPrefillText) - this manual entry covers the case
        // where the flag was already standing before that watcher was ever wired up this session.
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

    private void WrapSelection(string open, string close)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, inputText.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, inputText.Length);

        if (start != end)
        {
            var selected = inputText.Substring(start, end - start);
            inputText = inputText[..start] + open + selected + close + inputText[end..];
        }
        else
        {
            var pos = Math.Clamp(cursorPos, 0, inputText.Length);
            inputText = inputText[..pos] + open + close + inputText[pos..];
        }

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

    // Plain icon + plain text was too easy to miss, which was a big part of "you still can't
    // tell who you're writing to" - this now uses the same per-target color the whisper
    // tab/sender name already uses (see ChannelDisplay.WhisperColor) and wraps it in a bordered
    // badge so it visually stands out from the rest of the toolbar instead of blending in. Fixed
    // height (one frame line) so this never changes size frame-to-frame.
    private static void DrawActiveWhisperIndicator(Configuration config, string target)
    {
        var color = ChannelDisplay.WhisperColor(target, config);

        // A couple px more than the tightest single-line value, not a big flat pad - the real fix
        // for baseline misalignment is the per-font AlignTextToFramePadding() calls below, not
        // extra height. Padding this generously has a real cost here (unlike a one-off UI element):
        // the window's total height is fixed, so anything this row grows by is space taken
        // directly from the compose area at the bottom, which showed up as the send-channel
        // button/footer getting pushed to (or past) the window's bottom edge.
        var lineHeight = MathF.Max(ImGui.GetTextLineHeight(), ImGui.GetFrameHeight()) + 2f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(color.X, color.Y, color.Z, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(color.X, color.Y, color.Z, 0.75f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.BeginChild("##activeWhisperIndicator", new Vector2(-1, lineHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // AlignTextToFramePadding() has to run once *per font* right before that font's own text
        // call, not once up front - it bases its baseline math on whichever font is active at the
        // moment it's called, so calling it only before the icon (in the old version) aligned the
        // icon to the icon font's baseline but left the plain-text call after it with no alignment
        // of its own.
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color, FontAwesomeIcon.EnvelopeOpen.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color, Loc.T("Whisper.ActiveIndicator", target));

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

    /// <summary>
    /// The one tab bar controlling what's shown in the log below: the default "Alle" tab plus any
    /// custom tabs from <see cref="Configuration.ChatTabs"/> (pure display filters, never touch
    /// the send target), followed by one closable tab per entry in
    /// <see cref="CharacterState.RecentWhisperTargets"/> (clicking one both shows that
    /// conversation and, like before, readies the compose area to reply to it).
    /// </summary>
    private void DrawLogTabBar(Configuration config, CharacterState state)
    {
        if (!ImGui.BeginTabBar("##logTabs", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            return;
        }

        foreach (var tab in config.ChatTabs)
        {
            // Torn off into the secondary window (see TabDragHelper/Plugin.MoveTabToSecondaryWindow) -
            // it lives exclusively over there until dragged back.
            if (plugin.IsTabInSecondaryWindow(tab.Id))
            {
                continue;
            }

            // The label text itself deliberately never changes with the unread count - a tab
            // bar with ImGuiTabBarFlags.Reorderable re-lays-out based on each tab's measured
            // width, so appending e.g. " (3)" directly into the label made every tab visibly
            // shift position on its own as messages arrived. The count is instead drawn as a
            // small badge overlay after the tab (see DrawUnreadBadge), which doesn't affect
            // the tab's own measured width at all.
            var fixedUnread = fixedTabUnreadCounts.GetValueOrDefault(tab.Id);
            var tabFlags = tab.PositionLocked ? ImGuiTabItemFlags.NoReorder : ImGuiTabItemFlags.None;

            // BeginTabItem's return value IS ImGui's own authoritative "is this tab currently
            // selected" state - layering IsItemClicked/IsItemActivated edge detection on top of
            // it was unreliable because ImGui defers a tab-bar's selection change to the *next*
            // frame's BeginTabBar call, so a click and the resulting BeginTabItem()==true
            // practically never land on the same frame (looks like "the first click does
            // nothing, the second one switches"). Simply mirroring the return value here every
            // frame sidesteps that entirely.
            var tabIsOpen = ImGui.BeginTabItem(tab.Name + "##tab" + tab.Id, tabFlags);

            DrawUnreadBadge(fixedUnread);

            // Hover/tear-off has to be checked right after BeginTabItem, not after a
            // conditional EndTabItem() below - for a non-selected tab EndTabItem never runs at
            // all, and either way this is the one point guaranteed to still refer to the tab
            // header item itself.
            if (!tab.PositionLocked)
            {
                TabDragHelper.HandleHoverAndTearOff(() => plugin.MoveTabToSecondaryWindow(tab.Id));
            }

            if (tabIsOpen)
            {
                if (activeTabId != tab.Id || !string.IsNullOrEmpty(activeWhisperTabTarget))
                {
                    SwitchToFixedTab(config, state, tab);
                    fixedTabUnreadCounts.Remove(tab.Id);
                }

                ImGui.EndTabItem();
            }

            DrawFixedTabQuickEdit(tab);
        }

        string? closedTarget = null;

        foreach (var targetName in state.RecentWhisperTargets)
        {
            if (plugin.IsWhisperInSecondaryWindow(targetName))
            {
                continue;
            }

            // SetSelected is only passed on the frame we explicitly want to force a tab switch
            // (see pendingTabForceSelect), not every frame based on activeWhisperTabTarget -
            // otherwise it fights with the user's own clicks on a different tab and can never
            // "let go" of whichever tab happened to be first.
            var flags = targetName == pendingTabForceSelect ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (state.LockedWhisperTargets.Contains(targetName))
            {
                flags |= ImGuiTabItemFlags.NoReorder;
            }

            var whisperUnread = whisperUnreadCounts.GetValueOrDefault(targetName);

            // No count appended here either - see the fixed tabs above for why a
            // width-changing label fights with ImGuiTabBarFlags.Reorderable. Only the visible
            // part is shortened to the forename - the ##id half keeps the full target name so
            // tab identity, drag/drop and quick-edit lookups are unaffected.
            var whisperLabel = FirstName(targetName) + "##whisper" + targetName;
            var open = true;

            // Same reasoning as the fixed tabs above: trust BeginTabItem's own return value as
            // the selected-tab signal instead of racing it against IsItemClicked/IsItemActivated,
            // which lag a frame behind an actual click.
            var tabIsOpen = ImGui.BeginTabItem(whisperLabel, ref open, flags);

            // Same small red count badge as the fixed tabs above (see DrawUnreadBadge) rather
            // than blinking the whole tab's text color - a persistent badge reads as "you have
            // unread messages" at a glance without the dated, attention-grabbing blink.
            DrawUnreadBadge(whisperUnread);

            if (!state.LockedWhisperTargets.Contains(targetName))
            {
                TabDragHelper.HandleHoverAndTearOff(() => plugin.MoveWhisperToSecondaryWindow(targetName));
            }

            // The tab label itself only shows the forename (see FirstName above) - show the
            // full "Forename Surname@World" on hover so players with the same forename stay
            // distinguishable.
            if (ImGui.IsItemHovered() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                ImGui.SetTooltip(targetName);
            }

            if (tabIsOpen)
            {
                if (activeWhisperTabTarget != targetName)
                {
                    EnterWhisperView(config, state, targetName);
                    whisperUnreadCounts.Remove(targetName);
                    plugin.SaveConfiguration();
                }

                ImGui.EndTabItem();
            }

            DrawWhisperTabQuickEdit(state, targetName);

            if (!open)
            {
                closedTarget = targetName;
            }
        }

        ImGui.EndTabBar();
        pendingTabForceSelect = null;

        if (closedTarget != null)
        {
            state.RecentWhisperTargets.Remove(closedTarget);
            whisperUnreadCounts.Remove(closedTarget);

            // If this conversation gets a fresh tab later (new message either way), it should
            // only show what's said from this point on - not the history that's still sitting
            // in the shared buffer.
            var entries = plugin.ChatLog.Entries;
            whisperCloseCutoff[closedTarget] = entries.Count > 0 ? entries[^1].Sequence : 0;

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

    /// <summary>
    /// Switches to a fixed tab, saving the current send-channel selection into whichever fixed
    /// tab is being left (skipped if a whisper conversation was being viewed instead - its
    /// "Whisper" channel isn't something to save back into the fixed tab underneath it, since
    /// that fixed tab's own selection was already saved at the point the whisper tab was
    /// entered) and restoring the destination tab's own remembered selection.
    /// </summary>
    private void SwitchToFixedTab(Configuration config, CharacterState state, ChatTab newTab)
    {
        if (string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            SaveChannelSelection(config.ChatTabs.Find(t => t.Id == activeTabId), state);
        }

        activeTabId = newTab.Id;
        activeWhisperTabTarget = string.Empty;
        LoadChannelSelection(newTab, state);
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

    /// <summary>
    /// Switches the view to a whisper conversation - the one place that does so (a tab click, a
    /// context-menu "Whisper" action, the channel picker's Whisper option, ...) all route
    /// through this, since whichever fixed tab is being left behind needs its current send
    /// channel saved *before* state.LastChannel gets forced to Whisper immediately below, not
    /// afterwards where it'd already be overwritten.
    /// </summary>
    private void EnterWhisperView(Configuration config, CharacterState state, string target)
    {
        if (string.IsNullOrEmpty(activeWhisperTabTarget))
        {
            SaveChannelSelection(config.ChatTabs.Find(t => t.Id == activeTabId), state);
        }

        state.LastWhisperTarget = target;
        state.LastChannel = ChatChannel.Whisper;
        activeWhisperTabTarget = target;
    }

    /// <summary>
    /// Right-click quick-edit popup for a custom tab: rename inline instead of having to open
    /// Settings > Tabs, plus a lock-position toggle so it's exempt from the tab bar's native
    /// drag-to-reorder (see ImGuiTabItemFlags.NoReorder above).
    /// </summary>
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

        ImGui.EndPopup();
    }

    /// <summary>
    /// Right-click quick-edit popup for a whisper tab - no rename (its name is the actual
    /// whisper target, renaming it would desync the tab label from who messages actually go to),
    /// just the same lock-position toggle the fixed tabs get.
    /// </summary>
    private void DrawWhisperTabQuickEdit(CharacterState state, string targetName)
    {
        if (!ImGui.BeginPopupContextItem("##whisperTabQuickEdit" + targetName))
        {
            return;
        }

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

        var config = plugin.Configuration;
        var color = ChannelDisplay.WhisperColor(targetName, config);
        ImGui.SetNextItemWidth(160);
        if (ImGui.ColorEdit4(Loc.T("Tabs.WhisperColorLabel") + "##whisperColor" + targetName, ref color))
        {
            config.WhisperColours[targetName] = color;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.UndoAlt))
        {
            config.WhisperColours.Remove(targetName);
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// A small unread-count badge drawn over a tab's top-right corner - must be called right
    /// after the tab item it belongs to (relies on GetItemRectMin/Max for that item). Doesn't
    /// touch the tab's own label, so unlike appending "(N)" to the label text, it can't affect
    /// the tab's measured width or shift its position in a Reorderable tab bar.
    /// </summary>
    private static void DrawUnreadBadge(int count)
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
        var center = new Vector2(max.X - radius * 0.6f, min.Y + radius * 0.6f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.85f, 0.15f, 0.15f, 1f)));
        drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);
    }

    private ChatTab? GetActiveFixedTab(Configuration config)
    {
        if (config.ChatTabs.Count == 0)
        {
            return null;
        }

        return config.ChatTabs.Find(t => t.Id == activeTabId) ?? config.ChatTabs[0];
    }

    private bool MatchesWhisperTarget(ChatLogEntry entry, string target)
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

        // A closed-then-reopened conversation should only show what's been said since it was
        // closed, not the entire history still sitting in the shared buffer - see
        // whisperCloseCutoff.
        if (whisperCloseCutoff.TryGetValue(target, out var cutoff) && entry.Sequence <= cutoff)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The channel-picker button's own label - shows the specific linkshell/cross-world
    /// linkshell name next to its number when one is known, instead of just "Linkshell [3]".
    /// </summary>
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

    // Whisper targets are stored as "Forename Surname@World" - shorten to just the forename
    // for compact UI spots (tab labels, the channel-picker label) while every internal lookup
    // (dictionaries, tab quick-edit, RecentWhisperTargets) keeps using the full name as its key.
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
