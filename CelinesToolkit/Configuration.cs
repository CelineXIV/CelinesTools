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

    /// <summary>Off by default - opens a separate window that live-mirrors Glamourer's own design list, letting you attach a preview image per design and apply it to yourself.</summary>
    public bool GlamourerPreviewEnabled { get; set; }

    /// <summary>Off by default - shows overlay(s) marking the slidecast window near the end of a cast.</summary>
    public bool SlidecastEnabled { get; set; }

    public bool SlidecastShowCastBar { get; set; } = true;

    public bool SlidecastShowCursorCircle { get; set; }

    /// <summary>How much time (in ms) before a cast finishes counts as safe to start moving - the server locks the action in slightly before the visual bar hits zero.</summary>
    public float SlidecastThresholdMs { get; set; } = 500f;
}
