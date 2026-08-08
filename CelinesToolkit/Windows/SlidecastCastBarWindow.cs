using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows;

/// <summary>
/// Small, draggable, borderless overlay showing a custom cast bar with a translucent red zone
/// marking the last SlidecastThresholdMs of the cast - the window the progress fill visibly
/// reaches marks "safe to move now" without interrupting the cast. Manually drawn via the
/// window's own draw list (not ImGui.ProgressBar) so the zone can be painted independently of the
/// fill. Same overlay conventions as the existing QuickBarWindow (no title bar, not closable,
/// click-through isn't needed here since it sits in a corner rather than over gameplay).
/// </summary>
internal sealed class SlidecastCastBarWindow : Window
{
    private static readonly Vector4 BackgroundColor = new(0.08f, 0.08f, 0.08f, 0.85f);
    private static readonly Vector4 FillColor = new(0.85f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 SlidecastZoneColor = new(1f, 0.2f, 0.2f, 0.5f);
    private static readonly Vector4 BorderColor = new(0f, 0f, 0f, 0.6f);
    private static readonly Vector4 TextColor = new(1f, 1f, 1f, 1f);

    private readonly Plugin plugin;
    private readonly SlidecastService slidecastService;

    public SlidecastCastBarWindow(Plugin plugin, SlidecastService slidecastService)
        : base("##CelinesToolkitSlidecastCastBar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;
        this.slidecastService = slidecastService;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Size = new Vector2(260, 46);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions()
    {
        if (!plugin.Configuration.SlidecastEnabled || !plugin.Configuration.SlidecastShowCastBar)
        {
            return false;
        }

        if (plugin.SlidecastPreviewMode)
        {
            return true;
        }

        return slidecastService.TryGetState(plugin.Configuration.SlidecastThresholdMs / 1000f, out _);
    }

    public override void Draw()
    {
        var thresholdSeconds = plugin.Configuration.SlidecastThresholdMs / 1000f;
        SlidecastState state;
        if (plugin.SlidecastPreviewMode)
        {
            state = SlidecastService.GetPreviewState(thresholdSeconds);
        }
        else if (!slidecastService.TryGetState(thresholdSeconds, out state))
        {
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var size = new Vector2(MathF.Max(avail.X, 40f), MathF.Max(avail.Y, 24f));
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(BackgroundColor), 4f);

        var fillWidth = size.X * state.Progress01;
        if (fillWidth > 0f)
        {
            drawList.AddRectFilled(origin, origin + new Vector2(fillWidth, size.Y), ImGui.ColorConvertFloat4ToU32(FillColor), 4f);
        }

        // The slidecast zone is a fixed marker at a constant position (the last
        // threshold/total fraction of the bar) - drawn on top with translucency so it tints
        // whatever's underneath (background or fill) rather than moving with the progress fill.
        var zoneFraction = state.TotalSeconds > 0f ? Math.Clamp(thresholdSeconds / state.TotalSeconds, 0f, 1f) : 0f;
        if (zoneFraction > 0f)
        {
            var zoneX = size.X * (1f - zoneFraction);
            drawList.AddRectFilled(origin + new Vector2(zoneX, 0f), origin + size, ImGui.ColorConvertFloat4ToU32(SlidecastZoneColor), 4f);
        }

        drawList.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(BorderColor), 4f);

        var label = string.IsNullOrEmpty(state.ActionName)
            ? $"{state.RemainingSeconds:0.00}s"
            : $"{state.ActionName}  {state.RemainingSeconds:0.00}s";
        var textSize = ImGui.CalcTextSize(label);
        var textPos = origin + new Vector2((size.X - textSize.X) / 2f, (size.Y - textSize.Y) / 2f);
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(TextColor), label);

        // Reserves layout space matching the drawn area so the window's own size/drag/resize
        // handling lines up with what was actually drawn.
        ImGui.Dummy(size);
    }
}
