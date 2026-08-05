using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace CelinesToolkit.Windows.Pages;

internal sealed class CommandToolPage
{
    private readonly Plugin plugin;
    private int selectedIndex = -1;

    public CommandToolPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.TextUnformatted(Loc.T("Delay.Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        var delay = config.DelayMs;
        if (ImGui.InputInt("##delay", ref delay, 50, 200))
        {
            config.DelayMs = Math.Max(0, delay);
            plugin.SaveConfiguration();
        }

        ImGui.Separator();

        DrawMacroList(config);
        ImGui.SameLine();
        DrawMacroDetails(config);
    }

    private void DrawMacroList(Configuration config)
    {
        ImGui.BeginGroup();

        ImGui.BeginChild("##macroList", new Vector2(180, -30), true);
        for (var i = 0; i < config.Macros.Count; i++)
        {
            var macro = config.Macros[i];
            if (ImGui.Selectable($"{macro.Name}##macro{i}", i == selectedIndex))
            {
                selectedIndex = i;
            }
        }
        ImGui.EndChild();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("Macro.New")))
        {
            config.Macros.Add(new MacroSequence { Name = Loc.T("Macro.NewDefaultName", config.Macros.Count + 1) });
            selectedIndex = config.Macros.Count - 1;
            plugin.SaveConfiguration();
        }

        ImGui.EndGroup();
    }

    private void DrawMacroDetails(Configuration config)
    {
        ImGui.BeginChild("##macroDetails", Vector2.Zero, false);

        if (selectedIndex < 0 || selectedIndex >= config.Macros.Count)
        {
            ImGui.TextUnformatted(Loc.T("Macro.SelectHint"));
            ImGui.EndChild();
            return;
        }

        var macro = config.Macros[selectedIndex];

        ImGui.SetNextItemWidth(250);
        var name = macro.Name;
        if (ImGui.InputText(Loc.T("Macro.Name") + "##macroName", ref name, 100))
        {
            macro.Name = name;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, Loc.T("Macro.Run")))
        {
            plugin.RunMacro(macro);
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.TrashAlt, Loc.T("Macro.Delete")))
        {
            config.Macros.RemoveAt(selectedIndex);
            selectedIndex = -1;
            plugin.SaveConfiguration();
            ImGui.EndChild();
            return;
        }

        var runOnLogin = macro.RunOnLogin;
        if (ImGui.Checkbox(Loc.T("Macro.RunOnLogin") + "##runOnLogin", ref runOnLogin))
        {
            macro.RunOnLogin = runOnLogin;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Macro.CommandsHeader"));
        ImGui.Spacing();

        var removeIndex = -1;

        for (var i = 0; i < macro.Commands.Count; i++)
        {
            ImGui.PushID(i);

            ImGui.SetNextItemWidth(350);
            var cmdText = macro.Commands[i].Text;
            if (ImGui.InputText("##cmd", ref cmdText, 500))
            {
                macro.Commands[i].Text = cmdText;
                plugin.SaveConfiguration();
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronUp) && i > 0)
            {
                (macro.Commands[i - 1], macro.Commands[i]) = (macro.Commands[i], macro.Commands[i - 1]);
                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Macro.Up"));
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronDown) && i < macro.Commands.Count - 1)
            {
                (macro.Commands[i + 1], macro.Commands[i]) = (macro.Commands[i], macro.Commands[i + 1]);
                plugin.SaveConfiguration();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Macro.Down"));
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Macro.Remove"));
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            macro.Commands.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Loc.T("Macro.AddCommand")))
        {
            macro.Commands.Add(new MacroCommandEntry());
            plugin.SaveConfiguration();
        }

        ImGui.EndChild();
    }
}
