using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace C4TChat.Services;

internal sealed class ChatLogService : IDisposable
{
    private const int MaxEntries = 3000;

    private readonly IChatGui chatGui;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    private readonly WhisperFailureDetector whisperFailureDetector;
    private readonly ChatLogFileWriter fileWriter;
    private readonly List<ChatLogEntry> entries = new();

    private readonly Dictionary<ChatCategory, int> entryCountByCategory = new();
    private readonly Hook<RaptureLogModule.Delegates.AddMsgSourceEntry>? contentIdHook;
    private long nextSequence = 1;

    private readonly object entriesLock = new();

    private ChatLogEntry? lastDispatchedEntry;

    public string LogsFolderPath { get; }

    public IReadOnlyList<ChatLogEntry> Entries => entries;

    public unsafe ChatLogService(IChatGui chatGui, Configuration configuration, IDalamudPluginInterface pluginInterface, IPluginLog log, IGameInteropProvider gameInteropProvider, IPlayerState playerState, WhisperFailureDetector whisperFailureDetector)
    {
        this.chatGui = chatGui;
        this.configuration = configuration;
        this.log = log;
        this.playerState = playerState;
        this.whisperFailureDetector = whisperFailureDetector;
        fileWriter = new ChatLogFileWriter(log);

        LogsFolderPath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "Logs");
        try
        {
            Directory.CreateDirectory(LogsFolderPath);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Log-Ordner nicht anlegen.");
        }

        if (configuration.PreloadRecentHistoryOnStartup)
        {
            LoadRecentHistory();
        }

        this.chatGui.ChatMessage += OnChatMessage;

        try
        {
            contentIdHook = gameInteropProvider.HookFromAddress<RaptureLogModule.Delegates.AddMsgSourceEntry>(
                (nint)RaptureLogModule.MemberFunctionPointers.AddMsgSourceEntry,
                OnAddMsgSourceEntry);
            contentIdHook.Enable();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Konnte AddMsgSourceEntry-Hook nicht einrichten - Cross-World-Einladung und Mute-Liste bleiben ohne Content-ID deaktiviert.");
        }
    }

    private void LoadRecentHistory()
    {
        const int maxHistoryEntries = MaxEntries;
        const int maxDaysBack = 30;

        try
        {
            var dates = GetAvailableLogDates();
            var loaded = new List<ChatLogEntry>();

            foreach (var date in dates.Take(maxDaysBack))
            {
                var dayEntries = LoadHistoryFile(date);
                loaded.InsertRange(0, dayEntries);

                if (loaded.Count >= maxHistoryEntries)
                {
                    break;
                }
            }

            if (loaded.Count > maxHistoryEntries)
            {
                loaded = loaded.GetRange(loaded.Count - maxHistoryEntries, maxHistoryEntries);
            }

            lock (entriesLock)
            {
                foreach (var entry in loaded)
                {
                    entry.Sequence = nextSequence++;
                    AddEntryLocked(entry);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Konnte letzte Chat-Historie nicht laden.");
        }
    }

    public void Clear()
    {
        lock (entriesLock)
        {
            entries.Clear();
            entryCountByCategory.Clear();
        }
    }

    internal int EntryCount
    {
        get
        {
            lock (entriesLock)
            {
                return entries.Count;
            }
        }
    }

    internal List<ChatLogEntry> Snapshot()
    {
        lock (entriesLock)
        {
            return new List<ChatLogEntry>(entries);
        }
    }

    internal void CopyEntriesTo(List<ChatLogEntry> target)
    {
        lock (entriesLock)
        {
            target.Clear();
            target.AddRange(entries);
        }
    }

    internal List<ChatLogEntry> SnapshotSince(long sequence)
    {
        lock (entriesLock)
        {
            return entries.Where(e => e.Sequence > sequence).ToList();
        }
    }

    internal ChatLogEntry? FindBySequence(long sequence)
    {
        lock (entriesLock)
        {
            return entries.FirstOrDefault(e => e.Sequence == sequence);
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!IsKnownChannel(message.LogKind))
        {
            lastDispatchedEntry = null;
            return;
        }

        try
        {
            ProcessChatMessage(message);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Konnte Chatnachricht nicht verarbeiten.");
        }
    }

    private void ProcessChatMessage(IHandleableChatMessage message)
    {
        var playerPayload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();

        var entry = new ChatLogEntry
        {
            Sender = playerPayload != null ? playerPayload.PlayerName : SanitizeSenderName(message.Sender.TextValue),
            SenderWorld = playerPayload?.World.ValueNullable?.Name.ExtractText(),
            Text = message.Message.TextValue,
            ChatType = message.LogKind,
            Timestamp = DateTime.Now,
            Sequence = nextSequence++,
            Payloads = message.Message.Payloads.ToList(),
            SenderPayloads = message.Sender.Payloads.ToList(),
        };

        if (entry.Payloads?.Any(p => p is ItemPayload) == true)
        {
            var dump = string.Join(" | ", entry.Payloads.Select(p => p switch
            {
                UIForegroundPayload fg => $"UIForeground(enabled={fg.IsEnabled}, colorKey={fg.ColorKey}, rgba={(fg.IsEnabled ? fg.RGBA.ToString("X8") : "n/a")})",
                ItemPayload item => $"Item(id={item.RawItemId}, hq={item.IsHQ})",
                RawPayload raw when raw.Equals(RawPayload.LinkTerminator) => "LinkTerminator",
                TextPayload text => $"Text(\"{text.Text}\")",
                _ => p.GetType().Name,
            }));

            log.Verbose("[C4TChat][ColorDiag] item-link message payloads: {Dump}", dump);
        }

        if (message.LogKind == XivChatType.NoviceNetworkSystem)
        {
            NoviceNetworkService.ObserveSystemMessage(entry.Text);
        }

        if (message.LogKind == XivChatType.ErrorMessage)
        {
            entry.RelatedWhisperTarget = whisperFailureDetector.TryExtractTarget(entry.Text);
        }

        if (message.LogKind is XivChatType.TellIncoming or XivChatType.TellOutgoing)
        {
            AppendToWhisperFile(entry);
        }
        else if (IsFileLoggingEnabled(message.LogKind))
        {
            AppendToFile(entry);
        }

        lock (entriesLock)
        {
            AddEntryLocked(entry);
            while (entries.Count > MaxEntries)
            {
                RemoveOldestFromLargestCategory();
            }
        }

        lastDispatchedEntry = entry;

        NotifyIfNeeded(entry);

        EntryAdded?.Invoke(entry);
    }

    internal event Action<ChatLogEntry>? EntryAdded;

    internal event Action<ChatLogEntry>? SenderIdentified;

    private void AddEntryLocked(ChatLogEntry entry)
    {
        entries.Add(entry);
        if (ChannelDisplay.CategoryOf(entry.ChatType) is { } category)
        {
            entryCountByCategory[category] = entryCountByCategory.GetValueOrDefault(category) + 1;
        }
    }

    private void RemoveEntryAtLocked(int index)
    {
        if (ChannelDisplay.CategoryOf(entries[index].ChatType) is { } category
            && entryCountByCategory.TryGetValue(category, out var count))
        {
            if (count <= 1)
            {
                entryCountByCategory.Remove(category);
            }
            else
            {
                entryCountByCategory[category] = count - 1;
            }
        }

        entries.RemoveAt(index);
    }

    private void RemoveOldestFromLargestCategory()
    {
        ChatCategory? largestCategory = null;
        var largestCount = 0;
        foreach (var (category, count) in entryCountByCategory)
        {
            if (count > largestCount)
            {
                largestCategory = category;
                largestCount = count;
            }
        }

        if (largestCategory is { } largest)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (ChannelDisplay.CategoryOf(entries[i].ChatType) == largest)
                {
                    RemoveEntryAtLocked(i);
                    return;
                }
            }
        }

        RemoveEntryAtLocked(0);
    }

    private unsafe void NotifyIfNeeded(ChatLogEntry entry)
    {
        if (MessageFilter.IsFilteredOut(entry, configuration, playerState.IsLoaded ? playerState.CharacterName : null))
        {
            return;
        }

        var isIncomingWhisper = entry.ChatType == XivChatType.TellIncoming;
        var isMention = !isIncomingWhisper && IsSelfMentioned(entry);
        entry.NotifyKind = isIncomingWhisper ? 1 : isMention ? 2 : 0;

        if ((isIncomingWhisper && configuration.FlashTaskbarOnWhisper) || (isMention && configuration.FlashTaskbarOnMention))
        {
            TaskbarFlasher.FlashIfInBackground();
        }

        if (!configuration.PlaySounds)
        {
            return;
        }

        if (configuration.WhisperSoundEnabled && isIncomingWhisper)
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(configuration.WhisperSoundEffect, 1, 16));
            return;
        }

        if (configuration.MentionSoundEnabled && isMention)
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(configuration.MentionSoundEffect, 1, 16));
            return;
        }

        if (!IsOwnMessage(entry)
            && ChannelDisplay.CategoryOf(entry.ChatType) is { } category
            && configuration.ChannelSounds.TryGetValue(category, out var channelSound)
            && channelSound.Enabled)
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(channelSound.SoundEffect, 1, 16));
        }
    }

    private bool IsOwnMessage(ChatLogEntry entry)
    {
        return playerState.IsLoaded
            && !string.IsNullOrEmpty(playerState.CharacterName)
            && string.Equals(entry.Sender, playerState.CharacterName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSelfMentioned(ChatLogEntry entry)
    {
        if (entry.ChatType == XivChatType.TellOutgoing || IsOwnMessage(entry))
        {
            return false;
        }

        var firstName = string.Empty;
        if (playerState.IsLoaded && !string.IsNullOrEmpty(playerState.CharacterName))
        {
            var spaceIndex = playerState.CharacterName.IndexOf(' ');
            firstName = spaceIndex > 0 ? playerState.CharacterName[..spaceIndex] : playerState.CharacterName;
        }

        return MentionMatcher.TextMentions(entry.Text, MentionMatcher.BuildTerms(firstName, configuration.MentionKeywords));
    }

    private unsafe void OnAddMsgSourceEntry(RaptureLogModule* module, ulong contentId, ulong accountId, int messageIndex, ushort worldId, ushort chatType)
    {
        contentIdHook?.Original(module, contentId, accountId, messageIndex, worldId, chatType);

        try
        {
            if (lastDispatchedEntry != null)
            {
                lastDispatchedEntry.ContentId = contentId;
                lastDispatchedEntry.AccountId = accountId;
                lastDispatchedEntry.ContentIdKnown = true;

                if (contentId != 0 && !string.IsNullOrEmpty(lastDispatchedEntry.Sender))
                {
                    SenderIdentified?.Invoke(lastDispatchedEntry);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Fehler im AddMsgSourceEntry-Hook.");
        }
    }

    private void AppendToFile(ChatLogEntry entry)
    {
        var fileName = entry.Timestamp.ToString("yyyy-MM-dd") + ".txt";
        var path = Path.Combine(LogsFolderPath, fileName);
        var senderPart = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        fileWriter.Enqueue(path, $"[{entry.Timestamp:HH:mm:ss}] {ChannelDisplay.Tag(entry.ChatType)} {senderPart}: {SingleLine(entry.Text)}");
    }

    private static string SingleLine(string text) =>
        text.Contains('\n') || text.Contains('\r') ? text.Replace("\r", " ").Replace("\n", " ") : text;

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private const string WhispersSubfolder = "Whispers";

    private string GetWhisperLogPath(string target) =>
        Path.Combine(LogsFolderPath, WhispersSubfolder, SanitizeForFileName(target) + ".txt");

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string SanitizeForFileName(string name)
    {
        foreach (var invalid in InvalidFileNameChars)
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private void AppendToWhisperFile(ChatLogEntry entry)
    {
        var senderPart = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var path = GetWhisperLogPath(senderPart);
        fileWriter.Enqueue(path, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {ChannelDisplay.Tag(entry.ChatType)} {senderPart}: {SingleLine(entry.Text)}");
    }

    public List<string> GetWhisperLogTargets()
    {
        var folder = Path.Combine(LogsFolderPath, WhispersSubfolder);
        var targets = new List<string>();
        if (!Directory.Exists(folder))
        {
            return targets;
        }

        var allFileTargets = new List<string>();
        foreach (var file in Directory.GetFiles(folder, "*.txt"))
        {
            allFileTargets.Add(Path.GetFileNameWithoutExtension(file));
        }

        var worldedCountByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileTarget in allFileTargets)
        {
            if (fileTarget.Contains('@'))
            {
                var nameOnly = fileTarget.Split('@')[0];
                worldedCountByName[nameOnly] = worldedCountByName.GetValueOrDefault(nameOnly) + 1;
            }
        }

        foreach (var fileTarget in allFileTargets)
        {
            if (!fileTarget.Contains('@') && worldedCountByName.GetValueOrDefault(fileTarget) == 1)
            {
                continue;
            }

            targets.Add(fileTarget);
        }

        targets.Sort(StringComparer.OrdinalIgnoreCase);
        return targets;
    }

    public List<ChatLogEntry> LoadWhisperHistory(string target)
    {
        var result = new List<ChatLogEntry>();
        var folder = Path.Combine(LogsFolderPath, WhispersSubfolder);

        fileWriter.Flush();

        if (!Directory.Exists(folder))
        {
            return result;
        }

        var exactFileName = SanitizeForFileName(target);
        var files = new List<string> { Path.Combine(folder, exactFileName + ".txt") };

        if (target.Contains('@'))
        {
            var nameOnly = target.Split('@')[0];
            var otherWorldFileExists = Directory.GetFiles(folder, SanitizeForFileName(nameOnly) + "@*.txt")
                .Any(f => !string.Equals(Path.GetFileNameWithoutExtension(f), exactFileName, StringComparison.OrdinalIgnoreCase));

            if (!otherWorldFileExists)
            {
                files.Add(Path.Combine(folder, SanitizeForFileName(nameOnly) + ".txt"));
            }
        }

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                foreach (var line in ReadLinesShared(file))
                {
                    var entry = ParseWhisperLine(line);
                    if (entry != null)
                    {
                        result.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Konnte Whisper-Logdatei nicht laden.");
            }
        }

        return result.OrderBy(e => e.Timestamp).ToList();
    }

    internal sealed class SearchProgress
    {
        public int FilesTotal;
        public int FilesDone;
        public int Hits;
    }

    public List<ChatLogEntry> SearchAllLogs(string term, int maxResults = 2000, SearchProgress? progress = null, CancellationToken cancellation = default)
    {
        var results = new List<ChatLogEntry>();
        if (string.IsNullOrWhiteSpace(term))
        {
            return results;
        }

        fileWriter.Flush();

        bool Matches(ChatLogEntry entry) =>
            entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Sender.Contains(term, StringComparison.OrdinalIgnoreCase);

        var jobs = new List<(string? Date, string? Path)>();
        var onDisk = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (Directory.Exists(LogsFolderPath))
            {
                foreach (var file in Directory.GetFiles(LogsFolderPath, "*.txt"))
                {
                    var date = Path.GetFileNameWithoutExtension(file);
                    onDisk.Add(date);
                    jobs.Add((date, file));
                }
            }

            foreach (var date in LogArchive.ListArchivedDates(LogsFolderPath))
            {
                if (!onDisk.Contains(date))
                {
                    jobs.Add((date, null));
                }
            }

            var whisperFolder = Path.Combine(LogsFolderPath, WhispersSubfolder);
            if (Directory.Exists(whisperFolder))
            {
                foreach (var file in Directory.GetFiles(whisperFolder, "*.txt"))
                {
                    jobs.Add((null, file));
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Fehler bei der Suche in den Log-Dateien.");
        }

        if (progress != null)
        {
            progress.FilesTotal = jobs.Count;
        }

        var found = new System.Collections.Concurrent.ConcurrentBag<ChatLogEntry>();
        try
        {
            Parallel.ForEach(
                jobs,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = cancellation },
                job =>
                {
                    try
                    {
                        IEnumerable<string> lines = job.Path != null
                            ? ReadLinesShared(job.Path)
                            : (IEnumerable<string>?)LogArchive.ReadArchivedDay(LogsFolderPath, job.Date!) ?? Array.Empty<string>();

                        foreach (var line in lines)
                        {
                            if (line.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }

                            var entry = job.Date != null ? ParseLine(job.Date, line) : ParseWhisperLine(line);
                            if (entry != null && Matches(entry))
                            {
                                found.Add(entry);
                                if (progress != null)
                                {
                                    Interlocked.Increment(ref progress.Hits);
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log.Warning(ex, "[C4TChat] Fehler bei der Suche in einer Log-Datei.");
                    }

                    if (progress != null)
                    {
                        Interlocked.Increment(ref progress.FilesDone);
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }

        results.AddRange(found);
        var sorted = results.OrderBy(e => e.Timestamp).ToList();
        return sorted.Count > maxResults ? sorted.GetRange(sorted.Count - maxResults, maxResults) : sorted;
    }

    public void RenameWhisperLog(string oldTarget, string newTarget)
    {
        try
        {
            fileWriter.Flush();

            var oldPath = GetWhisperLogPath(oldTarget);
            if (!File.Exists(oldPath))
            {
                return;
            }

            var newPath = GetWhisperLogPath(newTarget);
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(newPath))
            {
                File.AppendAllText(newPath, File.ReadAllText(oldPath));
                File.Delete(oldPath);
            }
            else
            {
                File.Move(oldPath, newPath);
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Whisper-Logdatei nicht umbenennen.");
        }
    }

    private static string SanitizeSenderName(string rawName)
    {
        var start = 0;
        while (start < rawName.Length && !char.IsLetter(rawName[start]))
        {
            start++;
        }

        return start > 0 ? rawName[start..] : rawName;
    }

    private static bool IsKnownChannel(XivChatType type) => ChannelDisplay.CategoryOf(type) != null;

    private bool IsFileLoggingEnabled(XivChatType type)
    {
        if (ChannelDisplay.IsLinkshell(type) || ChannelDisplay.IsCrossWorldLinkshell(type))
        {
            return configuration.FileLogLinkshell;
        }

        return type switch
        {
            XivChatType.Say => configuration.FileLogSay,
            XivChatType.Party => configuration.FileLogParty,
            XivChatType.Yell => configuration.FileLogYell,
            XivChatType.Shout => configuration.FileLogShout,
            XivChatType.FreeCompany => configuration.FileLogFreeCompany,
            XivChatType.Alliance => configuration.FileLogAlliance,
            XivChatType.PvPTeam => configuration.FileLogPvpTeam,
            XivChatType.NoviceNetwork => configuration.FileLogNoviceNetwork,
            _ => false,
        };
    }

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

        foreach (var archived in LogArchive.ListArchivedDates(LogsFolderPath))
        {
            if (!dates.Contains(archived))
            {
                dates.Add(archived);
            }
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
            fileWriter.Flush();

            IEnumerable<string>? lines = File.Exists(path) ? ReadLinesShared(path) : LogArchive.ReadArchivedDay(LogsFolderPath, dateName);
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
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

        return result.OrderBy(e => e.Timestamp).ToList();
    }

    private static bool TryParseTagSenderText(string rest, out XivChatType chatType, out string sender, out string? senderWorld, out string text)
    {
        chatType = default;
        sender = string.Empty;
        senderWorld = null;
        text = string.Empty;

        if (rest.Length == 0 || rest[0] != '[')
        {
            return false;
        }

        var tagEnd = rest.IndexOf(']');
        if (tagEnd < 0)
        {
            return false;
        }

        var tag = rest[..(tagEnd + 1)];
        var afterTag = rest[(tagEnd + 1)..].TrimStart();
        var colonIndex = afterTag.IndexOf(':');
        if (colonIndex < 0)
        {
            return false;
        }

        var senderPart = afterTag[..colonIndex];
        text = afterTag[(colonIndex + 1)..].TrimStart();

        var atIndex = senderPart.IndexOf('@');
        if (atIndex >= 0)
        {
            sender = SanitizeSenderName(senderPart[..atIndex]);
            senderWorld = senderPart[(atIndex + 1)..];
        }
        else
        {
            sender = SanitizeSenderName(senderPart);
            senderWorld = null;
        }

        chatType = ChannelDisplay.ParseTag(tag);
        return true;
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

        if (!TryParseTagSenderText(rest, out var chatType, out var sender, out var senderWorld, out var text))
        {
            return null;
        }

        if (!DateTime.TryParse($"{dateName} {timeText}", out var timestamp))
        {
            timestamp = DateTime.Now;
        }

        return new ChatLogEntry
        {
            Sender = sender,
            SenderWorld = senderWorld,
            Text = text,
            ChatType = chatType,
            Timestamp = timestamp,
        };
    }

    private static ChatLogEntry? ParseWhisperLine(string line)
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

        if (!TryParseTagSenderText(rest, out var chatType, out var sender, out var senderWorld, out var text))
        {
            return null;
        }

        if (!DateTime.TryParse(timeText, out var timestamp))
        {
            timestamp = DateTime.Now;
        }

        return new ChatLogEntry
        {
            Sender = sender,
            SenderWorld = senderWorld,
            Text = text,
            ChatType = chatType,
            Timestamp = timestamp,
        };
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
        contentIdHook?.Dispose();
        fileWriter.Dispose();
    }
}
