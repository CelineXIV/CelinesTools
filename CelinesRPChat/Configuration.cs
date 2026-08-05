using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace CelinesRPChat;

public enum ChatChannel
{
    Say,
    Party,
    Whisper,
    Yell,
    Shout,
    FreeCompany,
    Linkshell,
}

public class Snippet
{
    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public class CharacterState
{
    public ChatChannel LastChannel { get; set; } = ChatChannel.Say;

    public string LastWhisperTarget { get; set; } = string.Empty;

    public List<string> RecentWhisperTargets { get; set; } = new();

    public int LinkshellNumber { get; set; } = 1;

    public Dictionary<string, string> Drafts { get; set; } = new();

    public List<Snippet> Snippets { get; set; } = new();

    public bool SnippetsMigrated { get; set; }
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int MaxMessageLength { get; set; } = 400;

    public int DelayMs { get; set; } = 600;

    public Vector4 DefaultTextColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 EmoteTextColor { get; set; } = new(0.68f, 0.42f, 0.87f, 1f);

    public Vector4 OocTextColor { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    public Vector4 MentionColor { get; set; } = new(1f, 0.85f, 0.2f, 1f);

    public Vector4 ChatLogSenderNameColor { get; set; } = new(0.55f, 0.75f, 1f, 1f);

    public bool ChatLogShowSay { get; set; } = true;

    public bool ChatLogShowParty { get; set; } = true;

    public bool ChatLogShowTell { get; set; } = true;

    public bool ChatLogShowYell { get; set; }

    public bool ChatLogShowShout { get; set; }

    public bool ChatLogShowFreeCompany { get; set; }

    public bool ChatLogShowLinkshell { get; set; }

    public bool FileLogSay { get; set; } = true;

    public bool FileLogParty { get; set; } = true;

    public bool FileLogTell { get; set; } = true;

    public bool FileLogYell { get; set; } = true;

    public bool FileLogShout { get; set; } = true;

    public bool FileLogFreeCompany { get; set; } = true;

    public bool FileLogLinkshell { get; set; } = true;

    public float FontScale { get; set; } = 1f;

    public float WindowOpacity { get; set; } = 1f;

    public float UnfocusedWindowOpacity { get; set; } = 0.35f;

    public float ChatLogBackgroundOpacity { get; set; } = 0.15f;

    public Vector4 SendAccentColor { get; set; } = new(0.16f, 0.45f, 0.24f, 1f);

    // Height in pixels reserved for the compose area (input box + action row) at the bottom of
    // the chat window, dragged via the splitter between it and the chat log above.
    public float ComposeAreaHeight { get; set; } = 130f;

    // Legacy global template list from before templates became per-character. Kept only so
    // existing saved templates survive the update; Plugin.GetCharacterState() copies them into
    // each character once (see CharacterState.SnippetsMigrated) and new templates are saved
    // per-character from then on.
    public List<Snippet> Snippets { get; set; } = new();

    public Dictionary<ulong, CharacterState> Characters { get; set; } = new();
}
