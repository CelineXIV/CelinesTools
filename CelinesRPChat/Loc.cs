using System;
using System.Collections.Generic;

namespace CelinesRPChat;

internal static class Loc
{
    private static bool german;

    private static readonly Dictionary<string, (string En, string De)> Strings = new()
    {
        ["Window.Settings"] = ("Settings", "Einstellungen"),
        ["Window.Preview"] = ("Message Preview", "Nachrichtenvorschau"),
        ["Channel.Label"] = ("Channel:", "Kanal:"),
        ["Channel.Say"] = ("Say", "Sagen"),
        ["Channel.Party"] = ("Party", "Gruppe"),
        ["Channel.Whisper"] = ("Whisper", "Fluestern"),
        ["Channel.Yell"] = ("Yell", "Ruf"),
        ["Channel.Shout"] = ("Shout", "Schrei"),
        ["Channel.FreeCompany"] = ("Free Company", "Freie Gesellschaft"),
        ["Channel.Linkshell"] = ("Linkshell", "Linkshell"),
        ["Channel.LinkshellNumberLabel"] = ("Linkshell #", "Linkshell Nr."),
        ["Whisper.TargetLabel"] = ("Target (Name@World):", "Ziel (Name@Welt):"),
        ["Whisper.UseTarget"] = ("Use target", "Ziel uebernehmen"),
        ["Whisper.Suggestions"] = ("Suggestions", "Vorschlaege"),
        ["Whisper.SuggestionsParty"] = ("-- Party --", "-- Gruppe --"),
        ["Whisper.SuggestionsRecent"] = ("-- Recent --", "-- Zuletzt --"),
        ["Whisper.NoSuggestions"] = ("No suggestions available", "Keine Vorschlaege vorhanden"),
        ["Compose.PreviewHeader"] = ("Preview:", "Vorschau:"),
        ["Compose.MessageCount"] = ("{0} message(s)", "{0} Nachricht(en)"),
        ["Compose.OpenPreviewWindow"] = ("Open preview in its own window", "Vorschau in eigenem Fenster oeffnen"),
        ["Compose.CharCount"] = ("{0}/{1} characters", "{0}/{1} Zeichen"),
        ["Compose.Send"] = ("Send", "Senden"),
        ["Compose.Sending"] = ("Sending {0}/{1}...", "Sende {0}/{1}..."),
        ["Compose.Cancel"] = ("Cancel", "Abbrechen"),
        ["Compose.Copy"] = ("Copy", "Kopieren"),
        ["Compose.Clear"] = ("Clear", "Leeren"),
        ["Compose.Settings"] = ("Settings", "Einstellungen"),
        ["Compose.OpenLog"] = ("Chat log", "Mitlesen"),
        ["Compose.Changelog"] = ("Changelog", "Changelog"),
        ["Compose.WhisperTargetMissing"] = ("Please enter a whisper target.", "Bitte ein Fluester-Ziel eingeben."),
        ["Compose.EmptyText"] = ("Please enter some text.", "Bitte Text eingeben."),
        ["Compose.WrapEmote"] = ("* Emote *", "* Emote *"),
        ["Compose.WrapOoc"] = ("(( OOC ))", "(( OOC ))"),
        ["Compose.WrapHint"] = ("Wraps the selected text (or inserts markers at the cursor).", "Setzt Markierung um den ausgewaehlten Text (oder fuegt Marker an der Schreibmarke ein)."),
        ["Compose.Snippets"] = ("Templates", "Vorlagen"),
        ["Compose.History"] = ("History", "Verlauf"),
        ["Compose.NoSnippets"] = ("No templates saved yet.", "Noch keine Vorlagen gespeichert."),
        ["Compose.SnippetSearchHint"] = ("Search templates...", "Vorlagen suchen..."),
        ["Compose.RemoveSnippet"] = ("Remove", "Entfernen"),
        ["Compose.SnippetNameLabel"] = ("Name", "Name"),
        ["Compose.SaveSnippet"] = ("Save as template", "Als Vorlage speichern"),
        ["Compose.NoHistory"] = ("No messages sent yet.", "Noch keine Nachrichten gesendet."),
        ["ChatLog.ClickToWhisper"] = ("Click to set as whisper target", "Klicken, um als Fluester-Ziel zu setzen"),
        ["ChatLog.CopyMessage"] = ("Copy to clipboard", "In die Zwischenablage kopieren"),
        ["ChatLog.OpenFolder"] = ("Open logs folder", "Log-Ordner oeffnen"),
        ["ChatLog.LoadHistory"] = ("Load history", "Verlauf laden"),
        ["ChatLog.NoHistoryFiles"] = ("No log files found.", "Keine Log-Dateien gefunden."),
        ["ChatLog.HistorySearchHint"] = ("Search date (e.g. 2026-07)...", "Datum suchen (z.B. 2026-07)..."),
        ["ChatLog.BackToLive"] = ("Back to live", "Zurueck zu Live"),
        ["ChatLog.SearchHint"] = ("Search text or sender...", "Text oder Absender suchen..."),
        ["Settings.DefaultColor"] = ("Default text color", "Standard-Textfarbe"),
        ["Settings.EmoteColor"] = ("Emote color (*text*)", "Emote-Farbe (*Text*)"),
        ["Settings.EmotePreview"] = ("*sample text*", "*Beispieltext*"),
        ["Settings.OocColor"] = ("OOC color ((text))", "OOC-Farbe ((Text))"),
        ["Settings.OocPreview"] = ("((sample text))", "((Beispieltext))"),
        ["Settings.SenderNameColor"] = ("Sender name color (read window)", "Absendername-Farbe (Mitlesefenster)"),
        ["Settings.SenderNamePreview"] = ("PlayerName:", "Spielername:"),
        ["Settings.MentionColor"] = ("Mention highlight color", "Erwaehnungs-Hervorhebungsfarbe"),
        ["Settings.MentionPreview"] = ("sample text", "Beispieltext"),
        ["Settings.SendAccentColor"] = ("Send button color", "Senden-Button-Farbe"),
        ["Settings.LogVisibilityHeader"] = ("Show in chat log:", "Im Chatverlauf anzeigen:"),
        ["Settings.LogBackgroundOpacity"] = ("Chat log background opacity", "Hintergrund-Transparenz des Chatverlaufs"),
        ["Settings.MaxLength"] = ("Max. characters per message", "Max. Zeichen pro Nachricht"),
        ["Settings.Delay"] = ("Delay between messages (ms)", "Verzoegerung zwischen Nachrichten (ms)"),
        ["Settings.FileLogHeader"] = ("Save to log file:", "In Log-Datei speichern:"),
        ["Settings.FontScale"] = ("Font size", "Schriftgroesse"),
        ["Settings.WindowOpacity"] = ("Window opacity (focused)", "Fenster-Transparenz (fokussiert)"),
        ["Settings.UnfocusedOpacity"] = ("Window opacity (unfocused)", "Fenster-Transparenz (nicht fokussiert)"),
        ["Settings.UnfocusedOpacityHint"] = ("Fades the compose and chat log windows when they're not focused, so they cover less of the screen while you're posing or playing an animation.", "Blendet Compose- und Mitlesefenster ab, wenn sie nicht fokussiert sind, damit sie weniger vom Bildschirm verdecken, waehrend du posierst oder eine Animation abspielst."),
        ["Settings.Reset"] = ("Reset to defaults", "Auf Standard zuruecksetzen"),
        ["Settings.WhisperDelayHint"] = ("Whispers always use at least {0} ms between messages, even if set lower above: /tell needs a server round-trip, and sending them too fast can make the game silently drop one.", "Fluesterbenachrichtigungen nutzen immer mindestens {0} ms zwischen Nachrichten, auch wenn oben weniger eingestellt ist: /tell braucht eine Server-Bestaetigung, und zu schnelles Senden kann dazu fuehren, dass das Spiel eine Nachricht stillschweigend verwirft."),
        ["Command.Help.Open"] = ("Opens the CelinesRPChat window.", "Oeffnet das CelinesRPChat-Fenster."),
        ["Command.Help.OpenLog"] = ("Opens the chat log window.", "Oeffnet das Mitlesefenster."),
        ["Log.SendError"] = ("Error sending message '{0}'.", "Fehler beim Senden der Nachricht '{0}'."),
    };

    public static void SetLanguage(string uiLanguage)
    {
        german = string.Equals(uiLanguage, "de", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string key)
    {
        return Strings.TryGetValue(key, out var pair) ? (german ? pair.De : pair.En) : key;
    }

    public static string T(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }
}
