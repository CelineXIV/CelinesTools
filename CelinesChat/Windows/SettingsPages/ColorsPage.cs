using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class ColorsPage
{
    private readonly Plugin plugin;

    public ColorsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        var defaultColor = config.DefaultTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.DefaultColor"), ref defaultColor))
        {
            config.DefaultTextColor = defaultColor;
            plugin.SaveConfiguration();
        }

        var emoteColor = config.EmoteTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.EmoteColor"), ref emoteColor))
        {
            config.EmoteTextColor = emoteColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(emoteColor, Loc.T("Settings.EmotePreview"));

        var oocColor = config.OocTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.OocColor"), ref oocColor))
        {
            config.OocTextColor = oocColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(oocColor, Loc.T("Settings.OocPreview"));

        var timestampColor = config.TimestampColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.TimestampColor"), ref timestampColor))
        {
            config.TimestampColor = timestampColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(timestampColor, "12:34");

        var mentionColor = config.MentionColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.MentionColor"), ref mentionColor))
        {
            config.MentionColor = mentionColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(mentionColor, Loc.T("Settings.MentionPreview"));

        var sendColor = config.SendAccentColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.SendAccentColor"), ref sendColor))
        {
            config.SendAccentColor = sendColor;
            plugin.SaveConfiguration();
        }

        var validCommandColor = config.ValidCommandColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.ValidCommandColor"), ref validCommandColor))
        {
            config.ValidCommandColor = validCommandColor;
            plugin.SaveConfiguration();
        }

        var invalidCommandColor = config.InvalidCommandColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.InvalidCommandColor"), ref invalidCommandColor))
        {
            config.InvalidCommandColor = invalidCommandColor;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextWrapped(Loc.T("Settings.ColorsDescription"));
        ImGui.Spacing();

        foreach (var group in ChannelDisplay.AllGroups)
        {
            var flags = group == ChatCategoryGroup.Standard ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(ChannelDisplay.GroupName(group) + "##colorgroup" + group, flags))
            {
                continue;
            }

            foreach (var category in ChannelDisplay.AllCategories)
            {
                if (ChannelDisplay.GroupOf(category) != group)
                {
                    continue;
                }

                // Linkshell/CrossWorldLinkshell get expanded into one row per actually-existing
                // number below instead of a single shared row here - a character can be in
                // several at once, and one shared color makes them indistinguishable from each
                // other.
                if (category is ChatCategory.Linkshell or ChatCategory.CrossWorldLinkshell)
                {
                    continue;
                }

                var color = config.ChatColours.TryGetValue(category, out var stored) ? stored : ChannelDisplay.DefaultColor(category);

                ImGui.PushID((int)category);

                if (ImGui.ColorEdit4(ChannelDisplay.CategoryName(category), ref color))
                {
                    config.ChatColours[category] = color;
                    plugin.SaveConfiguration();
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.UndoAlt))
                {
                    config.ChatColours.Remove(category);
                    plugin.SaveConfiguration();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
                }

                ImGui.PopID();
            }

            if (group == ChatCategoryGroup.Standard)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(Loc.T("Settings.LinkshellColorsHeader"));
                DrawNumberedColorRows(config, NativeChannels.AllSlots(NativeChannels.GetExistingLinkshells()), config.LinkshellColours, ChatCategory.Linkshell, "LS");

                ImGui.Spacing();
                ImGui.TextDisabled(Loc.T("Settings.CrossWorldLinkshellColorsHeader"));
                DrawNumberedColorRows(config, NativeChannels.AllSlots(NativeChannels.GetExistingCrossWorldLinkshells()), config.CrossWorldLinkshellColours, ChatCategory.CrossWorldLinkshell, "CW");
            }
        }
    }

    private void DrawNumberedColorRows(Configuration config, List<(int Number, string Name)> slots, Dictionary<int, Vector4> colours, ChatCategory fallbackCategory, string prefix)
    {
        foreach (var (number, name) in slots)
        {
            ImGui.PushID(prefix + number);

            var label = name.Length > 0 ? $"{prefix}{number}: {name}" : $"{prefix}{number}";
            var color = colours.TryGetValue(number, out var stored) ? stored : ChannelDisplay.Color(fallbackCategory, config);
            if (ImGui.ColorEdit4(label, ref color))
            {
                colours[number] = color;
                plugin.SaveConfiguration();
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.UndoAlt))
            {
                colours.Remove(number);
                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Settings.ColorsResetRow"));
            }

            ImGui.PopID();
        }
    }

    public static void ResetToDefaults(Configuration config)
    {
        config.DefaultTextColor = new Vector4(1f, 1f, 1f, 1f);
        config.EmoteTextColor = new Vector4(0.68f, 0.42f, 0.87f, 1f);
        config.OocTextColor = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        config.TimestampColor = new Vector4(1f, 1f, 1f, 1f);
        config.MentionColor = new Vector4(1f, 0.85f, 0.2f, 1f);
        config.SendAccentColor = new Vector4(0.16f, 0.45f, 0.24f, 1f);
        config.ValidCommandColor = new Vector4(0.8830769f, 0.44017988f, 0f, 1f);
        config.InvalidCommandColor = new Vector4(1f, 0.4f, 0.4f, 1f);
        config.ChatColours.Clear();
        config.LinkshellColours.Clear();

        config.CrossWorldLinkshellColours.Clear();
        config.CrossWorldLinkshellColours[1] = new Vector4(0.5261538f, 0f, 0f, 1f);
        config.CrossWorldLinkshellColours[2] = new Vector4(0.18153846f, 0.448483f, 1f, 1f);
        config.CrossWorldLinkshellColours[3] = new Vector4(0.41444552f, 0.32871005f, 0.47692305f, 1f);
        config.CrossWorldLinkshellColours[4] = new Vector4(0.3669964f, 1f, 0.36307693f, 1f);
        config.CrossWorldLinkshellColours[5] = new Vector4(0.82941526f, 0.27999997f, 1f, 1f);

        config.WhisperColours.Clear();
        config.WhisperColours["Sayuri Kwiat@Shiva"] = new Vector4(1f, 0.55f, 0.84907705f, 1f);
    }
}
