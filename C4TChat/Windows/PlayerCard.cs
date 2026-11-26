using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using C4TChat.Services;

namespace C4TChat.Windows;

internal sealed class PlayerCard
{
    private readonly Plugin plugin;
    private readonly Action<string> openWhisper;
    private readonly Action<ChatLogEntry> editNote;

    private ChatLogEntry? entry;
    private bool openRequested;
    private int messageCount;
    private DateTime? firstSeen;
    private DateTime? lastSeen;
    private string lastChannel = string.Empty;
    private bool hasWhisperLog;
    private string avatarKey = string.Empty;
    private string? avatarWorld;
    private readonly FileDialogManager fileDialog = new();
    private string? statusMessage;
    private string nicknameText = string.Empty;

    public PlayerCard(Plugin plugin, Action<string> openWhisper, Action<ChatLogEntry> editNote)
    {
        this.plugin = plugin;
        this.openWhisper = openWhisper;
        this.editNote = editNote;
    }

    public void Open(ChatLogEntry source)
    {
        entry = source;
        openRequested = true;
        statusMessage = null;

        avatarWorld = plugin.ResolveWorld(source);
        avatarKey = avatarWorld != null ? source.Sender + "@" + avatarWorld : source.Sender;
        nicknameText = plugin.FindNickname(source.SenderKey, source.Sender, avatarWorld) ?? string.Empty;

        messageCount = 0;
        firstSeen = null;
        lastSeen = null;
        lastChannel = string.Empty;
        var key = source.SenderKey;
        foreach (var other in plugin.ChatLog.Snapshot())
        {
            if (other.ChatType == Dalamud.Game.Text.XivChatType.TellOutgoing
                || !string.Equals(other.SenderKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            messageCount++;
            firstSeen ??= other.Timestamp;
            lastSeen = other.Timestamp;
            lastChannel = ChannelDisplay.DisplayTag(other.ChatType);
        }

        var target = WhisperTarget(source);
        hasWhisperLog = plugin.ChatLog.GetWhisperLogTargets().Exists(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
    }

    private static string WhisperTarget(ChatLogEntry e) => e.SenderWorld != null ? $"{e.Sender}@{e.SenderWorld}" : e.Sender;

    public void Draw()
    {
        fileDialog.Draw();

        if (openRequested)
        {
            ImGui.OpenPopup("##playerCard");
            openRequested = false;
        }

        using var popup = ImRaii.Popup("##playerCard");
        if (!popup.Success || entry == null)
        {
            return;
        }

        var config = plugin.Configuration;
        var e = entry;
        var relation = plugin.GetRelation(e);
        var nameColor = ChatWindow.RelationNameColor(relation, e, WhisperTarget(e), config) ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

        DrawAvatar(config, e);
        ImGui.SameLine();
        ImGui.BeginGroup();

        if (relation != 0)
        {
            ChatWindow.DrawRelationIcon(relation, config);
        }

        var nickname = nicknameText.Trim();
        if (nickname.Length > 0)
        {
            ImGui.TextColored(nameColor, nickname);
            ImGui.SameLine();
            ImGui.TextDisabled(e.SenderWorld != null ? $"({e.Sender}@{e.SenderWorld})" : $"({e.Sender})");
        }
        else
        {
            ImGui.TextColored(nameColor, e.Sender);
            if (e.SenderWorld != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("@" + e.SenderWorld);
            }
        }

        if (relation != 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· " + Loc.T(relation switch
            {
                Plugin.RelationFavorite => "Relation.Favorite",
                Plugin.RelationFriend => "Relation.Friend",
                _ => "Relation.FcMember",
            }));
        }

        if (plugin.GetPreviousNames(e) is { Count: > 0 } previous)
        {
            ImGui.TextDisabled(Loc.T("PlayerCard.PreviousNames", string.Join(", ", previous)));
        }

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##nickname", Loc.T("PlayerCard.NicknameHint"), ref nicknameText, 40))
        {
            plugin.SetNickname(avatarKey, e.Sender, nicknameText);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("PlayerCard.NicknameTooltip"));
        }

        ImGui.Separator();

        var note = plugin.GetPlayerNote(e);
        using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + 300f))
        {
            if (note != null)
            {
                ImGui.TextUnformatted(note);
            }
            else
            {
                ImGui.TextDisabled(Loc.T("PlayerCard.NoNote"));
            }
        }

        ImGui.Spacing();
        if (messageCount > 0 && lastSeen is { } last && firstSeen is { } first)
        {
            ImGui.TextDisabled(Loc.T("PlayerCard.Messages", messageCount, first.ToString("HH:mm")));
            ImGui.TextDisabled(Loc.T("PlayerCard.LastMessage", AgoText(last), lastChannel.Trim()));
        }
        else
        {
            ImGui.TextDisabled(Loc.T("PlayerCard.NoMessages"));
        }

        if (hasWhisperLog)
        {
            ImGui.TextDisabled(Loc.T("PlayerCard.HasWhisperLog"));
        }

        ImGui.EndGroup();
        ImGui.Separator();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Comment, Loc.T("ChatLog.WhisperAction")))
        {
            openWhisper(WhisperTarget(e));
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserPlus, Loc.T("PlayerCard.Invite")))
        {
            plugin.InviteToParty(e.Sender, e.SenderWorld, e.ContentId);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.IdCard, Loc.T("PlayerCard.Plate")))
        {
            plugin.OpenAdventurerPlate(e.Sender, e.SenderWorld, e.ContentId);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs))
        {
            plugin.TargetPlayer(e.Sender, e.SenderWorld);
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.TargetAction"));
        }

        var favorite = plugin.IsFavorite(e);
        using (ImRaii.PushColor(ImGuiCol.Text, config.FavoriteNameColor, favorite))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Heart, Loc.T(favorite ? "ChatLog.RemoveFavorite" : "ChatLog.AddFavorite")))
            {
                plugin.ToggleFavorite(e);
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.StickyNote, Loc.T(note != null ? "ChatLog.EditNoteAction" : "ChatLog.AddNoteAction")))
        {
            editNote(e);
            ImGui.CloseCurrentPopup();
        }
    }

    private const float AvatarSize = 72f;

    private void DrawAvatar(Configuration config, ChatLogEntry e)
    {
        config.CustomAvatars.TryGetValue(avatarKey, out var customFile);
        var texture = plugin.Avatars.Get(avatarKey, customFile, config.LodestoneAvatars, e.Sender, avatarWorld, out var lookupState);
        var size = new Vector2(AvatarSize, AvatarSize) * ImGuiHelpers.GlobalScale;

        using (ImRaii.Group())
        {
            var min = ImGui.GetCursorScreenPos();
            if (texture is { } handle)
            {
                var (uv0, uv1) = plugin.AvatarUv(avatarKey, customFile);
                ImGui.Image(handle, size, uv0, uv1);
            }
            else
            {
                ImGui.Dummy(size);
                var drawList = ImGui.GetWindowDrawList();
                var center = min + (size / 2f);
                drawList.AddCircleFilled(center, size.X / 2f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)));
                var label = lookupState == AvatarService.LookupState.Loading ? "..." : e.Sender.Length > 0 ? e.Sender[..1].ToUpperInvariant() : "?";
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T(customFile != null ? "PlayerCard.AvatarCustom" : lookupState == AvatarService.LookupState.NotFound ? "PlayerCard.AvatarNotFound" : "PlayerCard.AvatarLodestone"));
            }

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Image))
            {
                PickCustomAvatar();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("PlayerCard.AvatarPick"));
            }

            ImGui.SameLine();
            if (customFile != null)
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Crop))
                {
                    plugin.OpenAvatarCrop(customFile, avatarKey);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("Crop.Adjust"));
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                {
                    plugin.Avatars.DeleteCustom(customFile);
                    config.CustomAvatars.Remove(avatarKey);
                    plugin.SaveConfiguration();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("PlayerCard.AvatarRemove"));
                }
            }
            else if (config.LodestoneAvatars)
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt))
                {
                    plugin.Avatars.ForgetLodestone(avatarKey);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("PlayerCard.AvatarReload"));
                }
            }

            if (statusMessage != null)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.45f, 1f), statusMessage);
            }
        }
    }

    private void PickCustomAvatar()
    {
        var key = avatarKey;
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
                    var info = new System.IO.FileInfo(paths[0]);
                    if (info.Length > 8 * 1024 * 1024)
                    {
                        statusMessage = Loc.T("PlayerCard.AvatarTooLarge");
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
                    statusMessage = null;
                }
                catch (Exception ex)
                {
                    plugin.Log.Warning(ex, "[C4TChat] Eigenes Bild konnte nicht übernommen werden.");
                    statusMessage = Loc.T("PlayerCard.AvatarFailed");
                }
            },
            1,
            null,
            false);
    }

    private static string AgoText(DateTime time)
    {
        var ago = DateTime.Now - time;
        if (ago.TotalMinutes < 1)
        {
            return Loc.T("PlayerCard.JustNow");
        }

        if (ago.TotalHours < 1)
        {
            return Loc.T("PlayerCard.MinutesAgo", (int)ago.TotalMinutes);
        }

        return ago.TotalDays < 1
            ? Loc.T("PlayerCard.HoursAgo", (int)ago.TotalHours)
            : time.ToString("dd.MM. HH:mm");
    }
}
