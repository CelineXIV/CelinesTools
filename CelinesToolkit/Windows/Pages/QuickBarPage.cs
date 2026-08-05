using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class QuickBarPage
{
    private readonly Plugin plugin;

    public QuickBarPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextWrapped(Loc.T("QuickBar.Description"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = plugin.Configuration.QuickBarEnabled;
        if (ImGui.Checkbox(Loc.T("QuickBar.EnableCheckbox"), ref enabled))
        {
            plugin.SetQuickBarEnabled(enabled);
        }
    }
}
