using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using CelinesChat.Services;

namespace CelinesChat.Windows;

/// <summary>Messages the user bookmarked (right-click a message), newest first.</summary>
internal sealed class BookmarksWindow : Window
{
    private readonly Plugin plugin;
    private string filter = string.Empty;
    private string? statusMessage;

    public BookmarksWindow(Plugin plugin)
        : base(Loc.T("Bookmarks.Title") + "##celinesChatBookmarks")
    {
        this.plugin = plugin;
        Size = new Vector2(460, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var bookmarks = config.Bookmarks;

        if (bookmarks.Count == 0)
        {
            ImGui.TextWrapped(Loc.T("Bookmarks.Empty"));
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##bookmarkFilter", Loc.T("Bookmarks.FilterHint"), ref filter, 100);

        if (statusMessage != null)
        {
            ImGui.TextDisabled(statusMessage);
        }

        using var child = ImRaii.Child("##bookmarkList", new Vector2(-1, -1), false);
        if (!child.Success)
        {
            return;
        }

        ChatBookmark? toRemove = null;
        for (var i = bookmarks.Count - 1; i >= 0; i--)
        {
            var bookmark = bookmarks[i];
            var sender = bookmark.SenderWorld != null ? $"{bookmark.Sender}@{bookmark.SenderWorld}" : bookmark.Sender;
            if (filter.Length > 0
                && bookmark.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                && sender.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            using var id = ImRaii.PushId(i);

            var channelColor = ChannelDisplay.Color(bookmark.ChatType, config);
            ImGui.TextColored(config.TimestampColor, bookmark.Timestamp.ToString(Loc.T("Bookmarks.TimeFormat"), Loc.Culture));
            ImGui.SameLine();
            ImGui.TextColored(channelColor, ChannelDisplay.DisplayTag(bookmark.ChatType));
            if (sender.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(channelColor, sender + ":");
            }

            ImGui.TextWrapped(bookmark.Text);

            if (ImGuiComponents.IconButton("open", FontAwesomeIcon.ExternalLinkAlt))
            {
                statusMessage = plugin.ChatWindowInstance.OpenBookmark(bookmark) ? null : Loc.T("Bookmarks.NotInLog");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Bookmarks.Open"));
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton("copy", FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(sender.Length > 0 ? $"{sender}: {bookmark.Text}" : bookmark.Text);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.CopyMessage"));
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash))
            {
                toRemove = bookmark;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.RemoveBookmark"));
            }

            ImGui.Separator();
        }

        if (toRemove != null)
        {
            plugin.RemoveBookmark(toRemove);
        }
    }
}
