using System;
using System.Collections.Generic;
using System.Text;

namespace CelinesRPChat;

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

        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            var remainingWord = word;

            while (remainingWord.Length > maxLength)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                result.Add(remainingWord.Substring(0, maxLength));
                remainingWord = remainingWord.Substring(maxLength);
            }

            var candidateLength = current.Length == 0 ? remainingWord.Length : current.Length + 1 + remainingWord.Length;
            if (candidateLength > maxLength && current.Length > 0)
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
            RebalanceEmoteBoundaries(result, maxLength);
        }

        return result;
    }

    private static void RebalanceEmoteBoundaries(List<string> chunks, int maxLength)
    {
        for (var i = 0; i < chunks.Count - 1; i++)
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
                continue;
            }

            var moveWords = string.Join(' ', words, lastBalancedIndex + 1, words.Length - lastBalancedIndex - 1);
            var candidateNext = moveWords + " " + chunks[i + 1];

            if (candidateNext.Length > maxLength)
            {
                continue;
            }

            chunks[i] = string.Join(' ', words, 0, lastBalancedIndex + 1);
            chunks[i + 1] = candidateNext;
        }
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
