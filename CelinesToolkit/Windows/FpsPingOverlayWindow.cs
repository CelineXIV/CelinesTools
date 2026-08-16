using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows;

/// <summary>
/// Small, draggable, borderless overlay showing current FPS and/or ping - same conventions as
/// SlidecastCastBarWindow (no title bar, not closable, position/size persisted automatically by
/// ImGui's own window-state saving since this isn't NoSavedSettings). Position/size lock is a
/// separate opt-in (FpsPingPositionLocked) rather than always-on, since dragging it into place is
/// exactly how you're expected to position it in the first place.
/// </summary>
internal sealed class FpsPingOverlayWindow : Window
{
    // NoBackground - the background/border are drawn manually below (so the configurable opacity
    // slider can fade them all the way to nothing); without this flag ImGui's own default window
    // panel still renders underneath at its own fixed alpha, leaving a faint box behind even at 0.
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground;

    private static readonly Vector4 BackgroundColor = new(0.08f, 0.08f, 0.08f, 0.85f);
    private static readonly Vector4 TextColor = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 BorderColor = new(0f, 0f, 0f, 0.6f);
    private static readonly Vector4 GoodColor = new(0.3f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 WarnColor = new(0.95f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 BadColor = new(0.95f, 0.3f, 0.3f, 1f);

    private readonly Plugin plugin;
    private readonly FpsPingService fpsPingService;

    public FpsPingOverlayWindow(Plugin plugin, FpsPingService fpsPingService) : base("##CelinesToolkitFpsPing", BaseFlags)
    {
        this.plugin = plugin;
        this.fpsPingService = fpsPingService;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Position = new Vector2(20, 20);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions() =>
        plugin.Configuration.FpsPingEnabled && (plugin.Configuration.FpsPingShowFps || plugin.Configuration.FpsPingShowPing);

    public override void PreDraw()
    {
        Flags = plugin.Configuration.FpsPingPositionLocked ? BaseFlags | ImGuiWindowFlags.NoMove : BaseFlags;
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        ImGui.SetWindowFontScale(config.FpsPingScale);

        var fps = ImGui.GetIO().Framerate;
        var fpsColor = config.FpsPingColorCodeFps ? ColorForFps(fps, config) : TextColor;
        var ping = fpsPingService.LastPingMs;

        var segments = new List<(string Text, Vector4 Color)>();
        if (config.FpsPingShowFps)
        {
            segments.Add(($"{fps:0} FPS", fpsColor));
        }

        if (config.FpsPingShowPing)
        {
            segments.Add((ping.HasValue ? $"{ping.Value} ms" : "-- ms", TextColor));
        }

        const float gap = 16f;
        var padding = new Vector2(10f, 6f);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var contentSize = Vector2.Zero;
        foreach (var (text, _) in segments)
        {
            var textSize = ImGui.CalcTextSize(text);
            contentSize.X += textSize.X + (contentSize.X > 0f ? gap : 0f);
            contentSize.Y = System.MathF.Max(contentSize.Y, textSize.Y);
        }

        var size = contentSize + padding * 2f;

        // Background and border fade together, at the same configured opacity, so dragging it down
        // to 0 leaves plain floating text with no box at all rather than just a fainter box.
        var backgroundOpacity = config.FpsPingBackgroundOpacity;
        var background = new Vector4(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, backgroundOpacity);
        var border = new Vector4(BorderColor.X, BorderColor.Y, BorderColor.Z, BorderColor.W * backgroundOpacity);
        drawList.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(background), 4f);
        drawList.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(border), 4f);

        var cursorX = origin.X + padding.X;
        foreach (var (text, color) in segments)
        {
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(new Vector2(cursorX, origin.Y + padding.Y), ImGui.ColorConvertFloat4ToU32(color), text);
            cursorX += textSize.X + gap;
        }

        // Reserves layout space matching the drawn area, same as SlidecastCastBarWindow, so the
        // window's own size/drag handling lines up with what was actually drawn.
        ImGui.Dummy(size);
    }

    private static Vector4 ColorForFps(float fps, Configuration config)
    {
        if (fps <= config.FpsPingRedThreshold)
        {
            return BadColor;
        }

        return fps >= config.FpsPingGreenThreshold ? GoodColor : WarnColor;
    }
}
