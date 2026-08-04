using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace CelinesToolkit.Services;

public sealed class PenumbraPanelIntegration : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly PenumbraIpcService penumbraIpc;
    private readonly PreviewTextureCache textureCache;
    private readonly Configuration configuration;
    private readonly Action<string, float, float> onPreSettingsTabBarDraw;
    private string? cachedModDirectory;

    public PenumbraPanelIntegration(IDalamudPluginInterface pluginInterface, PenumbraIpcService penumbraIpc, PreviewTextureCache textureCache, Configuration configuration)
    {
        this.pluginInterface = pluginInterface;
        this.penumbraIpc = penumbraIpc;
        this.textureCache = textureCache;
        this.configuration = configuration;
        onPreSettingsTabBarDraw = OnPreSettingsTabBarDraw;

        try
        {
            this.pluginInterface.GetIpcSubscriber<string, float, float, object>("Penumbra.PreSettingsTabBarDraw").Subscribe(onPreSettingsTabBarDraw);
        }
        catch
        {
        }
    }

    private void OnPreSettingsTabBarDraw(string modFolderName, float width, float titleWidth)
    {
        if (!configuration.ShowPreviewInPenumbra || string.IsNullOrEmpty(modFolderName))
        {
            return;
        }

        cachedModDirectory ??= penumbraIpc.GetModDirectory();
        if (cachedModDirectory == null)
        {
            return;
        }

        var modFolder = Path.Combine(cachedModDirectory, modFolderName);
        if (!Directory.Exists(modFolder))
        {
            return;
        }

        var previewPath = ModPreviewScanner.FindPreviewImage(modFolder);
        if (previewPath == null)
        {
            return;
        }

        var texture = textureCache.GetOrLoad(previewPath);
        if (texture == null || texture.Width <= 0 || texture.Height <= 0)
        {
            return;
        }

        var maxHeight = width * 0.4f;
        var scale = Math.Min(width / texture.Width, maxHeight / texture.Height);
        var size = new Vector2(texture.Width * scale, texture.Height * scale);
        var offsetX = (width - size.X) / 2f;
        if (offsetX > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
        }

        ImGui.Image(texture.Handle, size);
        ImGui.Spacing();
    }

    public void Dispose()
    {
        try
        {
            pluginInterface.GetIpcSubscriber<string, float, float, object>("Penumbra.PreSettingsTabBarDraw").Unsubscribe(onPreSettingsTabBarDraw);
        }
        catch
        {
        }
    }
}
