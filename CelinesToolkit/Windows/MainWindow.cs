using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
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
    HousingTracker,
    GlamourerPreview,
    Slidecast,
}

internal sealed class MainWindow : Window
{
    private readonly OverviewPage overviewPage;
    private readonly CommandToolPage commandToolPage;
    private readonly QuickBarPage quickBarPage;
    private readonly OrchestrionPage orchestrionPage;
    private readonly PreviewManagerPage previewManagerPage;
    private readonly ShoppingListPage shoppingListPage;
    private readonly HousingTrackerPage housingTrackerPage;
    private readonly GlamourerPreviewPage glamourerPreviewPage;
    private readonly SlidecastPage slidecastPage;
    private readonly IClientState clientState;
    private ToolkitPage currentPage = ToolkitPage.Overview;

    public MainWindow(Plugin plugin, IClientState clientState) : base("CelinesToolkit##MainWindow")
    {
        this.clientState = clientState;
        Size = new Vector2(700, 460);
        SizeCondition = ImGuiCond.FirstUseEver;

        overviewPage = new OverviewPage();
        commandToolPage = new CommandToolPage(plugin);
        quickBarPage = new QuickBarPage(plugin);
        orchestrionPage = new OrchestrionPage(plugin);
        previewManagerPage = new PreviewManagerPage(plugin, plugin.ModPreviewScanner, plugin.PreviewImageService, plugin.PreviewTextureCache, plugin.FileDialogManager);
        shoppingListPage = new ShoppingListPage(plugin.ItemLookupService, plugin.ShoppingListPricingService, plugin.FileDialogManager);
        housingTrackerPage = new HousingTrackerPage(plugin.HousingTrackerService);
        glamourerPreviewPage = new GlamourerPreviewPage(plugin);
        slidecastPage = new SlidecastPage(plugin);
    }

    // Preserves this window's prior hidden-during-GPose behavior now that the plugin as a whole
    // opts out of Dalamud's automatic GPose UI hide (see Plugin's DisableGposeUiHide comment) -
    // only GlamourerPreviewWindow specifically needed to stay visible there.
    public override bool DrawConditions() => !clientState.IsGPosing;

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f));

        ImGui.BeginChild("##toolkitNav", new Vector2(160, 0), true);
        DrawNavEntry(Loc.T("Nav.Overview"), FontAwesomeIcon.Home, ToolkitPage.Overview);
        DrawNavEntry(Loc.T("Nav.CommandTool"), FontAwesomeIcon.ListOl, ToolkitPage.CommandTool);
        DrawNavEntry(Loc.T("Nav.QuickBar"), FontAwesomeIcon.Bolt, ToolkitPage.QuickBar);
        ImGui.Separator();
        DrawNavEntry(Loc.T("Nav.Orchestrion"), FontAwesomeIcon.Music, ToolkitPage.Orchestrion);
        DrawNavEntry(Loc.T("Nav.PreviewManager"), FontAwesomeIcon.Image, ToolkitPage.PreviewManager);
        DrawNavEntry(Loc.T("Nav.ShoppingList"), FontAwesomeIcon.ShoppingCart, ToolkitPage.ShoppingList);
        DrawNavEntry(Loc.T("Nav.HousingTracker"), FontAwesomeIcon.Building, ToolkitPage.HousingTracker);
        DrawNavEntry(Loc.T("Nav.GlamourerPreview"), FontAwesomeIcon.User, ToolkitPage.GlamourerPreview);
        DrawNavEntry(Loc.T("Nav.Slidecast"), FontAwesomeIcon.Running, ToolkitPage.Slidecast);
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
            case ToolkitPage.HousingTracker:
                housingTrackerPage.Draw();
                break;
            case ToolkitPage.GlamourerPreview:
                glamourerPreviewPage.Draw();
                break;
            case ToolkitPage.Slidecast:
                slidecastPage.Draw();
                break;
        }
        ImGui.EndChild();

        ImGui.PopStyleVar(2);
    }

    private void DrawNavEntry(string label, FontAwesomeIcon icon, ToolkitPage page)
    {
        var isSelected = currentPage == page;
        var startPos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetFrameHeight();

        if (ImGui.Selectable("##nav" + page, isSelected, ImGuiSelectableFlags.None, new Vector2(width, lineHeight)))
        {
            currentPage = page;
        }

        var drawList = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        ImGui.PushFont(UiBuilder.IconFont);
        var iconText = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconText);
        var iconPos = startPos + new Vector2(6f, (lineHeight - iconSize.Y) / 2f);
        drawList.AddText(iconPos, textColor, iconText);
        ImGui.PopFont();

        var textSize = ImGui.CalcTextSize(label);
        var textPos = startPos + new Vector2(6f + iconSize.X + 8f, (lineHeight - textSize.Y) / 2f);
        drawList.AddText(textPos, textColor, label);
    }
}
