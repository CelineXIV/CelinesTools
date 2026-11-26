using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class DisplayPage
{
    private readonly Plugin plugin;

    public DisplayPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(180);
        var designIndex = (int)config.Design;
        if (ImGui.Combo(Loc.T("Settings.Design"), ref designIndex, DesignLabels, DesignLabels.Length))
        {
            config.Design = (ChatDesign)designIndex;
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.DesignHint"));
        }

        using (ImRaii.Disabled(!C4tTheme.IsActive(config)))
        using (ImRaii.PushIndent())
        {
            var showLogo = config.ShowC4tLogo;
            if (ImGui.Checkbox(Loc.T("Settings.C4tLogo"), ref showLogo))
            {
                config.ShowC4tLogo = showLogo;
                plugin.SaveConfiguration();
            }

            var showWatermark = config.ShowC4tWatermark;
            if (ImGui.Checkbox(Loc.T("Settings.C4tWatermark"), ref showWatermark))
            {
                config.ShowC4tWatermark = showWatermark;
                plugin.SaveConfiguration();
            }
        }

        ImGui.Separator();

        ImGui.SetNextItemWidth(150);
        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.WindowOpacity"), ref opacity, 0.2f, 1.0f))
        {
            config.WindowOpacity = opacity;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var unfocusedOpacity = config.UnfocusedWindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.UnfocusedOpacity"), ref unfocusedOpacity, 0.0f, 1.0f))
        {
            config.UnfocusedWindowOpacity = unfocusedOpacity;
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.UnfocusedOpacityHint"));
        }

        ImGui.SetNextItemWidth(150);
        var logBgOpacity = config.ChatLogBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.LogBackgroundOpacity"), ref logBgOpacity, 0.0f, 1.0f))
        {
            config.ChatLogBackgroundOpacity = logBgOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var tabStripBgOpacity = config.TabStripBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.TabStripBackgroundOpacity"), ref tabStripBgOpacity, 0.0f, 1.0f))
        {
            config.TabStripBackgroundOpacity = tabStripBgOpacity;
            plugin.SaveConfiguration();
        }

        var showSenderStatusIcons = config.ShowSenderStatusIcons;
        if (ImGui.Checkbox(Loc.T("Settings.ShowSenderStatusIcons"), ref showSenderStatusIcons))
        {
            config.ShowSenderStatusIcons = showSenderStatusIcons;
            plugin.SaveConfiguration();
        }

        var showDivider = config.ShowNewMessagesDivider;
        if (ImGui.Checkbox(Loc.T("Settings.ShowNewMessagesDivider"), ref showDivider))
        {
            config.ShowNewMessagesDivider = showDivider;
            plugin.SaveConfiguration();
        }

        var group = config.GroupConsecutiveMessages;
        if (ImGui.Checkbox(Loc.T("Settings.GroupConsecutive"), ref group))
        {
            config.GroupConsecutiveMessages = group;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.GroupConsecutiveTooltip"));
        }

        DrawSpacingSlider(Loc.T("Settings.LineSpacing"), config.LineSpacing, 0f, 10f, v => config.LineSpacing = v);
        DrawSpacingSlider(Loc.T("Settings.MessageSpacing"), config.MessageSpacing, 0f, 16f, v => config.MessageSpacing = v);

        var showDates = config.ShowDateSeparators;
        if (ImGui.Checkbox(Loc.T("Settings.ShowDateSeparators"), ref showDates))
        {
            config.ShowDateSeparators = showDates;
            plugin.InvalidateChatLayout();
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(220);
        var previewPositionIndex = (int)config.PreviewPosition;
        if (ImGui.Combo(Loc.T("Settings.PreviewPosition"), ref previewPositionIndex, PreviewPositionLabels, PreviewPositionLabels.Length))
        {
            config.PreviewPosition = (PreviewPosition)previewPositionIndex;
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Settings.PreviewPositionHint"));
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.ToolbarHeader"));

        ImGui.SetNextItemWidth(180);
        var iconPackIndex = (int)config.ToolbarIconPack;
        if (ImGui.Combo(Loc.T("Settings.ToolbarIconPack"), ref iconPackIndex, ToolbarIconPackLabels, ToolbarIconPackLabels.Length))
        {
            config.ToolbarIconPack = (ToolbarIconPack)iconPackIndex;
            plugin.SaveConfiguration();
        }

        var isGlassPack = config.ToolbarIconPack is ToolbarIconPack.AeroGlass or ToolbarIconPack.FrostedGlass;
        Vector4 iconBgColor;
        bool useCustomIconColor;
        Vector4 iconColor;
        using (ImRaii.Disabled(!isGlassPack))
        using (ImRaii.PushIndent())
        {
            iconBgColor = config.ToolbarIconBackgroundColor;
            if (ImGui.ColorEdit4(Loc.T("Settings.ToolbarIconBackgroundColor"), ref iconBgColor))
            {
                config.ToolbarIconBackgroundColor = iconBgColor;
                plugin.SaveConfiguration();
            }
        }

        using (ImRaii.Disabled(config.ToolbarIconPack == ToolbarIconPack.Classic))
        using (ImRaii.PushIndent())
        {
            useCustomIconColor = config.ToolbarUseCustomIconColor;
            if (ImGui.Checkbox(Loc.T("Settings.ToolbarUseCustomIconColor"), ref useCustomIconColor))
            {
                config.ToolbarUseCustomIconColor = useCustomIconColor;
                plugin.SaveConfiguration();
            }

            using (ImRaii.Disabled(!useCustomIconColor))
            {
                iconColor = config.ToolbarIconColor;
                if (ImGui.ColorEdit4(Loc.T("Settings.ToolbarIconColor"), ref iconColor))
                {
                    config.ToolbarIconColor = iconColor;
                    plugin.SaveConfiguration();
                }
            }
        }

        ImGui.TextDisabled(Loc.T("Settings.ToolbarIconPackPreview"));
        ImGui.SameLine();

        if (config.ToolbarIconPack == ToolbarIconPack.Flat)
        {
            var flatColor = useCustomIconColor ? iconColor : (Vector4?)null;
            ChatWindow.DrawFlatIconButton("##flatPreview1", FontAwesomeIcon.Cog, flatColor, ImGui.GetFrameHeight(), 0.85f);
            ImGui.SameLine();
            ChatWindow.DrawFlatIconButton("##flatPreview2", FontAwesomeIcon.Wifi, flatColor, ImGui.GetFrameHeight(), 0.85f);
            ImGui.SameLine();
            ChatWindow.DrawFlatIconButton("##flatPreview3", FontAwesomeIcon.Users, flatColor, ImGui.GetFrameHeight(), 0.85f);
        }
        else if (config.ToolbarIconPack == ToolbarIconPack.Classic)
        {
            ImGuiComponents.IconButton(FontAwesomeIcon.Cog);
            ImGui.SameLine();
            ImGuiComponents.IconButton(FontAwesomeIcon.Wifi);
            ImGui.SameLine();
            ImGuiComponents.IconButton(FontAwesomeIcon.Users);
        }
        else
        {
            var previewIconColor = useCustomIconColor ? iconColor : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            GlassIconButton.Draw(FontAwesomeIcon.Cog, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview1");
            ImGui.SameLine();
            GlassIconButton.Draw(FontAwesomeIcon.Wifi, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview2");
            ImGui.SameLine();
            GlassIconButton.Draw(FontAwesomeIcon.Users, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview3");
        }
    }

    private static readonly string[] DesignLabels =
    {
        Loc.T("Settings.Design.Standard"),
        Loc.T("Settings.Design.C4T"),
        Loc.T("Settings.Design.C4TPurple"),
    };

    private static readonly string[] PreviewPositionLabels =
    {
        Loc.T("Settings.PreviewPosition.Window"),
        Loc.T("Settings.PreviewPosition.Inside"),
    };

    private static readonly string[] ToolbarIconPackLabels =
    {
        Loc.T("Settings.ToolbarIconPack.Classic"),
        Loc.T("Settings.ToolbarIconPack.AeroGlass"),
        Loc.T("Settings.ToolbarIconPack.FrostedGlass"),
        Loc.T("Settings.ToolbarIconPack.Flat"),
    };

    private void DrawSpacingSlider(string label, float value, float min, float max, System.Action<float> set)
    {
        using var id = ImRaii.PushId(label);
        var shown = value < 0f ? ImGui.GetStyle().ItemSpacing.Y : value;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderFloat(label, ref shown, min, max, value < 0f ? Loc.T("Settings.SpacingNormal") : "%.0f px"))
        {
            set(MathF.Round(shown));
            plugin.InvalidateChatLayout();
            plugin.SaveConfiguration();
        }

        if (value >= 0f)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.UndoAlt))
            {
                set(-1f);
                plugin.InvalidateChatLayout();
                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Settings.SpacingReset"));
            }
        }
    }
}
