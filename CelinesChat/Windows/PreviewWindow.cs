using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CelinesChat.Windows;

internal sealed class PreviewWindow : Window
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

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

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
    }

    public override void Draw()
    {
        // See ChatWindow.Draw's matching remarks - drawing proceeds either way. This window has no
        // cached heights to worry about (it redraws its preview text fresh every frame), so there's
        // nothing further to guard here beyond just letting it draw with whatever font is active.
        using var chatFontScope = plugin.PushChatFont();

        var config = plugin.Configuration;
        var chunks = plugin.CurrentPreviewChunks;

        if (chunks.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.EmptyText"));
            return;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < chunks.Count; i++)
        {
            ColoredTextRenderer.Draw(chunks[i], config.DefaultTextColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, LinkColor, availableWidth);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }
}
