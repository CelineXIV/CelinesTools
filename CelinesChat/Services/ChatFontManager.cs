using System;
using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace CelinesChat.Services;

/// <summary>
/// Builds (and rebuilds, if the choice/size changes) a custom font for the chat window, using only
/// fonts Dalamud ships with itself - see <see cref="ChatFontChoice"/>'s remarks for why an
/// arbitrary system-font browser (like Chat2's) isn't offered here. Uses the plugin's own
/// <see cref="IDalamudPluginInterface.UiBuilder"/>.FontAtlas (auto-created per plugin, already set
/// to rebuild asynchronously) rather than creating a separate atlas - there's no reason for this
/// plugin to manage its own.
/// </summary>
internal sealed class ChatFontManager : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private IFontHandle? fontHandle;
    private ChatFontChoice currentChoice = ChatFontChoice.Default;
    private float currentSizePx;

    public ChatFontManager(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    /// <summary>
    /// Rebuilds the font handle if the requested choice/size actually changed since last time -
    /// cheap to call every frame (see ChatWindow.Draw), since the common case (nothing changed)
    /// does no work at all.
    /// </summary>
    public void EnsureFont(ChatFontChoice choice, float sizePx)
    {
        if (fontHandle != null && choice == currentChoice && Math.Abs(sizePx - currentSizePx) < 0.1f)
        {
            return;
        }

        fontHandle?.Dispose();
        fontHandle = null;
        currentChoice = choice;
        currentSizePx = sizePx;

        if (choice == ChatFontChoice.Default)
        {
            // No custom handle - callers fall back to whatever ImGui's current default font is.
            return;
        }

        var asset = choice switch
        {
            ChatFontChoice.NotoSansCjkMedium => DalamudAsset.NotoSansCjkMedium,
            ChatFontChoice.InconsolataRegular => DalamudAsset.InconsolataRegular,
            _ => DalamudAsset.NotoSansCjkRegular,
        };

        fontHandle = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(
            tk => tk.AddDalamudAssetFont(asset, new SafeFontConfig { SizePx = sizePx })));
    }

    /// <summary>
    /// Pushes the custom font, if one is configured and ready - null (nothing to pop) otherwise,
    /// which naturally leaves ImGui's current font in place. <see cref="IFontHandle.Push"/> itself
    /// already handles "not built yet" gracefully by keeping the current font, per its own docs.
    /// </summary>
    public IDisposable? Push() => fontHandle?.Push();

    public void Dispose()
    {
        fontHandle?.Dispose();
        fontHandle = null;
    }
}
