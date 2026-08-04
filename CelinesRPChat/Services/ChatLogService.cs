using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CelinesRPChat.Services;

internal sealed class ChatLogService : IDisposable
{
    private const int MaxEntries = 200;

    private readonly IChatGui chatGui;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly List<ChatLogEntry> entries = new();

    public string LogsFolderPath { get; }

    public IReadOnlyList<ChatLogEntry> Entries => entries;

    public ChatLogService(IChatGui chatGui, Configuration configuration, IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.configuration = configuration;
        this.log = log;

        LogsFolderPath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "Logs");
        try
        {
            Directory.CreateDirectory(LogsFolderPath);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Log-Ordner nicht anlegen.");
        }

        this.chatGui.ChatMessage += OnChatMessage;
    }

    public void Clear() => entries.Clear();

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!IsKnownChannel(message.LogKind))
        {
            return;
        }

        var entry = new ChatLogEntry
        {
            Sender = message.Sender.TextValue,
            Text = message.Message.TextValue,
            ChatType = message.LogKind,
            Timestamp = DateTime.Now,
        };

        if (IsFileLoggingEnabled(message.LogKind))
        {
            AppendToFile(entry);
        }

        if (!IsDisplayEnabled(message.LogKind))
        {
            return;
        }

        entries.Add(entry);
        while (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }
    }

    private void AppendToFile(ChatLogEntry entry)
    {
        try
        {
            var fileName = entry.Timestamp.ToString("yyyy-MM-dd") + ".txt";
            var path = Path.Combine(LogsFolderPath, fileName);
            var line = $"[{entry.Timestamp:HH:mm:ss}] {ChannelDisplay.Tag(entry.ChatType)} {entry.Sender}: {entry.Text}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Chatnachricht nicht in Logdatei schreiben.");
        }
    }

    private static bool IsKnownChannel(XivChatType type) => type switch
    {
        XivChatType.Say => true,
        XivChatType.Party => true,
        XivChatType.TellIncoming => true,
        XivChatType.TellOutgoing => true,
        XivChatType.Yell => true,
        XivChatType.Shout => true,
        XivChatType.FreeCompany => true,
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => true,
        >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8 => true,
        _ => false,
    };

    private bool IsFileLoggingEnabled(XivChatType type) => type switch
    {
        XivChatType.Say => configuration.FileLogSay,
        XivChatType.Party => configuration.FileLogParty,
        XivChatType.TellIncoming => configuration.FileLogTell,
        XivChatType.TellOutgoing => configuration.FileLogTell,
        XivChatType.Yell => configuration.FileLogYell,
        XivChatType.Shout => configuration.FileLogShout,
        XivChatType.FreeCompany => configuration.FileLogFreeCompany,
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => configuration.FileLogLinkshell,
        >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8 => configuration.FileLogLinkshell,
        _ => false,
    };

    private bool IsDisplayEnabled(XivChatType type) => ChannelDisplay.IsVisible(type, configuration);

    public List<string> GetAvailableLogDates()
    {
        var dates = new List<string>();
        if (!Directory.Exists(LogsFolderPath))
        {
            return dates;
        }

        foreach (var file in Directory.GetFiles(LogsFolderPath, "*.txt"))
        {
            dates.Add(Path.GetFileNameWithoutExtension(file));
        }

        dates.Sort((a, b) => string.CompareOrdinal(b, a));
        return dates;
    }

    public List<ChatLogEntry> LoadHistoryFile(string dateName)
    {
        var result = new List<ChatLogEntry>();
        var path = Path.Combine(LogsFolderPath, dateName + ".txt");

        try
        {
            if (!File.Exists(path))
            {
                return result;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var entry = ParseLine(dateName, line);
                if (entry != null)
                {
                    result.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Logdatei nicht laden.");
        }

        return result;
    }

    private static ChatLogEntry? ParseLine(string dateName, string line)
    {
        if (line.Length == 0 || line[0] != '[')
        {
            return null;
        }

        var timeEnd = line.IndexOf(']');
        if (timeEnd < 0)
        {
            return null;
        }

        var timeText = line[1..timeEnd];
        var rest = line[(timeEnd + 1)..].TrimStart();
        if (rest.Length == 0 || rest[0] != '[')
        {
            return null;
        }

        var tagEnd = rest.IndexOf(']');
        if (tagEnd < 0)
        {
            return null;
        }

        var tag = rest[..(tagEnd + 1)];
        var afterTag = rest[(tagEnd + 1)..].TrimStart();
        var colonIndex = afterTag.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        var sender = afterTag[..colonIndex];
        var text = afterTag[(colonIndex + 1)..].TrimStart();

        if (!DateTime.TryParse($"{dateName} {timeText}", out var timestamp))
        {
            timestamp = DateTime.Now;
        }

        return new ChatLogEntry
        {
            Sender = sender,
            Text = text,
            ChatType = ChannelDisplay.ParseTag(tag),
            Timestamp = timestamp,
        };
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
    }
}
