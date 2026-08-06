using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CelinesChat.Windows;

/// <summary>
/// Draws a sender's status icon (Mentor crown, Sprout/New Adventurer, Returner, Role-Playing, ...)
/// right before their name, if their message's sender field carries one - shared between
/// <see cref="ChatWindow"/> and <see cref="SecondaryChatWindow"/>. The game embeds this as an
/// IconPayload directly in the sender field (confirmed against Chat2's own Message.cs, which runs
/// its "Sender" field through the exact same SeString-to-chunks conversion as the message body),
/// not something resolved from a live nearby actor - which is what makes it available for any
/// message regardless of whether the sender is even in the same zone.
/// </summary>
internal static class StatusIconRenderer
{
    /// <summary>
    /// Draws the icon (if any) and calls SameLine after it, ready for the name text to follow
    /// immediately. Returns the width consumed (0 if there was no icon to draw), so callers can
    /// fold it into their own prefix-width tracking for text wrapping.
    /// </summary>
    public static float Draw(Plugin plugin, List<Payload>? senderPayloads)
    {
        var iconPayload = senderPayloads?.OfType<IconPayload>().FirstOrDefault();
        if (iconPayload == null)
        {
            return 0f;
        }

        if (plugin.GetStatusIconInfo(iconPayload.Icon, ImGui.GetTextLineHeight()) is not { } info)
        {
            return 0f;
        }

        ImGui.Image(info.TextureHandle, info.Size, info.Uv0, info.Uv1);
        ImGui.SameLine(0, 3);
        return info.Size.X + 3;
    }
}
