using System;
using System.Collections.Generic;

namespace CelinesRPChat;

internal static class Loc
{
    private static bool german;

    private static readonly Dictionary<string, (string En, string De)> Strings = new()
    {
        ["Window.Settings"] = ("Settings", "Einstellungen"),
        ["Window.Read"] = ("Read", "Mitlesen"),
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
        ["Compose.RemoveSnippet"] = ("Remove", "Entfernen"),
        ["Compose.SnippetNameLabel"] = ("Name", "Name"),
        ["Compose.SaveSnippet"] = ("Save as template", "Als Vorlage speichern"),
        ["Compose.NoHistory"] = ("No messages sent yet.", "Noch keine Nachrichten gesendet."),
        ["ChatLog.ClickToWhisper"] = ("Click to set as whisper target", "Klicken, um als Fluester-Ziel zu setzen"),
        ["ChatLog.OpenFolder"] = ("Open logs folder", "Log-Ordner oeffnen"),
        ["ChatLog.LoadHistory"] = ("Load history", "Verlauf laden"),
        ["ChatLog.NoHistoryFiles"] = ("No log files found.", "Keine Log-Dateien gefunden."),
        ["ChatLog.BackToLive"] = ("Back to live", "Zurueck zu Live"),
        ["Settings.DefaultColor"] = ("Default text color", "Standard-Textfarbe"),
        ["Settings.EmoteColor"] = ("Emote color (*text*)", "Emote-Farbe (*Text*)"),
        ["Settings.EmotePreview"] = ("*sample text*", "*Beispieltext*"),
        ["Settings.OocColor"] = ("OOC color ((text))", "OOC-Farbe ((Text))"),
        ["Settings.OocPreview"] = ("((sample text))", "((Beispieltext))"),
        ["Settings.SenderNameColor"] = ("Sender name color (read window)", "Absendername-Farbe (Mitlesefenster)"),
        ["Settings.SenderNamePreview"] = ("PlayerName:", "Spielername:"),
        ["Settings.MentionColor"] = ("Mention highlight color", "Erwaehnungs-Hervorhebungsfarbe"),
        ["Settings.MentionPreview"] = ("sample text", "Beispieltext"),
        ["Settings.MaxLength"] = ("Max. characters per message", "Max. Zeichen pro Nachricht"),
        ["Settings.Delay"] = ("Delay between messages (ms)", "Verzoegerung zwischen Nachrichten (ms)"),
        ["Settings.FileLogHeader"] = ("Save to log file:", "In Log-Datei speichern:"),
        ["Settings.FontScale"] = ("Font size", "Schriftgroesse"),
        ["Settings.WindowOpacity"] = ("Window opacity", "Fenster-Transparenz"),
        ["Settings.Reset"] = ("Reset to defaults", "Auf Standard zuruecksetzen"),
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
