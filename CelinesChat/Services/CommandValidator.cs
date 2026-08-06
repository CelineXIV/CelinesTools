using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CelinesChat.Services;

/// <summary>
/// Knows every native slash command the game itself understands (from the "TextCommand" sheet -
/// "/dance", "/party", "/blist", ...), used together with Dalamud's own <see cref="ICommandManager"/>
/// (which only knows about plugin-registered commands like our own "/celineschat") to tell a real
/// command apart from a typo - see ChatWindow's compose-box color, which tints the whole box based
/// on this. Matches Chat2's own Commands.cs/InputHandler.IsValidCommand (verified against their
/// real source): unlike plugin commands, native ones aren't registered anywhere Dalamud exposes,
/// so this is the only way to recognise them at all.
///
/// TextCommand is a properly pre-generated Lumina row type (unlike Completion, the auto-translate
/// sheet - see AutoTranslateService's remarks on why that one needed a hand-written reader), so
/// no offset reverse-engineering is needed here.
/// </summary>
internal sealed class CommandValidator
{
    private readonly HashSet<string> nativeCommands = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded { get; private set; }

    public void EnsureLoaded(IDataManager dataManager, IPluginLog log)
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var sheet = dataManager.GetExcelSheet<TextCommand>();
            foreach (var row in sheet)
            {
                AddIfPresent(row.Command.ExtractText());
                AddIfPresent(row.ShortCommand.ExtractText());
                AddIfPresent(row.Alias.ExtractText());
                AddIfPresent(row.ShortAlias.ExtractText());
            }

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat] Konnte die TextCommand-Sheet nicht laden - unbekannte Befehle werden dann faelschlich als ungueltig markiert.");
        }
    }

    private void AddIfPresent(string command)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            nativeCommands.Add(command);
        }
    }

    public bool IsKnownNativeCommand(string command) => nativeCommands.Contains(command);
}
