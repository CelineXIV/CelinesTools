using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CelinesToolkit.Services.ShoppingList;

public sealed class ItemLookupService
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private Dictionary<string, Item>? namesToItems;
    private Task? buildTask;

    public ItemLookupService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public bool IsReady => namesToItems != null;

    public Task EnsureBuildStarted()
    {
        return buildTask ??= Task.Run(Build);
    }

    public Item? FindByName(string name)
    {
        return namesToItems != null && namesToItems.TryGetValue(name, out var item) ? item : null;
    }

    private void Build()
    {
        var sheet = dataManager.GetExcelSheet<Item>();
        var dict = new Dictionary<string, Item>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var item in sheet)
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (dict.TryGetValue(name, out var existing) && !existing.IsUntradable)
            {
                continue;
            }

            dict[name] = item;
        }

        namesToItems = dict;
        log.Debug($"[CelinesToolkit] Item-Namensindex fuer Einkaufsliste erstellt ({dict.Count} Eintraege).");
    }
}
