using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class WebClientPage
{
    private readonly Plugin plugin;

    public WebClientPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
        ImGui.TextWrapped(Loc.T("WebClient.WarningHeader"));
        ImGui.PopStyleColor();
        ImGui.TextWrapped(Loc.T("WebClient.WarningBody"));

        ImGui.Separator();

        var enabled = config.WebClientEnabled;
        if (ImGui.Checkbox(Loc.T("WebClient.Enable"), ref enabled))
        {
            plugin.SetWebClientEnabled(enabled);
        }

        if (!enabled)
        {
            return;
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(120);
        var port = config.WebClientPort;
        if (ImGui.InputInt(Loc.T("WebClient.Port"), ref port))
        {
            config.WebClientPort = Math.Clamp(port, 1024, 65535);
            plugin.SaveConfiguration();
        }

        if (plugin.WebServer.IsRunning && port != config.WebClientPort)
        {
            // Not auto-restarted on every keystroke while editing the field - only takes effect
            // the next time the server actually (re)starts (toggling the checkbox off/on again).
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("WebClient.PortHint"));
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextUnformatted(Loc.T("WebClient.AuthCodeLabel"));
        ImGui.PushFont(UiBuilder.MonoFont);
        ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), config.WebClientAuthCode);
        ImGui.PopFont();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
        {
            ImGui.SetClipboardText(config.WebClientAuthCode);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("WebClient.CopyCode"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Sync))
        {
            plugin.RegenerateWebClientAuthCode();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("WebClient.RegenerateCode"));
        }

        ImGui.Spacing();
        ImGui.Separator();

        var server = plugin.WebServer;
        var isRunning = server.IsRunning;
        ImGui.TextUnformatted(Loc.T("WebClient.StatusLabel"));
        ImGui.SameLine();
        ImGui.TextColored(
            isRunning ? new Vector4(0.4f, 0.85f, 0.4f, 1f) : new Vector4(0.85f, 0.4f, 0.4f, 1f),
            isRunning ? Loc.T("WebClient.StatusRunning") : Loc.T("WebClient.StatusStopped"));

        if (isRunning)
        {
            ImGui.TextUnformatted(Loc.T("WebClient.ConnectedDevices", server.ConnectedClientCount));

            foreach (var url in server.GetDisplayUrls(config.WebClientPort))
            {
                if (ImGui.Selectable(url))
                {
                    ImGui.SetClipboardText(url);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Loc.T("WebClient.CopyUrl"));
                }
            }
        }
        else if (server.LastError != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("WebClient.StartFailed", server.LastError));
        }
    }
}
