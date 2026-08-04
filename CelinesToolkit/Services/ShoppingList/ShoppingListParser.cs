using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CelinesToolkit.Services.ShoppingList;

internal static class ShoppingListParser
{
    private static readonly Regex MakePlaceItemLineRegex = new(@"^(.+):\s*(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex TeamcraftItemLineRegex = new(@"^(\d+)\s*x\s+(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses either a MakePlace ".list.txt" export ("Item Name: 5") or a Teamcraft list export/clipboard
    /// paste ("5x Item Name"), auto-detected by which item-line shape occurs more often in the text.
    /// </summary>
    public static List<ShoppingListEntry> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var makePlaceMatches = 0;
        var teamcraftMatches = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TeamcraftItemLineRegex.IsMatch(line))
            {
                teamcraftMatches++;
            }
            else if (MakePlaceItemLineRegex.IsMatch(line))
            {
                makePlaceMatches++;
            }
        }

        return teamcraftMatches > makePlaceMatches ? ParseTeamcraft(lines) : ParseMakePlace(lines);
    }

    /// <summary>
    /// Merges newly imported entries into an existing list, summing quantities for entries that share
    /// the same category and item name. Used so multiple Teamcraft category exports (Crystals, Items,
    /// Pre crafts, ...) can be imported one after another into a single combined shopping list.
    /// </summary>
    public static List<ShoppingListEntry> Merge(IReadOnlyList<ShoppingListEntry> existing, IReadOnlyList<ShoppingListEntry> imported)
    {
        var quantities = new Dictionary<(string Category, string Name), int>();
        foreach (var entry in existing.Concat(imported))
        {
            var key = (entry.Category, entry.Name);
            quantities[key] = quantities.GetValueOrDefault(key) + entry.Quantity;
        }

        return quantities
            .Select(kv => new ShoppingListEntry { Category = kv.Key.Category, Name = kv.Key.Name, Quantity = kv.Value })
            .ToList();
    }

    /// <summary>
    /// Categories whose header contains "with dye" (e.g. "Furniture (With Dye)") are skipped, since they
    /// only break the already counted "Furniture" quantities down by dye colour and would otherwise
    /// double the totals.
    /// </summary>
    private static List<ShoppingListEntry> ParseMakePlace(string[] lines)
    {
        var quantities = new Dictionary<(string Category, string Name), int>();
        string? pendingHeaderCandidate = null;
        string? currentCategory = null;
        var skipCategory = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length >= 3 && line.All(c => c == '='))
            {
                currentCategory = pendingHeaderCandidate ?? currentCategory;
                skipCategory = currentCategory != null && currentCategory.Contains("with dye", StringComparison.OrdinalIgnoreCase);
                pendingHeaderCandidate = null;
                continue;
            }

            var match = MakePlaceItemLineRegex.Match(line);
            if (!match.Success)
            {
                pendingHeaderCandidate = line;
                continue;
            }

            pendingHeaderCandidate = null;
            if (skipCategory || currentCategory == null)
            {
                continue;
            }

            var name = match.Groups[1].Value.Trim();
            var quantity = int.Parse(match.Groups[2].Value);
            var key = (currentCategory, name);
            quantities[key] = quantities.GetValueOrDefault(key) + quantity;
        }

        return quantities
            .Select(kv => new ShoppingListEntry { Category = kv.Key.Category, Name = kv.Key.Name, Quantity = kv.Value })
            .ToList();
    }

    /// <summary>
    /// Parses a Teamcraft list export/clipboard paste. Unlike MakePlace's format, every non-item line is
    /// treated as the start of a new category (Teamcraft has no separator line), with an optional trailing
    /// " :" stripped from the header text.
    /// </summary>
    private static List<ShoppingListEntry> ParseTeamcraft(string[] lines)
    {
        var quantities = new Dictionary<(string Category, string Name), int>();
        var currentCategory = "Teamcraft";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = TeamcraftItemLineRegex.Match(line);
            if (!match.Success)
            {
                var header = line.TrimEnd(':', ' ').Trim();
                currentCategory = header.Length > 0 ? header : "Teamcraft";
                continue;
            }

            var quantity = int.Parse(match.Groups[1].Value);
            var name = match.Groups[2].Value.Trim();
            var key = (currentCategory, name);
            quantities[key] = quantities.GetValueOrDefault(key) + quantity;
        }

        return quantities
            .Select(kv => new ShoppingListEntry { Category = kv.Key.Category, Name = kv.Key.Name, Quantity = kv.Value })
            .ToList();
    }
}
