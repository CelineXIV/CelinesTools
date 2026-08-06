using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class TabsPage
{
    private readonly Plugin plugin;

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

        ImGui.Spacing();
        ImGui.Separator();

        ChatTab? toRemove = null;

        foreach (var tab in config.ChatTabs)
        {
            ImGui.PushID(tab.Id.ToString());
            ImGui.Spacing();

            ImGui.SetNextItemWidth(200);
            var name = tab.Name;
            if (ImGui.InputText(Loc.T("Tabs.NameLabel"), ref name, 60))
            {
                tab.Name = name;
                plugin.SaveConfiguration();
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(!tab.Removable);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
            {
                toRemove = tab;
            }

            ImGui.EndDisabled();

            if (!tab.Removable && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Tabs.CannotRemoveDefault"));
            }

            // Grouped into collapsible sections (Standard/Announcements/Battle) rather than one
            // long flat grid - with ~50 selectable categories now, an unstructured grid would be
            // unreadable. Linkshell/CrossWorldLinkshell get pulled out of the Standard group and
            // handled separately - each has its own set of specific numbers (see
            // DrawLinkshellSection), which needs its own indented block rather than fitting into
            // a 3-per-row grid cell.
            foreach (var group in ChannelDisplay.AllGroups)
            {
                var flags = group == ChatCategoryGroup.Standard ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
                if (!ImGui.CollapsingHeader(ChannelDisplay.GroupName(group) + "##group" + group, flags))
                {
                    continue;
                }

                var gridCategories = new List<ChatCategory>();
                foreach (var category in ChannelDisplay.AllCategories)
                {
                    if (ChannelDisplay.GroupOf(category) == group && category is not (ChatCategory.Linkshell or ChatCategory.CrossWorldLinkshell))
                    {
                        gridCategories.Add(category);
                    }
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

                    if ((i + 1) % 3 != 0 && i < gridCategories.Count - 1)
                    {
                        ImGui.SameLine();
                    }
                }

                if (group == ChatCategoryGroup.Standard)
                {
                    ImGui.Spacing();
                    DrawLinkshellSection(tab, ChatCategory.Linkshell, NativeChannels.AllSlots(NativeChannels.GetExistingLinkshells()), tab.IncludedLinkshellNumbers, "LS");
                    ImGui.Spacing();
                    DrawLinkshellSection(tab, ChatCategory.CrossWorldLinkshell, NativeChannels.AllSlots(NativeChannels.GetExistingCrossWorldLinkshells()), tab.IncludedCrossWorldLinkshellNumbers, "CW");
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PopID();
        }

        if (toRemove != null)
        {
            config.ChatTabs.Remove(toRemove);
            plugin.SaveConfiguration();
        }
    }

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
