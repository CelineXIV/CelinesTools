using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

internal sealed class AutoTranslateService
{
    public readonly record struct Entry(uint Group, uint Key, string Text);

    private readonly List<Entry> entries = new();
    private readonly Dictionary<uint, string> groupTitles = new();

    public bool IsLoaded { get; private set; }

    public string? LoadError { get; private set; }

    public void EnsureLoaded(IDataManager dataManager, IPluginLog log)
    {
        if (IsLoaded)
        {
            return;
        }

        string? columnsDump = null;
        try
        {
            var sheet = dataManager.GetExcelSheet<CompletionRow>(name: "Completion");

            columnsDump = string.Join(", ", sheet.Columns.Select((c, i) => $"[{i}]={c.Type}@{c.Offset}"));

            foreach (var row in sheet)
            {
                if (!string.IsNullOrWhiteSpace(row.GroupTitle) && row.GroupTitle != "-"
                    && (!groupTitles.TryGetValue(row.Group, out var existingTitle) || existingTitle == "-"))
                {
                    groupTitles[row.Group] = row.GroupTitle;
                }

                if (string.IsNullOrWhiteSpace(row.Text) || row.Text.StartsWith('【'))
                {
                    continue;
                }

                entries.Add(new Entry(row.Group, row.RowId, row.Text));
            }

            IsLoaded = true;
            LoadError = null;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat] Failed to load the Completion (auto-translate) sheet.");
            LoadError = columnsDump != null ? $"{ex.Message}\nColumns: {columnsDump}" : ex.Message;
        }
    }

    public IReadOnlyList<Entry> Search(string term, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<Entry>();
        }

        var results = new List<Entry>(limit);
        foreach (var entry in entries)
        {
            if (entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(entry);
                if (results.Count >= limit)
                {
                    break;
                }
            }
        }

        return results;
    }

    public IEnumerable<uint> GetGroups() => groupTitles.Keys.OrderBy(g => g);

    public string GetGroupTitle(uint group) => groupTitles.TryGetValue(group, out var title) ? title : $"#{group}";

    public IEnumerable<Entry> GetEntries(uint group) => entries.Where(e => e.Group == group);
}
