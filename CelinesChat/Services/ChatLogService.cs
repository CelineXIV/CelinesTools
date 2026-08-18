using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace CelinesChat.Services;

internal sealed class ChatLogService : IDisposable
{
    // A live view's scroll position isn't anchored to a specific entry - it's a raw pixel offset into
    // whatever's currently rendered. Every eviction here (RemoveOldestFromLargestCategory, below)
    // shrinks that view's total content height by one entry's worth if the evicted entry happened to
    // be in the same category the user's looking at, and ImGui's own automatic end-of-frame clamp
    // then yanks their scroll position down to match the new, smaller max - during a busy fight
    // (usually the very category most likely to be both the largest and the one being watched),
    // evictions happen often enough for this to read as a continuous jitter rather than one clean
    // jump ("scrolls up and jitters out of nowhere"). Raising the cap doesn't fix the underlying
    // per-eviction clamp, but it cuts how often eviction happens at all by a wide enough margin that
    // it stops being noticeable during ordinary play - a properly anchor-preserving fix (compensating
    // the scroll position by exactly what was lost) was tried and reverted after it introduced a
    // worse regression (silently breaking auto-scroll-to-bottom for live chat) that wasn't worth
    // this bug's own severity to keep chasing further.
    private const int MaxEntries = 10000;

    private readonly IChatGui chatGui;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    private readonly WhisperFailureDetector whisperFailureDetector;
    private readonly List<ChatLogEntry> entries = new();
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
            log.Warning(ex, "[CelinesChat] Konnte AddMsgSourceEntry-Hook nicht einrichten - Cross-World-Einladung und Mute-Liste bleiben ohne Content-ID deaktiviert.");
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
                    entries.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat] Konnte letzte Chat-Historie nicht laden.");
        }
    }

    public void Clear()
    {
        lock (entriesLock)
        {
            entries.Clear();
        }
    }

    internal List<ChatLogEntry> Snapshot()
    {
        lock (entriesLock)
        {
            return new List<ChatLogEntry>(entries);
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
            entries.Add(entry);
            while (entries.Count > MaxEntries)
            {
                RemoveOldestFromLargestCategory();
            }
        }

        lastDispatchedEntry = entry;

        PlaySoundIfNeeded(entry);

        EntryAdded?.Invoke(entry);
    }

    internal event Action<ChatLogEntry>? EntryAdded;

    private void RemoveOldestFromLargestCategory()
    {
        var countsByCategory = new Dictionary<ChatCategory, int>();
        foreach (var existing in entries)
        {
            if (ChannelDisplay.CategoryOf(existing.ChatType) is { } category)
            {
                countsByCategory[category] = countsByCategory.GetValueOrDefault(category) + 1;
            }
        }

        if (countsByCategory.Count == 0)
        {
            entries.RemoveAt(0);
            return;
        }

        var largestCategory = countsByCategory.OrderByDescending(kv => kv.Value).First().Key;
        for (var i = 0; i < entries.Count; i++)
        {
            if (ChannelDisplay.CategoryOf(entries[i].ChatType) == largestCategory)
            {
                entries.RemoveAt(i);
                return;
            }
        }

        entries.RemoveAt(0);
    }

    private unsafe void PlaySoundIfNeeded(ChatLogEntry entry)
    {
        if (!configuration.PlaySounds)
        {
            return;
        }

        if (configuration.WhisperSoundEnabled && entry.ChatType == XivChatType.TellIncoming)
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(configuration.WhisperSoundEffect, 1, 16));
            return;
        }

        if (configuration.MentionSoundEnabled && IsSelfMentioned(entry))
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

    /// Say/Party/Yell/etc. don't have a separate "outgoing" XivChatType the way Tell does, so
    /// per-channel sounds would otherwise also fire for a message the player just typed themselves -
    /// not what "notify me when someone messages in this channel" is meant to mean.
    private bool IsOwnMessage(ChatLogEntry entry)
    {
        return playerState.IsLoaded
            && !string.IsNullOrEmpty(playerState.CharacterName)
            && string.Equals(entry.Sender, playerState.CharacterName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSelfMentioned(ChatLogEntry entry)
    {
        if (!playerState.IsLoaded || string.IsNullOrEmpty(playerState.CharacterName))
        {
            return false;
        }

        if (string.Equals(entry.Sender, playerState.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spaceIndex = playerState.CharacterName.IndexOf(' ');
        var firstName = spaceIndex > 0 ? playerState.CharacterName[..spaceIndex] : playerState.CharacterName;

        foreach (var word in entry.Text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(word.Trim(PunctuationTrim), firstName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] WordSeparators = { ' ', '\t', '\r', '\n' };
    private static readonly char[] PunctuationTrim = { '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')' };

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
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat] Fehler im AddMsgSourceEntry-Hook.");
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

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private const string WhispersSubfolder = "Whispers";

    private string GetWhisperLogPath(string target)
    {
        var folder = Path.Combine(LogsFolderPath, WhispersSubfolder);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SanitizeForFileName(target) + ".txt");
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
        try
        {
            var senderPart = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
            var path = GetWhisperLogPath(senderPart);

            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {ChannelDisplay.Tag(entry.ChatType)} {senderPart}: {entry.Text}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Whisper-Nachricht nicht in eigene Logdatei schreiben.");
        }
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
            // A bare "Name" file (no world ever resolved for it - see AppendToWhisperFile's own
            // remarks) only unambiguously belongs to one specific "Name@World" conversation when
            // exactly one such file exists for that name - with two or more, there's no way to
            // tell which one it split off from, so it's listed on its own rather than silently
            // guessing (and possibly folding a stray message into the wrong person's history).
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
        if (!Directory.Exists(folder))
        {
            return result;
        }

        var exactFileName = SanitizeForFileName(target);
        var files = new List<string> { Path.Combine(folder, exactFileName + ".txt") };

        // A message whose sender World failed to resolve that one time (see AppendToWhisperFile's
        // own remarks) lands in a bare "Name.txt" instead of this contact's usual "Name@World.txt" -
        // folded in here too so a restart doesn't lose it. Only done when target itself already
        // names a specific world and no *other* "Name@AnotherWorld" file exists for the same name -
        // with more than one real candidate there's no way to tell which person the bare file
        // actually belongs to, and guessing risks mixing two different people's private
        // conversations together.
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
                foreach (var line in File.ReadAllLines(file))
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

        // List<T>.Sort is not stable - two messages landing in the same second (easy in a quick
        // back-and-forth, since the file only stores second precision) could swap their relative
        // order unpredictably between reloads, which is exactly what a merged multi-file read (see
        // above) needs to avoid. OrderBy is a guaranteed-stable sort, so ties keep the order they
        // were added in (each file's own write order, oldest file-content first).
        return result.OrderBy(e => e.Timestamp).ToList();
    }

    public void RenameWhisperLog(string oldTarget, string newTarget)
    {
        try
        {
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

        // See LoadWhisperHistory's matching remarks - a stable sort by timestamp, not raw file
        // order. This is the path LoadRecentHistory rebuilds the *entire* live entry list from on
        // every single plugin reload (not just whisper history), so any line that ever landed
        // slightly out of order in the file (two messages in the same second, for instance) used to
        // reshuffle the whole visible chat log on every reload for the rest of that session - exactly
        // the "history gets mixed up on reload" symptom this fixes.
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
    }
}
