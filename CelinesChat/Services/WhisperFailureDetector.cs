using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace CelinesChat.Services;

internal sealed class WhisperFailureDetector
{
    private static readonly uint[] SinglePlaceholderRowIds = { 50, 7485, 1642 };

    private const uint InvalidNameRowId = 3803;
    private const uint PcNameAddonRowId = 3620;

    private readonly IDataManager dataManager;
    private List<Regex>? singlePlaceholderPatterns;
    private (Regex Pattern, string ExpectedType)? invalidNamePattern;
    private bool patternsLoaded;

    public WhisperFailureDetector(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public string? TryExtractTarget(string text)
    {
        EnsurePatternsLoaded();

        foreach (var pattern in singlePlaceholderPatterns!)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        if (invalidNamePattern is { } invalidName)
        {
            var match = invalidName.Pattern.Match(text);
            if (match.Success && string.Equals(match.Groups[2].Value.Trim(), invalidName.ExpectedType, System.StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private void EnsurePatternsLoaded()
    {
        if (patternsLoaded)
        {
            return;
        }

        patternsLoaded = true;
        singlePlaceholderPatterns = new List<Regex>();

        var logMessageSheet = dataManager.GetExcelSheet<LogMessage>();
        if (logMessageSheet == null)
        {
            return;
        }

        foreach (var id in SinglePlaceholderRowIds)
        {
            var row = logMessageSheet.GetRowOrDefault(id);
            if (row == null)
            {
                continue;
            }

            if (TryBuildSinglePlaceholderPattern(row.Value.Text) is { } pattern)
            {
                singlePlaceholderPatterns.Add(pattern);
            }
        }

        var invalidNameRow = logMessageSheet.GetRowOrDefault(InvalidNameRowId);
        var expectedType = dataManager.GetExcelSheet<Addon>()?.GetRowOrDefault(PcNameAddonRowId)?.Text.ExtractText();
        if (invalidNameRow != null && !string.IsNullOrEmpty(expectedType)
            && TryBuildTwoPlaceholderPattern(invalidNameRow.Value.Text) is { } twoPlaceholderPattern)
        {
            invalidNamePattern = (twoPlaceholderPattern, expectedType.Trim());
        }
    }

    private static Regex? TryBuildSinglePlaceholderPattern(ReadOnlySeString template)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        var seenPlaceholder = false;
        var seenPlaceholderEnd = false;

        foreach (var payload in template)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                if (!seenPlaceholder)
                {
                    prefix.Append(payload.ToString());
                }
                else if (!seenPlaceholderEnd)
                {
                    suffix.Append(payload.ToString());
                }
            }
            else if (!seenPlaceholder)
            {
                seenPlaceholder = true;
            }
            else
            {
                seenPlaceholderEnd = true;
            }
        }

        if (!seenPlaceholder || prefix.Length == 0)
        {
            return null;
        }

        return new Regex("^" + Regex.Escape(prefix.ToString()) + "(.+?)" + Regex.Escape(suffix.ToString()), RegexOptions.Singleline);
    }

    private static Regex? TryBuildTwoPlaceholderPattern(ReadOnlySeString template)
    {
        var parts = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        var macroCount = 0;

        foreach (var payload in template)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                if (macroCount <= 2)
                {
                    parts[macroCount].Append(payload.ToString());
                }
            }
            else
            {
                macroCount++;
            }
        }

        if (macroCount != 2)
        {
            return null;
        }

        var pattern = "^" + Regex.Escape(parts[0].ToString()) + "(.+?)" + Regex.Escape(parts[1].ToString()) + "(.+?)" + Regex.Escape(parts[2].ToString());
        return new Regex(pattern, RegexOptions.Singleline);
    }
}
