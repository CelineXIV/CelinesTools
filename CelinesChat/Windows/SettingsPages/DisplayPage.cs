using Dalamud.Bindings.ImGui;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class DisplayPage
{
    private readonly Plugin plugin;

    public DisplayPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(150);
        var fontScale = config.FontScale;
        if (ImGui.SliderFloat(Loc.T("Settings.FontScale"), ref fontScale, 0.7f, 2.0f))
        {
            config.FontScale = fontScale;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.WindowOpacity"), ref opacity, 0.2f, 1.0f))
        {
            config.WindowOpacity = opacity;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var unfocusedOpacity = config.UnfocusedWindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.UnfocusedOpacity"), ref unfocusedOpacity, 0.0f, 1.0f))
        {
            config.UnfocusedWindowOpacity = unfocusedOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.UnfocusedOpacityHint"));
        ImGui.PopStyleColor();

        ImGui.SetNextItemWidth(150);
        var logBgOpacity = config.ChatLogBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.LogBackgroundOpacity"), ref logBgOpacity, 0.0f, 1.0f))
        {
            config.ChatLogBackgroundOpacity = logBgOpacity;
            plugin.SaveConfiguration();
        }
    }
}
