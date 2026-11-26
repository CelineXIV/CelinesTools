using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class GeneralPage
{
    private readonly Plugin plugin;
    private readonly FileDialogManager fileDialog = new();
    private string? avatarStatus;

    public GeneralPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;
        fileDialog.Draw();

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

        DrawOwnAvatar(config);

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

    private string? OwnAvatarKey()
    {
        var name = plugin.OwnCharacterName;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var world = plugin.OwnHomeWorld;
        return world != null ? name + "@" + world : name;
    }

    private void DrawOwnAvatar(Configuration config)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.T("Settings.OwnAvatar"));

        var key = OwnAvatarKey();
        if (key == null)
        {
            ImGui.TextDisabled(Loc.T("Settings.OwnAvatarNotLoggedIn"));
            return;
        }

        config.CustomAvatars.TryGetValue(key, out var customFile);
        var texture = plugin.Avatars.Get(key, customFile, config.LodestoneAvatars, plugin.OwnCharacterName, plugin.OwnHomeWorld, out var lookup);
        var size = new Vector2(56f, 56f) * ImGuiHelpers.GlobalScale;
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var drawList = ImGui.GetWindowDrawList();
        var center = min + (size / 2f);
        if (texture is { } handle)
        {
            var (uv0, uv1) = plugin.AvatarUv(key, customFile);
            drawList.AddImageRounded(handle, min, min + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), size.X / 2f);
        }
        else
        {
            drawList.AddCircleFilled(center, size.X / 2f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)));
            var label = lookup == Services.AvatarService.LookupState.Loading ? "..." : key[..1].ToUpperInvariant();
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
        }

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.TextDisabled(customFile != null
                ? Loc.T("Settings.OwnAvatarCustom")
                : lookup == Services.AvatarService.LookupState.NotFound || !config.LodestoneAvatars
                    ? Loc.T("Settings.OwnAvatarNone")
                    : Loc.T("Settings.OwnAvatarLodestone"));

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Image, Loc.T("PlayerCard.AvatarPick")))
            {
                PickOwnAvatar(key);
            }

            if (customFile != null)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crop, Loc.T("Crop.Adjust")))
                {
                    plugin.OpenAvatarCrop(customFile, key);
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, Loc.T("Settings.OwnAvatarRemove")))
                {
                    plugin.Avatars.DeleteCustom(customFile);
                    config.CustomAvatars.Remove(key);
                    plugin.SaveConfiguration();
                }
            }
            else if (config.LodestoneAvatars)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt))
                {
                    plugin.Avatars.ForgetLodestone(key);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("PlayerCard.AvatarReload"));
                }
            }

            if (avatarStatus != null)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.45f, 1f), avatarStatus);
            }
        }
    }

    private void PickOwnAvatar(string key)
    {
        fileDialog.OpenFileDialog(
            Loc.T("PlayerCard.AvatarPick"),
            "Bilder{.png,.jpg,.jpeg,.bmp,.gif}",
            (ok, paths) =>
            {
                if (!ok || paths.Count == 0)
                {
                    return;
                }

                try
                {
                    if (new System.IO.FileInfo(paths[0]).Length > 8 * 1024 * 1024)
                    {
                        avatarStatus = Loc.T("PlayerCard.AvatarTooLarge");
                        return;
                    }

                    var config = plugin.Configuration;
                    if (config.CustomAvatars.TryGetValue(key, out var old))
                    {
                        plugin.Avatars.DeleteCustom(old);
                    }

                    var stored = plugin.Avatars.StoreCustom(key, paths[0]);
                    config.CustomAvatars[key] = stored;
                    plugin.OpenAvatarCrop(stored, key);
                    plugin.SaveConfiguration();
                    avatarStatus = null;
                }
                catch (Exception ex)
                {
                    plugin.Log.Warning(ex, "[CelinesChat] Eigenes Profilbild konnte nicht übernommen werden.");
                    avatarStatus = Loc.T("PlayerCard.AvatarFailed");
                }
            },
            1,
            null,
            false);
    }
}
