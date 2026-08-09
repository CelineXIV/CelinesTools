using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace CelinesToolkit.Windows.Pages;

internal sealed class AutoAcceptTradePage
{
    private readonly Plugin plugin;
    private string newWhitelistEntry = string.Empty;

    public AutoAcceptTradePage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextWrapped(Loc.T("AutoAcceptTrade.Description"));
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("AutoAcceptTrade.PhaseOneNote"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var enabled = config.AutoAcceptTradeEnabled;
        if (ImGui.Checkbox(Loc.T("AutoAcceptTrade.EnableCheckbox"), ref enabled))
        {
            plugin.SetAutoAcceptTradeEnabled(enabled);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(Loc.T("AutoAcceptTrade.WhitelistHeader"));
        ImGui.Spacing();

        var removeIndex = -1;
        for (var i = 0; i < config.AutoAcceptTradeWhitelist.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.TextUnformatted(config.AutoAcceptTradeWhitelist[i]);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            config.AutoAcceptTradeWhitelist.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##autoAcceptTradeNewEntry", Loc.T("AutoAcceptTrade.WhitelistInputHint"), ref newWhitelistEntry, 100);
        ImGui.SameLine();
        var canAdd = !string.IsNullOrWhiteSpace(newWhitelistEntry) && newWhitelistEntry.Contains('@');
        ImGui.BeginDisabled(!canAdd);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("AutoAcceptTrade.WhitelistAdd")))
        {
            config.AutoAcceptTradeWhitelist.Add(newWhitelistEntry.Trim());
            newWhitelistEntry = string.Empty;
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();
    }
}
