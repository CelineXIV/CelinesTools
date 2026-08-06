using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesChat.Services;

/// <summary>
/// Shared hover-highlight + "drag far enough away to tear off" detection for tab items, used by
/// both the main chat window and the secondary one tabs get torn off into.
/// </summary>
internal static class TabDragHelper
{
    /// <summary>How far (in pixels) a tab has to be dragged vertically away from the tab bar
    /// before it's treated as "tear this off" instead of an in-bar drag to reorder.</summary>
    private const float TearOffThreshold = 50f;

    /// <summary>
    /// Must be called right after <c>ImGui.BeginTabItem</c> returns - not after a conditional
    /// <c>EndTabItem()</c>, which for a non-selected tab never runs at all, and for a selected
    /// one still risks BeginTabItem/EndTabItem's own bookkeeping having moved on from the tab
    /// header item by the time hover is checked.
    /// </summary>
    public static void HandleHoverAndTearOff(Action onTornOff)
    {
        if (ImGui.IsItemHovered())
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.3f, 0.9f)), 3f, ImDrawFlags.None, 2f);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            return;
        }

        if (Math.Abs(ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y) < TearOffThreshold)
        {
            return;
        }

        ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
        onTornOff();
    }
}
