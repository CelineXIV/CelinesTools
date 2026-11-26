using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using C4TChat.Services;
using Newtonsoft.Json.Linq;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace C4TChat.Windows.SettingsPages;

internal sealed class DisplayPage
{
    private readonly Plugin plugin;
    private readonly FileDialogManager fileDialog = new();
    private JObject? undoTheme;
    private string? themeStatus;
    private bool themeStatusError;

    public DisplayPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;
        fileDialog.Draw();

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

        DrawThemeSharing();
    }

    private void ApplyTheme(Action<Configuration> apply, string status)
    {
        undoTheme = ThemeTransfer.Parse(ThemeTransfer.Export(plugin.Configuration));
        apply(plugin.Configuration);
        plugin.InvalidateChatLayout();
        plugin.SaveConfiguration();
        themeStatus = status;
        themeStatusError = false;
    }

    private void ImportTheme(string text)
    {
        if (ThemeTransfer.Parse(text) is { } theme)
        {
            ApplyTheme(c => ThemeTransfer.Apply(c, theme), Loc.T("Theme.Imported"));
        }
        else
        {
            themeStatus = Loc.T("Theme.Invalid");
            themeStatusError = true;
        }
    }

    private void DrawThemeSharing()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Theme.Header"));
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Theme.Hint"));
        }

        ImGui.TextUnformatted(Loc.T("Theme.Presets"));
        for (var i = 0; i < ThemeTransfer.Presets.Length; i++)
        {
            var preset = ThemeTransfer.Presets[i];
            if (i > 0)
            {
                ImGui.SameLine();
            }

            if (ImGui.Button(Loc.T(preset.NameKey) + "##preset" + i))
            {
                ApplyTheme(preset.Apply, Loc.T("Theme.PresetApplied", Loc.T(preset.NameKey)));
            }
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, Loc.T("Theme.CopyToClipboard")))
        {
            ImGui.SetClipboardText(ThemeTransfer.Export(plugin.Configuration));
            themeStatus = Loc.T("Theme.Copied");
            themeStatusError = false;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, Loc.T("Theme.PasteFromClipboard")))
        {
            ImportTheme(ImGui.GetClipboardText() ?? string.Empty);
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, Loc.T("Theme.SaveFile")))
        {
            fileDialog.SaveFileDialog(Loc.T("Theme.SaveFile"), ".c4ttheme", "C4T-Design", ".c4ttheme", (ok, path) =>
            {
                if (!ok)
                {
                    return;
                }

                try
                {
                    File.WriteAllText(path, ThemeTransfer.Export(plugin.Configuration));
                    themeStatus = Loc.T("Theme.Saved");
                    themeStatusError = false;
                }
                catch (Exception ex)
                {
                    themeStatus = ex.Message;
                    themeStatusError = true;
                }
            });
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FolderOpen, Loc.T("Theme.LoadFile")))
        {
            fileDialog.OpenFileDialog(Loc.T("Theme.LoadFile"), ".c4ttheme,.txt,.json", (ok, paths) =>
            {
                if (!ok || paths.Count == 0)
                {
                    return;
                }

                try
                {
                    ImportTheme(File.ReadAllText(paths[0]));
                }
                catch (Exception ex)
                {
                    themeStatus = ex.Message;
                    themeStatusError = true;
                }
            }, 1, null, false);
        }

        if (undoTheme != null)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, Loc.T("Theme.Undo")))
            {
                var previous = undoTheme;
                undoTheme = null;
                ThemeTransfer.Apply(plugin.Configuration, previous);
                plugin.InvalidateChatLayout();
                plugin.SaveConfiguration();
                themeStatus = Loc.T("Theme.Undone");
                themeStatusError = false;
            }
        }

        if (themeStatus != null)
        {
            ImGui.TextColored(themeStatusError ? new Vector4(1f, 0.5f, 0.45f, 1f) : C4tTheme.ActiveMarkerColor(plugin.Configuration), themeStatus);
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
