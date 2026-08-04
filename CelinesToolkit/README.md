# CelinesToolkit

Dalamud-Plugin für FFXIV, mit dem man Sequenzen aus mehreren Chat-/Plugin-Befehlen
(z.B. `/porch play 1`) als benanntes Makro speichern und wieder abspielen kann.

Die Oberfläche ist auf Deutsch und Englisch verfügbar und richtet sich
automatisch nach der Dalamud-Spracheinstellung (`/xlsettings` → General →
Language). Andere Sprachen fallen auf Englisch zurück.

## Funktionen

- ImGui-Fenster zum Anlegen mehrerer Makros, jedes mit einer beliebigen Anzahl
  an Befehlen (Reihenfolge per "Hoch"/"Runter" änderbar).
- Chat-Befehl `/celinestoolkit` öffnet/schließt das Fenster.
- Chat-Befehl `/ctrun <Name>` führt ein gespeichertes Makro direkt aus.
- Konfigurierbare Verzögerung zwischen den einzelnen Befehlen (Standard 600 ms),
  damit das Spiel die Eingaben nicht als Spam blockt.
- Checkbox "Bei Login automatisch ausführen" pro Makro: alle so markierten
  Makros laufen automatisch (mit 3 Sekunden Startverzögerung), sobald der
  Charakter eingeloggt ist.
- Einstellungen werden dauerhaft in der Dalamud-Plugin-Konfiguration gespeichert.
- "Quickbar"-Seite: aktiviert eine kleine, immer sichtbare Leiste (ohne Titelleiste,
  dunkel/transparent im Stil bekannter Ingame-Tool-Leisten) mit einem durchsuchbaren
  Dropdown zur Auswahl eines gespeicherten Makros und einem Play-Icon-Button zum
  sofortigen Ausfuehren. Standardmaessig deaktiviert; einmal aktiviert, oeffnet sie
  sich automatisch bei jedem Login.
- "Orchestrion"-Seite: schaltet [Porch](https://github.com/perchbird) bei Login/
  Gebietswechsel automatisch stumm bzw. spielt beim Deaktivieren den aktuellen
  Gebietssong wieder ab.
- "Preview Manager"-Seite: listet installierte Penumbra-Mods auf (mit Namenssuche
  und Anzeige, ob die Mod fuer den eigenen Charakter aktiv ist) und erlaubt es,
  ein fehlendes Vorschaubild per URL (inkl. XIVModArchive-Seiten), lokaler Datei
  oder aus der Zwischenablage zu setzen. Das Bild wird als `preview.png` direkt
  im Mod-Ordner selbst gespeichert (nicht in der Konfiguration von CelinesToolkit),
  damit es auch erhalten bleibt, falls dieses Plugin einmal entfernt wird.
  Ueber Penumbras eigene Erweiterungs-Events wird das Bild zusaetzlich direkt
  im Mod-Einstellungsfenster von Penumbra selbst angezeigt (abschaltbar per
  Checkbox).
- "Einkaufsliste"-Seite: importiert eine MakePlace-Einkaufsliste (`.list.txt`)
  und zeigt fuer jedes Item den aktuell guenstigsten Marktpreis sowie die
  Gesamtsumme an. Die Preisdaten kommen direkt von der oeffentlichen
  [Universalis](https://universalis.app)-API, es wird kein zusaetzliches Plugin
  benoetigt. Optional kann per Checkbox auf "nur meine Heimatwelt" umgeschaltet
  werden; standardmaessig wird ueber die gesamte Region (alle Rechenzentren des
  eigenen Spielgebiets) nach dem guenstigsten Angebot gesucht und angezeigt, auf
  welcher Welt/welchem Rechenzentrum es zu finden ist. Items, die auf dem
  Marktbrett nicht handelbar sind (z.B. manche Event-Belohnungen), werden als
  solche gekennzeichnet statt in die Summe einzufliessen.

## Bauen

Voraussetzung: .NET SDK (net10.0) und eine laufende XIVLauncher/Dalamud-Installation
(die Dalamud-DLLs werden automatisch aus `%AppData%\XIVLauncher\addon\Hooks\dev\`
referenziert).

```
dotnet build
```

Das Ergebnis liegt in `bin/Debug/` (DLL + `CelinesToolkit.json`).

## Als Dev-Plugin laden

1. Im Spiel `/xlsettings` öffnen → Tab "Experimentell" → "Enable Developer Mode".
2. Unter "Entwickler-Plugin Pfade" über "Select Dev Plugin DLL" direkt die Datei
   `C:\Entwicklungsumgebung\Cursor\FFSmartphonePlugin\CelinesToolkit\bin\Debug\CelinesToolkit.dll`
   auswählen (der Pfad muss auf die DLL zeigen, nicht nur auf den Ordner).
3. `/xlplugins` öffnen → Tab "Dev Tools" → "Installed Dev Plugins"
   → CelinesToolkit aktivieren (ggf. vorher "Scan Dev Plugins" klicken).
4. Nach Codeänderungen reicht `dotnet build` und ein Neuladen des Plugins im
   Dev-Tools-Tab (kein Client-Neustart nötig).

## Nutzung

- `/celinestoolkit` öffnet das Fenster.
- Links: Makros anlegen/auswählen. Rechts: Befehle des ausgewählten Makros
  bearbeiten, per "Ausführen" sofort testen.
- `/ctrun <Makroname>` spielt ein Makro auch ohne offenes Fenster ab
  (z.B. auf eine Hotbar legbar über einen Makro-Slot mit `/ctrun Mein Makro`).

## Übersetzung

Alle sichtbaren Texte liegen zentral in [Loc.cs](Loc.cs) als Deutsch/Englisch-Paare.
Neue UI-Texte sollten dort als neuer Key ergänzt und per `Loc.T("Key")`
verwendet werden, statt Text direkt im Code zu hinterlegen.
