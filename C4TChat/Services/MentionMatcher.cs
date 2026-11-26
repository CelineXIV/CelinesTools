using System;
using System.Collections.Generic;
using System.Text;

namespace C4TChat.Services;

internal static class MentionMatcher
{
    private static readonly char[] WordSeparators = { ' ', '\t', '\r', '\n' };

    public static string BuildTerms(string firstName, IReadOnlyList<string> keywords)
    {
        var builder = new StringBuilder(firstName);
        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    public static bool MatchesToken(string word, string terms)
    {
        if (word.Length == 0 || string.IsNullOrEmpty(terms))
        {
            return false;
        }

        var span = terms.AsSpan();
        foreach (var range in span.Split('\n'))
        {
            if (span[range].Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TextMentions(string text, string terms)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(terms))
        {
            return false;
        }

        string[]? words = null;
        foreach (var term in terms.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (term.Contains(' '))
            {
                if (ContainsPhrase(text, term))
                {
                    return true;
                }

                continue;
            }

            words ??= text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (StripPunctuation(word).Equals(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        var index = 0;
        while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index + phrase.Length;
            var startsOnBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endsOnBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsOnBoundary && endsOnBoundary)
            {
                return true;
            }

            index++;
        }

        return false;
    }

    internal static string StripPunctuation(string word)
    {
        var start = 0;
        var end = word.Length;

        while (start < end && !char.IsLetterOrDigit(word[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(word[end - 1]))
        {
            end--;
        }

        return word[start..end];
    }
}
