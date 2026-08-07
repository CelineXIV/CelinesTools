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

        // Every tab offers the exact same universe of categories (AllCategories), so whether a
        // search term matches anything at all doesn't vary per tab - computed once, not per tab.
        var anyCategoryMatches = searching && HasAnyMatchingCategory(categorySearch);

        // Each tab collapses into its own tree node (mirroring Chat2's own Tabs settings) instead
        // of every tab's full checkbox grid being permanently expanded and stacked one after
        // another - with several tabs and ~50 selectable categories each, that used to mean
        // scrolling past a wall of checkboxes to find anything, which was a big part of this page
        // being hard to get an overview of. While searching, every tab force-opens (so long as the
        // term matches something at all) - otherwise a search would just find something behind a
        // collapsed node the user would then have to go open manually anyway.
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

    /// <summary>
    /// The row of icon buttons to the right of a tab's header: reorder up/down, then delete -
    /// same order and icons as Chat2's own Tabs page. Placed on the same line as the TreeNode via
    /// SameLine, so the header stays clickable to expand/collapse while these stay reachable
    /// without opening the tab first.
    /// </summary>
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

        // Grouped into collapsible sections (Standard/Announcements/Battle) rather than one long
        // flat grid - with ~50 selectable categories now, an unstructured grid would be
        // unreadable. Linkshell/CrossWorldLinkshell get pulled out of the Standard group and
        // handled separately - each has its own set of specific numbers (see
        // DrawLinkshellSection), which needs its own indented block rather than fitting into a
        // 3-per-row grid cell (and aren't searchable the same way, so they're skipped entirely
        // while a search is active).
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

            // Same idea as Chat2's check/cross icon pair next to each group header - selecting or
            // clearing ~15+ Announcements checkboxes one at a time was the single biggest reason
            // this page felt tedious rather than just unclear.
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

    /// <summary>
    /// A short explanation for the handful of category names that aren't self-explanatory from
    /// their label alone (unlike e.g. "Party" or "Yell") - directly targets the "translation is
    /// hard to understand" feedback without having to rename the categories themselves, which
    /// still need to match the vanilla client's own terminology elsewhere (file-log settings,
    /// etc). Null for every category whose name already says what it is.
    /// </summary>
    private static string? CategoryTooltip(ChatCategory category) => category switch
    {
        ChatCategory.Notice => Loc.T("Channel.NoticeTooltip"),
        ChatCategory.Urgent => Loc.T("Channel.UrgentTooltip"),
        ChatCategory.Debug => Loc.T("Channel.DebugTooltip"),
        ChatCategory.SystemError => Loc.T("Channel.SystemErrorTooltip"),
        _ => null,
    };

    /// <summary>
    /// The Linkshell/CrossWorldLinkshell category checkbox, plus - once it's checked - one
    /// indented sub-checkbox per numbered slot (1-8, not just ones currently joined - configuring
    /// a color/visibility in advance for a linkshell you're not in right now is legitimate),
    /// letting a tab show e.g. just CW-Linkshell 1 and 3 instead of all of them. An empty
    /// "included" set is treated as "no restriction yet" (every sub-checkbox shows checked) - see
    /// ChatTab.IncludedLinkshellNumbers - so unchecking one has to materialize the full set
    /// first, or the very next frame's "empty means show all" fallback would make it look like
    /// nothing was ever unchecked.
    /// </summary>
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
