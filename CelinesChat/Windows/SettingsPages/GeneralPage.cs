using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class GeneralPage
{
    private readonly Plugin plugin;

    public GeneralPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(120);
        var maxLength = config.MaxMessageLength;
        if (ImGui.InputInt(Loc.T("Settings.MaxLength"), ref maxLength, 10, 50))
        {
            config.MaxMessageLength = Math.Max(20, maxLength);
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(120);
        var delay = config.DelayMs;
        if (ImGui.InputInt(Loc.T("Settings.Delay"), ref delay, 50, 200))
        {
            config.DelayMs = Math.Max(0, delay);
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.WhisperDelayHint", Plugin.MinWhisperDelayMs));
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.HotkeysHeader"));

        var sendOnEnter = config.SendOnEnter;
        if (ImGui.Checkbox(Loc.T("Settings.SendOnEnter"), ref sendOnEnter))
        {
            config.SendOnEnter = sendOnEnter;
            plugin.SaveConfiguration();
        }

        if (!sendOnEnter)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(Loc.T("Settings.SendOnEnterOff"));
        }

        var keepFocus = config.KeepInputFocusAfterSend;
        if (ImGui.Checkbox(Loc.T("Settings.KeepInputFocusAfterSend"), ref keepFocus))
        {
            config.KeepInputFocusAfterSend = keepFocus;
            plugin.SaveConfiguration();
        }

        var replyHotkey = config.ReplyHotkeyEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.ReplyHotkey"), ref replyHotkey))
        {
            config.ReplyHotkeyEnabled = replyHotkey;
            plugin.SaveConfiguration();
        }

        var historyPerChannel = config.SentHistoryPerChannel;
        if (ImGui.Checkbox(Loc.T("Compose.HistoryPerChannel"), ref historyPerChannel))
        {
            config.SentHistoryPerChannel = historyPerChannel;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.HistoryPerChannelTooltip"));
        }

        var snippetShortcuts = config.SnippetShortcutsEnabled;
        if (ImGui.Checkbox(Loc.T("Compose.SnippetShortcutsEnabled"), ref snippetShortcuts))
        {
            config.SnippetShortcutsEnabled = snippetShortcuts;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.SnippetShortcutTooltip"));
        }

        var playerCard = config.PlayerCardOnClick;
        if (ImGui.Checkbox(Loc.T("Settings.PlayerCardOnClick"), ref playerCard))
        {
            config.PlayerCardOnClick = playerCard;
            plugin.SaveConfiguration();
        }

        var inGameMenus = config.PlayerCardInGameMenus;
        if (ImGui.Checkbox(Loc.T("Settings.PlayerCardInGameMenus"), ref inGameMenus))
        {
            config.PlayerCardInGameMenus = inGameMenus;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.PlayerCardInGameMenusTooltip"));
        }

        var lodestone = config.LodestoneAvatars;
        if (ImGui.Checkbox(Loc.T("Settings.LodestoneAvatars"), ref lodestone))
        {
            config.LodestoneAvatars = lodestone;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.LodestoneAvatarsTooltip"));
        }

        var showNicknames = config.ShowNicknames;
        if (ImGui.Checkbox(Loc.T("Settings.ShowNicknames"), ref showNicknames))
        {
            config.ShowNicknames = showNicknames;
            plugin.InvalidateChatLayout();
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.ShowNicknamesTooltip"));
        }

        var detectNameChanges = config.DetectNameChanges;
        if (ImGui.Checkbox(Loc.T("Settings.DetectNameChanges"), ref detectNameChanges))
        {
            config.DetectNameChanges = detectNameChanges;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.DetectNameChangesTooltip"));
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.VisibilityHeader"));

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.VisibilityHint"));
        }

        var showDuringCutscenes = config.ShowChatDuringCutscenes;
        if (ImGui.Checkbox(Loc.T("Settings.ShowDuringCutscenes"), ref showDuringCutscenes))
        {
            config.ShowChatDuringCutscenes = showDuringCutscenes;
            plugin.SaveConfiguration();
        }

        var showDuringLoadingScreens = config.ShowChatDuringLoadingScreens;
        if (ImGui.Checkbox(Loc.T("Settings.ShowDuringLoadingScreens"), ref showDuringLoadingScreens))
        {
            config.ShowChatDuringLoadingScreens = showDuringLoadingScreens;
            plugin.SaveConfiguration();
        }

        var hideNativeChat = config.HideNativeChat;
        if (ImGui.Checkbox(Loc.T("Settings.HideNativeChat"), ref hideNativeChat))
        {
            config.HideNativeChat = hideNativeChat;
            plugin.SaveConfiguration();
        }

        var preloadHistory = config.PreloadRecentHistoryOnStartup;
        if (ImGui.Checkbox(Loc.T("Settings.PreloadRecentHistoryOnStartup"), ref preloadHistory))
        {
            config.PreloadRecentHistoryOnStartup = preloadHistory;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.PreloadRecentHistoryOnStartupTooltip"));
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.PreloadRecentHistoryOnStartupHint"));
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.MiscHeader"));

        var replaceSocial = config.ReplaceSocialWindow;
        if (ImGui.Checkbox(Loc.T("Settings.ReplaceSocialWindow"), ref replaceSocial))
        {
            config.ReplaceSocialWindow = replaceSocial;
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.ReplaceSocialWindowHint"));
        }

        var estateTeleportEnabled = config.EstateTeleportContextMenuEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.EstateTeleportContextMenu"), ref estateTeleportEnabled))
        {
            config.EstateTeleportContextMenuEnabled = estateTeleportEnabled;
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.EstateTeleportContextMenuHint"));
        }
    }
}
