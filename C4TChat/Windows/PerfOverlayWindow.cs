using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using C4TChat.Services;

namespace C4TChat.Windows;

internal sealed class PerfOverlayWindow : Window
{
    private readonly Plugin plugin;

    public PerfOverlayWindow(Plugin plugin)
        : base("C4TChat - " + Loc.T("Perf.Title") + "###celineschatPerf", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        Position = new Vector2(40, 40);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose()
    {
        plugin.Configuration.ShowPerformanceOverlay = false;
        plugin.SaveConfiguration();
    }

    public override void Draw()
    {
        var perf = plugin.Perf;
        DrawSeries(Loc.T("Perf.ChatWindow"), perf.ChatWindow);
        if (plugin.HasSecondaryWindow)
        {
            DrawSeries(Loc.T("Perf.SecondaryWindow"), perf.SecondaryWindow);
        }

        DrawSeries(Loc.T("Perf.Update"), perf.FrameworkUpdate);

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Perf.Messages", plugin.ChatLog.EntryCount, perf.VisibleEntries, perf.CachedHeights));
        ImGui.TextUnformatted(Loc.T("Perf.WebClients", plugin.Configuration.WebClientEnabled ? plugin.WebServer.ConnectedClientCount : 0));

        var frameMs = 1000f / System.Math.Max(1f, ImGui.GetIO().Framerate);
        ImGui.TextDisabled(Loc.T("Perf.GameFrame", ImGui.GetIO().Framerate.ToString("0"), frameMs.ToString("0.0")));
        ImGui.TextDisabled(Loc.T("Perf.Hint"));
    }

    private static void DrawSeries(string label, PerfStats.Series series)
    {
        var avg = series.Average;
        var color = avg switch
        {
            < 1.0 => new Vector4(0.45f, 0.9f, 0.5f, 1f),
            < 3.0 => new Vector4(1f, 0.85f, 0.3f, 1f),
            _ => new Vector4(1f, 0.45f, 0.4f, 1f),
        };

        ImGui.TextColored(color, $"{label}: {avg:0.00} ms");
        ImGui.SameLine();
        ImGui.TextDisabled($"(max {series.Max:0.00} ms)");

        var values = series.Snapshot();
        if (values.Length > 1)
        {
            ImGui.PlotLines("##plot" + label, values, values.Length, string.Empty, 0f, (float)System.Math.Max(2.0, series.Max), new Vector2(220, 28));
        }
    }
}
