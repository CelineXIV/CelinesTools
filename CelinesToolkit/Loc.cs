using System;
using System.Collections.Generic;

namespace CelinesToolkit;

internal static class Loc
{
    private static bool german;

    private static readonly Dictionary<string, (string En, string De)> Strings = new()
    {
        ["Delay.Label"] = ("Delay between commands (ms):", "Verzoegerung zwischen Befehlen (ms):"),
        ["Macro.New"] = ("+ New macro", "+ Neues Makro"),
        ["Macro.NewDefaultName"] = ("Macro {0}", "Makro {0}"),
        ["Macro.SelectHint"] = ("Select a macro on the left or create a new one.", "Waehle links ein Makro aus oder lege ein neues an."),
        ["Macro.Name"] = ("Name", "Name"),
        ["Macro.Run"] = ("Run", "Ausfuehren"),
        ["Macro.Delete"] = ("Delete macro", "Makro loeschen"),
        ["Macro.RunOnLogin"] = ("Run automatically on login", "Bei Login automatisch ausfuehren"),
        ["Macro.CommandsHeader"] = ("Commands (sent in order, e.g. /porch play 1):", "Befehle (werden der Reihe nach gesendet, z.B. /porch play 1):"),
        ["Macro.Up"] = ("Up", "Hoch"),
        ["Macro.Down"] = ("Down", "Runter"),
        ["Macro.Remove"] = ("Remove", "Entfernen"),
        ["Macro.AddCommand"] = ("+ Add command", "+ Befehl hinzufuegen"),
        ["Command.Help.Open"] = ("Opens the CelinesToolkit window.", "Oeffnet das CelinesToolkit-Fenster."),
        ["Command.Help.Run"] = ("Runs a saved macro: /ctrun <name>", "Fuehrt ein gespeichertes Makro aus: /ctrun <Name>"),
        ["Log.NoNameGiven"] = ("No macro name given. Usage: /ctrun <name>", "Kein Makroname angegeben. Nutzung: /ctrun <Name>"),
        ["Log.MacroNotFound"] = ("Macro '{0}' was not found.", "Makro '{0}' wurde nicht gefunden."),
        ["Log.SendError"] = ("Error sending command '{0}'.", "Fehler beim Senden des Befehls '{0}'."),
        ["Nav.Overview"] = ("Overview", "Uebersicht"),
        ["Nav.CommandTool"] = ("Commandtool", "Commandtool"),
        ["Nav.Orchestrion"] = ("Orchestrion", "Orchestrion"),
        ["Overview.Title"] = ("Welcome to CelinesToolkit", "Willkommen bei CelinesToolkit"),
        ["Overview.Intro"] = ("A small collection of quality-of-life tools. Pick a category on the left to get started.", "Eine kleine Sammlung von Komfort-Werkzeugen. Waehle links eine Kategorie, um loszulegen."),
        ["Overview.FeatureCommandTool"] = ("Commandtool: build and run macros made of several chat commands", "Commandtool: Makros aus mehreren Chat-Befehlen erstellen und ausfuehren"),
        ["Overview.FeatureOrchestrion"] = ("Orchestrion: automatically mute Porch on login and zone change", "Orchestrion: Porch bei Login und Gebietswechsel automatisch stummschalten"),
        ["Orchestrion.RequiresPorch"] = ("Requires the main plugin by perchbird", "Benoetigt das Hauptplugin von perchbird"),
        ["Orchestrion.MuteCheckbox"] = ("Mute Orchestrion", "Mute Orchestrion"),
        ["Nav.PreviewManager"] = ("Preview Manager", "Preview Manager"),
        ["Nav.QuickBar"] = ("Quickbar", "Quickbar"),
        ["Overview.FeaturePreviewManager"] = ("Preview Manager: add preview images to Penumbra mods that are missing one", "Preview Manager: Vorschaubilder zu Penumbra-Mods hinzufuegen, denen eines fehlt"),
        ["Overview.FeatureQuickBar"] = ("Quickbar: a small always-on bar to run a saved macro with one click", "Quickbar: eine kleine, immer sichtbare Leiste, um ein gespeichertes Makro mit einem Klick auszufuehren"),
        ["QuickBar.Description"] = ("The quickbar is a small, always-visible bar with a dropdown to pick one of your saved macros and a Play button to run it instantly. It opens automatically on login once enabled here.", "Die Quickbar ist eine kleine, immer sichtbare Leiste mit einem Dropdown zur Auswahl eines gespeicherten Makros und einem Play-Button, um es sofort auszufuehren. Sie oeffnet sich nach dem Aktivieren automatisch beim Einloggen."),
        ["QuickBar.EnableCheckbox"] = ("Enable quickbar", "Quickbar aktivieren"),
        ["QuickBar.SelectHint"] = ("Select macro...", "Makro waehlen..."),
        ["QuickBar.FilterHint"] = ("Filter...", "Filter..."),
        ["QuickBar.NoMacros"] = ("No macros yet. Create one in Commandtool.", "Noch keine Makros. Lege eins im Commandtool an."),
        ["QuickBar.Play"] = ("Play", "Abspielen"),
        ["PreviewManager.Refresh"] = ("Refresh Mod List", "Modliste aktualisieren"),
        ["PreviewManager.OnlyMissing"] = ("Only show mods without a preview", "Nur Mods ohne Vorschaubild anzeigen"),
        ["PreviewManager.ShowInPenumbra"] = ("Also show preview image inside Penumbra's own mod settings", "Vorschaubild auch in Penumbras eigenem Mod-Fenster anzeigen"),
        ["PreviewManager.SearchHint"] = ("Search mod name...", "Mod-Name suchen..."),
        ["PreviewManager.TotalCount"] = ("{0} mods found", "{0} Mods gefunden"),
        ["PreviewManager.FilteredCount"] = ("{0} of {1} mods shown", "{0} von {1} Mods angezeigt"),
        ["PreviewManager.Enabled"] = ("enabled", "aktiv"),
        ["PreviewManager.Disabled"] = ("disabled", "inaktiv"),
        ["PreviewManager.EnabledUnknown"] = ("unknown", "unbekannt"),
        ["PreviewManager.PenumbraNotFound"] = ("Penumbra was not found. Please make sure it is installed and loaded.", "Penumbra wurde nicht gefunden. Bitte stelle sicher, dass es installiert und geladen ist."),
        ["PreviewManager.SelectHint"] = ("Select a mod on the left.", "Waehle links eine Mod aus."),
        ["PreviewManager.Author"] = ("Author: {0}", "Autor: {0}"),
        ["PreviewManager.Version"] = ("Version: {0}", "Version: {0}"),
        ["PreviewManager.NoPreview"] = ("No Preview Image Found", "Kein Vorschaubild gefunden"),
        ["PreviewManager.FromUrl"] = ("Grab Image from URL / XIVModArchive", "Bild von URL / XIVModArchive laden"),
        ["PreviewManager.GrabFromUrl"] = ("Grab & Scale Image", "Bild laden & skalieren"),
        ["PreviewManager.Loading"] = ("Loading...", "Laedt..."),
        ["PreviewManager.FromFile"] = ("Set Local Image File", "Lokale Bilddatei setzen"),
        ["PreviewManager.Browse"] = ("Browse...", "Durchsuchen..."),
        ["PreviewManager.SetLocalImage"] = ("Set Local Image", "Lokales Bild setzen"),
        ["PreviewManager.PasteFromClipboard"] = ("Paste Image from Clipboard", "Bild aus Zwischenablage einfuegen"),
        ["PreviewManager.Saved"] = ("Preview image saved to the mod folder.", "Vorschaubild im Mod-Ordner gespeichert."),
        ["PreviewManager.Error.Generic"] = ("Something went wrong while saving the image.", "Beim Speichern des Bildes ist etwas schiefgelaufen."),
        ["PreviewManager.Error.ClipboardEmpty"] = ("No image found in the clipboard.", "Kein Bild in der Zwischenablage gefunden."),
        ["PreviewManager.Error.NoImageFound"] = ("No image could be found on that page.", "Auf dieser Seite konnte kein Bild gefunden werden."),
        ["Nav.ShoppingList"] = ("Shopping List", "Einkaufsliste"),
        ["Overview.FeatureShoppingList"] = ("Shopping List: import a MakePlace list and see the cheapest total price", "Einkaufsliste: MakePlace-Liste importieren und den guenstigsten Gesamtpreis sehen"),
        ["ShoppingList.Description"] = ("Import a MakePlace shopping list (.list.txt) to see the cheapest current market board price for every item, plus the grand total. Price data comes from the public Universalis API (universalis.app).", "Importiere eine MakePlace-Einkaufsliste (.list.txt), um fuer jedes Item den aktuell guenstigsten Marktpreis sowie die Gesamtsumme zu sehen. Die Preisdaten stammen von der oeffentlichen Universalis-API (universalis.app)."),
        ["ShoppingList.ImportButton"] = ("Import List...", "Liste importieren..."),
        ["ShoppingList.NoListLoaded"] = ("No list imported yet.", "Noch keine Liste importiert."),
        ["ShoppingList.HomeWorldOnlyCheckbox"] = ("Only consider offers on my home world", "Nur Angebote auf meiner Heimatwelt beruecksichtigen"),
        ["ShoppingList.FetchPrices"] = ("Fetch Prices", "Preise abrufen"),
        ["ShoppingList.Fetching"] = ("Fetching prices...", "Preise werden abgerufen..."),
        ["ShoppingList.LookupLoading"] = ("Loading item database...", "Item-Datenbank wird geladen..."),
        ["ShoppingList.ColumnCategory"] = ("Category", "Kategorie"),
        ["ShoppingList.ColumnItem"] = ("Item", "Item"),
        ["ShoppingList.ColumnQuantity"] = ("Qty", "Menge"),
        ["ShoppingList.ColumnUnitPrice"] = ("Price/Unit", "Preis/Stueck"),
        ["ShoppingList.ColumnWorld"] = ("World (DC)", "Welt (DC)"),
        ["ShoppingList.ColumnTotal"] = ("Total", "Gesamt"),
        ["ShoppingList.StatusNotFound"] = ("Item not found", "Item nicht gefunden"),
        ["ShoppingList.StatusNotTradable"] = ("Not tradable", "Nicht handelbar"),
        ["ShoppingList.StatusNoListings"] = ("No market data", "Keine Marktdaten"),
        ["ShoppingList.StatusError"] = ("Error (home world unknown)", "Fehler (Heimatwelt unbekannt)"),
        ["ShoppingList.GrandTotal"] = ("Grand total: {0}", "Gesamtsumme: {0}"),
        ["ShoppingList.ItemCount"] = ("{0}x", "{0}x"),
        ["ShoppingList.WarningCount"] = ("{0} item(s) without a valid price", "{0} Item(s) ohne gueltigen Preis"),
        ["ShoppingList.LastUpdated"] = ("Last updated: {0}", "Zuletzt aktualisiert: {0}"),
        ["ShoppingList.SearchHint"] = ("Search item name...", "Item-Name suchen..."),
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
