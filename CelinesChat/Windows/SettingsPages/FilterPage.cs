using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class FilterPage
{
    private readonly Plugin plugin;
    private string newFilterText = string.Empty;

    public FilterPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextUnformatted(Loc.T("Filter.WordsHeader"));
        DrawHint(Loc.T("Filter.WordsHint"));

        var removeIndex = -1;
        var changed = false;
        for (var i = 0; i < config.WordFilters.Count; i++)
        {
            var rule = config.WordFilters[i];
            using var id = ImRaii.PushId(i);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                removeIndex = i;
            }

            ImGui.SameLine();
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                rule.Enabled = enabled;
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Filter.RuleEnabled"));
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(160);
            var text = rule.Text;
            if (ImGui.InputText("##text", ref text, 80))
            {
                rule.Text = text;
                changed = true;
            }

            ImGui.SameLine();
            var channelsLabel = rule.Categories.Count == 0
                ? Loc.T("Filter.AllChannels")
                : Loc.T("Filter.SomeChannels", rule.Categories.Count);
            if (ImGui.Button(channelsLabel + "##channels"))
            {
                ImGui.OpenPopup("##ruleChannels");
            }

            using var popup = ImRaii.Popup("##ruleChannels");
            if (popup.Success)
            {
                DrawHint(Loc.T("Filter.ChannelsHint"));
                foreach (var category in ChannelDisplay.AllCategories)
                {
                    if (ChannelDisplay.GroupOf(category) != ChatCategoryGroup.Standard)
                    {
                        continue;
                    }

                    var included = rule.Categories.Contains(category);
                    if (ImGui.Checkbox(ChannelDisplay.CategoryName(category) + "##cat" + category, ref included))
                    {
                        if (included)
                        {
                            rule.Categories.Add(category);
                        }
                        else
                        {
                            rule.Categories.Remove(category);
                        }

                        changed = true;
                    }
                }
            }
        }

        if (removeIndex >= 0)
        {
            config.WordFilters.RemoveAt(removeIndex);
            changed = true;
        }

        ImGui.SetNextItemWidth(200);
        var submitted = ImGui.InputTextWithHint("##newWordFilter", Loc.T("Filter.NewHint"), ref newFilterText, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var newText = newFilterText.Trim();
        var canAdd = newText.Length > 0 && !config.WordFilters.Any(r => string.Equals(r.Text.Trim(), newText, StringComparison.OrdinalIgnoreCase));
        using (ImRaii.Disabled(!canAdd))
        {
            if ((ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("Mentions.Add")) || submitted) && canAdd)
            {
                config.WordFilters.Add(new WordFilterRule { Text = newText });
                newFilterText = string.Empty;
                changed = true;
            }
        }

        if (changed)
        {
            plugin.OnFiltersChanged();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Filter.SpamHeader"));

        var collapse = config.CollapseRepeatedMessages;
        if (ImGui.Checkbox(Loc.T("Filter.CollapseRepeats"), ref collapse))
        {
            config.CollapseRepeatedMessages = collapse;
            plugin.SaveConfiguration();
        }

        DrawHint(Loc.T("Filter.CollapseRepeatsHint"));
    }

    private static void DrawHint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(text);
        }
    }
}
