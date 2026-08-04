using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class OrchestrionPage
{
    private readonly Plugin plugin;

    public OrchestrionPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), Loc.T("Orchestrion.RequiresPorch"));
        ImGui.Separator();
        ImGui.Spacing();

        var config = plugin.Configuration;
        var muted = config.MuteOrchestrion;
        if (ImGui.Checkbox(Loc.T("Orchestrion.MuteCheckbox"), ref muted))
        {
            config.MuteOrchestrion = muted;
            plugin.SaveConfiguration();
            plugin.SetOrchestrionMute(muted);
        }
    }
}
