using Dalamud.Bindings.ImGui;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal static class StatusIconRenderer
{
    public static float Draw(Plugin plugin, ChatLogEntry entry)
    {
        var iconPayload = entry.SenderIcon;
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
