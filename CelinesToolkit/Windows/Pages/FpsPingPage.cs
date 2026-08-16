using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class FpsPingPage
{
    private readonly Plugin plugin;

    public FpsPingPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextWrapped(Loc.T("FpsPing.Description"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = plugin.Configuration.FpsPingEnabled;
        if (ImGui.Checkbox(Loc.T("FpsPing.EnableCheckbox"), ref enabled))
        {
            plugin.SetFpsPingEnabled(enabled);
        }

        ImGui.BeginDisabled(!enabled);

        var showFps = plugin.Configuration.FpsPingShowFps;
        if (ImGui.Checkbox(Loc.T("FpsPing.ShowFps"), ref showFps))
        {
            plugin.Configuration.FpsPingShowFps = showFps;
            plugin.SaveConfiguration();
        }

        var showPing = plugin.Configuration.FpsPingShowPing;
        if (ImGui.Checkbox(Loc.T("FpsPing.ShowPing"), ref showPing))
        {
            plugin.Configuration.FpsPingShowPing = showPing;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("FpsPing.PingHint"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var positionLocked = plugin.Configuration.FpsPingPositionLocked;
        if (ImGui.Checkbox(Loc.T("FpsPing.PositionLocked"), ref positionLocked))
        {
            plugin.Configuration.FpsPingPositionLocked = positionLocked;
            plugin.SaveConfiguration();
        }

        var scale = plugin.Configuration.FpsPingScale;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Loc.T("FpsPing.ScaleSlider"), ref scale, 0.5f, 3f, "%.2fx"))
        {
            plugin.Configuration.FpsPingScale = scale;
            plugin.SaveConfiguration();
        }

        var backgroundOpacity = plugin.Configuration.FpsPingBackgroundOpacity;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Loc.T("FpsPing.BackgroundOpacity"), ref backgroundOpacity, 0f, 1f, "%.2f"))
        {
            plugin.Configuration.FpsPingBackgroundOpacity = backgroundOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var colorCode = plugin.Configuration.FpsPingColorCodeFps;
        if (ImGui.Checkbox(Loc.T("FpsPing.ColorCodeFps"), ref colorCode))
        {
            plugin.Configuration.FpsPingColorCodeFps = colorCode;
            plugin.SaveConfiguration();
        }

        ImGui.BeginDisabled(!colorCode);

        var greenThreshold = plugin.Configuration.FpsPingGreenThreshold;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Loc.T("FpsPing.GreenThreshold"), ref greenThreshold, 1f, 240f, "%.0f FPS"))
        {
            plugin.Configuration.FpsPingGreenThreshold = greenThreshold;
            plugin.SaveConfiguration();
        }

        var redThreshold = plugin.Configuration.FpsPingRedThreshold;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Loc.T("FpsPing.RedThreshold"), ref redThreshold, 1f, 240f, "%.0f FPS"))
        {
            plugin.Configuration.FpsPingRedThreshold = redThreshold;
            plugin.SaveConfiguration();
        }

        ImGui.TextDisabled(Loc.T("FpsPing.ColorCodeHint"));

        ImGui.EndDisabled();

        ImGui.EndDisabled();
    }
}
