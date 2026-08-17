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

        ImGui.SetNextItemWidth(150);
        var tabStripBgOpacity = config.TabStripBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.TabStripBackgroundOpacity"), ref tabStripBgOpacity, 0.0f, 1.0f))
        {
            config.TabStripBackgroundOpacity = tabStripBgOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var footerBgOpacity = config.FooterBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.FooterBackgroundOpacity"), ref footerBgOpacity, 0.0f, 1.0f))
        {
            config.FooterBackgroundOpacity = footerBgOpacity;
            plugin.SaveConfiguration();
        }

        var showSenderStatusIcons = config.ShowSenderStatusIcons;
        if (ImGui.Checkbox(Loc.T("Settings.ShowSenderStatusIcons"), ref showSenderStatusIcons))
        {
            config.ShowSenderStatusIcons = showSenderStatusIcons;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(220);
        var previewPositionIndex = (int)config.PreviewPosition;
        if (ImGui.Combo(Loc.T("Settings.PreviewPosition"), ref previewPositionIndex, PreviewPositionLabels, PreviewPositionLabels.Length))
        {
            config.PreviewPosition = (PreviewPosition)previewPositionIndex;
            plugin.SaveConfiguration();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.PreviewPositionHint"));
        ImGui.PopStyleColor();
    }

    private static readonly string[] PreviewPositionLabels =
    {
        Loc.T("Settings.PreviewPosition.Window"),
        Loc.T("Settings.PreviewPosition.Inside"),
    };
}
