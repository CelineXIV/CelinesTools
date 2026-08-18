using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class RpPage
{
    private readonly Plugin plugin;

    public RpPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // Only the channels people actually type free-form roleplay text into - excludes system/notice/
    // combat-log categories, where a coincidental "*" or "((" in unrelated text was never RP
    // formatting to begin with (see Configuration.RpFormattingCategories' own remarks). Linkshell and
    // CrossWorldLinkshell are handled separately below, per numbered slot.
    private static readonly ChatCategory[] Categories =
    {
        ChatCategory.Say,
        ChatCategory.Party,
        ChatCategory.Whisper,
        ChatCategory.Yell,
        ChatCategory.Shout,
        ChatCategory.FreeCompany,
        ChatCategory.Alliance,
        ChatCategory.PvpTeam,
        ChatCategory.NoviceNetwork,
    };

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped(Loc.T("Rp.Description"));
        ImGui.Spacing();
        ImGui.Separator();

        foreach (var category in Categories)
        {
            DrawCategoryCheckbox(config, category);
        }

        ImGui.Spacing();
        DrawLinkshellSection(config, ChatCategory.Linkshell, NativeChannels.AllSlots(NativeChannels.GetExistingLinkshells()), config.RpFormattingLinkshellNumbers, config.LinkshellColours, "LS");
        ImGui.Spacing();
        DrawLinkshellSection(config, ChatCategory.CrossWorldLinkshell, NativeChannels.AllSlots(NativeChannels.GetExistingCrossWorldLinkshells()), config.RpFormattingCrossWorldLinkshellNumbers, config.CrossWorldLinkshellColours, "CW");
    }

    private void DrawCategoryCheckbox(Configuration config, ChatCategory category)
    {
        var enabled = config.RpFormattingCategories.Contains(category);

        using (ImRaii.PushColor(ImGuiCol.Text, ChannelDisplay.Color(category, config)))
        {
            if (ImGui.Checkbox($"{Loc.T($"Channel.{category}")}##rp{category}", ref enabled))
            {
                if (enabled)
                {
                    config.RpFormattingCategories.Add(category);
                }
                else
                {
                    config.RpFormattingCategories.Remove(category);
                }

                plugin.SaveConfiguration();
            }
        }
    }

    private void DrawLinkshellSection(Configuration config, ChatCategory category, List<(int Number, string Name)> slots, HashSet<int> included, Dictionary<int, Vector4> colours, string prefix)
    {
        var categoryEnabled = config.RpFormattingCategories.Contains(category);
        using (ImRaii.PushColor(ImGuiCol.Text, ChannelDisplay.Color(category, config)))
        {
            if (ImGui.Checkbox($"{ChannelDisplay.CategoryName(category)}##rp{category}", ref categoryEnabled))
            {
                if (categoryEnabled)
                {
                    config.RpFormattingCategories.Add(category);
                }
                else
                {
                    config.RpFormattingCategories.Remove(category);
                }

                plugin.SaveConfiguration();
            }
        }

        if (!categoryEnabled)
        {
            return;
        }

        using var indent = ImRaii.PushIndent();
        if (!ImGui.TreeNodeEx($"{Loc.T("Rp.LinkshellDetails")}##rpDetails{category}", ImGuiTreeNodeFlags.None))
        {
            return;
        }

        foreach (var (number, name) in slots)
        {
            var label = name.Length > 0 ? $"{prefix}{number}: {name}" : $"{prefix}{number}";
            var isChecked = included.Count == 0 || included.Contains(number);
            var color = colours.TryGetValue(number, out var stored) ? stored : ChannelDisplay.Color(category, config);

            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                if (ImGui.Checkbox(label + "##rp" + prefix + number, ref isChecked))
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
        }

        ImGui.TreePop();
    }
}
