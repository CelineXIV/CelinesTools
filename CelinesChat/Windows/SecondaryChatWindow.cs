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
/// </summary>
internal sealed class SecondaryChatWindow : Window
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    private readonly Plugin plugin;
    private readonly Action onEmpty;
    private Guid activeTabId;
    private string activeWhisperTarget = string.Empty;

    public SecondaryChatWindow(Plugin plugin, Action onEmpty)
        : base(Loc.T("SecondaryChat.Title") + "##secondaryChat")
    {
        this.plugin = plugin;
        this.onEmpty = onEmpty;
        Size = new Vector2(420, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        // Closable and moveable like a normal window, just without the collapse arrow - there's
        // nothing meaningful to collapse into here.
        Flags |= ImGuiWindowFlags.NoCollapse;
    }

    public override void OnClose()
    {
        // Give every tab currently living here back to the main window rather than losing them
        // - closing this window is "I'm done with the second window", not "delete these tabs".
        plugin.ReturnAllTabsFromSecondaryWindow();
        onEmpty();
    }

    public override void Draw()
    {
        // See ChatWindow.Draw's remarks - scoped to the whole method so it covers everything
        // drawn here, popped automatically whenever Draw returns.
        using var chatFontScope = plugin.PushChatFont();

        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);

        var config = plugin.Configuration;
        var state = plugin.GetCharacterState();

        DrawTabBar(config, state);

        var availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.BeginChild("##secondaryChatLog", new Vector2(-1, -1), true);
        DrawFilteredEntries(config, availableWidth);
        ImGui.EndChild();
    }

    private void DrawTabBar(Configuration config, CharacterState state)
    {
        if (!ImGui.BeginTabBar("##secondaryTabs", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            return;
        }

        var myTabs = config.ChatTabs.FindAll(t => plugin.IsTabInSecondaryWindow(t.Id));
        foreach (var tab in myTabs)
        {
            var tabIsOpen = ImGui.BeginTabItem(tab.Name + "##secTab" + tab.Id);
            TabDragHelper.HandleHoverAndTearOff(() => plugin.MoveTabToMainWindow(tab.Id));

            if (tabIsOpen)
            {
                if (activeTabId != tab.Id || !string.IsNullOrEmpty(activeWhisperTarget))
                {
                    activeTabId = tab.Id;
                    activeWhisperTarget = string.Empty;
                }

                ImGui.EndTabItem();
            }
        }

        var myWhispers = state.RecentWhisperTargets.FindAll(plugin.IsWhisperInSecondaryWindow);
        foreach (var target in myWhispers)
        {
            var open = true;
            var tabIsOpen = ImGui.BeginTabItem(target + "##secWhisper" + target, ref open);
            TabDragHelper.HandleHoverAndTearOff(() => plugin.MoveWhisperToMainWindow(target));

            if (tabIsOpen)
            {
                if (activeWhisperTarget != target)
                {
                    activeTabId = Guid.Empty;
                    activeWhisperTarget = target;
                }

                ImGui.EndTabItem();
            }

            if (!open)
            {
                plugin.MoveWhisperToMainWindow(target);
            }
        }

        ImGui.EndTabBar();

        // Nothing left here (everything got dragged back out or closed) - no point leaving an
        // empty shell of a window open.
        if (myTabs.Count == 0 && myWhispers.Count == 0)
        {
            IsOpen = false;
            onEmpty();
            return;
        }

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

            DrawEntry(entry, config, index, availableWidth);
            index++;
        }

        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f)
        {
            ImGui.SetScrollHereY(1f);
        }
    }

    private void DrawEntry(ChatLogEntry entry, Configuration config, int index, float availableWidth)
    {
        ImGui.PushID(index);
        ImGui.BeginGroup();

        var channelColor = ChannelDisplay.Color(entry.ChatType, config);
        var prefixSize = Vector2.Zero;

        var timestamp = entry.Timestamp.ToString("HH:mm") + " ";
        ImGui.TextColored(config.TimestampColor, timestamp);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(timestamp);

        var tag = ChannelDisplay.Tag(entry.ChatType) + " ";
        ImGui.TextColored(channelColor, tag);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(tag);

        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        if (!isOutgoingTell)
        {
            prefixSize.X += StatusIconRenderer.Draw(plugin, entry.SenderPayloads);
        }

        var namePart = (isOutgoingTell ? plugin.OwnCharacterName : entry.Sender) + ": ";

        // Only the partner's own NAME picks up their per-conversation color override - see
        // ChatWindow.DrawLogEntry's matching remarks.
        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : entry.ChatType == XivChatType.TellIncoming
                ? ChannelDisplay.WhisperColor(entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender, config)
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
