using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows;

/// <summary>
/// Full-screen, click-through overlay that draws a ring around the mouse cursor while casting - a
/// progress arc fills in as the cast advances, and the ring turns red (matching the cast bar's own
/// slidecast-zone color, for a consistent "red means you can move" signal) once the slidecast
/// window is reached. NoInputs keeps this fully transparent to actual mouse clicks.
/// </summary>
internal sealed class SlidecastCursorOverlayWindow : Window
{
    private const float Radius = 18f;
    private const float Thickness = 3f;

    private static readonly Vector4 CastingColor = new(0.9f, 0.9f, 0.9f, 0.9f);
    private static readonly Vector4 SlidecastColor = new(1f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 TrackColor = new(1f, 1f, 1f, 0.25f);

    private readonly Plugin plugin;
    private readonly SlidecastService slidecastService;

    public SlidecastCursorOverlayWindow(Plugin plugin, SlidecastService slidecastService)
        : base(
            "##CelinesToolkitSlidecastCursorOverlay",
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration)
    {
        this.plugin = plugin;
        this.slidecastService = slidecastService;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    public override bool DrawConditions()
    {
        if (!plugin.Configuration.SlidecastEnabled || !plugin.Configuration.SlidecastShowCursorCircle)
        {
            return false;
        }

        if (plugin.SlidecastPreviewMode)
        {
            return true;
        }

        return slidecastService.TryGetState(plugin.Configuration.SlidecastThresholdMs / 1000f, out _);
    }

    public override void PreDraw()
    {
        Position = Vector2.Zero;
        PositionCondition = ImGuiCond.Always;
        Size = ImGui.GetIO().DisplaySize;
        SizeCondition = ImGuiCond.Always;
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

        var center = ImGui.GetMousePos();
        var drawList = ImGui.GetWindowDrawList();
        var ringColor = state.IsInSlidecastWindow ? SlidecastColor : CastingColor;

        drawList.AddCircle(center, Radius, ImGui.ColorConvertFloat4ToU32(TrackColor), 48, Thickness);

        // Progress arc starting at the top, sweeping clockwise with cast progress.
        const float startAngle = -System.MathF.PI / 2f;
        var endAngle = startAngle + (System.MathF.PI * 2f * state.Progress01);
        if (state.Progress01 > 0f)
        {
            drawList.PathArcTo(center, Radius, startAngle, endAngle, 48);
            drawList.PathStroke(ImGui.ColorConvertFloat4ToU32(ringColor), ImDrawFlags.None, Thickness);
        }
    }
}
