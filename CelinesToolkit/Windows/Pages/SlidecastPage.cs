using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class SlidecastPage
{
    private readonly Plugin plugin;

    public SlidecastPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextWrapped(Loc.T("Slidecast.Description"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = plugin.Configuration.SlidecastEnabled;
        if (ImGui.Checkbox(Loc.T("Slidecast.EnableCheckbox"), ref enabled))
        {
            plugin.Configuration.SlidecastEnabled = enabled;
            plugin.SaveConfiguration();
        }

        ImGui.BeginDisabled(!enabled);

        var showCastBar = plugin.Configuration.SlidecastShowCastBar;
        if (ImGui.Checkbox(Loc.T("Slidecast.ShowCastBar"), ref showCastBar))
        {
            plugin.Configuration.SlidecastShowCastBar = showCastBar;
            plugin.SaveConfiguration();
        }

        var showCursorCircle = plugin.Configuration.SlidecastShowCursorCircle;
        if (ImGui.Checkbox(Loc.T("Slidecast.ShowCursorCircle"), ref showCursorCircle))
        {
            plugin.Configuration.SlidecastShowCursorCircle = showCursorCircle;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();

        var thresholdMs = plugin.Configuration.SlidecastThresholdMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Loc.T("Slidecast.ThresholdSlider"), ref thresholdMs, 100f, 800f, "%.0f ms"))
        {
            plugin.Configuration.SlidecastThresholdMs = thresholdMs;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var preview = plugin.SlidecastPreviewMode;
        if (ImGui.Checkbox(Loc.T("Slidecast.PreviewCheckbox"), ref preview))
        {
            plugin.SlidecastPreviewMode = preview;
        }

        ImGui.TextDisabled(Loc.T("Slidecast.PreviewHint"));

        ImGui.EndDisabled();
    }
}
