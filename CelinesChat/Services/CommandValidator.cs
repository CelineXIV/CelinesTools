using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CelinesChat.Services;

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
