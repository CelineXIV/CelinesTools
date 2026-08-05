using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesRPChat;

internal static class ColoredTextRenderer
{
    public static void Draw(
        string text,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        string mentionTerm = "",
        float initialLineWidthUsed = 0f,
        string highlightTerm = "",
        Vector4? highlightColor = null)
    {
        var wrapWidth = ImGui.GetContentRegionAvail().X + initialLineWidthUsed;
        var lineWidthUsed = initialLineWidthUsed;
        var startOfLine = initialLineWidthUsed <= 0f;

        foreach (var (word, kind) in Tokenize(text, mentionTerm))
        {
            var display = word + " ";
            var size = ImGui.CalcTextSize(display);

            if (!startOfLine && lineWidthUsed + size.X > wrapWidth)
            {
                startOfLine = true;
                lineWidthUsed = 0f;
            }

            if (!startOfLine)
            {
                ImGui.SameLine(0, 0);
            }

            var color = kind switch
            {
                TextSegmentKind.Mention => mentionColor,
                TextSegmentKind.Ooc => oocColor,
                TextSegmentKind.Emote => emoteColor,
                _ => defaultColor,
            };

            if (highlightTerm.Length > 0 && word.Contains(highlightTerm, StringComparison.OrdinalIgnoreCase))
            {
                color = highlightColor ?? color;
            }

            ImGui.TextColored(color, display);

            lineWidthUsed += size.X;
            startOfLine = false;
        }

        if (startOfLine)
        {
            ImGui.TextColored(defaultColor, " ");
        }
    }

    private enum TextSegmentKind
    {
        Default,
        Emote,
        Ooc,
        Mention,
    }

    private static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm)
    {
        var words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var inEmote = false;
        var inOoc = false;

        foreach (var word in words)
        {
            var startedInEmote = inEmote;
            var startedInOoc = inOoc;

            if (CountOccurrences(word, '*') % 2 == 1)
            {
                inEmote = !inEmote;
            }

            var oocOpens = CountOccurrences(word, "((");
            var oocCloses = CountOccurrences(word, "))");
            if (oocOpens > oocCloses)
            {
                inOoc = true;
            }
            else if (oocCloses > oocOpens)
            {
                inOoc = false;
            }

            var isOoc = startedInOoc || oocOpens > 0 || oocCloses > 0;
            var isEmote = !isOoc && (startedInEmote || word.Contains('*'));
            var isMention = !string.IsNullOrEmpty(mentionTerm) && string.Equals(StripPunctuation(word), mentionTerm, StringComparison.OrdinalIgnoreCase);

            var kind = isMention ? TextSegmentKind.Mention : (isOoc ? TextSegmentKind.Ooc : (isEmote ? TextSegmentKind.Emote : TextSegmentKind.Default));
            yield return (word, kind);
        }
    }

    private static string StripPunctuation(string word)
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

    private static int CountOccurrences(string text, char c)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == c)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
