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

[Serializable]
public class GroupPoseCountdownTrigger
{
    public string Codeword { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<MacroSequence> Macros { get; set; } = new();

    public int DelayMs { get; set; } = 600;

    /// <summary>Width of MainWindow's left nav column in pixels - user-draggable via the splitter between it and the page content.</summary>
    public float ToolkitNavWidth { get; set; } = 180f;

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

    /// <summary>Off by default - automatically accepts trades with whitelisted partners once they've put items in (see TradeAutoAcceptService).</summary>
    public bool AutoAcceptTradeEnabled { get; set; }

    /// <summary>Each entry is "Name@World" - only trades with a whitelisted partner are ever auto-accepted.</summary>
    public List<string> AutoAcceptTradeWhitelist { get; set; } = new();

    /// <summary>Off by default - watches all chat for configured codewords and runs the matching command (e.g. an emote) when one appears, from any sender in any channel - for synchronizing group RP poses.</summary>
    public bool GroupPoseCountdownEnabled { get; set; }

    public List<GroupPoseCountdownTrigger> GroupPoseCountdownTriggers { get; set; } = new();
}
