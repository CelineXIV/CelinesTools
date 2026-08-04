using Dalamud.Bindings.ImGui;

namespace CelinesToolkit.Windows.Pages;

internal sealed class OverviewPage
{
    public void Draw()
    {
        ImGui.TextUnformatted(Loc.T("Overview.Title"));
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("Overview.Intro"));
        ImGui.Spacing();
        ImGui.BulletText(Loc.T("Overview.FeatureCommandTool"));
        ImGui.BulletText(Loc.T("Overview.FeatureQuickBar"));
        ImGui.BulletText(Loc.T("Overview.FeatureOrchestrion"));
        ImGui.BulletText(Loc.T("Overview.FeaturePreviewManager"));
        ImGui.BulletText(Loc.T("Overview.FeatureShoppingList"));
    }
}
