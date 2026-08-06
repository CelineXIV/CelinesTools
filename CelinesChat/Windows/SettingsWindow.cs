using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using CelinesChat.Windows.SettingsPages;

namespace CelinesChat.Windows;

internal enum SettingsPage
{
    General,
    Colors,
    Tabs,
    Display,
    Fonts,
    FileLog,
    Info,
}

internal sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private readonly GeneralPage generalPage;
    private readonly ColorsPage colorsPage;
    private readonly TabsPage tabsPage;
    private readonly DisplayPage displayPage;
    private readonly FontsPage fontsPage;
    private readonly FileLogPage fileLogPage;
    private readonly InfoPage infoPage;
    private SettingsPage currentPage = SettingsPage.General;

    public SettingsWindow(Plugin plugin) : base(WindowTitles.Settings)
    {
        this.plugin = plugin;
        Size = new Vector2(560, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        generalPage = new GeneralPage(plugin);
        colorsPage = new ColorsPage(plugin);
        tabsPage = new TabsPage(plugin);
        displayPage = new DisplayPage(plugin);
        fontsPage = new FontsPage(plugin);
        fileLogPage = new FileLogPage(plugin);
        infoPage = new InfoPage(plugin);
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(plugin.Configuration.WindowOpacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));

        ImGui.BeginChild("##settingsNav", new Vector2(140, 0), true);
        DrawNavEntry(Loc.T("Nav.General"), FontAwesomeIcon.SlidersH, SettingsPage.General);
        DrawNavEntry(Loc.T("Nav.Colors"), FontAwesomeIcon.Palette, SettingsPage.Colors);
        DrawNavEntry(Loc.T("Nav.Tabs"), FontAwesomeIcon.FolderOpen, SettingsPage.Tabs);
        DrawNavEntry(Loc.T("Nav.Display"), FontAwesomeIcon.Desktop, SettingsPage.Display);
        DrawNavEntry(Loc.T("Nav.Fonts"), FontAwesomeIcon.Font, SettingsPage.Fonts);
        DrawNavEntry(Loc.T("Nav.FileLog"), FontAwesomeIcon.Save, SettingsPage.FileLog);
        ImGui.Separator();
        DrawNavEntry(Loc.T("Nav.Info"), FontAwesomeIcon.InfoCircle, SettingsPage.Info);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##settingsContent", Vector2.Zero, false);
        switch (currentPage)
        {
            case SettingsPage.General:
                generalPage.Draw();
                break;
            case SettingsPage.Colors:
                colorsPage.Draw();
                break;
            case SettingsPage.Tabs:
                tabsPage.Draw();
                break;
            case SettingsPage.Display:
                displayPage.Draw();
                break;
            case SettingsPage.Fonts:
                fontsPage.Draw();
                break;
            case SettingsPage.FileLog:
                fileLogPage.Draw();
                break;
            case SettingsPage.Info:
                infoPage.Draw();
                break;
        }
        ImGui.EndChild();

        ImGui.PopStyleVar(2);
    }

    private void DrawNavEntry(string label, FontAwesomeIcon icon, SettingsPage page)
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
