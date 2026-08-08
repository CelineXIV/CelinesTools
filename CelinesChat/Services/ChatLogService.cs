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
    // Every entry is small (a few strings + a timestamp), and only the entries matching the
    // currently active tab's filter actually get drawn each frame - 500 buffered messages costs
    // negligible memory and lets a busy multi-channel session scroll back further than before.
    private const int MaxEntries = 500;

    private readonly IChatGui chatGui;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    private readonly List<ChatLogEntry> entries = new();
    private readonly Hook<RaptureLogModule.Delegates.AddMsgSourceEntry>? contentIdHook;
    private long nextSequence = 1;

    // Every existing reader/writer of `entries` (OnChatMessage, LoadRecentHistory, Clear, and the
    // public Entries property's own call sites in ChatWindow/SecondaryChatWindow) runs on
    // Dalamud's framework/UI thread, which is single-threaded relative to itself - no lock was
    // ever needed before. Services/Web is the first thing that reads this list from an arbitrary
    // HTTP server thread, concurrently with the framework thread still appending to it - this
    // lock exists solely for that new access pattern (see Snapshot/SnapshotSince/FindBySequence
    // below), not because the original single-threaded assumption was ever wrong.
    private readonly object entriesLock = new();

    // Updated on every incoming chat message, tracked or not (see OnChatMessage) - the native
    // AddMsgSourceEntry function this plugin hooks below fires for every message the game
    // processes, not just the ones IsKnownChannel keeps, so correlating against "the last entry
    // we kept" instead of "the last message that arrived at all" would occasionally stamp a
    // stale, unrelated entry with the wrong content/account ID.
    private ChatLogEntry? lastDispatchedEntry;

    public string LogsFolderPath { get; }

    /// <summary>
    /// Framework/UI-thread-only, like every other member of this class historically - safe to
    /// enumerate from ChatWindow/SecondaryChatWindow's Draw calls exactly as before. Do NOT read
    /// this from Services/Web (an arbitrary HTTP thread) - use Snapshot()/SnapshotSince(long)
    /// instead, which take entriesLock and hand back an independent copy.
    /// </summary>
    public IReadOnlyList<ChatLogEntry> Entries => entries;

    public unsafe ChatLogService(IChatGui chatGui, Configuration configuration, IDalamudPluginInterface pluginInterface, IPluginLog log, IGameInteropProvider gameInteropProvider, IPlayerState playerState)
    {
        this.chatGui = chatGui;
        this.configuration = configuration;
        this.log = log;
        this.playerState = playerState;

        LogsFolderPath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "Logs");
        try
        {
            Directory.CreateDirectory(LogsFolderPath);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Log-Ordner nicht anlegen.");
        }

        LoadRecentHistory();

        this.chatGui.ChatMessage += OnChatMessage;

        // The game calls this immediately after dispatching a chat message, to record who sent
        // it into its own internal chat log module - Dalamud's ChatMessage event doesn't expose
        // that content/account ID at all, so this is the only way to get it (needed for
        // cross-world/in-instance party invites and the mute list). Verified against ChatTwo's
        // own MessageManager.cs (ContentIdResolver), which uses this exact hook target the same
        // way: call the original first, then read off the ID it was just given.
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

    /// <summary>
    /// Pre-populates the live view from recent log files on startup, so the chat log doesn't
    /// always open completely empty - it reads back in over the last few days (capped by both
    /// day count and total entries, so a very chatty history doesn't slow down startup or eat
    /// the whole live buffer). Sequence numbers are assigned here too, in chronological order, so
    /// these entries look exactly like ones that "arrived earlier" to anything reading Sequence -
    /// see ChatWindow's constructor, which fast-forwards its own tracking past them so they don't
    /// get treated as brand new (auto-creating whisper tabs, blinking, bumping unread counts).
    /// </summary>
    private void LoadRecentHistory()
    {
        const int maxHistoryEntries = 200;
        const int maxDaysBack = 7;

        try
        {
            var dates = GetAvailableLogDates(); // newest date first
            var loaded = new List<ChatLogEntry>();

            foreach (var date in dates.Take(maxDaysBack))
            {
                var dayEntries = LoadHistoryFile(date); // oldest-first within the day already
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

    /// <summary>
    /// An independent copy of every currently-buffered entry, safe to call from any thread - see
    /// entriesLock's remarks. Used by Services/Web for a fresh SSE client's initial full resync.
    /// </summary>
    internal List<ChatLogEntry> Snapshot()
    {
        lock (entriesLock)
        {
            return new List<ChatLogEntry>(entries);
        }
    }

    /// <summary>
    /// Same as <see cref="Snapshot"/>, but only entries strictly newer than <paramref name="sequence"/> -
    /// used for a reconnecting SSE client's delta resync (see Services/Web/WebSseHub), which is
    /// lighter than resending the full buffer on every reconnect.
    /// </summary>
    internal List<ChatLogEntry> SnapshotSince(long sequence)
    {
        lock (entriesLock)
        {
            return entries.Where(e => e.Sequence > sequence).ToList();
        }
    }

    /// <summary>
    /// Looks up one entry by its Sequence - used by Services/Web to resolve a link click (the web
    /// page only ever holds a sequence number + link index, never the real Payload objects) back
    /// to the actual entry so the existing Plugin.HandleChatLinkClicked can run unchanged.
    /// </summary>
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

        // A cross-world sender's raw text isn't one clean name string - the game puts the world
        // name in its own separate TextPayload after an IconPayload (the little "different world"
        // icon), both sitting right after the name's own TextPayload with nothing in between.
        // message.Sender.TextValue flattens every ITextProvider payload it finds and IconPayload
        // isn't one, so it silently drops the icon but keeps both text pieces - producing
        // "Vorname NachnameWorldName" with no separator at all, which then got the world appended
        // a *second* time once SenderWorld (extracted correctly below) was also displayed.
        // PlayerPayload.PlayerName is Dalamud's own already-clean name - "does not contain the
        // server name" per its own doc comment - so use that directly whenever a PlayerPayload
        // exists instead of trying to reconstruct a clean name from the flattened text.
        var entry = new ChatLogEntry
        {
            Sender = playerPayload != null ? playerPayload.PlayerName : SanitizeSenderName(message.Sender.TextValue),
            SenderWorld = playerPayload?.World.ValueNullable?.Name.ExtractText(),
            Text = message.Message.TextValue,
            ChatType = message.LogKind,
            Timestamp = DateTime.Now,
            Sequence = nextSequence++,
            // Kept for rendering map/item/Dalamud links inline and clickable - see
            // ColoredTextRenderer.DrawRich. Map/item link payloads themselves carry no visible
            // text (that's a separate TextPayload right after them, already part of Text above);
            // this is what lets the renderer find the payload that TEXT should link to.
            Payloads = message.Message.Payloads.ToList(),
            // Kept separately for the sender's own status icon (Mentor/Sprout/Returner/RP) - see
            // ChatLogEntry.SenderPayloads.
            SenderPayloads = message.Sender.Payloads.ToList(),
        };

        if (IsFileLoggingEnabled(message.LogKind))
        {
            AppendToFile(entry);
        }

        // Always buffered regardless of the "show in chat log" setting, which is instead applied
        // at draw time (see ChannelDisplay.IsVisible callers) - so toggling a channel on/off takes
        // effect immediately on already-buffered messages instead of only affecting new arrivals.
        lock (entriesLock)
        {
            entries.Add(entry);
            while (entries.Count > MaxEntries)
            {
                entries.RemoveAt(0);
            }
        }

        lastDispatchedEntry = entry;

        PlaySoundIfNeeded(entry);

        // Raised after the entry is fully in the buffer (and thus visible to a concurrent
        // Snapshot()/SnapshotSince() call from another thread) - Services/Web's WebSseHub
        // subscribes to this to push new messages to connected web clients live, the same
        // trigger point ChatWindow's own "new entry" detection would see next Draw().
        EntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// Fired once per newly-buffered live entry (never for history reloaded on startup), always
    /// on the framework thread since OnChatMessage itself is. Services/Web's only supported way
    /// to learn about new messages - do not add a second, polling-based path.
    /// </summary>
    internal event Action<ChatLogEntry>? EntryAdded;

    /// <summary>
    /// Since this plugin fully replaces the native chat window (and hides it - see
    /// Plugin.SetNativeChatVisible), it also has to reintroduce whatever audio cue a player would
    /// otherwise rely on to notice a message without looking at the screen. Deliberately narrow:
    /// only a whisper (on by default - the one thing you can't just catch by glancing at the log
    /// later, since it also opens/keeps a conversation) and an opt-in "someone said your name"
    /// mention, not a sound for every category, so this stays useful instead of becoming noise.
    /// Goes through the game's own UIGlobals.PlayChatSoundEffect (the same 16 slots <se.1>-<se.16>
    /// macros use), so it automatically respects the game's own SFX volume/mute settings.
    /// </summary>
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
        }
    }

    /// <summary>
    /// A plain word match against the player's own first name (same identifier
    /// ColoredTextRenderer highlights mentions with) - not used for own messages, so posting your
    /// own name doesn't ping yourself.
    /// </summary>
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
        // Always call through first - this must keep behaving exactly like the un-hooked
        // function from the game's own perspective no matter what happens in our own handling
        // below.
        contentIdHook?.Original(module, contentId, accountId, messageIndex, worldId, chatType);

        try
        {
            if (lastDispatchedEntry != null)
            {
                lastDispatchedEntry.ContentId = contentId;
                lastDispatchedEntry.AccountId = accountId;
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

    // ChannelDisplay.CategoryOf is the single source of truth for which XivChatType values this
    // plugin understands at all - keeping that logic in exactly one place (rather than a second,
    // separately-maintained list here) is what avoids a repeat of an earlier bug where a numeric
    // range check for cross-world linkshells silently also matched Damage/Loot/Crafting/Gathering/
    // system messages in between (37-107 isn't contiguous for just linkshells).
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
            XivChatType.TellIncoming => configuration.FileLogTell,
            XivChatType.TellOutgoing => configuration.FileLogTell,
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
        contentIdHook?.Dispose();
    }
}
