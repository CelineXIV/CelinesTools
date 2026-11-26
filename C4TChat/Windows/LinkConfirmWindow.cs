using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace C4TChat.Windows;

internal sealed class LinkConfirmWindow : Window
{
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.3f, 1f);

    private readonly Plugin plugin;
    private string? url;
    private string host = string.Empty;
    private bool trustHost;
    private int pushedColors;
    private int focusFrames;

    public LinkConfirmWindow(Plugin plugin)
        : base(Loc.T("LinkConfirm.Title") + "###c4tchatLinkConfirm", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
    }

    internal static string HostOf(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : string.Empty;

    internal void Open(string link)
    {
        url = link;
        host = HostOf(link);
        trustHost = false;
        var viewport = ImGui.GetMainViewport();
        var center = viewport.Pos + viewport.Size * 0.5f;
        Position = center - new Vector2(220f, 90f) * ImGuiHelpers.GlobalScale;
        PositionCondition = ImGuiCond.Appearing;
        IsOpen = true;
        focusFrames = 4;
        BringToFront();
    }

    public override void PreDraw()
    {
        pushedColors = C4tTheme.PushColors(plugin.Configuration);
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(pushedColors);
        pushedColors = 0;
    }

    public override void OnClose()
    {
        url = null;
    }

    public override void Draw()
    {
        if (url == null)
        {
            IsOpen = false;
            return;
        }

        if (focusFrames > 0)
        {
            focusFrames--;
            ImGui.SetWindowFocus();
        }

        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsAnyMouseDown())
        {
            ImGui.SetWindowFocus();
        }

        var width = 440f * ImGuiHelpers.GlobalScale;
        using (ImRaii.PushColor(ImGuiCol.Text, WarningColor))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T("LinkConfirm.Question"));
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextDisabled(Loc.T("LinkConfirm.Hint"));
        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.T("LinkConfirm.Site"));
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, C4tTheme.ActiveMarkerColor(plugin.Configuration)))
        {
            ImGui.TextUnformatted(host.Length > 0 ? host : "?");
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(url.Length > 300 ? url[..297] + "..." : url);
        }

        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (host.Length > 0)
        {
            ImGui.Checkbox(Loc.T("LinkConfirm.Trust", host), ref trustHost);
        }

        ImGui.Spacing();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, Loc.T("LinkConfirm.Open")))
        {
            if (trustHost && host.Length > 0 && !plugin.Configuration.TrustedLinkHosts.Contains(host))
            {
                plugin.Configuration.TrustedLinkHosts.Add(host);
                plugin.SaveConfiguration();
            }

            ChatWindow.OpenUrl(url, plugin);
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("LinkConfirm.Cancel")) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("LinkConfirm.Copy")))
        {
            ImGui.SetClipboardText(url);
        }
    }
}
