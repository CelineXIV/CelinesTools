using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CelinesRPChat.Windows;

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

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);

        var config = plugin.Configuration;
        var chunks = plugin.CurrentPreviewChunks;

        if (chunks.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.EmptyText"));
            return;
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            ColoredTextRenderer.Draw(chunks[i], config.DefaultTextColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }
}
