using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CelinesToolkit.Services.ShoppingList;

internal static class ShoppingListParser
{
    private static readonly Regex ItemLineRegex = new(@"^(.+):\s*(\d+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a MakePlace ".list.txt" shopping list export. Categories whose header contains
    /// "with dye" (e.g. "Furniture (With Dye)") are skipped, since they only break the already
    /// counted "Furniture" quantities down by dye colour and would otherwise double the totals.
    /// </summary>
    public static List<ShoppingListEntry> Parse(string text)
    {
        var quantities = new Dictionary<(string Category, string Name), int>();
        string? pendingHeaderCandidate = null;
        string? currentCategory = null;
        var skipCategory = false;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
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

            var match = ItemLineRegex.Match(line);
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
}
