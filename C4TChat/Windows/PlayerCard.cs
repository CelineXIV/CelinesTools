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

    private const float CardWidth = 330f;
    private const float AvatarSize = 64f;

    public void Draw()
    {
        fileDialog.Draw();

        if (openRequested)
        {
            ImGui.OpenPopup("##playerCard");
            openRequested = false;
        }

        var scale = ImGuiHelpers.GlobalScale;
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f) * scale)
            .Push(ImGuiStyleVar.PopupRounding, 10f * scale)
            .Push(ImGuiStyleVar.FrameRounding, 6f * scale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f) * scale);
        using var popup = ImRaii.Popup("##playerCard");
        if (!popup.Success || entry == null)
        {
            return;
        }

        var config = plugin.Configuration;
        var e = entry;
        var accent = C4tTheme.ActiveMarkerColor(config);
        var relation = plugin.GetRelation(e);
        var nameColor = ChatWindow.RelationNameColor(relation, e, WhisperTarget(e), config) ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var muted = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        var width = CardWidth * scale;

        DrawAvatar(config, e, accent);
        ImGui.SameLine(0, 12f * scale);
        using (ImRaii.Group())
        {
            var nickname = nicknameText.Trim();
            ImGui.Dummy(new Vector2(1f, 2f * scale));
            ImGui.TextColored(nameColor, nickname.Length > 0 ? nickname : e.Sender);
            ImGui.TextColored(muted, nickname.Length > 0 ? WhisperTarget(e) : e.SenderWorld != null ? "@" + e.SenderWorld : string.Empty);

            if (relation != 0)
            {
                DrawPill(Loc.T(relation switch
                {
                    Plugin.RelationFavorite => "Relation.Favorite",
                    Plugin.RelationFriend => "Relation.Friend",
                    _ => "Relation.FcMember",
                }), nameColor);
            }

            if (plugin.GetPreviousNames(e) is { Count: > 0 } previous)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - ((AvatarSize + 12f) * scale));
                ImGui.TextColored(muted, Loc.T("PlayerCard.PreviousNames", string.Join(", ", previous)));
                ImGui.PopTextWrapPos();
            }
        }

        if (statusMessage != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.45f, 1f), statusMessage);
        }

        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##nickname", Loc.T("PlayerCard.NicknameHint"), ref nicknameText, 40))
        {
            plugin.SetNickname(avatarKey, e.Sender, nicknameText);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("PlayerCard.NicknameTooltip"));
        }

        var note = plugin.GetPlayerNote(e);
        DrawNoteBox(note, width, e);

        DrawStat(FontAwesomeIcon.Comments, messageCount > 0 && firstSeen is { } first
            ? Loc.T("PlayerCard.Messages", messageCount, first.ToString("HH:mm"))
            : Loc.T("PlayerCard.NoMessages"), muted, width);
        if (messageCount > 0 && lastSeen is { } last)
        {
            DrawStat(FontAwesomeIcon.Clock, Loc.T("PlayerCard.LastMessage", AgoText(last), lastChannel.Trim()), muted, width);
        }

        if (hasWhisperLog)
        {
            DrawStat(FontAwesomeIcon.EnvelopeOpen, Loc.T("PlayerCard.HasWhisperLog"), muted, width);
        }

        var lineY = ImGui.GetCursorScreenPos().Y + (2f * scale);
        var lineX = ImGui.GetCursorScreenPos().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, lineY), new Vector2(lineX + width, lineY), ImGui.GetColorU32(ImGuiCol.Separator, 0.6f));
        ImGui.Dummy(new Vector2(width, 5f * scale));

        if (DrawPrimaryButton(FontAwesomeIcon.Comment, Loc.T("ChatLog.WhisperAction"), accent))
        {
            openWhisper(WhisperTarget(e));
            ImGui.CloseCurrentPopup();
        }

        var iconWidth = ImGui.GetFrameHeight() * 1.15f;
        ImGui.SameLine(0, 10f * scale);
        if (IconAction("##invite", FontAwesomeIcon.UserPlus, Loc.T("PlayerCard.Invite"), null, iconWidth))
        {
            plugin.InviteToParty(e.Sender, e.SenderWorld, e.ContentId);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 2f * scale);
        if (IconAction("##plate", FontAwesomeIcon.IdCard, Loc.T("PlayerCard.Plate"), null, iconWidth))
        {
            plugin.OpenAdventurerPlate(e.Sender, e.SenderWorld, e.ContentId);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 2f * scale);
        if (IconAction("##target", FontAwesomeIcon.Crosshairs, Loc.T("ChatLog.TargetAction"), null, iconWidth))
        {
            plugin.TargetPlayer(e.Sender, e.SenderWorld);
            ImGui.CloseCurrentPopup();
        }

        var favorite = plugin.IsFavorite(e);
        ImGui.SameLine(0, 2f * scale);
        if (IconAction("##favorite", FontAwesomeIcon.Heart, Loc.T(favorite ? "ChatLog.RemoveFavorite" : "ChatLog.AddFavorite"), favorite ? config.FavoriteNameColor : null, iconWidth))
        {
            plugin.ToggleFavorite(e);
        }

        ImGui.SameLine(0, 2f * scale);
        if (IconAction("##note", FontAwesomeIcon.StickyNote, Loc.T(note != null ? "ChatLog.EditNoteAction" : "ChatLog.AddNoteAction"), note != null ? accent : null, iconWidth))
        {
            editNote(e);
            ImGui.CloseCurrentPopup();
        }
    }

    private static bool IconAction(string id, FontAwesomeIcon icon, string tooltip, Vector4? color, float width)
    {
        var pressed = ChatWindow.DrawFlatIconButton(id, icon, color, width, 0.9f);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    private static bool DrawPrimaryButton(FontAwesomeIcon icon, string label, Vector4 accent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var glyph = icon.ToIconString();
        Vector2 glyphSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            glyphSize = ImGui.CalcTextSize(glyph) * 0.85f;
        }

        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(glyphSize.X + textSize.X + (30f * scale), ImGui.GetFrameHeight());
        var min = ImGui.GetCursorScreenPos();
        bool pressed;
        using (ImRaii.PushColor(ImGuiCol.Button, accent with { W = 0.75f })
                   .Push(ImGuiCol.ButtonHovered, accent with { W = 0.9f })
                   .Push(ImGuiCol.ButtonActive, accent))
        {
            pressed = ImGui.Button("##primary" + label, size);
        }

        var drawList = ImGui.GetWindowDrawList();
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        var x = min.X + (12f * scale);
        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 0.85f, new Vector2(x, min.Y + ((size.Y - glyphSize.Y) / 2f)), white, glyph);
        drawList.AddText(new Vector2(x + glyphSize.X + (7f * scale), min.Y + ((size.Y - textSize.Y) / 2f)), white, label);
        return pressed;
    }

    private static void DrawPill(string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var textSize = ImGui.CalcTextSize(text);
        var size = new Vector2(textSize.X + (14f * scale), textSize.Y + (4f * scale));
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(color with { W = 0.18f }), size.Y / 2f);
        drawList.AddRect(min, min + size, ImGui.GetColorU32(color with { W = 0.55f }), size.Y / 2f, ImDrawFlags.None, 1f);
        drawList.AddText(min + new Vector2(7f * scale, 2f * scale), ImGui.GetColorU32(color), text);
    }

    private void DrawNoteBox(string? note, float width, ChatLogEntry e)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var pad = new Vector2(10f, 7f) * scale;
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + pad);
        using (ImRaii.Group())
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - (pad.X * 2f));
            if (note != null)
            {
                ImGui.TextUnformatted(note);
            }
            else
            {
                ImGui.TextDisabled(Loc.T("PlayerCard.NoNote"));
            }

            ImGui.PopTextWrapPos();
        }

        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + pad.Y);
        drawList.ChannelsSetCurrent(0);
        var hovered = ImGui.IsMouseHoveringRect(start, end);
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.07f : 0.04f)), 8f * scale);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(start);
        ImGui.InvisibleButton("##noteBox", end - start);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T(note != null ? "ChatLog.EditNoteAction" : "ChatLog.AddNoteAction"));
        }

        if (ImGui.IsItemClicked())
        {
            editNote(e);
            ImGui.CloseCurrentPopup();
        }
    }

    private static void DrawStat(FontAwesomeIcon icon, string text, Vector4 color, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var min = ImGui.GetCursorScreenPos();
        var glyph = icon.ToIconString();
        var iconBox = 16f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 0.75f, new Vector2(min.X + (1f * scale), min.Y + (lineHeight * 0.14f)), ImGui.GetColorU32(color with { W = color.W * 0.8f }), glyph);
        ImGui.SetCursorScreenPos(new Vector2(min.X + iconBox + (4f * scale), min.Y));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - iconBox - (4f * scale));
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    private void DrawAvatar(Configuration config, ChatLogEntry e, Vector4 accent)
    {
        config.CustomAvatars.TryGetValue(avatarKey, out var customFile);
        var texture = plugin.Avatars.Get(avatarKey, customFile, config.LodestoneAvatars, e.Sender, avatarWorld, out var lookupState);
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(AvatarSize) * scale;
        var min = ImGui.GetCursorScreenPos();
        var center = min + (size / 2f);
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton("##avatar", size);
        var hovered = ImGui.IsItemHovered();

        if (texture is { } handle)
        {
            var (uv0, uv1) = plugin.AvatarUv(avatarKey, customFile);
            drawList.AddImageRounded(handle, min, min + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), size.X / 2f);
        }
        else
        {
            drawList.AddCircleFilled(center, size.X / 2f, ImGui.GetColorU32(accent with { W = 0.2f }));
            var label = lookupState == AvatarService.LookupState.Loading ? "..." : e.Sender.Length > 0 ? e.Sender[..1].ToUpperInvariant() : "?";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(ImGuiCol.Text), label);
        }

        drawList.AddCircle(center, (size.X / 2f) + (2f * scale), ImGui.GetColorU32(accent with { W = 0.85f }), 0, 2f * scale);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            drawList.AddCircleFilled(center, size.X / 2f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
            var glyph = FontAwesomeIcon.Camera.ToIconString();
            Vector2 glyphSize;
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                glyphSize = ImGui.CalcTextSize(glyph);
            }

            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), center - (glyphSize / 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), glyph);
            ImGui.SetTooltip(Loc.T(customFile != null ? "PlayerCard.AvatarCustom" : lookupState == AvatarService.LookupState.NotFound ? "PlayerCard.AvatarNotFound" : "PlayerCard.AvatarLodestone"));
        }

        if (ImGui.IsItemClicked())
        {
            ImGui.OpenPopup("##avatarMenu");
        }

        using var menu = ImRaii.Popup("##avatarMenu");
        if (!menu.Success)
        {
            return;
        }

        if (ChatWindow.MenuItem(Loc.T("PlayerCard.AvatarPick"), config))
        {
            PickCustomAvatar();
        }

        if (customFile != null)
        {
            if (ChatWindow.MenuItem(Loc.T("Crop.Adjust"), config))
            {
                plugin.OpenAvatarCrop(customFile, avatarKey);
            }

            if (ChatWindow.MenuItem(Loc.T("PlayerCard.AvatarRemove"), config, new Vector4(1f, 0.55f, 0.5f, 1f)))
            {
                plugin.Avatars.DeleteCustom(customFile);
                config.CustomAvatars.Remove(avatarKey);
                plugin.SaveConfiguration();
            }
        }
        else if (config.LodestoneAvatars && ChatWindow.MenuItem(Loc.T("PlayerCard.AvatarReload"), config))
        {
            plugin.Avatars.ForgetLodestone(avatarKey);
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
