using System;
using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace CelinesChat.Services;

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

        var asset = choice switch
        {
            ChatFontChoice.NotoSansCjkMedium => DalamudAsset.NotoSansCjkMedium,
            ChatFontChoice.InconsolataRegular => DalamudAsset.InconsolataRegular,
            _ => DalamudAsset.NotoSansCjkRegular,
        };

        fontHandle = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(
            tk => tk.AddDalamudAssetFont(asset, new SafeFontConfig { SizePx = sizePx })));
    }

    // IFontHandle.Push() silently does nothing if the handle isn't Available yet (Dalamud's own
    // docs: "pushes ... if available", leaving whatever font was already active in effect) -
    // callers need to know explicitly when that happened, rather than unknowingly drawing a whole
    // frame with the wrong font's metrics.
    public bool IsAvailable => fontHandle?.Available ?? false;

    public IDisposable? Push() => fontHandle?.Push();

    public void Dispose()
    {
        fontHandle?.Dispose();
        fontHandle = null;
    }
}
