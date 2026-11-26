using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using C4TChat.Windows.SettingsPages;

namespace C4TChat.Windows;

internal enum SettingsPage
{
    General,
    Sounds,
    Mentions,
    Filter,
    Colors,
    Tabs,
    Rp,
    Display,
    Fonts,
    FileLog,
    WhisperContacts,
    WebClient,
    Backup,
    Info,
}

internal sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private readonly GeneralPage generalPage;
    private readonly SoundsPage soundsPage;
    private readonly MentionsPage mentionsPage;
    private readonly FilterPage filterPage;
    private readonly BackupPage backupPage;
    private readonly ColorsPage colorsPage;
    private readonly TabsPage tabsPage;
    private readonly RpPage rpPage;
    private readonly DisplayPage displayPage;
    private readonly FontsPage fontsPage;
    private readonly FileLogPage fileLogPage;
    private readonly WhisperContactsPage whisperContactsPage;
    private readonly WebClientPage webClientPage;
    private readonly InfoPage infoPage;
    private SettingsPage currentPage = SettingsPage.General;

    public SettingsWindow(Plugin plugin) : base(WindowTitles.Settings)
    {
        this.plugin = plugin;
        Size = new Vector2(560, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        generalPage = new GeneralPage(plugin);
        soundsPage = new SoundsPage(plugin);
        mentionsPage = new MentionsPage(plugin);
        filterPage = new FilterPage(plugin);
        backupPage = new BackupPage(plugin);
        colorsPage = new ColorsPage(plugin);
        tabsPage = new TabsPage(plugin);
        rpPage = new RpPage(plugin);
        displayPage = new DisplayPage(plugin);
        fontsPage = new FontsPage(plugin);
        fileLogPage = new FileLogPage(plugin);
        whisperContactsPage = new WhisperContactsPage(plugin);
        webClientPage = new WebClientPage(plugin);
        infoPage = new InfoPage(plugin);
    }

    private int c4tColorsPushed;

    public override void PreDraw()
    {
        c4tColorsPushed = C4tTheme.PushColors(plugin.Configuration);
    }

    internal void ShowPage(SettingsPage page)
    {
        currentPage = page;
        IsOpen = true;
        BringToFront();
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(c4tColorsPushed);
        c4tColorsPushed = 0;
    }

    public override void Draw()
    {
        C4tTheme.DrawDecorations(plugin.Configuration, 1f);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 0f)
            .Push(ImGuiStyleVar.FrameRounding, 4f)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f))
            .Push(ImGuiStyleVar.ChildRounding, 6f)
            .Push(ImGuiStyleVar.PopupRounding, 6f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 6f)
            .Push(ImGuiStyleVar.GrabRounding, 4f);

        using (ImRaii.Child("##settingsNav", new Vector2(140, 0), true))
        {
            DrawNavEntry(Loc.T("Nav.General"), FontAwesomeIcon.SlidersH, SettingsPage.General);
            DrawNavEntry(Loc.T("Nav.Sounds"), FontAwesomeIcon.VolumeUp, SettingsPage.Sounds);
            DrawNavEntry(Loc.T("Nav.Mentions"), FontAwesomeIcon.At, SettingsPage.Mentions);
            DrawNavEntry(Loc.T("Nav.Filter"), FontAwesomeIcon.Filter, SettingsPage.Filter);
            DrawNavEntry(Loc.T("Nav.Colors"), FontAwesomeIcon.Palette, SettingsPage.Colors);
            DrawNavEntry(Loc.T("Nav.Tabs"), FontAwesomeIcon.FolderOpen, SettingsPage.Tabs);
            DrawNavEntry(Loc.T("Nav.Rp"), FontAwesomeIcon.TheaterMasks, SettingsPage.Rp);
            DrawNavEntry(Loc.T("Nav.Display"), FontAwesomeIcon.Desktop, SettingsPage.Display);
            DrawNavEntry(Loc.T("Nav.Fonts"), FontAwesomeIcon.Font, SettingsPage.Fonts);
            DrawNavEntry(Loc.T("Nav.FileLog"), FontAwesomeIcon.Save, SettingsPage.FileLog);
            DrawNavEntry(Loc.T("Nav.WhisperContacts"), FontAwesomeIcon.UserEdit, SettingsPage.WhisperContacts);
            DrawNavEntry(Loc.T("Nav.WebClient"), FontAwesomeIcon.Wifi, SettingsPage.WebClient);
            DrawNavEntry(Loc.T("Nav.Backup"), FontAwesomeIcon.Archive, SettingsPage.Backup);
            ImGui.Separator();
            DrawNavEntry(Loc.T("Nav.Info"), FontAwesomeIcon.InfoCircle, SettingsPage.Info);
        }

        ImGui.SameLine();

        using (ImRaii.Child("##settingsContent", Vector2.Zero, false))
        {
            switch (currentPage)
            {
                case SettingsPage.General:
                    generalPage.Draw();
                    break;
                case SettingsPage.Sounds:
                    soundsPage.Draw();
                    break;
                case SettingsPage.Mentions:
                    mentionsPage.Draw();
                    break;
                case SettingsPage.Filter:
                    filterPage.Draw();
                    break;
                case SettingsPage.Backup:
                    backupPage.Draw();
                    break;
                case SettingsPage.Colors:
                    colorsPage.Draw();
                    break;
                case SettingsPage.Tabs:
                    tabsPage.Draw();
                    break;
                case SettingsPage.Rp:
                    rpPage.Draw();
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
                case SettingsPage.WhisperContacts:
                    whisperContactsPage.Draw();
                    break;
                case SettingsPage.WebClient:
                    webClientPage.Draw();
                    break;
                case SettingsPage.Info:
                    infoPage.Draw();
                    break;
            }
        }

        backupPage.DrawDialogs();
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

        if (isSelected)
        {
            var accent = plugin.Configuration.SendAccentColor;
            drawList.AddRectFilled(
                startPos,
                startPos + new Vector2(3f, lineHeight),
                ImGui.GetColorU32(accent));
        }

        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var iconText = icon.ToIconString();
            iconSize = ImGui.CalcTextSize(iconText);
            var iconPos = startPos + new Vector2(10f, (lineHeight - iconSize.Y) / 2f);
            drawList.AddText(iconPos, textColor, iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPos = startPos + new Vector2(10f + iconSize.X + 8f, (lineHeight - textSize.Y) / 2f);
        drawList.AddText(textPos, textColor, label);
    }
}
