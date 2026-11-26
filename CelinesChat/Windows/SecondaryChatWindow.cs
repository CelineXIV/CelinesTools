using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal sealed class SecondaryChatWindow : Window
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private const float BottomToleranceForNewMessagePx = 12f;

    private readonly Plugin plugin;
    private readonly Action onEmpty;
    private Guid activeTabId;
    private string activeWhisperTarget = string.Empty;
    private Vector2 lastKnownPosition;
    private Vector2 lastKnownSize;
    private long lastBoundsChangeTicks;
    private bool boundsSavePending;

    private readonly Dictionary<string, long> lastSeenSequenceByView = new();
    private string lastViewKey = string.Empty;

    private readonly Dictionary<long, float> entryHeightCache = new();
    private readonly HashSet<long> provisionalHeightSequences = new();

    private readonly Dictionary<long, byte> entryVariantCache = new();
    private bool lastKnownFontAvailable = true;

    private readonly List<ChatLogEntry> viewMatchingLiveEntriesBuffer = new();

    private readonly List<ChatLogEntry> liveEntriesBuffer = new();

    private const long BoundsSaveDebounceMs = 400;

    private int seenLayoutVersion = -1;

    private string composeText = string.Empty;
    private string? composeError;
    private bool refocusCompose;

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

        RespectCloseHotkey = false;
    }

    public override void OnClose()
    {
        plugin.IsSecondaryChatInputActive = false;

        if (plugin.IsDisposing || plugin.IsSecondaryWindowHiddenByGameState)
        {
            return;
        }

        plugin.ReturnAllTabsFromSecondaryWindow();
        onEmpty();
    }

    private int c4tColorsPushed;
    private float currentOpacity = 1f;

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
        currentOpacity = opacity;
        c4tColorsPushed = C4tTheme.PushColors(plugin.Configuration);
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
            plugin.Perf.SecondaryWindow.Add(PerfStats.ElapsedMs(start));
        }
    }

    private void DrawCore()
    {
        C4tTheme.DrawDecorations(plugin.Configuration, currentOpacity);

        using var chatFontScope = plugin.PushChatFont();

        var fontAvailableNow = plugin.IsChatFontAvailable;
        if (fontAvailableNow && !lastKnownFontAvailable && provisionalHeightSequences.Count > 0)
        {
            foreach (var seq in provisionalHeightSequences)
            {
                entryHeightCache.Remove(seq);
            }

            provisionalHeightSequences.Clear();
        }

        lastKnownFontAvailable = fontAvailableNow;

        var config = plugin.Configuration;

        if (plugin.LayoutVersion != seenLayoutVersion)
        {
            seenLayoutVersion = plugin.LayoutVersion;
            entryHeightCache.Clear();
            entryVariantCache.Clear();
            provisionalHeightSequences.Clear();
        }

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f)
            .Push(ImGuiStyleVar.FrameRounding, 4f)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f))
            .Push(ImGuiStyleVar.ChildRounding, 6f)
            .Push(ImGuiStyleVar.PopupRounding, 6f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 6f)
            .Push(ImGuiStyleVar.GrabRounding, 4f)
            .Push(ImGuiStyleVar.TabRounding, 4f);

        var state = plugin.GetCharacterState();

        DrawTabBar(config, state);

        if (!IsOpen)
        {
            plugin.IsSecondaryChatInputActive = false;
            return;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var composeHeight = ImGui.GetFrameHeightWithSpacing() + (composeError != null ? ImGui.GetTextLineHeightWithSpacing() : 0f);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, C4tTheme.LogBackground(config)))
        using (ImRaii.Child("##secondaryChatLog", new Vector2(-1, -composeHeight), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            ImGui.SetScrollX(0f);
            C4tTheme.DrawWatermark(plugin);

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, config.LineSpacing), config.LineSpacing >= 0f))
            {
                DrawFilteredEntries(config, availableWidth);
            }
        }

        DrawCompose(config);
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

        var tabBarOrigin = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            tabBarOrigin,
            tabBarOrigin + new Vector2(ImGui.GetContentRegionAvail().X, lineHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, config.TabStripBackgroundOpacity)));

        using (ImRaii.Child("##secondaryTabStrip", new Vector2(-1, lineHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
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

            using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f)))
            {
                foreach (var tab in myTabs)
                {
                    using var id = ImRaii.PushId(tab.Id.ToString());

                    var isSelected = string.IsNullOrEmpty(activeWhisperTarget) && activeTabId == tab.Id;
                    var tabWidth = ImGui.CalcTextSize(tab.Name).X + ImGui.GetStyle().FramePadding.X * 2f;
                    var tabSize = new Vector2(tabWidth, lineHeight);

                    ChatWindow.DrawTabBackgroundFill(tab.BackgroundColor ?? (config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null), tabSize);
                    var clicked = ImGui.Selectable(tab.Name + "##secTab", isSelected, ImGuiSelectableFlags.None, tabSize);
                    ChatWindow.DrawActiveTabUnderline(isSelected, config);
                    DrawReturnToMainWindowContextMenu("##secTabQuickEdit", () => plugin.MoveTabToMainWindow(tab.Id));

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

                    ImGui.SameLine();
                }

                foreach (var target in myWhispers)
                {
                    using var id = ImRaii.PushId(target);

                    var isSelected = activeWhisperTarget == target;
                    var label = ChatWindow.FirstName(target);
                    var tabWidth = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f;
                    var tabSize = new Vector2(tabWidth, lineHeight);

                    var whisperBg = config.WhisperTabBackgroundColours.TryGetValue(target, out var customBg)
                        ? customBg
                        : config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null;
                    ChatWindow.DrawTabBackgroundFill(whisperBg, tabSize);
                    var clicked = ImGui.Selectable(label + "##secWhisper", isSelected, ImGuiSelectableFlags.None, tabSize);
                    ChatWindow.DrawActiveTabUnderline(isSelected, config);
                    DrawReturnToMainWindowContextMenu("##secWhisperQuickEdit", () => plugin.MoveWhisperToMainWindow(target));

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

                    ImGui.SameLine();
                }
            }

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
        }

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

    private static void DrawReturnToMainWindowContextMenu(string popupId, Action onReturn)
    {
        using var popup = ImRaii.ContextPopupItem(popupId);
        if (!popup.Success)
        {
            return;
        }

        if (ImGui.Selectable(Loc.T("Tabs.ReturnToMainWindow")))
        {
            onReturn();
            ImGui.CloseCurrentPopup();
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

        var nearBottomBeforeDraw = ImGui.GetScrollMaxY() - ImGui.GetScrollY() <= BottomToleranceForNewMessagePx;

        var hasNewEntries = false;
        var hasNewOwnMessageInView = false;

        var viewSeenBefore = lastSeenSequenceByView.GetValueOrDefault(viewKey, 0L);
        var viewSeenMax = viewSeenBefore;

        plugin.ChatLog.CopyEntriesTo(liveEntriesBuffer);
        var liveEntries = liveEntriesBuffer;

        const int maxLiveEntriesConsidered = 600;
        viewMatchingLiveEntriesBuffer.Clear();
        for (var i = liveEntries.Count - 1; i >= 0 && viewMatchingLiveEntriesBuffer.Count < maxLiveEntriesConsidered; i--)
        {
            var entry = liveEntries[i];
            if ((showingWhisper ? MatchesWhisper(entry, activeWhisperTarget) : tab!.Matches(entry.ChatType)) && !plugin.IsFilteredOut(entry))
            {
                viewMatchingLiveEntriesBuffer.Add(entry);
            }
        }

        viewMatchingLiveEntriesBuffer.Reverse();
        var entries = viewMatchingLiveEntriesBuffer;
        var showTimestamps = tab?.ShowTimestamps ?? true;
        DateTime? previousDate = null;

        ChatLogEntry? lastShownEntry = null;

        for (var i = 0; i < entries.Count; i++)
        {
            var last = i;
            if (config.CollapseRepeatedMessages)
            {
                while (last + 1 < entries.Count && MessageGrouping.IsRepeat(entries[last], entries[last + 1]))
                {
                    last++;
                }
            }

            for (var k = i; k <= last; k++)
            {
                var tracked = entries[k];
                if (tracked.Sequence > viewSeenMax)
                {
                    viewSeenMax = tracked.Sequence;
                }

                if (tracked.Sequence > viewSeenBefore)
                {
                    hasNewEntries = true;

                    if (IsSelfAuthored(tracked))
                    {
                        hasNewOwnMessageInView = true;
                    }
                }
            }

            var runFirst = entries[i];
            var entry = entries[last];
            var repeatCount = last - i + 1;
            var trailingSpacing = !(config.GroupConsecutiveMessages && last + 1 < entries.Count && MessageGrouping.Continues(entry, entries[last + 1]));
            i = last;

            var markerDrawn = false;
            var date = entry.Timestamp.Date;
            if (config.ShowDateSeparators && (previousDate is { } previous ? previous != date : date != DateTime.Today))
            {
                ChatWindow.DrawCenteredMarker(date.ToString(Loc.T("ChatLog.DateSeparatorFormat"), Loc.Culture), ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], availableWidth);
                markerDrawn = true;
            }

            previousDate = date;

            var continuation = config.GroupConsecutiveMessages && !markerDrawn && lastShownEntry != null && MessageGrouping.Continues(lastShownEntry, runFirst);
            lastShownEntry = entry;
            var variant = MessageGrouping.Variant(continuation, trailingSpacing, repeatCount);

            var hadCache = entryHeightCache.TryGetValue(entry.Sequence, out var cachedHeight)
                && entryVariantCache.TryGetValue(entry.Sequence, out var cachedVariant)
                && cachedVariant == variant;
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
            DrawEntry(entry, config, availableWidth, showingWhisper, showTimestamps, repeatCount, continuation, trailingSpacing);
            var newHeight = ImGui.GetCursorPosY() - entryTop;

            if (!plugin.IsChatFontAvailable)
            {
                provisionalHeightSequences.Add(entry.Sequence);

                if (hadCache)
                {
                    continue;
                }
            }

            entryHeightCache[entry.Sequence] = newHeight;
            entryVariantCache[entry.Sequence] = variant;
        }

        if (entryVariantCache.Count > 20000)
        {
            entryVariantCache.Clear();
        }

        if (viewChanged)
        {
            ImGui.SetScrollHereY(1f);
        }
        else if (hasNewOwnMessageInView || (hasNewEntries && nearBottomBeforeDraw))
        {
            ImGui.SetScrollHereY(1f);
        }

        lastSeenSequenceByView[viewKey] = viewSeenMax;
    }

    private bool IsSelfAuthored(ChatLogEntry entry) =>
        entry.ChatType == XivChatType.TellOutgoing
        || string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase);

    private void DrawEntry(ChatLogEntry entry, Configuration config, float availableWidth, bool showingWhisper, bool showTimestamp, int repeatCount, bool continuation, bool trailingSpacing)
    {
        using var id = ImRaii.PushId((int)entry.Sequence);
        using var group = ImRaii.Group();

        var channelColor = ChannelDisplay.Color(entry.ChatType, config);
        var allowRpFormatting = config.AllowsRpFormatting(entry.ChatType);
        var prefixSize = Vector2.Zero;

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

        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
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
            ImGui.SameLine(0, 0);
            prefixSize += ImGui.CalcTextSize(badge);
        }

        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

        var rawName = isOutgoingTell && showingWhisper ? plugin.OwnCharacterName : displayName;
        var namePart = continuation || string.IsNullOrEmpty(rawName) ? string.Empty : rawName + ": ";

        var relation = namePart.Length > 0 ? plugin.GetRelation(entry) : 0;
        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : ChatWindow.RelationNameColor(relation, entry, displayName, config)
                ?? (entry.ChatType == XivChatType.TellIncoming
                    ? ChannelDisplay.WhisperColor(displayName, config)
                    : channelColor);
        if (relation != 0 && config.ShowRelationIcons)
        {
            prefixSize.X += ChatWindow.DrawRelationIcon(relation, config);
        }

        if (namePart.Length > 0)
        {
            ImGui.TextColored(senderColor, namePart);
            if (ImGui.IsItemHovered() && plugin.GetPlayerNote(entry) is { } note)
            {
                ImGui.SetTooltip(note);
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
            plugin.MentionTerms,
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
            },
            allowRpFormatting: allowRpFormatting);

        if (trailingSpacing)
        {
            ChatWindow.AddMessageGap(config);
        }
    }

    private void DrawCompose(Configuration config)
    {
        var showingWhisper = activeWhisperTarget.Length > 0;
        var tab = showingWhisper ? null : config.ChatTabs.Find(t => t.Id == activeTabId);

        string channelLabel;
        if (showingWhisper)
        {
            channelLabel = Loc.T("Channel.WhisperWithTarget", ChatWindow.FirstName(activeWhisperTarget));
        }
        else if (tab != null)
        {
            channelLabel = ChannelLabel(tab.LastChannel, tab.LinkshellNumber, tab.CrossWorldLinkshellNumber);
        }
        else
        {
            plugin.IsSecondaryChatInputActive = false;
            return;
        }

        if (showingWhisper)
        {
            ImGui.TextColored(config.SendAccentColor, channelLabel);
            ImGui.SameLine();
        }
        else
        {
            if (ImGui.Button(channelLabel + "##secCompChannel"))
            {
                ImGui.OpenPopup("##secComposeChannelPicker");
            }

            ImGui.SameLine();
            DrawChannelPicker(tab!);
        }

        if (refocusCompose)
        {
            ImGui.SetKeyboardFocusHere();
            refocusCompose = false;
        }

        ImGui.SetNextItemWidth(-1);
        var submitted = ImGui.InputTextWithHint("##secondaryCompose", Loc.T("SecondaryChat.ComposeHint"), ref composeText, 2000, ImGuiInputTextFlags.EnterReturnsTrue);
        plugin.IsSecondaryChatInputActive = ImGui.IsItemActive();

        if (submitted)
        {
            var text = composeText;
            var error = showingWhisper
                ? plugin.SendToChannel(ChatChannel.Whisper, 1, 1, activeWhisperTarget, text)
                : plugin.SendToChannel(tab!.LastChannel == ChatChannel.Whisper ? ChatChannel.Say : tab.LastChannel, tab.LinkshellNumber, tab.CrossWorldLinkshellNumber, string.Empty, text);
            composeError = error;
            if (error == null)
            {
                composeText = string.Empty;
            }

            refocusCompose = true;
        }

        if (composeError != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), composeError);
        }
    }

    private static readonly (ChatChannel Channel, string LocKey)[] PickableChannels =
    {
        (ChatChannel.Say, "Channel.Say"),
        (ChatChannel.Party, "Channel.Party"),
        (ChatChannel.Alliance, "Channel.Alliance"),
        (ChatChannel.FreeCompany, "Channel.FreeCompany"),
        (ChatChannel.Yell, "Channel.Yell"),
        (ChatChannel.Shout, "Channel.Shout"),
        (ChatChannel.PvpTeam, "Channel.PvpTeam"),
        (ChatChannel.NoviceNetwork, "Channel.NoviceNetwork"),
    };

    private void DrawChannelPicker(ChatTab tab)
    {
        using var popup = ImRaii.Popup("##secComposeChannelPicker");
        if (!popup.Success)
        {
            return;
        }

        foreach (var (channel, key) in PickableChannels)
        {
            if (ImGui.Selectable(Loc.T(key) + "##secPick" + channel, tab.LastChannel == channel))
            {
                tab.LastChannel = channel;
                plugin.SaveConfiguration();
            }
        }

        ImGui.Separator();
        for (var number = 1; number <= 8; number++)
        {
            var label = ChannelLabel(ChatChannel.Linkshell, number, 1);
            if (ImGui.Selectable(label + "##secPickLs" + number, tab.LastChannel == ChatChannel.Linkshell && tab.LinkshellNumber == number))
            {
                tab.LastChannel = ChatChannel.Linkshell;
                tab.LinkshellNumber = number;
                plugin.SaveConfiguration();
            }
        }

        ImGui.Separator();
        for (var number = 1; number <= 8; number++)
        {
            var label = ChannelLabel(ChatChannel.CrossWorldLinkshell, 1, number);
            if (ImGui.Selectable(label + "##secPickCwls" + number, tab.LastChannel == ChatChannel.CrossWorldLinkshell && tab.CrossWorldLinkshellNumber == number))
            {
                tab.LastChannel = ChatChannel.CrossWorldLinkshell;
                tab.CrossWorldLinkshellNumber = number;
                plugin.SaveConfiguration();
            }
        }
    }

    private static string ChannelLabel(ChatChannel channel, int linkshellNumber, int crossWorldLinkshellNumber)
    {
        foreach (var (pickable, key) in PickableChannels)
        {
            if (pickable == channel)
            {
                return Loc.T(key);
            }
        }

        return channel switch
        {
            ChatChannel.Linkshell => NumberedLabel(Loc.T("Channel.Linkshell"), linkshellNumber, NativeChannels.GetExistingLinkshells()),
            ChatChannel.CrossWorldLinkshell => NumberedLabel(Loc.T("Channel.CrossWorldLinkshell"), crossWorldLinkshellNumber, NativeChannels.GetExistingCrossWorldLinkshells()),
            _ => Loc.T("Channel.Say"),
        };
    }

    private static string NumberedLabel(string baseName, int number, List<(int Number, string Name)> existing)
    {
        var match = existing.Find(e => e.Number == number);
        return match.Name != null ? $"{baseName} [{number}]: {match.Name}" : $"{baseName} [{number}]";
    }

    private static bool MatchesWhisper(ChatLogEntry entry, string target) =>
        ChatWindow.MatchesWhisperTargetIgnoringCutoff(entry, target);
}
