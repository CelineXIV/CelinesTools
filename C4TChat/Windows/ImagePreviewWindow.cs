using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using C4TChat.Services;

namespace C4TChat.Windows;

internal sealed class ImagePreviewWindow : Window
{
    private readonly Plugin plugin;
    private string? url;
    private int pushedColors;

    public ImagePreviewWindow(Plugin plugin)
        : base(Loc.T("ImagePreview.Title") + "###c4tchatImagePreview", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
    }

    internal void Open(string imageUrl)
    {
        url = imageUrl;
        IsOpen = true;
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

    public override void Draw()
    {
        if (url == null)
        {
            IsOpen = false;
            return;
        }

        var wrap = plugin.ImagePreviews.Get(url, out var state);
        if (wrap == null)
        {
            ImGui.TextDisabled(Loc.T(state == ImagePreviewService.PreviewState.Failed ? "ImagePreview.Failed" : "Social.Loading"));
        }
        else
        {
            var viewport = ImGui.GetMainViewport().Size * 0.75f;
            var scale = MathF.Min(1f, MathF.Min(viewport.X / wrap.Width, viewport.Y / wrap.Height));
            ImGui.Image(wrap.Handle, new Vector2(wrap.Width, wrap.Height) * scale);
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, Loc.T("ImagePreview.OpenBrowser")))
        {
            plugin.RequestOpenUrl(url);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(url.Length > 70 ? url[..67] + "..." : url);
    }
}
