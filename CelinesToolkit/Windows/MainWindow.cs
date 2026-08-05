using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CelinesToolkit.Windows.Pages;

namespace CelinesToolkit.Windows;

internal enum ToolkitPage
{
    Overview,
    CommandTool,
    QuickBar,
    Orchestrion,
    PreviewManager,
    ShoppingList,
}

internal sealed class MainWindow : Window
{
    private readonly OverviewPage overviewPage;
    private readonly CommandToolPage commandToolPage;
    private readonly QuickBarPage quickBarPage;
    private readonly OrchestrionPage orchestrionPage;
    private readonly PreviewManagerPage previewManagerPage;
    private readonly ShoppingListPage shoppingListPage;
    private ToolkitPage currentPage = ToolkitPage.Overview;

    public MainWindow(Plugin plugin) : base("CelinesToolkit##MainWindow")
    {
        Size = new Vector2(700, 460);
        SizeCondition = ImGuiCond.FirstUseEver;

        overviewPage = new OverviewPage();
        commandToolPage = new CommandToolPage(plugin);
        quickBarPage = new QuickBarPage(plugin);
        orchestrionPage = new OrchestrionPage(plugin);
        previewManagerPage = new PreviewManagerPage(plugin, plugin.ModPreviewScanner, plugin.PreviewImageService, plugin.PreviewTextureCache, plugin.FileDialogManager);
        shoppingListPage = new ShoppingListPage(plugin.ItemLookupService, plugin.ShoppingListPricingService, plugin.FileDialogManager);
    }

    public override void Draw()
    {
        ImGui.BeginChild("##toolkitNav", new Vector2(150, 0), true);
        DrawNavEntry(Loc.T("Nav.Overview"), ToolkitPage.Overview);
        DrawNavEntry(Loc.T("Nav.CommandTool"), ToolkitPage.CommandTool);
        DrawNavEntry(Loc.T("Nav.QuickBar"), ToolkitPage.QuickBar);
        ImGui.Separator();
        DrawNavEntry(Loc.T("Nav.Orchestrion"), ToolkitPage.Orchestrion);
        DrawNavEntry(Loc.T("Nav.PreviewManager"), ToolkitPage.PreviewManager);
        DrawNavEntry(Loc.T("Nav.ShoppingList"), ToolkitPage.ShoppingList);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##toolkitContent", Vector2.Zero, false);
        switch (currentPage)
        {
            case ToolkitPage.Overview:
                overviewPage.Draw();
                break;
            case ToolkitPage.CommandTool:
                commandToolPage.Draw();
                break;
            case ToolkitPage.QuickBar:
                quickBarPage.Draw();
                break;
            case ToolkitPage.Orchestrion:
                orchestrionPage.Draw();
                break;
            case ToolkitPage.PreviewManager:
                previewManagerPage.Draw();
                break;
            case ToolkitPage.ShoppingList:
                shoppingListPage.Draw();
                break;
        }
        ImGui.EndChild();
    }

    private void DrawNavEntry(string label, ToolkitPage page)
    {
        if (ImGui.Selectable(label, currentPage == page))
        {
            currentPage = page;
        }
    }
}
