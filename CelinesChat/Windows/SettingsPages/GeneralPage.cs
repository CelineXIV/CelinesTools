using System;
using Dalamud.Bindings.ImGui;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class GeneralPage
{
    private readonly Plugin plugin;

    public GeneralPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(120);
        var maxLength = config.MaxMessageLength;
        if (ImGui.InputInt(Loc.T("Settings.MaxLength"), ref maxLength, 10, 50))
        {
            config.MaxMessageLength = Math.Max(20, maxLength);
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(120);
        var delay = config.DelayMs;
        if (ImGui.InputInt(Loc.T("Settings.Delay"), ref delay, 50, 200))
        {
            config.DelayMs = Math.Max(0, delay);
            plugin.SaveConfiguration();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.WhisperDelayHint", Plugin.MinWhisperDelayMs));
        ImGui.PopStyleColor();

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.HotkeysHeader"));

        var sendOnEnter = config.SendOnEnter;
        if (ImGui.Checkbox(Loc.T("Settings.SendOnEnter"), ref sendOnEnter))
        {
            config.SendOnEnter = sendOnEnter;
            plugin.SaveConfiguration();
        }

        if (!sendOnEnter)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(Loc.T("Settings.SendOnEnterOff"));
            ImGui.PopStyleColor();
        }
    }
}
