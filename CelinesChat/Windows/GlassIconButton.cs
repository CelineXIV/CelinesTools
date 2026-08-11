using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace CelinesChat.Windows;

/// <summary>
/// Hand-drawn stand-ins for ImGuiComponents.IconButton, used wherever Configuration.ToolbarIconPack
/// isn't Classic (see ChatWindow.DrawToolbarIconButton) - ImGui's themed Button has nothing like
/// either look to opt into. Both draw on top of a plain InvisibleButton, so hover/active/click
/// state, tooltips (IsItemHovered right after the call) and popups (BeginPopupContextItem) all keep
/// working exactly like they did against the real IconButton.
/// </summary>
internal static class GlassIconButton
{
    private const float Rounding = 5f;

    /// <param name="backgroundColor">
    /// Idle-state tint and transparency (Configuration.ToolbarIconBackgroundColor) - hover/active
    /// are brightened/darkened relative to this rather than a separate hardcoded color per pack.
    /// </param>
    /// <param name="iconColor">
    /// Already resolved by the caller - either the theme's normal text color, a per-button dynamic
    /// override (search-active orange, Novice Network connected/connecting), or the user's own
    /// custom color from Configuration.ToolbarIconColor. This method just draws it.
    /// </param>
    internal static bool Draw(FontAwesomeIcon icon, ToolbarIconPack pack, Vector4 backgroundColor, Vector4 iconColor, string idSuffix = "")
    {
        var size = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton("##glassIcon" + (int)icon + idSuffix, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var drawList = ImGui.GetWindowDrawList();
        var min = pos;
        var max = pos + size;

        if (pack == ToolbarIconPack.FrostedGlass)
        {
            DrawFrosted(drawList, min, max, backgroundColor, hovered, active);
        }
        else
        {
            DrawAero(drawList, min, max, size, backgroundColor, hovered, active);
        }

        ImGui.PushFont(UiBuilder.IconFont);
        var iconText = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconText);
        var iconPos = pos + (size - iconSize) * 0.5f + (active ? new Vector2(0f, 1f) : Vector2.Zero);
        drawList.AddText(iconPos, ImGui.GetColorU32(iconColor), iconText);
        ImGui.PopFont();

        return clicked;
    }

    /// <summary>Lerps color's RGB toward white, and bumps (not multiplies) its alpha - additive so hover/active still give visible feedback even starting from a near-zero idle alpha.</summary>
    private static Vector4 Brighten(Vector4 color, float rgbAmount, float alphaBump)
    {
        var rgb = Vector3.Lerp(new Vector3(color.X, color.Y, color.Z), Vector3.One, rgbAmount);
        return new Vector4(rgb, MathF.Min(1f, color.W + alphaBump));
    }

    /// <summary>Lerps color's RGB toward black, and bumps its alpha - see Brighten's remarks.</summary>
    private static Vector4 Darken(Vector4 color, float rgbAmount, float alphaBump)
    {
        var rgb = Vector3.Lerp(new Vector3(color.X, color.Y, color.Z), Vector3.Zero, rgbAmount);
        return new Vector4(rgb, MathF.Min(1f, color.W + alphaBump));
    }

    /// <summary>Glossy Windows-Vista "Aero" look - the user's tint, brightened on hover and darkened (pressed-in) on active, plus a hard top-half sheen band that reads as glass regardless of the chosen tint.</summary>
    private static void DrawAero(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector2 size, Vector4 backgroundColor, bool hovered, bool active)
    {
        var fillColor = active
            ? Darken(backgroundColor, 0.2f, 0.22f)
            : hovered
                ? Brighten(backgroundColor, 0.15f, 0.12f)
                : backgroundColor;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fillColor), Rounding);

        var glossAlpha = active ? 0.28f : hovered ? 0.45f : 0.32f;
        var glossMin = min + new Vector2(1f, 1f);
        var glossMax = new Vector2(max.X - 1f, min.Y + size.Y * 0.55f);
        drawList.AddRectFilled(glossMin, glossMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, glossAlpha)), Rounding - 1f, ImDrawFlags.RoundCornersTop);

        var borderColor = hovered ? new Vector4(0.75f, 0.9f, 1f, 0.8f) : new Vector4(1f, 1f, 1f, 0.35f);
        drawList.AddRect(min, max, ImGui.GetColorU32(borderColor), Rounding, ImDrawFlags.None, 1f);
    }

    /// <summary>Frosted/transparent glass look - the user's tint as-is at rest (typically set near-invisible), gently brightened on hover/active, plus a soft top-to-bottom fade instead of Aero's hard-edged sheen.</summary>
    private static void DrawFrosted(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 backgroundColor, bool hovered, bool active)
    {
        var fillColor = active
            ? Brighten(backgroundColor, 0.1f, 0.22f)
            : hovered
                ? Brighten(backgroundColor, 0.05f, 0.12f)
                : backgroundColor;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fillColor), Rounding);

        // AddRectFilledMultiColor ignores rounding entirely, so it's inset a couple pixels from
        // the base rect's own bounds - just enough to keep its square corners tucked inside the
        // base's already-rounded silhouette instead of poking out past it.
        var sheenTopAlpha = active ? 0.16f : hovered ? 0.22f : 0.1f;
        var sheenMin = min + new Vector2(1.5f, 1.5f);
        var sheenMax = max - new Vector2(1.5f, 1.5f);
        drawList.AddRectFilledMultiColor(
            sheenMin, sheenMax,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, sheenTopAlpha)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, sheenTopAlpha)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));

        var borderAlpha = active ? 0.55f : hovered ? 0.4f : 0.18f;
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, borderAlpha)), Rounding, ImDrawFlags.None, 1f);
    }
}
