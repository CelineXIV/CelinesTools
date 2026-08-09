using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace CelinesToolkit.Windows.Pages;

internal sealed class GroupPoseCountdownPage
{
    private readonly Plugin plugin;
    private string newCodeword = string.Empty;
    private string newCommand = string.Empty;

    public GroupPoseCountdownPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped(Loc.T("GroupPoseCountdown.Description"));
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("GroupPoseCountdown.SafetyNote"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = config.GroupPoseCountdownEnabled;
        if (ImGui.Checkbox(Loc.T("GroupPoseCountdown.EnableCheckbox"), ref enabled))
        {
            plugin.SetGroupPoseCountdownEnabled(enabled);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(Loc.T("GroupPoseCountdown.TriggersHeader"));
        ImGui.Spacing();

        var removeIndex = -1;
        if (ImGui.BeginTable("##groupPoseCountdownTriggers", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T("GroupPoseCountdown.ColumnCodeword"), ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn(Loc.T("GroupPoseCountdown.ColumnCommand"), ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 32f);
            ImGui.TableHeadersRow();

            var triggers = config.GroupPoseCountdownTriggers;
            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var codeword = trigger.Codeword;
                if (ImGui.InputText("##codeword", ref codeword, 100))
                {
                    trigger.Codeword = codeword;
                    plugin.SaveConfiguration();
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var command = trigger.Command;
                if (ImGui.InputText("##command", ref command, 200))
                {
                    trigger.Command = command;
                    plugin.SaveConfiguration();
                }

                ImGui.TableNextColumn();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                {
                    removeIndex = i;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (removeIndex >= 0)
        {
            config.GroupPoseCountdownTriggers.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##groupPoseCountdownNewCodeword", Loc.T("GroupPoseCountdown.ColumnCodeword"), ref newCodeword, 100);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##groupPoseCountdownNewCommand", Loc.T("GroupPoseCountdown.CommandInputHint"), ref newCommand, 200);
        ImGui.SameLine();
        var canAdd = !string.IsNullOrWhiteSpace(newCodeword) && !string.IsNullOrWhiteSpace(newCommand);
        ImGui.BeginDisabled(!canAdd);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("GroupPoseCountdown.TriggerAdd")))
        {
            config.GroupPoseCountdownTriggers.Add(new GroupPoseCountdownTrigger { Codeword = newCodeword.Trim(), Command = newCommand.Trim() });
            newCodeword = string.Empty;
            newCommand = string.Empty;
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();
    }
}
