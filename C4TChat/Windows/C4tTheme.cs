using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace C4TChat.Windows;

internal static class C4tTheme
{
    internal sealed class Palette
    {
        public required Vector4 Bg { get; init; }
        public required Vector4 LogBg { get; init; }
        public required Vector4 Accent { get; init; }
        public required Vector4 Glow { get; init; }
        public required Vector4 Second { get; init; }
        public required Vector4 Text { get; init; }
        public required Vector4 Muted { get; init; }
        public required Vector4 Input { get; init; }
        public required Vector4 TitleActive { get; init; }
        public required string LogoFile { get; init; }
    }

    public static readonly Palette Blue = new()
    {
        Bg = Rgb(0x08, 0x0C, 0x18),
        LogBg = Rgb(0x04, 0x06, 0x0E),
        Accent = Rgb(0x28, 0x78, 0xFF),
        Glow = Rgb(0x6E, 0xAA, 0xFF),
        Second = Rgb(0x00, 0xE6, 0xC8),
        Text = Rgb(0xDD, 0xE8, 0xFF),
        Muted = Rgb(0x87, 0x96, 0xB8),
        Input = Rgb(0x05, 0x08, 0x12),
        TitleActive = Rgb(0x0A, 0x28, 0x8C),
        LogoFile = "c4t-logo-blau.png",
    };

    public static readonly Palette Purple = new()
    {
        Bg = Rgb(0x10, 0x09, 0x1B),
        LogBg = Rgb(0x08, 0x04, 0x0E),
        Accent = Rgb(0xB0, 0x26, 0xFF),
        Glow = Rgb(0xD6, 0x5C, 0xFF),
        Second = Rgb(0xFF, 0x3E, 0xC8),
        Text = Rgb(0xF1, 0xE8, 0xFF),
        Muted = Rgb(0xA0, 0x8C, 0xBF),
        Input = Rgb(0x06, 0x03, 0x0B),
        TitleActive = Rgb(0x2A, 0x0C, 0x55),
        LogoFile = "c4t-logo-lila.png",
    };

    private static readonly (ImGuiCol Col, Vector4 Color)[] BlueColors = BuildColors(Blue);
    private static readonly (ImGuiCol Col, Vector4 Color)[] PurpleColors = BuildColors(Purple);

    public static Palette? Active(Configuration config) => config.Design switch
    {
        ChatDesign.C4T => Blue,
        ChatDesign.C4TPurple => Purple,
        _ => null,
    };

    public static bool IsActive(Configuration config) => Active(config) != null;

    public static int PushColors(Configuration config)
    {
        var colors = config.Design switch
        {
            ChatDesign.C4T => BlueColors,
            ChatDesign.C4TPurple => PurpleColors,
            _ => null,
        };

        if (colors == null)
        {
            return 0;
        }

        foreach (var (col, color) in colors)
        {
            ImGui.PushStyleColor(col, color);
        }

        return colors.Length;
    }

    public static void PopColors(int count)
    {
        if (count > 0)
        {
            ImGui.PopStyleColor(count);
        }
    }

    public static Vector4 LogBackground(Configuration config) =>
        WithAlpha(Active(config)?.LogBg ?? new Vector4(0f, 0f, 0f, 1f), config.ChatLogBackgroundOpacity);

    public static Vector4 ActiveMarkerColor(Configuration config) => Active(config)?.Second ?? config.SendAccentColor;

    public static void DrawDecorations(Configuration config, float opacity)
    {
        if (Active(config) is not { } palette)
        {
            return;
        }

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var scale = ImGuiHelpers.GlobalScale;
        var rounding = ImGui.GetStyle().WindowRounding;
        var strength = Math.Clamp(opacity, 0.3f, 1f);

        var background = ImGui.GetBackgroundDrawList();
        var glowSteps = (int)MathF.Max(4f, 9f * scale);
        for (var i = glowSteps; i >= 1; i--)
        {
            var t = 1f - ((float)i / (glowSteps + 1));
            var offset = new Vector2(i, i);
            background.AddRect(min - offset, max + offset, ImGui.ColorConvertFloat4ToU32(WithAlpha(palette.Accent, 0.23f * t * t * strength)), rounding + i, ImDrawFlags.None, 2f);
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(min, max, false);
        var length = 16f * scale;
        var thickness = MathF.Max(2f, 2f * scale);
        var bracket = ImGui.ColorConvertFloat4ToU32(WithAlpha(palette.Second, strength));
        drawList.AddRectFilled(min, min + new Vector2(length, thickness), bracket);
        drawList.AddRectFilled(min, min + new Vector2(thickness, length), bracket);
        drawList.AddRectFilled(max - new Vector2(length, thickness), max, bracket);
        drawList.AddRectFilled(max - new Vector2(thickness, length), max, bracket);
        drawList.PopClipRect();
    }

    public static void DrawLogoItem(Plugin plugin)
    {
        if (Active(plugin.Configuration) is not { } palette || !plugin.Configuration.ShowC4tLogo
            || plugin.GetC4tLogo(palette.LogoFile) is not { } logo)
        {
            return;
        }

        var size = ImGui.GetFrameHeight();
        ImGui.Image(logo, new Vector2(size, size));
        ImGui.SameLine();
    }

    public static void DrawWatermark(Plugin plugin)
    {
        if (Active(plugin.Configuration) is not { } palette || !plugin.Configuration.ShowC4tWatermark
            || plugin.GetC4tLogo(palette.LogoFile) is not { } logo)
        {
            return;
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var side = MathF.Min(size.X, size.Y) * 0.7f;
        if (side < 32f)
        {
            return;
        }

        var center = pos + (size / 2f);
        var half = new Vector2(side / 2f, side / 2f);
        ImGui.GetWindowDrawList().AddImage(logo, center - half, center + half, Vector2.Zero, Vector2.One, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.08f)));
    }

    private static (ImGuiCol Col, Vector4 Color)[] BuildColors(Palette p) => new[]
    {
        (ImGuiCol.Text, p.Text),
        (ImGuiCol.TextDisabled, p.Muted),
        (ImGuiCol.WindowBg, p.Bg),
        (ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f)),
        (ImGuiCol.PopupBg, WithAlpha(p.Bg, 0.96f)),
        (ImGuiCol.Border, WithAlpha(p.Accent, 0.5f)),
        (ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f)),
        (ImGuiCol.FrameBg, WithAlpha(p.Input, 0.8f)),
        (ImGuiCol.FrameBgHovered, WithAlpha(p.Accent, 0.18f)),
        (ImGuiCol.FrameBgActive, WithAlpha(p.Accent, 0.28f)),
        (ImGuiCol.TitleBg, p.Bg),
        (ImGuiCol.TitleBgActive, p.TitleActive),
        (ImGuiCol.TitleBgCollapsed, WithAlpha(p.Bg, 0.8f)),
        (ImGuiCol.MenuBarBg, p.Bg),
        (ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0.15f)),
        (ImGuiCol.ScrollbarGrab, WithAlpha(p.Accent, 0.35f)),
        (ImGuiCol.ScrollbarGrabHovered, WithAlpha(p.Accent, 0.55f)),
        (ImGuiCol.ScrollbarGrabActive, WithAlpha(p.Glow, 0.8f)),
        (ImGuiCol.CheckMark, p.Second),
        (ImGuiCol.SliderGrab, p.Accent),
        (ImGuiCol.SliderGrabActive, p.Glow),
        (ImGuiCol.Button, WithAlpha(p.Accent, 0.12f)),
        (ImGuiCol.ButtonHovered, WithAlpha(p.Accent, 0.28f)),
        (ImGuiCol.ButtonActive, WithAlpha(p.Accent, 0.45f)),
        (ImGuiCol.Header, WithAlpha(p.Accent, 0.22f)),
        (ImGuiCol.HeaderHovered, WithAlpha(p.Accent, 0.30f)),
        (ImGuiCol.HeaderActive, WithAlpha(p.Accent, 0.45f)),
        (ImGuiCol.Separator, WithAlpha(p.Accent, 0.25f)),
        (ImGuiCol.SeparatorHovered, WithAlpha(p.Glow, 0.6f)),
        (ImGuiCol.SeparatorActive, p.Second),
        (ImGuiCol.ResizeGrip, WithAlpha(p.Accent, 0.15f)),
        (ImGuiCol.ResizeGripHovered, WithAlpha(p.Accent, 0.4f)),
        (ImGuiCol.ResizeGripActive, WithAlpha(p.Second, 0.8f)),
        (ImGuiCol.Tab, WithAlpha(p.Accent, 0.12f)),
        (ImGuiCol.TabHovered, WithAlpha(p.Accent, 0.35f)),
        (ImGuiCol.TabActive, WithAlpha(p.Accent, 0.30f)),
        (ImGuiCol.TabUnfocused, WithAlpha(p.Accent, 0.08f)),
        (ImGuiCol.TabUnfocusedActive, WithAlpha(p.Accent, 0.20f)),
        (ImGuiCol.TableHeaderBg, WithAlpha(p.Accent, 0.15f)),
        (ImGuiCol.TableBorderStrong, WithAlpha(p.Accent, 0.35f)),
        (ImGuiCol.TableBorderLight, WithAlpha(p.Accent, 0.18f)),
        (ImGuiCol.TextSelectedBg, WithAlpha(p.Accent, 0.40f)),
        (ImGuiCol.DragDropTarget, p.Second),
        (ImGuiCol.NavHighlight, p.Second),
        (ImGuiCol.ModalWindowDimBg, WithAlpha(p.Bg, 0.6f)),
    };

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    private static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);
}
