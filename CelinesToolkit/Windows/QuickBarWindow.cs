using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Windows;

internal sealed class QuickBarWindow : Window
{
    private const float Scale = 1.2f;

    private static readonly Vector4 WindowBgColor = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 ButtonColor = new(0.04f, 0.04f, 0.04f, 0.56f);
    private static readonly Vector4 FrameColor = new(0.22f, 0.22f, 0.22f, 0.56f);

    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private string filter = string.Empty;
    private int windowStylePushCount;
    private int windowColorPushCount;

    public QuickBarWindow(Plugin plugin, IDalamudPluginInterface pluginInterface, IClientState clientState, ICondition condition)
        : base("##CelinesToolkitQuickBar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.condition = condition;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    public override bool DrawConditions()
    {
        return clientState.IsLoggedIn
            && !condition[ConditionFlag.BetweenAreas]
            && !condition[ConditionFlag.BetweenAreas51]
            && !condition[ConditionFlag.CreatingCharacter]
            && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.WatchingCutscene78]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent];
    }

    public override void PreDraw()
    {
        var defaultStyle = ImGui.GetStyle();
        var framePadding = defaultStyle.FramePadding * Scale;
        var itemSpacing = defaultStyle.ItemSpacing * Scale;
        var windowPadding = new Vector2(4f) * Scale;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, windowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, itemSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        windowStylePushCount = 4;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBgColor);
        windowColorPushCount = 1;
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(windowColorPushCount);
        ImGui.PopStyleVar(windowStylePushCount);
    }

    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameColor);
        ImGui.PushStyleColor(ImGuiCol.Button, ButtonColor);

        var macros = plugin.Configuration.Macros;
        var selected = macros.Find(m => m.Name == plugin.Configuration.QuickBarSelectedMacroName);
        var frameHeight = ImGui.GetFrameHeight();

        ImGui.SetNextItemWidth(180f * Scale);
        if (ImGui.BeginCombo("##quickBarMacro", selected?.Name ?? Loc.T("QuickBar.SelectHint")))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##quickBarFilter", Loc.T("QuickBar.FilterHint"), ref filter, 100);
            ImGui.Separator();

            if (macros.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("QuickBar.NoMacros"));
            }

            foreach (var macro in macros)
            {
                if (!string.IsNullOrWhiteSpace(filter) && macro.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (ImGui.Selectable(macro.Name, macro == selected))
                {
                    plugin.Configuration.QuickBarSelectedMacroName = macro.Name;
                    plugin.SaveConfiguration();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(selected == null);
        var pressed = false;
        using (pluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var iconText = FontAwesomeIcon.PlayCircle.ToIconString();
            var iconSize = ImGui.CalcTextSize(iconText);
            var framePadding = ImGui.GetStyle().FramePadding;
            var buttonDimension = MathF.Max(frameHeight, MathF.Max(iconSize.X, iconSize.Y) + framePadding.Y * 2f);
            pressed = ImGui.Button(iconText, new Vector2(buttonDimension));
        }

        if (pressed && selected != null)
        {
            plugin.RunMacro(selected);
        }

        ImGui.EndDisabled();

        ImGui.PopStyleColor(2);
    }
}
