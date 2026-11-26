using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace C4TChat.Windows;

internal sealed class PreviewWindow : Window
{
    private readonly Plugin plugin;

    public PreviewWindow(Plugin plugin) : base(WindowTitles.Preview)
    {
        this.plugin = plugin;
        Size = new Vector2(420, 260);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 120),
            MaximumSize = new Vector2(2000, 2000),
        };
    }

    public override bool DrawConditions() => plugin.Configuration.PreviewPosition == PreviewPosition.Window;

    private int c4tColorsPushed;
    private float currentOpacity = 1f;

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
        currentOpacity = opacity;
        c4tColorsPushed = C4tTheme.PushColors(plugin.Configuration);
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(c4tColorsPushed);
        c4tColorsPushed = 0;
    }

    public override void Draw()
    {
        C4tTheme.DrawDecorations(plugin.Configuration, currentOpacity);

        using var chatFontScope = plugin.PushChatFont();

        var config = plugin.Configuration;
        var chunks = plugin.CurrentPreviewMappedChunks;

        if (chunks.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.EmptyText"));
            return;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < chunks.Count; i++)
        {
            PreviewRenderer.DrawMappedChunk(chunks[i], config, availableWidth, plugin.RequestComposeCursorActivation, i * 10000);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Text.Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }
}
