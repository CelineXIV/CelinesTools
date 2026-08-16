using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CelinesChat.Windows;

internal static class StatusIconRenderer
{
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
