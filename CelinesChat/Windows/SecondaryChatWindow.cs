using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

/// <summary>
/// The "torn off into its own window" home for tabs dragged out of the main chat window's tab
/// bar - a bit like World of Warcraft's chat frames: one extra window with its own tab bar,
/// which any number of tabs can be dragged into (and back out of, to either return to the main
/// window or, in principle, a future additional window). Read-only for now - composing still
/// happens in the main window.
///
/// Which tabs live here, plus this window's own position/size, are persisted (see
/// Configuration.SecondaryChatTabIds/SecondaryChatWindowPosition) - a layout like "main chat on
/// the left, a torn-off loot log on the right" is meant to survive a restart, not reset itself
/// back into the main window every load.
/// </summary>
internal sealed class SecondaryChatWindow : Window
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private readonly Plugin plugin;
    private readonly Action onEmpty;
    private Guid activeTabId;
    private string activeWhisperTarget = string.Empty;
    private Vector2 lastKnownPosition;
    private Vector2 lastKnownSize;
    private long lastBoundsChangeTicks;
    private bool boundsSavePending;

    private const long BoundsSaveDebounceMs = 400;

    public SecondaryChatWindow(Plugin plugin, Action onEmpty)
        : base(Loc.T("SecondaryChat.Title") + "##secondaryChat")
    {
        this.plugin = plugin;
        this.onEmpty = onEmpty;
        Size = new Vector2(420, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        // No title bar - a torn-off tab is meant to read as part of the same chat, not a separate
        // titled window with its own "Chat (2)" label and close button. The tab strip itself is
        // the only header it needs; see the drag handle drawn after the tabs in DrawTabBar for how
        // it's still movable without one, and resizing still works via the plain border grips
        // (independent of the title bar).
        Flags |= ImGuiWindowFlags.NoTitleBar;
    }

    public override void OnClose()
    {
        // Give every tab currently living here back to the main window rather than losing them
        // - closing this window is "I'm done with the second window", not "delete these tabs".
        plugin.ReturnAllTabsFromSecondaryWindow();
        onEmpty();
    }

    public override void PreDraw()
    {
        // Same background handling as the main ChatWindow - without this, this window used the
        // default fully-opaque ImGui background regardless of the user's own opacity settings,
        // which was a big part of it visually reading as "a different, plainer plugin".
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
    }

    public override void Draw()
    {
        // See ChatWindow.Draw's remarks - scoped to the whole method so it covers everything
        // drawn here, popped automatically whenever Draw returns.
        using var chatFontScope = plugin.PushChatFont();

        var config = plugin.Configuration;

        // Same chrome as the main ChatWindow, so this doesn't look like a different, older plugin
        // once it's torn off into its own window.
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
        ImGui.BeginChild("##secondaryChatLog", new Vector2(-1, -1), true);
        DrawFilteredEntries(config, availableWidth);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.PopStyleVar(8);

        TrackWindowBounds();
    }

    /// <summary>
    /// Persists this window's position/size once they've held still for BoundsSaveDebounceMs,
    /// not the instant either changes - position/size change on literally every frame while
    /// actually being dragged or resized, and SaveSecondaryWindowBounds writes the whole plugin
    /// config to disk synchronously (see Plugin.SaveConfiguration) - doing that 60 times a second
    /// mid-drag was blocking the UI thread long enough each frame to look like the whole window
    /// (and the chat log inside it) violently stuttering.
    /// </summary>
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

    /// <summary>
    /// Same hand-rolled Selectable-based tab look as ChatWindow.DrawLogTabBar (background fills,
    /// hover highlight, drag-and-drop tear-off) instead of Dear ImGui's own native tab bar this
    /// used before - the two windows are meant to look like one plugin, not a styled main window
    /// next to a plain default-themed second one.
    /// </summary>
    private void DrawTabBar(Configuration config, CharacterState state)
    {
        var myTabs = config.ChatTabs.FindAll(t => plugin.IsTabInSecondaryWindow(t.Id));
        var myWhispers = state.RecentWhisperTargets.FindAll(plugin.IsWhisperInSecondaryWindow);

        // Nothing left here (everything got dragged back out or closed) - no point leaving an
        // empty shell of a window open.
        if (myTabs.Count == 0 && myWhispers.Count == 0)
        {
            IsOpen = false;
            onEmpty();
            return;
        }

        var lineHeight = ImGui.GetFrameHeight();

        // Bounding the whole row to exactly lineHeight (same as ChatWindow.DrawLogTabBar's own
        // "##logTabsStrip" child) instead of letting it fall out naturally via SameLine+NewLine -
        // the latter left a visibly oversized gap between the tabs and the log below, since a
        // bare NewLine advances by a full text line plus ImGui's own item spacing on top, not the
        // tighter, deliberate height a child clips its content to.
        ImGui.BeginChild("##secondaryTabStrip", new Vector2(-1, lineHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // The whole strip is a drop target for tabs dragged out of the main window - see
        // TabDragHelper.DrawDropZone's remarks. Whatever's dropped here always joins this window
        // regardless of whether it's a fixed tab (a Guid) or a whisper (a plain name).
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

        // Centers each tab's own label within its Selectable - see ChatWindow.DrawLogTabBar's
        // matching PushStyleVar for why (a background-filled tab with left-aligned text read as
        // visually unbalanced/off, not just "not perfectly centered").
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

        foreach (var tab in myTabs)
        {
            ImGui.PushID(tab.Id.ToString());

            var isSelected = string.IsNullOrEmpty(activeWhisperTarget) && activeTabId == tab.Id;
            var tabWidth = ImGui.CalcTextSize(tab.Name).X + ImGui.GetStyle().FramePadding.X * 2f;
            var tabSize = new Vector2(tabWidth, lineHeight);

            ChatWindow.DrawTabBackgroundFill(tab.BackgroundColor ?? (config.TabStripBackgroundColorEnabled ? config.TabStripBackgroundColor : (Vector4?)null), tabSize);
            var clicked = ImGui.Selectable(tab.Name + "##secTab", isSelected, ImGuiSelectableFlags.None, tabSize);

            // Returning to the main window now requires an explicit drop onto its own tab strip
            // (see ChatWindow's DrawDropZone call) rather than just dragging any distance away -
            // dropping onto empty space elsewhere no longer silently sends it back.
            TabDragHelper.HandleHoverAndTearOff(tab.Id.ToString(), tab.Name, _ => { });

            if (TabDragHelper.DrawReorderTarget(tab.Id.ToString(), out var insertBefore) is { } droppedTabIdStr
                && Guid.TryParse(droppedTabIdStr, out var droppedTabId2))
            {
                // See ChatWindow.DrawLogTabBar's matching reorder accept for why this is needed -
                // a drop on a specific tab's left/right half can come from the main window just as
                // easily as from elsewhere in this same strip.
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
                // See the fixed-tab reorder accept above for why this is needed.
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

            // Same middle-click-to-close convention as the main window's whisper tabs - see
            // ChatWindow.DrawLogTabBar's remarks on why there's no visible "x" instead.
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

        // No title bar to grab (see the constructor's remarks) - the empty space in the tab strip
        // after the last tab doubles as the drag handle for moving this window instead.
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
            // Whatever was active just left (dragged back to the main window) - fall back to
            // whatever's left instead of showing a blank log.
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

        var index = 0;
        foreach (var entry in plugin.ChatLog.Entries)
        {
            if (showingWhisper)
            {
                if (!MatchesWhisper(entry, activeWhisperTarget))
                {
                    continue;
                }
            }
            else if (!tab!.Matches(entry.ChatType))
            {
                continue;
            }

            DrawEntry(entry, config, index, availableWidth, showingWhisper);
            index++;
        }

        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f)
        {
            ImGui.SetScrollHereY(1f);
        }
    }

    private void DrawEntry(ChatLogEntry entry, Configuration config, int index, float availableWidth, bool showingWhisper)
    {
        ImGui.PushID(index);
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
        if (!isOutgoingTell)
        {
            prefixSize.X += StatusIconRenderer.Draw(plugin, entry.SenderPayloads);
        }

        // See ChatWindow.DrawLogEntry's matching remarks - SanitizeSenderName strips the world
        // back out of a cross-world sender's name, so it has to be appended back on here for the
        // visible name.
        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;

        // See ChatWindow.DrawLogEntry's matching remarks - outside the dedicated whisper tab for
        // this conversation, showing your own name again doesn't say who you sent it to. The tag
        // drawn above already renders "[T>]" for TellOutgoing - do not add it again here.
        var rawName = isOutgoingTell && showingWhisper ? plugin.OwnCharacterName : displayName;
        var namePart = rawName + ": ";

        // Only the partner's own NAME picks up their per-conversation color override - see
        // ChatWindow.DrawLogEntry's matching remarks.
        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : entry.ChatType == XivChatType.TellIncoming
                ? ChannelDisplay.WhisperColor(displayName, config)
                : channelColor;
        ImGui.TextColored(senderColor, namePart);
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
                plugin.MentionFirstName,
                prefixSize.X + namePartSize.X,
                string.Empty,
                null,
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
                plugin.MentionFirstName,
                prefixSize.X + namePartSize.X);
        }

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
