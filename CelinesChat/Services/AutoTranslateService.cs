using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

/// <summary>
/// Loads and searches the game's "Completion" (auto-translate) sheet - dungeon names, greetings,
/// actions, and so on, same data as the native chat box's Tab popup. Loaded lazily on first use
/// (see <see cref="EnsureLoaded" />) rather than at plugin startup, since walking every row in
/// the sheet just to have it ready "in case" isn't worth the extra startup time for a feature
/// that's only needed once the user actually presses Tab.
/// </summary>
internal sealed class AutoTranslateService
{
    /// <summary>Key here is the sheet row's own RowId - see CompletionRow's remarks for why
    /// that's what AutoTranslatePayload actually needs, not a separate data column.</summary>
    public readonly record struct Entry(uint Group, uint Key, string Text);

    private readonly List<Entry> entries = new();
    private readonly Dictionary<uint, string> groupTitles = new();

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Set when the last load attempt threw - lets the popup show "couldn't load this" instead
    /// of an indefinite "Loading..." that never explains why nothing ever shows up.
    /// </summary>
    public string? LoadError { get; private set; }

    public void EnsureLoaded(IDataManager dataManager, IPluginLog log)
    {
        if (IsLoaded)
        {
            return;
        }

        // Only mark this loaded on success - a transient failure (e.g. sheet definitions not
        // matching this game version yet) should let a later attempt retry instead of
        // permanently leaving auto-translate empty for the rest of the session.
        string? columnsDump = null;
        try
        {
            var sheet = dataManager.GetExcelSheet<CompletionRow>(name: "Completion");

            // The exact column layout (type + byte offset per logical field) reported at
            // runtime - included in LoadError below if reading rows fails, since
            // CompletionRow's field-index-to-column mapping was worked out from a live
            // diagnostic dump rather than a pre-generated Lumina row type, and this is the
            // fastest way to see what changed if a future game update shifts it.
            columnsDump = string.Join(", ", sheet.Columns.Select((c, i) => $"[{i}]={c.Type}@{c.Offset}"));

            foreach (var row in sheet)
            {
                // The real group title only ever lives on the "【Languages】"-style header row
                // itself - every other row in the group reports GroupTitle as a literal "-"
                // placeholder (not blank, so it still passes an IsNullOrWhiteSpace check on its
                // own). Capturing titles here, before the header-row filter below skips that row
                // from the selectable list, is what keeps a group's placeholder "-" from
                // permanently winning just for being seen first.
                if (!string.IsNullOrWhiteSpace(row.GroupTitle) && row.GroupTitle != "-"
                    && (!groupTitles.TryGetValue(row.Group, out var existingTitle) || existingTitle == "-"))
                {
                    groupTitles[row.Group] = row.GroupTitle;
                }

                // Rows like "【Languages】" are the category's own header/label, not a real
                // selectable phrase - including them made the very first thing shown in each
                // category a dud that evaluates to nothing meaningful in-game instead of an
                // actual translatable entry.
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
