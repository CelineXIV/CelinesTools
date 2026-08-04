using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CelinesRPChat.Services;

namespace CelinesRPChat.Windows;

internal sealed class ChatLogWindow : Window
{
    private readonly Plugin plugin;
    private int lastEntryCount;
    private List<ChatLogEntry>? loadedHistoryEntries;
    private string? loadedHistoryLabel;

    public ChatLogWindow(Plugin plugin) : base(WindowTitles.Read)
    {
        this.plugin = plugin;
        Size = new Vector2(480, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(plugin.Configuration.WindowOpacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);

        var config = plugin.Configuration;

        DrawFilters(config);

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Clear")))
        {
            plugin.ChatLog.Clear();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ChatLog.OpenFolder")))
        {
            Process.Start(new ProcessStartInfo(plugin.ChatLog.LogsFolderPath) { UseShellExecute = true });
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ChatLog.LoadHistory")))
        {
            ImGui.OpenPopup("##historyDatesPopup");
        }

        DrawHistoryDatesPopup();

        if (loadedHistoryEntries != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({loadedHistoryLabel})");
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("ChatLog.BackToLive")))
            {
                loadedHistoryEntries = null;
                loadedHistoryLabel = null;
            }
        }

        ImGui.Separator();

        var viewingHistory = loadedHistoryEntries != null;
        IReadOnlyList<ChatLogEntry> entries = loadedHistoryEntries ?? plugin.ChatLog.Entries;

        ImGui.BeginChild("##chatLogScroll", Vector2.Zero, false);

        var nearBottomBeforeDraw = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f;

        var mentionTerm = plugin.MentionFirstName;

        foreach (var entry in entries)
        {
            if (viewingHistory && !ChannelDisplay.IsVisible(entry.ChatType, config))
            {
                continue;
            }

            DrawEntry(entry, config, mentionTerm);
        }

        if (!viewingHistory && entries.Count != lastEntryCount && nearBottomBeforeDraw)
        {
            ImGui.SetScrollHereY(1f);
        }

        lastEntryCount = entries.Count;

        ImGui.EndChild();
    }

    private void DrawEntry(ChatLogEntry entry, Configuration config, string mentionTerm)
    {
        var prefixSize = Vector2.Zero;

        var timestamp = entry.Timestamp.ToString("HH:mm") + " ";
        ImGui.TextDisabled(timestamp);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(timestamp);

        var tag = ChannelDisplay.Tag(entry.ChatType) + " ";
        ImGui.TextColored(ChannelDisplay.Color(entry.ChatType), tag);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(tag);

        var namePart = entry.Sender + ": ";
        ImGui.TextColored(config.ChatLogSenderNameColor, namePart);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("ChatLog.ClickToWhisper"));
        }

        if (ImGui.IsItemClicked())
        {
            plugin.SetWhisperTarget(entry.Sender);
        }

        var namePartSize = ImGui.CalcTextSize(namePart);

        ImGui.SameLine(0, 0);
        ColoredTextRenderer.Draw(entry.Text, config.DefaultTextColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, mentionTerm, prefixSize.X + namePartSize.X);
        ImGui.Spacing();
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
        }

        foreach (var date in dates)
        {
            if (ImGui.Selectable(date))
            {
                loadedHistoryEntries = plugin.ChatLog.LoadHistoryFile(date);
                loadedHistoryLabel = date;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void DrawFilters(Configuration config)
    {
        var showSay = config.ChatLogShowSay;
        if (ImGui.Checkbox(Loc.T("Channel.Say") + "##filterSay", ref showSay))
        {
            config.ChatLogShowSay = showSay;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showParty = config.ChatLogShowParty;
        if (ImGui.Checkbox(Loc.T("Channel.Party") + "##filterParty", ref showParty))
        {
            config.ChatLogShowParty = showParty;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showTell = config.ChatLogShowTell;
        if (ImGui.Checkbox(Loc.T("Channel.Whisper") + "##filterTell", ref showTell))
        {
            config.ChatLogShowTell = showTell;
            plugin.SaveConfiguration();
        }

        var showYell = config.ChatLogShowYell;
        if (ImGui.Checkbox(Loc.T("Channel.Yell") + "##filterYell", ref showYell))
        {
            config.ChatLogShowYell = showYell;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showShout = config.ChatLogShowShout;
        if (ImGui.Checkbox(Loc.T("Channel.Shout") + "##filterShout", ref showShout))
        {
            config.ChatLogShowShout = showShout;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showFc = config.ChatLogShowFreeCompany;
        if (ImGui.Checkbox(Loc.T("Channel.FreeCompany") + "##filterFc", ref showFc))
        {
            config.ChatLogShowFreeCompany = showFc;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showLs = config.ChatLogShowLinkshell;
        if (ImGui.Checkbox(Loc.T("Channel.Linkshell") + "##filterLs", ref showLs))
        {
            config.ChatLogShowLinkshell = showLs;
            plugin.SaveConfiguration();
        }
    }
}
