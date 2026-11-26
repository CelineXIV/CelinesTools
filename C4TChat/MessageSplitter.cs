using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C4TChat.Services;

namespace C4TChat;

internal readonly struct ChunkSegment
{
    public int ChunkStart { get; }
    public int SourceStart { get; }
    public int Length { get; }

    public ChunkSegment(int chunkStart, int sourceStart, int length)
    {
        ChunkStart = chunkStart;
        SourceStart = sourceStart;
        Length = length;
    }
}

internal sealed class MappedChunk
{
    public string Text { get; }
    public IReadOnlyList<ChunkSegment> Segments { get; }

    public MappedChunk(string text, IReadOnlyList<ChunkSegment> segments)
    {
        Text = text;
        Segments = segments;
    }

    public int MapToSource(int chunkOffset)
    {
        if (Segments.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];
            if (chunkOffset < seg.ChunkStart)
            {
                if (i > 0)
                {
                    var prev = Segments[i - 1];
                    var prevEndChunk = prev.ChunkStart + prev.Length;
                    var prevEndSource = prev.SourceStart + prev.Length;
                    return chunkOffset - prevEndChunk <= seg.ChunkStart - chunkOffset
                        ? prevEndSource
                        : seg.SourceStart;
                }

                return seg.SourceStart;
            }

            if (chunkOffset < seg.ChunkStart + seg.Length)
            {
                return seg.SourceStart + (chunkOffset - seg.ChunkStart);
            }
        }

        var last = Segments[^1];
        return last.SourceStart + last.Length;
    }
}

internal sealed class ChunkBuilder
{
    private readonly StringBuilder text;
    private readonly List<ChunkSegment> segments;

    public ChunkBuilder()
    {
        text = new StringBuilder();
        segments = new List<ChunkSegment>();
    }

    private ChunkBuilder(StringBuilder text, List<ChunkSegment> segments)
    {
        this.text = text;
        this.segments = segments;
    }

    public int Length => text.Length;

    public int ByteLength => Encoding.UTF8.GetByteCount(text.ToString());

    public int SegmentCount => segments.Count;

    public void AppendVerbatim(string s, int sourceStart)
    {
        if (s.Length == 0)
        {
            return;
        }

        segments.Add(new ChunkSegment(text.Length, sourceStart, s.Length));
        text.Append(s);
    }

    public void AppendSynthetic(string s) => text.Append(s);

    public void PrependSynthetic(string s)
    {
        if (s.Length == 0)
        {
            return;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            segments[i] = new ChunkSegment(seg.ChunkStart + s.Length, seg.SourceStart, seg.Length);
        }

        text.Insert(0, s);
    }

    public string SegmentTextForParity(int index)
    {
        var s = segments[index];
        var start = index == 0 ? 0 : s.ChunkStart;
        var end = index == segments.Count - 1 ? text.Length : s.ChunkStart + s.Length;
        return text.ToString(start, end - start);
    }

    public string PeekTrailingText(int fromSegmentIndex)
    {
        var start = segments[fromSegmentIndex].ChunkStart;
        return text.ToString(start, text.Length - start);
    }

    public ChunkBuilder ExtractTrailingSegments(int fromSegmentIndex)
    {
        var startChunkOffset = segments[fromSegmentIndex].ChunkStart;
        var extractedText = text.ToString(startChunkOffset, text.Length - startChunkOffset);
        var extractedSegments = new List<ChunkSegment>();
        for (var i = fromSegmentIndex; i < segments.Count; i++)
        {
            var s = segments[i];
            extractedSegments.Add(new ChunkSegment(s.ChunkStart - startChunkOffset, s.SourceStart, s.Length));
        }

        var dropSeparator = fromSegmentIndex > 0 ? 1 : 0;
        text.Length = Math.Max(0, startChunkOffset - dropSeparator);
        segments.RemoveRange(fromSegmentIndex, segments.Count - fromSegmentIndex);

        return new ChunkBuilder(new StringBuilder(extractedText), extractedSegments);
    }

    public void PrependFrom(ChunkBuilder other, string separator)
    {
        var offset = other.text.Length + separator.Length;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            segments[i] = new ChunkSegment(s.ChunkStart + offset, s.SourceStart, s.Length);
        }

        var newSegments = new List<ChunkSegment>(other.segments.Count + segments.Count);
        newSegments.AddRange(other.segments);
        newSegments.AddRange(segments);
        segments.Clear();
        segments.AddRange(newSegments);

        text.Insert(0, separator);
        text.Insert(0, other.text.ToString());
    }

    public MappedChunk Build() => new(text.ToString(), segments);
}

internal static class MessageSplitter
{
    public const int MaxChatLineBytes = 500;

    public static List<string> BuildMessages(string fullText, int maxLength, int prefixBytes = 0)
        => BuildMappedMessages(fullText, maxLength, prefixBytes).Select(c => c.Text).ToList();

    public static List<MappedChunk> BuildMappedMessages(string fullText, int maxLength, int prefixBytes = 0)
    {
        var text = Sanitize(fullText);
        var limit = new Limit(Math.Max(10, maxLength), Math.Max(10, MaxChatLineBytes - prefixBytes));

        var builders = SplitParagraphs(text, limit);
        if (builders.Count <= 1)
        {
            return builders.Select(b => b.Build()).ToList();
        }

        var suffixLength = SuffixLength(builders.Count);
        while (true)
        {
            builders = SplitParagraphs(text, limit.Shrink(suffixLength));
            var needed = SuffixLength(builders.Count);
            if (needed <= suffixLength)
            {
                break;
            }

            suffixLength = needed;
        }

        var result = new List<MappedChunk>(builders.Count);
        for (var i = 0; i < builders.Count; i++)
        {
            builders[i].AppendSynthetic($" ({i + 1}/{builders.Count})");
            result.Add(builders[i].Build());
        }

        return result;
    }

    private static int SuffixLength(int count) => 4 + (2 * count.ToString().Length);

    private static string Sanitize(string text)
    {
        char[]? chars = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isLineBreak = c == '\n' || (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n');
            if (isLineBreak || !char.IsControl(c))
            {
                continue;
            }

            chars ??= text.ToCharArray();
            chars[i] = ' ';
        }

        return chars != null ? new string(chars) : text;
    }

    private static List<ChunkBuilder> SplitParagraphs(string fullText, Limit limit)
    {
        var result = new List<ChunkBuilder>();

        foreach (var (line, lineStart) in SplitLinesWithOffsets(fullText))
        {
            var leadingTrim = line.Length - line.TrimStart().Length;
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            result.AddRange(SplitParagraph(trimmed, lineStart + leadingTrim, limit));
        }

        return result;
    }

    private static List<(string Line, int Start)> SplitLinesWithOffsets(string fullText)
    {
        var result = new List<(string, int)>();
        var start = 0;
        for (var i = 0; i <= fullText.Length; i++)
        {
            if (i == fullText.Length || fullText[i] == '\n')
            {
                var lineEnd = i;
                if (lineEnd > start && fullText[lineEnd - 1] == '\r')
                {
                    lineEnd--;
                }

                result.Add((fullText[start..lineEnd], start));
                start = i + 1;
            }
        }

        return result;
    }

    private static List<ChunkBuilder> SplitParagraph(string paragraph, int paragraphSourceStart, Limit limit)
    {
        var result = new List<ChunkBuilder>();
        if (limit.Fits(paragraph))
        {
            var single = new ChunkBuilder();
            single.AppendVerbatim(paragraph, paragraphSourceStart);
            result.Add(single);
            return result;
        }

        var packLimit = limit.Shrink(2);
        var words = SplitWordsWithOffsets(paragraph);
        var current = new ChunkBuilder();
        var currentBytes = 0;

        foreach (var (word, wordOffset) in words)
        {
            var remainingWord = word;
            var remainingSourceStart = paragraphSourceStart + wordOffset;
            var remainingBytes = Encoding.UTF8.GetByteCount(remainingWord);

            while (!packLimit.Fits(remainingWord.Length, remainingBytes))
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = new ChunkBuilder();
                    currentBytes = 0;
                }

                var cutLength = LongestFittingPrefix(remainingWord, packLimit);
                var cut = new ChunkBuilder();
                cut.AppendVerbatim(remainingWord[..cutLength], remainingSourceStart);
                result.Add(cut);

                remainingWord = remainingWord[cutLength..];
                remainingSourceStart += cutLength;
                remainingBytes = Encoding.UTF8.GetByteCount(remainingWord);
            }

            if (remainingWord.Length == 0)
            {
                continue;
            }

            if (current.Length > 0 && !packLimit.Fits(current.Length + 1 + remainingWord.Length, currentBytes + 1 + remainingBytes))
            {
                result.Add(current);
                current = new ChunkBuilder();
                currentBytes = 0;
            }

            if (current.Length > 0)
            {
                current.AppendSynthetic(" ");
                currentBytes++;
            }

            current.AppendVerbatim(remainingWord, remainingSourceStart);
            currentBytes += remainingBytes;
        }

        if (current.Length > 0)
        {
            result.Add(current);
        }

        if (result.Count > 1)
        {
            for (var i = 0; i < result.Count - 1; i++)
            {
                FixEmoteBoundary(result, i, limit);
            }
        }

        return result;
    }

    private static List<(string Word, int Offset)> SplitWordsWithOffsets(string text)
    {
        var result = new List<(string, int)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && text[i] == ' ')
            {
                i++;
            }

            if (i >= text.Length)
            {
                break;
            }

            var start = i;
            while (i < text.Length && text[i] != ' ')
            {
                if (text[i] == '[' && MessageMarkerEncoder.TryGetMarkerEnd(text, i, out var markerEnd))
                {
                    i = markerEnd;
                    continue;
                }

                i++;
            }

            result.Add((text[start..i], start));
        }

        return result;
    }

    private static int LongestFittingPrefix(string word, Limit limit)
    {
        if (MessageMarkerEncoder.TryGetMarkerEnd(word, 0, out var leadingMarkerEnd))
        {
            var markerBytes = Encoding.UTF8.GetByteCount(word.AsSpan(0, leadingMarkerEnd));
            if (!limit.Fits(leadingMarkerEnd, markerBytes) && markerBytes <= limit.MaxBytes)
            {
                return leadingMarkerEnd;
            }
        }

        var chars = 0;
        var bytes = 0;
        while (chars < word.Length)
        {
            var isPair = char.IsHighSurrogate(word[chars]) && chars + 1 < word.Length && char.IsLowSurrogate(word[chars + 1]);
            var unitChars = isPair ? 2 : 1;
            var unitBytes = isPair ? 4 : Utf8Length(word[chars]);
            if (!limit.Fits(chars + unitChars, bytes + unitBytes))
            {
                break;
            }

            chars += unitChars;
            bytes += unitBytes;
        }

        for (var i = 0; i < chars; i++)
        {
            if (word[i] != '[' || !MessageMarkerEncoder.TryGetMarkerEnd(word, i, out var markerEnd))
            {
                continue;
            }

            if (markerEnd > chars && i > 0)
            {
                chars = i;
                break;
            }

            i = markerEnd - 1;
        }

        if (chars == 0)
        {
            chars = char.IsHighSurrogate(word[0]) && word.Length > 1 && char.IsLowSurrogate(word[1]) ? 2 : 1;
        }

        return chars;
    }

    private static int Utf8Length(char c) => c < 0x80 ? 1 : c < 0x800 ? 2 : 3;

    private static void FixEmoteBoundary(List<ChunkBuilder> chunks, int i, Limit limit)
    {
        if (!EndsInsideEmote(chunks[i]))
        {
            return;
        }

        if (TryMoveTrailingEmoteWords(chunks, i, limit))
        {
            return;
        }

        TryCloseAndReopenEmote(chunks, i, limit);
    }

    private static bool TryMoveTrailingEmoteWords(List<ChunkBuilder> chunks, int i, Limit limit)
    {
        var builder = chunks[i];
        var count = builder.SegmentCount;
        var insideEmote = false;
        var lastBalancedIndex = -1;

        for (var w = 0; w < count; w++)
        {
            if (CountAsterisks(builder.SegmentTextForParity(w)) % 2 == 1)
            {
                insideEmote = !insideEmote;
            }

            if (!insideEmote)
            {
                lastBalancedIndex = w;
            }
        }

        if (!insideEmote || lastBalancedIndex < 0 || lastBalancedIndex >= count - 1)
        {
            return false;
        }

        var trailingPreview = builder.PeekTrailingText(lastBalancedIndex + 1);
        var candidateChars = trailingPreview.Length + 1 + chunks[i + 1].Length;
        var candidateBytes = Encoding.UTF8.GetByteCount(trailingPreview) + 1 + chunks[i + 1].ByteLength;
        if (!limit.Shrink(1).Fits(candidateChars, candidateBytes))
        {
            return false;
        }

        var moved = builder.ExtractTrailingSegments(lastBalancedIndex + 1);
        chunks[i + 1].PrependFrom(moved, " ");
        return true;
    }

    private static bool TryCloseAndReopenEmote(List<ChunkBuilder> chunks, int i, Limit limit)
    {
        if (!limit.Fits(chunks[i].Length + 1, chunks[i].ByteLength + 1)
            || !limit.Fits(chunks[i + 1].Length + 1, chunks[i + 1].ByteLength + 1))
        {
            return false;
        }

        chunks[i].AppendSynthetic("*");
        chunks[i + 1].PrependSynthetic("*");
        return true;
    }

    private static bool EndsInsideEmote(ChunkBuilder chunk)
    {
        var insideEmote = false;
        for (var w = 0; w < chunk.SegmentCount; w++)
        {
            if (CountAsterisks(chunk.SegmentTextForParity(w)) % 2 == 1)
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

    private readonly record struct Limit(int MaxChars, int MaxBytes)
    {
        public bool Fits(string text) => Fits(text.Length, Encoding.UTF8.GetByteCount(text));

        public bool Fits(int chars, int bytes) => chars <= MaxChars && bytes <= MaxBytes;

        public Limit Shrink(int amount) => new(Math.Max(1, MaxChars - amount), Math.Max(1, MaxBytes - amount));
    }
}
