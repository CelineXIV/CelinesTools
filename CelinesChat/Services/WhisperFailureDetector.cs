using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace CelinesChat.Services;

/// <summary>
/// The game only ever gives IChatGui a flattened XivChatType.ErrorMessage string for a failed
/// /tell (offline/not found, in a restricted area, on another World, in PvP, an invalid name, ...) -
/// no player payload, and no ChatType-based way to route it to the right whisper tab the way every
/// other whisper entry gets via TellIncoming/TellOutgoing + Sender.
///
/// Rather than hardcoding the English (or any other single language's) wording to parse the player
/// name back out - which would only work for that one client language - this builds match patterns
/// straight from the actual LogMessage/Addon sheet rows Dalamud's own data manager exposes, in
/// whatever language the client is currently running. Each relevant row embeds the player's name (and,
/// for the "not a valid PC name" case, a second, reused-elsewhere noun) as macro placeholders between
/// fixed text; splitting the row's payloads there and regex-escaping the fixed parts turns the
/// template back into "prefix(name)suffix", matching a real runtime message regardless of language -
/// verified directly against the game's own English and German data before shipping.
/// </summary>
internal sealed class WhisperFailureDetector
{
    // "Message to <name> could not be sent." (target offline/not found), its "on another World and
    // not on your friend list" variant, and the PvP-specific wording - the other failure-reason rows
    // (e.g. "recipient is in a restricted area") don't name the player at all, so there's nothing to
    // extract from them and they're not included here.
    private static readonly uint[] SinglePlaceholderRowIds = { 50, 7485, 1642 };

    // "<name> is not a valid <type>." - reused by the game for many different validation failures,
    // not just an invalid /tell target, so the second placeholder (<type>) has to actually be checked
    // against Addon row 3620 ("PC name") rather than just assumed, or this would misroute unrelated
    // "X is not a valid <something else>" errors into a whisper tab too.
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

    /// Returns the player name a failed-/tell notice is about, or null if `text` doesn't match any
    /// known failure wording (i.e. it's an unrelated error message).
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
                // A second non-text payload (e.g. row 7485's trailing <br>/conditional text) marks
                // where the name placeholder's own "slot" ends - only the text directly bordering it
                // is needed to anchor the match, not whatever comes after.
                seenPlaceholderEnd = true;
            }
        }

        // No placeholder at all means this row doesn't name a player (e.g. "recipient is in a
        // restricted area") - nothing to build a capture group around.
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

        // A different shape than expected (this row means something else entirely in some
        // languages - verified true for German, where row 3803 is an unrelated syntax-error
        // message) - returning null here just means this specific pattern never matches for that
        // client language, not a wrong or misleading one.
        if (macroCount != 2)
        {
            return null;
        }

        var pattern = "^" + Regex.Escape(parts[0].ToString()) + "(.+?)" + Regex.Escape(parts[1].ToString()) + "(.+?)" + Regex.Escape(parts[2].ToString());
        return new Regex(pattern, RegexOptions.Singleline);
    }
}
