using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesChat.Services;

internal static class TabDragHelper
{
    private const string DragDropPayloadId = "CelinesChatTab";

    private const float TearOffThreshold = 24f;

    private static string? draggedItemId;
    private static Vector2 dragStartPos;

    private static string? pendingFallbackItemId;
    private static Vector2 pendingFallbackDropPos;

    public static void HandleHoverAndTearOff(string itemId, string previewLabel, Action<Vector2> onDroppedWithNoTarget)
    {
        if (pendingFallbackItemId == itemId)
        {
            pendingFallbackItemId = null;

            if (draggedItemId == itemId)
            {
                draggedItemId = null;
                var distance = Vector2.Distance(dragStartPos, pendingFallbackDropPos);
                if (distance >= TearOffThreshold)
                {
                    onDroppedWithNoTarget(pendingFallbackDropPos);
                }
            }
        }

        if (ImGui.IsItemHovered() && draggedItemId == null)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.3f, 0.9f)), 3f, ImDrawFlags.None, 2f);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
        {
            if (draggedItemId != itemId)
            {
                draggedItemId = itemId;
                dragStartPos = ImGui.GetMousePos();
            }

            ImGui.SetDragDropPayload(DragDropPayloadId, ReadOnlySpan<byte>.Empty);
            ImGui.TextUnformatted(previewLabel);
            ImGui.EndDragDropSource();
            return;
        }

        if (draggedItemId != itemId || !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return;
        }

        pendingFallbackItemId = itemId;
        pendingFallbackDropPos = ImGui.GetMousePos();
    }

    public static string? DrawDropZone(float width, float height)
    {
        var cursorBefore = ImGui.GetCursorPos();
        ImGui.InvisibleButton("##tabDropZone", new Vector2(MathF.Max(1f, width), MathF.Max(1f, height)));
        ImGui.SetItemAllowOverlap();

        string? dropped = null;
        if (draggedItemId != null && ImGui.BeginDragDropTarget())
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(0.3f, 0.75f, 1f, 0.95f)), 4f, ImDrawFlags.None, 3f);

            var payload = ImGui.AcceptDragDropPayload(DragDropPayloadId);
            if (!payload.IsNull)
            {
                dropped = draggedItemId;
                draggedItemId = null;
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.SetCursorPos(cursorBefore);
        return dropped;
    }

    public static string? DrawReorderTarget(string ownItemId, out bool insertBefore)
    {
        insertBefore = false;

        if (draggedItemId == null || draggedItemId == ownItemId || !ImGui.BeginDragDropTarget())
        {
            return null;
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        insertBefore = ImGui.GetMousePos().X < (min.X + max.X) / 2f;

        var lineX = insertBefore ? min.X : max.X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, min.Y), new Vector2(lineX, max.Y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), 3f);

        string? dropped = null;
        var payload = ImGui.AcceptDragDropPayload(DragDropPayloadId);
        if (!payload.IsNull)
        {
            dropped = draggedItemId;
            draggedItemId = null;
        }

        ImGui.EndDragDropTarget();
        return dropped;
    }
}
