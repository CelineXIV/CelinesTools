using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesChat.Services;

/// <summary>
/// Shared hover-highlight + drag-and-drop for tab items, used by both the main chat window and
/// the secondary one tabs get torn off into. Built around Dear ImGui's own drag-and-drop
/// (BeginDragDropSource on each tab, BeginDragDropTarget on each window's whole tab strip) rather
/// than the original raw mouse-delta threshold that acted the instant it was crossed regardless of
/// where the mouse actually was - that moved the tab away mid-drag with no feedback and no regard
/// for which window (if either) the cursor was even over. This gives a floating label that follows
/// the cursor, a highlighted border on whichever window's tab strip is currently under it, and only
/// actually moves the tab once it's dropped there.
/// </summary>
internal static class TabDragHelper
{
    private const string DragDropPayloadId = "CelinesChatTab";

    /// <summary>How far (in pixels, in any direction) the mouse has to move from where the drag started before releasing outside any drop zone still counts as "tear this off" rather than just a click - only relevant for the one bootstrap case (see callers) where no second window exists yet to drop onto.</summary>
    private const float TearOffThreshold = 24f;

    // Which specific tab/whisper item is currently mid-drag, if any - a plain static rather than
    // per-call state since exactly one drag can ever be active at a time regardless of how many
    // tab items call this per frame, and every one of them needs to agree on whose drag it is.
    private static string? draggedItemId;
    private static Vector2 dragStartPos;

    // A release that finds no target claims it *immediately* used to race against every other
    // tab's own DrawReorderTarget/DrawDropZone call this same frame: the loop draws each tab at
    // its own fixed position, so whichever one - the dragged tab's own release check here, or a
    // valid target elsewhere in the strip - happens to run first in that frame's draw order won.
    // Dragging onto a tab drawn *earlier* in the loop (a same-strip drag toward a lower index)
    // meant the target's accept ran first and correctly claimed the drop before this ever saw it.
    // Dragging onto one drawn *later* (toward a higher index) meant this ran first instead, saw
    // nothing had claimed it yet, and incorrectly tore the tab off - exactly the reported
    // right-to-left-works-but-left-to-right-pops-out bug. Deferring the decision to the *next*
    // frame sidesteps the race entirely: by then, every target anywhere (either window) that could
    // have claimed the drop this frame already has, regardless of which one happened to draw
    // first, so checking whether draggedItemId is still set is a reliable answer either way.
    private static string? pendingFallbackItemId;
    private static Vector2 pendingFallbackDropPos;

    /// <summary>
    /// Must be called right after the tab widget (Selectable or BeginTabItem) it applies to.
    /// itemId identifies this specific tab (a ChatTab's Id, or a whisper target name) so a release
    /// only ever resolves for the one actually being dragged. previewLabel is whatever shows in
    /// the small floating box that follows the cursor while dragging - normally just the tab's own
    /// display name. onDroppedWithNoTarget only fires if the tab was dragged far enough and
    /// released somewhere no DrawDropZone call claimed it this same frame - callers should only
    /// actually act on it for the one case where the other window doesn't exist yet to drop onto
    /// (see ChatWindow/SecondaryChatWindow's own remarks on this), otherwise a drop onto empty
    /// space would tear the tab off same as a deliberate drop onto a real target.
    /// </summary>
    public static void HandleHoverAndTearOff(string itemId, string previewLabel, Action<Vector2> onDroppedWithNoTarget)
    {
        // Resolve a fallback decision deferred from last frame's release (see pendingFallbackItemId's
        // remarks) before anything else - draggedItemId being null here means some target elsewhere
        // claimed it since; still matching itemId means nothing did, so it's genuinely unclaimed.
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

        // Don't resolve this same frame - see pendingFallbackItemId's remarks on why that raced
        // with same-frame targets drawn later in the loop. Left set (not cleared to null) so any
        // DrawReorderTarget/DrawDropZone call anywhere later this frame can still see draggedItemId
        // == itemId and claim the drop exactly as before.
        pendingFallbackItemId = itemId;
        pendingFallbackDropPos = ImGui.GetMousePos();
    }

    /// <summary>
    /// Draws an invisible drop target spanning (width, height) at the current cursor position -
    /// call this before drawing the actual tab items on top of it (it already calls
    /// SetItemAllowOverlap so they still get their own clicks/hover despite the overlap - same fix
    /// as the Tabs settings page's reorder/delete buttons needed) - and resets the cursor
    /// afterwards so the real tabs draw at the same starting position. Paints a highlighted border
    /// around itself for as long as a drag is hovering it, and returns the dragged item's id the
    /// instant it's actually dropped here (null otherwise, including every other frame).
    /// </summary>
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

    /// <summary>
    /// Must be called right after a tab item (Selectable), same as HandleHoverAndTearOff - makes
    /// that same tab a reorder target: while another tab is being dragged over it, paints a
    /// vertical "|" bar on whichever half (left/right) of it the cursor is currently over, and
    /// returns the dragged item's id only once actually dropped here (never mid-drag) - reordering
    /// used to happen live the instant a drag crossed a distance threshold, with no preview of
    /// where it'd land; this replaces that with the same "see it, then commit on release" feel as
    /// the cross-window drop zones.
    /// </summary>
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
