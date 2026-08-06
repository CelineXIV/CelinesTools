using Dalamud.Bindings.ImGui;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class FontsPage
{
    private readonly Plugin plugin;

    public FontsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped(Loc.T("Fonts.Description"));
        ImGui.Spacing();

        ImGui.SetNextItemWidth(220);
        var choiceIndex = (int)config.ChatFont;
        if (ImGui.Combo("##fontChoice", ref choiceIndex, FontChoiceLabels, FontChoiceLabels.Length))
        {
            config.ChatFont = (ChatFontChoice)choiceIndex;
            plugin.SaveConfiguration();
        }

        if (config.ChatFont != ChatFontChoice.Default)
        {
            ImGui.SetNextItemWidth(150);
            var sizePx = config.ChatFontSizePx;
            if (ImGui.SliderFloat(Loc.T("Fonts.SizeLabel"), ref sizePx, 10f, 28f, "%.0fpx"))
            {
                config.ChatFontSizePx = sizePx;
                plugin.SaveConfiguration();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Fonts.PreviewLabel"));

        using (plugin.PushChatFont())
        {
            ImGui.TextUnformatted(Loc.T("Fonts.PreviewText"));
        }
    }

    private static readonly string[] FontChoiceLabels =
    {
        "Default",
        "Dalamud: NotoSansCjkRegular",
        "Dalamud: NotoSansCjkMedium",
        "Dalamud: InconsolataRegular (monospace)",
    };
}
