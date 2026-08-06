using System;
using System.Collections.Generic;
using System.Text;

namespace CelinesChat;

internal static class MessageSplitter
{
    private const int ReservedSuffixLength = 8;

    public static List<string> BuildMessages(string fullText, int maxLength)
    {
        maxLength = Math.Max(10, maxLength);

        var chunks = SplitParagraphs(fullText, maxLength);
        if (chunks.Count <= 1)
        {
            return chunks;
        }

        var reservedMax = Math.Max(1, maxLength - ReservedSuffixLength);
        chunks = SplitParagraphs(fullText, reservedMax);

        var numbered = new List<string>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            numbered.Add($"{chunks[i]} ({i + 1}/{chunks.Count})");
        }

        return numbered;
    }

    private static List<string> SplitParagraphs(string fullText, int maxLength)
    {
        var result = new List<string>();
        var paragraphs = fullText.Replace("\r\n", "\n").Split('\n');

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            result.AddRange(SplitParagraph(trimmed, maxLength));
        }

        return result;
    }

    private static List<string> SplitParagraph(string paragraph, int maxLength)
    {
        var result = new List<string>();
        if (paragraph.Length <= maxLength)
        {
            result.Add(paragraph);
            return result;
        }

        // Reserve 2 characters of headroom on every chunk: a chunk in the middle of a long
        // "*emote*" span can need a synthetic "*" appended (to close it for its left boundary)
        // AND a synthetic "*" prepended (to reopen it for its right boundary) at the same time.
        // This guarantees FixEmoteBoundary's close/reopen fallback always fits within maxLength.
        var effectiveMax = Math.Max(1, maxLength - 2);

        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            var remainingWord = word;

            while (remainingWord.Length > effectiveMax)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                result.Add(remainingWord.Substring(0, effectiveMax));
                remainingWord = remainingWord.Substring(effectiveMax);
            }

            var candidateLength = current.Length == 0 ? remainingWord.Length : current.Length + 1 + remainingWord.Length;
            if (candidateLength > effectiveMax && current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(remainingWord);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        if (result.Count > 1)
        {
            for (var i = 0; i < result.Count - 1; i++)
            {
                FixEmoteBoundary(result, i, maxLength);
            }
        }

        return result;
    }

    /// <summary>
    /// If chunk <paramref name="i"/> ends in the middle of an open "*emote*" span, tries to keep the
    /// emote intact by moving its trailing words to the next chunk. If that would make either chunk
    /// too long, it instead closes the emote at the end of this chunk and reopens it at the start of
    /// the next one with a synthetic "*", since otherwise the continuation would render as plain
    /// (white) text instead of the emote colour once the messages are sent as separate chat lines.
    /// Thanks to the headroom reserved in <see cref="SplitParagraph"/>, the close/reopen fallback is
    /// always able to fit within <paramref name="maxLength"/>.
    /// </summary>
    private static void FixEmoteBoundary(List<string> chunks, int i, int maxLength)
    {
        if (!EndsInsideEmote(chunks[i]))
        {
            return;
        }

        if (TryMoveTrailingEmoteWords(chunks, i, maxLength))
        {
            return;
        }

        TryCloseAndReopenEmote(chunks, i, maxLength);
    }

    private static bool TryMoveTrailingEmoteWords(List<string> chunks, int i, int maxLength)
    {
        var words = chunks[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var insideEmote = false;
        var lastBalancedIndex = -1;

        for (var w = 0; w < words.Length; w++)
        {
            if (CountAsterisks(words[w]) % 2 == 1)
            {
                insideEmote = !insideEmote;
            }

            if (!insideEmote)
            {
                lastBalancedIndex = w;
            }
        }

        if (!insideEmote || lastBalancedIndex < 0 || lastBalancedIndex >= words.Length - 1)
        {
            return false;
        }

        var moveWords = string.Join(' ', words, lastBalancedIndex + 1, words.Length - lastBalancedIndex - 1);
        var candidateNext = moveWords + " " + chunks[i + 1];

        if (candidateNext.Length > maxLength)
        {
            return false;
        }

        chunks[i] = string.Join(' ', words, 0, lastBalancedIndex + 1);
        chunks[i + 1] = candidateNext;
        return true;
    }

    private static bool TryCloseAndReopenEmote(List<string> chunks, int i, int maxLength)
    {
        if (chunks[i].Length + 1 > maxLength || chunks[i + 1].Length + 1 > maxLength)
        {
            return false;
        }

        chunks[i] += "*";
        chunks[i + 1] = "*" + chunks[i + 1];
        return true;
    }

    private static bool EndsInsideEmote(string chunk)
    {
        var insideEmote = false;
        foreach (var word in chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CountAsterisks(word) % 2 == 1)
            {
                insideEmote = !insideEmote;
            }
        }

        return insideEmote;
    }

    private static int CountAsterisks(string word)
    {
        var count = 0;
        foreach (var c in word)
        {
            if (c == '*')
            {
                count++;
            }
        }

        return count;
    }
}
