using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CelinesChat.Windows;

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

    // When the "Inside" preview mode is selected, ChatWindow draws the same preview content
    // embedded directly below the compose box instead - this floating window stays fully closed in
    // that mode (regardless of its own manually-toggled IsOpen state) so there's never a duplicate.
    public override bool DrawConditions() => plugin.Configuration.PreviewPosition == PreviewPosition.Window;

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
        var chunks = plugin.CurrentPreviewMappedChunks;

        if (chunks.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.EmptyText"));
            return;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < chunks.Count; i++)
        {
            // Goes through the exact same activation trigger the physical Enter key uses (see
            // Plugin.RequestComposeCursorActivation's own remarks) rather than nudging ChatWindow's
            // fields directly from here - reacting to a click inside THIS separate window only ever
            // won the cross-window focus fight one frame too late, after PreviewWindow itself already
            // claimed it that frame, leaving the compose box positioned correctly but not typable
            // without an extra Enter press or click. The "Inside" preview mode (ChatWindow.cs) doesn't
            // need this at all, since there both live in the same window to begin with.
            PreviewRenderer.DrawMappedChunk(chunks[i], config, availableWidth, plugin.RequestComposeCursorActivation, i * 10000);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Text.Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }
}
