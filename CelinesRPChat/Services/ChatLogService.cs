using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
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

        var playerPayload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();

        var entry = new ChatLogEntry
        {
            Sender = SanitizeSenderName(message.Sender.TextValue),
            SenderWorld = playerPayload?.World.ValueNullable?.Name.ExtractText(),
            Text = message.Message.TextValue,
            ChatType = message.LogKind,
            Timestamp = DateTime.Now,
        };

        if (IsFileLoggingEnabled(message.LogKind))
        {
            AppendToFile(entry);
        }

        // Always buffered regardless of the "show in chat log" setting, which is instead applied
        // at draw time (see ChannelDisplay.IsVisible callers) - so toggling a channel on/off takes
        // effect immediately on already-buffered messages instead of only affecting new arrivals.
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
            var senderPart = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
            var line = $"[{entry.Timestamp:HH:mm:ss}] {ChannelDisplay.Tag(entry.ChatType)} {senderPart}: {entry.Text}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Chatnachricht nicht in Logdatei schreiben.");
        }
    }

    /// <summary>
    /// Strips leading decoration characters (e.g. the ★/circle/heart icons the game prepends when
    /// the sender is tagged with a friend list group marker) that aren't part of the actual
    /// character name. Names always start with a letter, so anything before the first letter is
    /// safe to drop - otherwise it ends up baked into the whisper target (e.g. "★Name@World"),
    /// which the game doesn't recognize as a valid /tell target.
    /// </summary>
    private static string SanitizeSenderName(string rawName)
    {
        var start = 0;
        while (start < rawName.Length && !char.IsLetter(rawName[start]))
        {
            start++;
        }

        return start > 0 ? rawName[start..] : rawName;
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

        var senderPart = afterTag[..colonIndex];
        var text = afterTag[(colonIndex + 1)..].TrimStart();

        string sender;
        string? senderWorld;
        var atIndex = senderPart.IndexOf('@');
        if (atIndex >= 0)
        {
            sender = senderPart[..atIndex];
            senderWorld = senderPart[(atIndex + 1)..];
        }
        else
        {
            sender = senderPart;
            senderWorld = null;
        }

        sender = SanitizeSenderName(sender);

        if (!DateTime.TryParse($"{dateName} {timeText}", out var timestamp))
        {
            timestamp = DateTime.Now;
        }

        return new ChatLogEntry
        {
            Sender = sender,
            SenderWorld = senderWorld,
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
