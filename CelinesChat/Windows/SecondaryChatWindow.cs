using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal sealed class SecondaryChatWindow : Window
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    // See ChatWindow.BottomToleranceForNewMessagePx's own remarks.
    private const float BottomToleranceForNewMessagePx = 12f;

    private readonly Plugin plugin;
    private readonly Action onEmpty;
    private Guid activeTabId;
    private string activeWhisperTarget = string.Empty;
    private Vector2 lastKnownPosition;
    private Vector2 lastKnownSize;
    private long lastBoundsChangeTicks;
    private bool boundsSavePending;

    // See ChatWindow.lastSeenSequenceByView's own remarks - same bug, same fix: a single global
    // watermark let this window's own cross-tab/whisper traffic race a lower-traffic view's
    // watermark past a message before that view ever got credit for it as "new", silently
    // suppressing auto-scroll-to-bottom for it specifically.
    private readonly Dictionary<string, long> lastSeenSequenceByView = new();
    private string lastViewKey = string.Empty;

    // See ChatWindow.entryHeightCache's own remarks - same viewport-culling technique, same
    // reasoning (skip the real per-word ImGui work for an entry outside the visible area).
    private readonly Dictionary<long, float> entryHeightCache = new();
    private readonly HashSet<long> provisionalHeightSequences = new();
    private bool lastKnownFontAvailable = true;

    // See ChatWindow.viewMatchingLiveEntriesBuffer's own remarks - reused every frame to avoid
    // per-frame list allocation/growth.
    private readonly List<ChatLogEntry> viewMatchingLiveEntriesBuffer = new();

    private const long BoundsSaveDebounceMs = 400;

    public SecondaryChatWindow(Plugin plugin, Action onEmpty)
        : base(Loc.T("SecondaryChat.Title") + "##secondaryChat")
    {
        this.plugin = plugin;
        this.onEmpty = onEmpty;
        Size = new Vector2(420, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 180),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Flags |= ImGuiWindowFlags.NoTitleBar;
    }

    public override void OnClose()
    {
        plugin.ReturnAllTabsFromSecondaryWindow();
        onEmpty();
    }

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
    }

    public override void Draw()
    {
        // See ChatWindow.Draw's matching remarks - drawing proceeds either way; only caching a
        // height measured with the wrong font is skipped, not the whole frame.
        using var chatFontScope = plugin.PushChatFont();

        var fontAvailableNow = plugin.IsChatFontAvailable;
        if (fontAvailableNow && !lastKnownFontAvailable && provisionalHeightSequences.Count > 0)
        {
            // See ChatWindow.Draw's matching remarks - purge any heights that were measured with
            // the wrong (fallback) font while it wasn't ready, so they get correctly re-measured
            // immediately instead of drifting until the entry happens to scroll back into view.
            foreach (var seq in provisionalHeightSequences)
            {
                entryHeightCache.Remove(seq);
            }

            provisionalHeightSequences.Clear();
        }

        lastKnownFontAvailable = fontAvailableNow;

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

        DrawTabBar(config, state);

        var availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, config.ChatLogBackgroundOpacity));
        ImGui.BeginChild("##secondaryChatLog", new Vector2(-1, -1), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        ImGui.SetScrollX(0f);

        DrawFilteredEntries(config, availableWidth);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.PopStyleVar(8);

        TrackWindowBounds();
    }

    private void TrackWindowBounds()
    {
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        if (position != lastKnownPosition || size != lastKnownSize)
        {
            lastKnownPosition = position;
            lastKnownSize = size;
            lastBoundsChangeTicks = Environment.TickCount64;
            boundsSavePending = true;
            return;
        }

        if (boundsSavePending && Environment.TickCount64 - lastBoundsChangeTicks >= BoundsSaveDebounceMs)
        {
            boundsSavePending = false;
            plugin.SaveSecondaryWindowBounds(lastKnownPosition, lastKnownSize);
        }
    }

    private void DrawTabBar(Configuration config, CharacterState state)
    {
        var myTabs = config.ChatTabs.FindAll(t => plugin.IsTabInSecondaryWindow(t.Id));
        var myWhispers = state.RecentWhisperTargets.FindAll(plugin.IsWhisperInSecondaryWindow);

        if (myTabs.Count == 0 && myWhispers.Count == 0)
        {
            IsOpen = false;
            onEmpty();
            return;
        }

        var lineHeight = ImGui.GetFrameHeight();

        ImGui.BeginChild("##secondaryTabStrip", new Vector2(-1, lineHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (TabDragHelper.DrawDropZone(ImGui.GetContentRegionAvail().X, lineHeight) is { } droppedHere)
        {
            if (Guid.TryParse(droppedHere, out var droppedTabId))
            {
                plugin.MoveTabToSecondaryWindow(droppedTabId);
            }
            else
            {
                plugin.MoveWhisperToSecondaryWindow(droppedHere);
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

        foreach (var tab in myTabs)
        {
            ImGui.PushID(tab.Id.ToString());

            var isSelected = string.IsNullOrEmpty(activeWhisperTarget) && activeTabId == tab.Id;
            var tabWidth = ImGui.CalcTextSize(tab.Name).X + ImGui.GetStyle().FramePadding.X * 2f;
            var tabSize = new Vector2(tabWidth, lineHeight);

            ChatWindow.DrawTabBackgroundFill(tab.BackgroundColor ?? (config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null), tabSize);
            var clicked = ImGui.Selectable(tab.Name + "##secTab", isSelected, ImGuiSelectableFlags.None, tabSize);

            TabDragHelper.HandleHoverAndTearOff(tab.Id.ToString(), tab.Name, _ => { });

            if (TabDragHelper.DrawReorderTarget(tab.Id.ToString(), out var insertBefore) is { } droppedTabIdStr
                && Guid.TryParse(droppedTabIdStr, out var droppedTabId2))
            {
                plugin.MoveTabToSecondaryWindow(droppedTabId2);

                var draggedIndex = config.ChatTabs.FindIndex(t => t.Id == droppedTabId2);
                if (draggedIndex >= 0)
                {
                    var draggedTab = config.ChatTabs[draggedIndex];
                    config.ChatTabs.RemoveAt(draggedIndex);
                    var targetIndex = config.ChatTabs.IndexOf(tab);
                    config.ChatTabs.Insert(insertBefore ? targetIndex : targetIndex + 1, draggedTab);
                    plugin.SaveConfiguration();
                }
            }

            if (clicked && !isSelected)
            {
                activeTabId = tab.Id;
                activeWhisperTarget = string.Empty;
            }

            ImGui.PopID();
            ImGui.SameLine();
        }

        foreach (var target in myWhispers)
        {
            ImGui.PushID(target);

            var isSelected = activeWhisperTarget == target;
            var label = ChatWindow.FirstName(target);
            var tabWidth = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f;
            var tabSize = new Vector2(tabWidth, lineHeight);

            var whisperBg = config.WhisperTabBackgroundColours.TryGetValue(target, out var customBg)
                ? customBg
                : config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null;
            ChatWindow.DrawTabBackgroundFill(whisperBg, tabSize);
            var clicked = ImGui.Selectable(label + "##secWhisper", isSelected, ImGuiSelectableFlags.None, tabSize);

            TabDragHelper.HandleHoverAndTearOff(target, label, _ => { });

            if (TabDragHelper.DrawReorderTarget(target, out var insertWhisperBefore) is { } droppedTargetName
                && !Guid.TryParse(droppedTargetName, out _))
            {
                plugin.MoveWhisperToSecondaryWindow(droppedTargetName);

                var draggedIndex = state.RecentWhisperTargets.IndexOf(droppedTargetName);
                if (draggedIndex >= 0)
                {
                    state.RecentWhisperTargets.RemoveAt(draggedIndex);
                    var targetIndex = state.RecentWhisperTargets.IndexOf(target);
                    state.RecentWhisperTargets.Insert(insertWhisperBefore ? targetIndex : targetIndex + 1, droppedTargetName);
                    plugin.SaveConfiguration();
                }
            }

            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    plugin.MoveWhisperToMainWindow(target);
                }
                else if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ImGui.SetTooltip(target);
                }
            }

            if (clicked && !isSelected)
            {
                activeTabId = Guid.Empty;
                activeWhisperTarget = target;
            }

            ImGui.PopID();
            ImGui.SameLine();
        }

        ImGui.PopStyleVar();

        var dragHandleWidth = ImGui.GetContentRegionAvail().X;
        if (dragHandleWidth > 1f)
        {
            ImGui.InvisibleButton("##secondaryDragHandle", new Vector2(dragHandleWidth, lineHeight));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
        }

        ImGui.EndChild();

        var stillHaveActiveTab = activeTabId != Guid.Empty && myTabs.Exists(t => t.Id == activeTabId);
        var stillHaveActiveWhisper = activeWhisperTarget.Length > 0 && myWhispers.Contains(activeWhisperTarget);
        if (!stillHaveActiveTab && !stillHaveActiveWhisper)
        {
            if (myTabs.Count > 0)
            {
                activeTabId = myTabs[0].Id;
                activeWhisperTarget = string.Empty;
            }
            else
            {
                activeTabId = Guid.Empty;
                activeWhisperTarget = myWhispers[0];
            }
        }
    }

    private void DrawFilteredEntries(Configuration config, float availableWidth)
    {
        var showingWhisper = activeWhisperTarget.Length > 0;
        var tab = showingWhisper ? null : config.ChatTabs.Find(t => t.Id == activeTabId);

        if (!showingWhisper && tab == null)
        {
            return;
        }

        var viewKey = showingWhisper ? $"W:{activeWhisperTarget}" : $"T:{activeTabId}";
        var viewChanged = viewKey != lastViewKey;
        lastViewKey = viewKey;

        // See ChatWindow.DrawChatLogEntries' matching remarks - a small (well under one line's
        // height) tolerance, not exact equality: SetScrollHereY(1f) itself can settle a handful of
        // pixels short of GetScrollMaxY(), and exact equality never recovers from that afterward.
        var nearBottomBeforeDraw = ImGui.GetScrollMaxY() - ImGui.GetScrollY() <= BottomToleranceForNewMessagePx;

        var hasNewEntries = false;
        var hasNewOwnMessageInView = false;

        var viewSeenBefore = lastSeenSequenceByView.GetValueOrDefault(viewKey, 0L);
        var viewSeenMax = viewSeenBefore;

        // See ChatWindow.DrawChatLogEntries' matching remarks - Snapshot(), not the live Entries
        // property, taken once here.
        var liveEntries = plugin.ChatLog.Snapshot();

        // See ChatWindow.DrawChatLogEntries' matching remarks - filter to the active view *before*
        // capping (walking backward and stopping once the cap is filled, not collecting every match
        // forward and trimming afterward), reusing viewMatchingLiveEntriesBuffer across frames
        // instead of allocating a new list every frame. Capping the raw global stream first meant a
        // busy, unrelated tab/channel's own volume alone could push this (possibly much lower-
        // traffic) view's recent messages out of the "last 600" window entirely, while older file-
        // preloaded whisper history (not subject to this cap) kept showing - "newest messages
        // vanish, old ones stay". Allocating a fresh, potentially-large list every frame before
        // trimming it was also producing enough GC pressure to show up as an actual UI hitch.
        const int maxLiveEntriesConsidered = 600;
        viewMatchingLiveEntriesBuffer.Clear();
        for (var i = liveEntries.Count - 1; i >= 0 && viewMatchingLiveEntriesBuffer.Count < maxLiveEntriesConsidered; i--)
        {
            var entry = liveEntries[i];
            if (showingWhisper ? MatchesWhisper(entry, activeWhisperTarget) : tab!.Matches(entry.ChatType))
            {
                viewMatchingLiveEntriesBuffer.Add(entry);
            }
        }

        viewMatchingLiveEntriesBuffer.Reverse();
        var entries = viewMatchingLiveEntriesBuffer;

        foreach (var entry in entries)
        {
            if (entry.Sequence > viewSeenMax)
            {
                viewSeenMax = entry.Sequence;
            }

            if (entry.Sequence > viewSeenBefore)
            {
                hasNewEntries = true;

                if (IsSelfAuthored(entry))
                {
                    hasNewOwnMessageInView = true;
                }
            }

            // See ChatWindow.DrawChatLogEntries' matching remarks - ImGui.IsItemVisible() against a
            // Dummy of the cached height, not a hand-rolled scrollY/viewHeight comparison.
            var hadCache = entryHeightCache.TryGetValue(entry.Sequence, out var cachedHeight);
            if (hadCache)
            {
                var beforeDummy = ImGui.GetCursorPos();
                ImGui.Dummy(new Vector2(availableWidth, cachedHeight));

                if (!ImGui.IsItemVisible())
                {
                    continue;
                }

                ImGui.SetCursorPos(beforeDummy);
            }

            var entryTop = ImGui.GetCursorPosY();
            DrawEntry(entry, config, availableWidth, showingWhisper);
            var newHeight = ImGui.GetCursorPosY() - entryTop;

            // See ChatWindow.DrawChatLogEntries' matching remarks - only skip caching if an entry
            // already had a real, correctly-measured height to protect; an uncached entry still
            // needs something written so culling has a height to work with at all. Either way,
            // mark it provisional so Draw's own font-restored check can fix it up immediately.
            if (!plugin.IsChatFontAvailable)
            {
                provisionalHeightSequences.Add(entry.Sequence);

                if (hadCache)
                {
                    continue;
                }
            }

            entryHeightCache[entry.Sequence] = newHeight;
        }

        if (viewChanged)
        {
            ImGui.SetScrollHereY(1f);
        }
        else if (hasNewOwnMessageInView || (hasNewEntries && nearBottomBeforeDraw))
        {
            // See ChatWindow.DrawChatLogEntries' matching remarks - only snaps when something
            // actually new arrived while already near the bottom, not just because the current
            // position happens to be close to (but not exactly at) GetScrollMaxY(). Snapping on
            // proximity alone hijacked manual scroll-back-toward-bottom gestures the instant they
            // crossed into range.
            ImGui.SetScrollHereY(1f);
        }

        lastSeenSequenceByView[viewKey] = viewSeenMax;
    }

    private bool IsSelfAuthored(ChatLogEntry entry) =>
        entry.ChatType == XivChatType.TellOutgoing
        || string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase);

    private void DrawEntry(ChatLogEntry entry, Configuration config, float availableWidth, bool showingWhisper)
    {
        ImGui.PushID((int)entry.Sequence);
        ImGui.BeginGroup();

        var channelColor = ChannelDisplay.Color(entry.ChatType, config);
        var prefixSize = Vector2.Zero;

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
        var namePart = rawName + ": ";

        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : entry.ChatType == XivChatType.TellIncoming
                ? ChannelDisplay.WhisperColor(displayName, config)
                : channelColor;
        ImGui.TextColored(senderColor, namePart);
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
            plugin.MentionFirstName,
            prefixSize.X + namePartSize.X,
            string.Empty,
            null,
            target =>
            {
                if (target.Url != null)
                {
                    ChatWindow.OpenUrl(target.Url, plugin);
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
        ImGui.PopID();
    }

    private static bool MatchesWhisper(ChatLogEntry entry, string target)
    {
        if (entry.ChatType != XivChatType.TellIncoming && entry.ChatType != XivChatType.TellOutgoing)
        {
            return false;
        }

        var entryIdentity = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        if (string.Equals(entryIdentity, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(entry.Sender, target.Split('@')[0], StringComparison.OrdinalIgnoreCase);
    }
}
