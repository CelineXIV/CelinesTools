using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace CelinesToolkit;

[Serializable]
public class MacroCommandEntry
{
    public string Text { get; set; } = string.Empty;
}

[Serializable]
public class MacroSequence
{
    public string Name { get; set; } = "Neues Makro";
    public List<MacroCommandEntry> Commands { get; set; } = new();

    public bool RunOnLogin { get; set; }
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<MacroSequence> Macros { get; set; } = new();

    public int DelayMs { get; set; } = 600;

    public bool MuteOrchestrion { get; set; }

    public bool ShowPreviewInPenumbra { get; set; }

    public bool QuickBarEnabled { get; set; }

    public string? QuickBarSelectedMacroName { get; set; }
}
