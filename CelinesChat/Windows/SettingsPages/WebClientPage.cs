using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class WebClientPage
{
    private readonly Plugin plugin;
    private string customCodeInput = string.Empty;

    // Masked by default now that this can be a real, freely-typed password rather than just a
    // short throwaway numeric code - a settings window is exactly the kind of thing that ends up
    // visible in a screenshot or stream without anyone thinking twice about it.
    private bool showCode;

    public WebClientPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f)))
        {
            ImGui.TextWrapped(Loc.T("WebClient.WarningHeader"));
        }

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
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("WebClient.PortHint"));
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(120);
        var sessionDays = config.WebClientSessionDays;
        if (ImGui.InputInt(Loc.T("WebClient.SessionDays"), ref sessionDays))
        {
            config.WebClientSessionDays = Math.Clamp(sessionDays, 1, 365);
            plugin.SaveConfiguration();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("WebClient.SessionDaysHint"));
        }

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextUnformatted(Loc.T("WebClient.AuthCodeLabel"));
        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), showCode ? config.WebClientAuthCode : new string('*', config.WebClientAuthCode.Length));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(showCode ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye))
        {
            showCode = !showCode;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(showCode ? "WebClient.HideCode" : "WebClient.ShowCode"));
        }

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

        // No length/character restriction beyond the input buffer itself - the login page's own
        // field no longer caps at 5 numeric digits either (see index.html), so this can be a short
        // numeric code for quick phone-keypad entry or a full free-form password, whichever is
        // wanted. Masked (Password flag) since this field is as sensitive as the value it sets, and
        // may be visible on a shared/streamed screen.
        ImGui.SetNextItemWidth(220);
        var codeReady = customCodeInput.Length > 0;
        if (ImGui.InputTextWithHint("##customWebCode", Loc.T("WebClient.CustomCodeHint"), ref customCodeInput, 100, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue) && codeReady)
        {
            plugin.SetWebClientAuthCode(customCodeInput);
            customCodeInput = string.Empty;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!codeReady))
        {
            if (ImGui.Button(Loc.T("WebClient.ApplyCode")))
            {
                plugin.SetWebClientAuthCode(customCodeInput);
                customCodeInput = string.Empty;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("WebClient.ApplyCodeTooltip"));
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
