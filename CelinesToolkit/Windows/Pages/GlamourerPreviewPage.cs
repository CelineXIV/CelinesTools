using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class GlamourerPreviewPage
{
    private readonly Plugin plugin;

    public GlamourerPreviewPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextWrapped(Loc.T("GlamourerPreview.Description"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = plugin.Configuration.GlamourerPreviewEnabled;
        if (ImGui.Checkbox(Loc.T("GlamourerPreview.EnableCheckbox"), ref enabled))
        {
            plugin.SetGlamourerPreviewEnabled(enabled);
        }
    }
}
