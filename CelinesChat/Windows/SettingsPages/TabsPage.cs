using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class TabsPage
{
    private readonly Plugin plugin;
    private string categorySearch = string.Empty;

    public TabsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped(Loc.T("Tabs.Description"));
        ImGui.Spacing();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("Tabs.AddButton")))
        {
            config.ChatTabs.Add(new ChatTab { Name = Loc.T("Tabs.NewTabDefaultName") });
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##categorySearch", Loc.T("Tabs.SearchHint"), ref categorySearch, 60);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.SearchTooltip"));
        }

        ImGui.Spacing();
        ImGui.Separator();

        ChatTab? toRemove = null;
        var searching = !string.IsNullOrWhiteSpace(categorySearch);

        var anyCategoryMatches = searching && HasAnyMatchingCategory(categorySearch);

        for (var i = 0; i < config.ChatTabs.Count; i++)
        {
            var tab = config.ChatTabs[i];
            ImGui.PushID(tab.Id.ToString());

            if (anyCategoryMatches)
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }

            var open = ImGui.TreeNodeEx(
                "##tabNode",
                ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding,
                TabHeaderLabel(tab));

            ImGui.SetItemAllowOverlap();

            DrawTabToolbar(config, tab, i, out var removed);
            if (removed)
            {
                toRemove = tab;
            }

            if (open)
            {
                ImGui.Indent();
                DrawTabDetails(tab, searching ? categorySearch : string.Empty);
                ImGui.Unindent();
                ImGui.TreePop();
            }

            ImGui.Spacing();
            ImGui.PopID();
        }

        if (toRemove != null)
        {
            config.ChatTabs.Remove(toRemove);
            plugin.SaveConfiguration();
        }
    }

    private static bool HasAnyMatchingCategory(string search)
    {
        foreach (var category in ChannelDisplay.AllCategories)
        {
            if (ChannelDisplay.CategoryMatchesSearch(category, search))
            {
                return true;
            }
        }

        return false;
    }

    private static string TabHeaderLabel(ChatTab tab)
    {
        var count = tab.IncludedChannels.Count;
        return $"{tab.Name} ({count} {Loc.T("Tabs.MessageCountSuffix")})";
    }

    private void DrawTabToolbar(Configuration config, ChatTab tab, int index, out bool removed)
    {
        removed = false;

        ImGui.SameLine();
        ImGui.BeginDisabled(index == 0);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
        {
            (config.ChatTabs[index - 1], config.ChatTabs[index]) = (config.ChatTabs[index], config.ChatTabs[index - 1]);
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.MoveUp"));
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(index == config.ChatTabs.Count - 1);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
        {
            (config.ChatTabs[index + 1], config.ChatTabs[index]) = (config.ChatTabs[index], config.ChatTabs[index + 1]);
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.MoveDown"));
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!tab.Removable);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
        {
            removed = true;
        }

        ImGui.EndDisabled();
        if (!tab.Removable && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Tabs.CannotRemoveDefault"));
        }
    }

    private void DrawTabDetails(ChatTab tab, string search)
    {
        ImGui.SetNextItemWidth(250);
        var name = tab.Name;
        if (ImGui.InputText(Loc.T("Tabs.NameLabel"), ref name, 60))
        {
            tab.Name = name;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();

        var searching = !string.IsNullOrWhiteSpace(search);

        foreach (var group in ChannelDisplay.AllGroups)
        {
            var gridCategories = new List<ChatCategory>();
            foreach (var category in ChannelDisplay.AllCategories)
            {
                if (ChannelDisplay.GroupOf(category) == group
                    && category is not (ChatCategory.Linkshell or ChatCategory.CrossWorldLinkshell)
                    && ChannelDisplay.CategoryMatchesSearch(category, search))
                {
                    gridCategories.Add(category);
                }
            }

            if (searching && gridCategories.Count == 0)
            {
                continue;
            }

            ImGui.PushID("group" + group);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Check))
            {
                foreach (var category in gridCategories)
                {
                    tab.IncludedChannels.Add(category);
                }

                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Tabs.SelectAllInGroup"));
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                foreach (var category in gridCategories)
                {
                    tab.IncludedChannels.Remove(category);
                }

                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Tabs.DeselectAllInGroup"));
            }

            ImGui.SameLine();
            if (searching)
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }

            var flags = group == ChatCategoryGroup.Standard ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            var groupOpen = ImGui.CollapsingHeader(ChannelDisplay.GroupName(group), flags);
            ImGui.PopID();

            if (!groupOpen)
            {
                continue;
            }

            for (var i = 0; i < gridCategories.Count; i++)
            {
                var category = gridCategories[i];
                var included = tab.IncludedChannels.Contains(category);
                if (ImGui.Checkbox(ChannelDisplay.CategoryName(category) + "##cat" + category, ref included))
                {
                    if (included)
                    {
                        tab.IncludedChannels.Add(category);
                    }
                    else
                    {
                        tab.IncludedChannels.Remove(category);
                    }

                    plugin.SaveConfiguration();
                }

                if (CategoryTooltip(category) is { } tooltip && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(tooltip);
                }

                if ((i + 1) % 3 != 0 && i < gridCategories.Count - 1)
                {
                    ImGui.SameLine();
                }
            }

            if (group == ChatCategoryGroup.Standard && !searching)
            {
                ImGui.Spacing();
                DrawLinkshellSection(tab, ChatCategory.Linkshell, NativeChannels.AllSlots(NativeChannels.GetExistingLinkshells()), tab.IncludedLinkshellNumbers, "LS");
                ImGui.Spacing();
                DrawLinkshellSection(tab, ChatCategory.CrossWorldLinkshell, NativeChannels.AllSlots(NativeChannels.GetExistingCrossWorldLinkshells()), tab.IncludedCrossWorldLinkshellNumbers, "CW");
            }
        }
    }

    private static string? CategoryTooltip(ChatCategory category) => category switch
    {
        ChatCategory.Notice => Loc.T("Channel.NoticeTooltip"),
        ChatCategory.Urgent => Loc.T("Channel.UrgentTooltip"),
        ChatCategory.Debug => Loc.T("Channel.DebugTooltip"),
        ChatCategory.SystemError => Loc.T("Channel.SystemErrorTooltip"),
        ChatCategory.AmbientBattle => Loc.T("Channel.AmbientBattleTooltip"),
        _ => null,
    };

    private void DrawLinkshellSection(ChatTab tab, ChatCategory category, List<(int Number, string Name)> slots, HashSet<int> included, string prefix)
    {
        var categoryIncluded = tab.IncludedChannels.Contains(category);
        if (ImGui.Checkbox(ChannelDisplay.CategoryName(category) + "##cat" + category, ref categoryIncluded))
        {
            if (categoryIncluded)
            {
                tab.IncludedChannels.Add(category);
            }
            else
            {
                tab.IncludedChannels.Remove(category);
            }

            plugin.SaveConfiguration();
        }

        if (!categoryIncluded)
        {
            return;
        }

        ImGui.Indent();
        foreach (var (number, name) in slots)
        {
            var label = name.Length > 0 ? $"{prefix}{number}: {name}" : $"{prefix}{number}";
            var isChecked = included.Count == 0 || included.Contains(number);
            if (ImGui.Checkbox(label + "##" + prefix + number, ref isChecked))
            {
                if (included.Count == 0)
                {
                    foreach (var (existingNumber, _) in slots)
                    {
                        included.Add(existingNumber);
                    }
                }

                if (isChecked)
                {
                    included.Add(number);
                }
                else
                {
                    included.Remove(number);
                }

                plugin.SaveConfiguration();
            }
        }

        ImGui.Unindent();
    }
}
