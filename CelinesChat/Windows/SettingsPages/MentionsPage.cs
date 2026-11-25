using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat.Windows.SettingsPages;

/// <summary>Mention keywords, taskbar flashing and the players hidden from the chat.</summary>
internal sealed class MentionsPage
{
    private readonly Plugin plugin;
    private string newKeyword = string.Empty;

    public MentionsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextUnformatted(Loc.T("Mentions.KeywordsHeader"));
        DrawHint(Loc.T("Mentions.KeywordsHint"));

        var removeIndex = -1;
        for (var i = 0; i < config.MentionKeywords.Count; i++)
        {
            using var id = ImRaii.PushId(i);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(config.MentionKeywords[i]);
        }

        if (removeIndex >= 0)
        {
            config.MentionKeywords.RemoveAt(removeIndex);
            plugin.OnMentionKeywordsChanged();
        }

        ImGui.SetNextItemWidth(200);
        var submitted = ImGui.InputTextWithHint("##newMentionKeyword", Loc.T("Mentions.KeywordHint"), ref newKeyword, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var keyword = newKeyword.Trim();
        var canAdd = keyword.Length > 0 && !config.MentionKeywords.Any(k => string.Equals(k, keyword, System.StringComparison.OrdinalIgnoreCase));
        using (ImRaii.Disabled(!canAdd))
        {
            if ((ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("Mentions.Add")) || submitted) && canAdd)
            {
                config.MentionKeywords.Add(keyword);
                newKeyword = string.Empty;
                plugin.OnMentionKeywordsChanged();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Mentions.TaskbarHeader"));

        var flashWhisper = config.FlashTaskbarOnWhisper;
        if (ImGui.Checkbox(Loc.T("Mentions.FlashOnWhisper"), ref flashWhisper))
        {
            config.FlashTaskbarOnWhisper = flashWhisper;
            plugin.SaveConfiguration();
        }

        var flashMention = config.FlashTaskbarOnMention;
        if (ImGui.Checkbox(Loc.T("Mentions.FlashOnMention"), ref flashMention))
        {
            config.FlashTaskbarOnMention = flashMention;
            plugin.SaveConfiguration();
        }

        DrawHint(Loc.T("Mentions.TaskbarHint"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Away.SettingsHeader"));

        var awayEnabled = config.AwaySummaryEnabled;
        if (ImGui.Checkbox(Loc.T("Away.Enable"), ref awayEnabled))
        {
            config.AwaySummaryEnabled = awayEnabled;
            plugin.SaveConfiguration();
        }

        using (ImRaii.Disabled(!config.AwaySummaryEnabled))
        {
            ImGui.SetNextItemWidth(100);
            var minutes = config.AwaySummaryMinutes;
            if (ImGui.InputInt(Loc.T("Away.Minutes"), ref minutes))
            {
                config.AwaySummaryMinutes = System.Math.Clamp(minutes, 0, 120);
                plugin.SaveConfiguration();
            }
        }

        DrawHint(Loc.T("Away.Hint"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Relation.Header"));

        var favorites = config.HighlightFavorites;
        if (ImGui.Checkbox(Loc.T("Relation.HighlightFavorites"), ref favorites))
        {
            config.HighlightFavorites = favorites;
            plugin.OnRelationSettingsChanged();
        }

        ImGui.SameLine();
        var favoriteColor = config.FavoriteNameColor;
        if (ImGui.ColorEdit4("##favoriteColor", ref favoriteColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            config.FavoriteNameColor = favoriteColor;
            plugin.SaveConfiguration();
        }

        var friends = config.HighlightFriends;
        if (ImGui.Checkbox(Loc.T("Relation.HighlightFriends"), ref friends))
        {
            config.HighlightFriends = friends;
            plugin.OnRelationSettingsChanged();
        }

        ImGui.SameLine();
        var friendColor = config.FriendNameColor;
        if (ImGui.ColorEdit4("##friendColor", ref friendColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            config.FriendNameColor = friendColor;
            plugin.SaveConfiguration();
        }

        var fc = config.HighlightFcMembers;
        if (ImGui.Checkbox(Loc.T("Relation.HighlightFc"), ref fc))
        {
            config.HighlightFcMembers = fc;
            plugin.OnRelationSettingsChanged();
        }

        ImGui.SameLine();
        var fcColor = config.FcMemberNameColor;
        if (ImGui.ColorEdit4("##fcColor", ref fcColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            config.FcMemberNameColor = fcColor;
            plugin.SaveConfiguration();
        }

        var icons = config.ShowRelationIcons;
        if (ImGui.Checkbox(Loc.T("Relation.ShowIcons"), ref icons))
        {
            config.ShowRelationIcons = icons;
            plugin.InvalidateChatLayout();
            plugin.SaveConfiguration();
        }

        DrawHint(Loc.T("Relation.Hint"));

        if (config.FavoritePlayers.Count > 0)
        {
            ImGui.TextDisabled(Loc.T("Relation.FavoritesList"));
            string? removeFavorite = null;
            foreach (var key in config.FavoritePlayers.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase))
            {
                using var favId = ImRaii.PushId("fav" + key);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                {
                    removeFavorite = key;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("ChatLog.RemoveFavorite"));
                }

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(config.FavoriteNameColor, key);
            }

            if (removeFavorite != null)
            {
                plugin.RemoveFavorite(removeFavorite);
            }
        }

        var known = plugin.GetCharacterState().KnownFcMembers;
        if (known.Count > 0)
        {
            ImGui.TextDisabled(Loc.T("Relation.KnownFcCount", known.Count));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Relation.ForgetFc")))
            {
                known.Clear();
                plugin.OnRelationSettingsChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Relation.ForgetFcTooltip"));
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Mentions.HiddenHeader"));
        DrawHint(Loc.T("Mentions.HiddenHint"));

        if (config.HiddenPlayers.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Mentions.HiddenEmpty"));
        }

        string? unhide = null;
        foreach (var key in config.HiddenPlayers.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase))
        {
            using var id = ImRaii.PushId(key);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Eye))
            {
                unhide = key;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Mentions.Unhide"));
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(key);
        }

        if (unhide != null)
        {
            plugin.UnhidePlayer(unhide);
        }

        DrawPlayerNotes(config);
    }

    private void DrawPlayerNotes(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Mentions.NotesHeader"));
        DrawHint(Loc.T("Mentions.NotesHint"));

        if (config.PlayerNotes.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Mentions.NotesEmpty"));
            return;
        }

        string? removeKey = null;
        string? changedKey = null;
        var changedText = string.Empty;
        foreach (var (key, note) in config.PlayerNotes.OrderBy(n => n.Key, System.StringComparer.OrdinalIgnoreCase))
        {
            using var id = ImRaii.PushId("note" + key);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                removeKey = key;
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(key);

            var text = note;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##noteText", ref text, 500) && text != note)
            {
                changedKey = key;
                changedText = text;
            }
        }

        if (removeKey != null)
        {
            plugin.SetPlayerNote(removeKey, string.Empty);
        }
        else if (changedKey != null && changedText.Trim().Length > 0)
        {
            plugin.SetPlayerNote(changedKey, changedText);
        }
    }

    private static void DrawHint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(text);
        }
    }
}
