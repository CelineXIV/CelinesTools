using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat;

internal static class ColoredTextRenderer
{
    internal sealed class LinkTarget
    {
        public Payload? Payload { get; }

        public string? Url { get; }

        public LinkTarget(Payload payload) => Payload = payload;

        public LinkTarget(string url) => Url = url;
    }

    private static readonly char[] UrlTrailingPunctuation = { '.', ',', ')', ']', '!', '?', ':', ';' };

    private static bool TryGetUrl(string word, out string url)
    {
        var candidate = word.TrimEnd(UrlTrailingPunctuation);

        if (candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        if ((candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Contains('.'))
        {
            url = candidate;
            return true;
        }

        url = string.Empty;
        return false;
    }

    public static void Draw(
        string text,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        float availableWidth,
        string mentionTerm = "",
        float initialLineWidthUsed = 0f,
        string highlightTerm = "",
        Vector4? highlightColor = null,
        Action<LinkTarget>? onLinkClicked = null)
    {
        DrawSequence(
            BuildPlainRuns(text, mentionTerm),
            defaultColor, emoteColor, oocColor, mentionColor, linkColor,
            availableWidth, initialLineWidthUsed, highlightTerm, highlightColor, onLinkClicked);
    }

    internal static IEnumerable<(string Word, TextSegmentKind Kind, LinkTarget? Link, Vector4? Foreground)> BuildPlainRuns(string text, string mentionTerm, bool allowRpFormatting = true)
    {
        var state = new TokenizeState();
        foreach (var (word, kind) in Tokenize(text, mentionTerm, state, allowRpFormatting))
        {
            var link = TryGetUrl(word, out var url) ? new LinkTarget(url) : null;
            yield return (word, kind, link, null);
        }
    }

    public static void DrawForEntry(
        Services.ChatLogEntry entry,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        float availableWidth,
        string mentionTerm,
        float initialLineWidthUsed,
        string highlightTerm,
        Vector4? highlightColor,
        Action<LinkTarget> onLinkClicked,
        Action<LinkTarget>? onLinkHovered = null,
        bool allowRpFormatting = true)
    {
        if (entry.CachedRuns == null
            || entry.CachedRunsMentionTerm != mentionTerm
            || entry.CachedRunsRpFormatting != allowRpFormatting)
        {
            entry.CachedRuns = entry.Payloads is { } payloads
                ? BuildRuns(payloads, mentionTerm, allowRpFormatting).ToList()
                : BuildPlainRuns(entry.Text, mentionTerm, allowRpFormatting).ToList();
            entry.CachedRunsMentionTerm = mentionTerm;
            entry.CachedRunsRpFormatting = allowRpFormatting;
        }

        DrawSequence(
            entry.CachedRuns,
            defaultColor, emoteColor, oocColor, mentionColor, linkColor,
            availableWidth, initialLineWidthUsed, highlightTerm, highlightColor, onLinkClicked, onLinkHovered);
    }

    public static void DrawRich(
        IReadOnlyList<Payload> payloads,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        float availableWidth,
        string mentionTerm,
        float initialLineWidthUsed,
        string highlightTerm,
        Vector4? highlightColor,
        Action<LinkTarget> onLinkClicked,
        Action<LinkTarget>? onLinkHovered = null)
    {
        DrawSequence(
            BuildRuns(payloads, mentionTerm),
            defaultColor, emoteColor, oocColor, mentionColor, linkColor,
            availableWidth, initialLineWidthUsed, highlightTerm, highlightColor, onLinkClicked, onLinkHovered);
    }

    internal static IEnumerable<(string Word, TextSegmentKind Kind, LinkTarget? Link, Vector4? Foreground)> BuildRuns(IReadOnlyList<Payload> payloads, string mentionTerm, bool allowRpFormatting = true)
    {
        var state = new TokenizeState();
        Payload? currentLink = null;
        var foregroundStack = new Stack<Vector4>();

        foreach (var payload in payloads)
        {
            switch (payload)
            {
                case UIForegroundPayload foreground:
                    if (foreground.IsEnabled)
                    {
                        foregroundStack.Push(RgbaToVector4(foreground.RGBA));
                    }
                    else if (foregroundStack.Count > 0)
                    {
                        foregroundStack.Pop();
                    }

                    break;
                case MapLinkPayload or ItemPayload or DalamudLinkPayload:
                    currentLink = payload;
                    break;
                case RawPayload raw when raw.Equals(RawPayload.LinkTerminator):
                    currentLink = null;

                    foregroundStack.Clear();
                    break;
                case ITextProvider textProvider:
                    var currentForeground = foregroundStack.Count > 0 ? foregroundStack.Peek() : (Vector4?)null;
                    var currentLinkTarget = currentLink != null ? new LinkTarget(currentLink) : null;
                    foreach (var (word, kind) in Tokenize(textProvider.Text, mentionTerm, state, allowRpFormatting))
                    {
                        var link = currentLinkTarget ?? (TryGetUrl(word, out var url) ? new LinkTarget(url) : null);
                        yield return (word, kind, link, currentForeground);
                    }

                    break;
            }
        }
    }

    private static Vector4 RgbaToVector4(uint rgba)
    {
        var r = (byte)((rgba & 0xFF000000) >> 24);
        var g = (byte)((rgba & 0xFF0000) >> 16);
        var b = (byte)((rgba & 0xFF00) >> 8);
        var a = (byte)(rgba & 0xFF);
        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    internal static Vector4 ResolveColor(Vector4? foreground, LinkTarget? link, TextSegmentKind kind, Vector4 defaultColor, Vector4 emoteColor, Vector4 oocColor, Vector4 mentionColor, Vector4 linkColor)
    {
        return foreground ?? (link != null
            ? linkColor
            : kind switch
            {
                TextSegmentKind.Mention => mentionColor,
                TextSegmentKind.Ooc => oocColor,
                TextSegmentKind.Emote => emoteColor,
                _ => defaultColor,
            });
    }

    private static void DrawSequence(
        IEnumerable<(string Word, TextSegmentKind Kind, LinkTarget? Link, Vector4? Foreground)> words,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        float availableWidth,
        float initialLineWidthUsed,
        string highlightTerm,
        Vector4? highlightColor,
        Action<LinkTarget>? onLinkClicked,
        Action<LinkTarget>? onLinkHovered = null)
    {
        var wrapWidth = availableWidth - ImGui.GetStyle().ScrollbarSize - 6f;
        var lineWidthUsed = initialLineWidthUsed;
        var startOfLine = initialLineWidthUsed <= 0f;

        List<string>? runWords = null;
        var runWidth = 0f;
        Vector4 runColor = default;
        LinkTarget? runLink = null;

        void FlushRun()
        {
            if (runWords == null)
            {
                return;
            }

            if (!startOfLine)
            {
                ImGui.SameLine(0, 0);
            }

            var bareText = runWords.Count == 1 ? runWords[0] : string.Join(' ', runWords);
            DrawUnit(bareText + " ", bareText, runColor, runLink, onLinkClicked, onLinkHovered);
            lineWidthUsed += runWidth;
            startOfLine = false;
            runWords = null;
            runWidth = 0f;
        }

        foreach (var (word, kind, link, foreground) in words)
        {
            var size = ImGui.CalcTextSize(word + " ");

            var color = ResolveColor(foreground, link, kind, defaultColor, emoteColor, oocColor, mentionColor, linkColor);

            if (highlightTerm.Length > 0 && word.Contains(highlightTerm, StringComparison.OrdinalIgnoreCase))
            {
                color = highlightColor ?? color;
            }

            if (kind == TextSegmentKind.IconGlyph)
            {
                FlushRun();

                var fitsIcon = lineWidthUsed + size.X <= wrapWidth;
                if (!fitsIcon)
                {
                    if (!startOfLine)
                    {
                        ImGui.NewLine();
                    }

                    startOfLine = true;
                    lineWidthUsed = 0f;
                }

                if (!startOfLine)
                {
                    ImGui.SameLine(0, 0);
                }

                DrawIconGlyphUnit(word + " ", color);
                lineWidthUsed += size.X;
                startOfLine = false;
                continue;
            }

            if (size.X > wrapWidth)
            {
                FlushRun();

                if (!startOfLine)
                {
                    ImGui.NewLine();
                }

                startOfLine = true;
                lineWidthUsed = 0f;

                var remaining = word;
                while (remaining.Length > 0)
                {
                    var take = FittingLength(remaining, wrapWidth);
                    var piece = remaining[..take];
                    remaining = remaining[take..];
                    var last = remaining.Length == 0;
                    DrawUnit(last ? piece + " " : piece, piece, color, link, onLinkClicked, onLinkHovered);
                    if (last)
                    {
                        lineWidthUsed = ImGui.CalcTextSize(piece + " ").X;
                    }
                }

                startOfLine = false;
                continue;
            }

            var sameRun = runWords != null && color == runColor && link == runLink;
            var fitsCurrentLine = lineWidthUsed + runWidth + size.X <= wrapWidth;

            if (!fitsCurrentLine)
            {
                FlushRun();
                startOfLine = true;
                lineWidthUsed = 0f;
                sameRun = false;
            }

            if (sameRun)
            {
                runWords!.Add(word);
                runWidth += size.X;
                continue;
            }

            FlushRun();
            runWords = new List<string> { word };
            runWidth = size.X;
            runColor = color;
            runLink = link;
        }

        FlushRun();

        if (startOfLine)
        {
            ImGui.TextColored(defaultColor, " ");
        }
    }

    private static int FittingLength(string text, float width)
    {
        var low = 1;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= width)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (low < text.Length && char.IsHighSurrogate(text[low - 1]))
        {
            low = low > 1 ? low - 1 : low + 1;
        }

        return Math.Clamp(low, 1, text.Length);
    }

    private static void DrawUnit(string display, string bareText, Vector4 color, LinkTarget? link, Action<LinkTarget>? onLinkClicked, Action<LinkTarget>? onLinkHovered)
    {
        ImGui.TextColored(color, display);

        if (link == null || !ImGui.IsItemHovered())
        {
            return;
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        onLinkHovered?.Invoke(link);

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var underlineEndX = rectMin.X + ImGui.CalcTextSize(bareText).X;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(rectMin.X, rectMax.Y - 2),
            new Vector2(underlineEndX, rectMax.Y - 2),
            ImGui.GetColorU32(color));

        if (ImGui.IsItemClicked())
        {
            onLinkClicked?.Invoke(link);
        }
    }

    private static void DrawIconGlyphUnit(string display, Vector4 color)
    {
        var defaultFont = ImGui.GetIO().FontDefault;
        using (ImRaii.PushFont(defaultFont, !defaultFont.IsNull))
        {
            ImGui.TextColored(color, display);
        }
    }

    internal sealed class TokenizeState
    {
        public bool InEmote;

        public int OocDepth;
    }

    internal enum TextSegmentKind
    {
        Default,
        Emote,
        Ooc,
        Mention,
        IconGlyph,
    }

    private const int IconGlyphRangeStart = 0xE020;
    private const int IconGlyphRangeEnd = 0xE0FF;

    private static bool IsIconGlyphChar(char c) => c >= IconGlyphRangeStart && c <= IconGlyphRangeEnd;

    private static IEnumerable<(string Text, bool IsIconGlyph)> SplitIconGlyphRuns(string word)
    {
        var start = 0;
        var current = IsIconGlyphChar(word[0]);

        for (var i = 1; i < word.Length; i++)
        {
            var isIcon = IsIconGlyphChar(word[i]);
            if (isIcon != current)
            {
                yield return (word[start..i], current);
                start = i;
                current = isIcon;
            }
        }

        yield return (word[start..], current);
    }

    internal static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm, TokenizeState state, bool allowRpFormatting = true)
    {
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var treatAsTypoCorrection = allowRpFormatting && words.Length == 1 && IsLikelyTypoCorrectionMarker(words[0]);

        foreach (var word in words)
        {
            foreach (var (segment, isIconGlyph) in SplitIconGlyphRuns(word))
            {
                if (isIconGlyph)
                {
                    yield return (segment, TextSegmentKind.IconGlyph);
                    continue;
                }

                yield return (segment, ClassifySegment(segment, mentionTerm, state, allowRpFormatting && !treatAsTypoCorrection));
            }
        }
    }

    private static bool IsLikelyTypoCorrectionMarker(string word)
        => word.Length > 1 && word[0] == '*' && word.IndexOf('*', 1) < 0;

    private static TextSegmentKind ClassifySegment(string word, string mentionTerm, TokenizeState state, bool allowRpFormatting = true)
    {
        var isMention = !string.IsNullOrEmpty(mentionTerm) && Services.MentionMatcher.MatchesToken(StripPunctuation(word), mentionTerm);
        if (isMention)
        {
            return TextSegmentKind.Mention;
        }

        if (!allowRpFormatting)
        {
            return TextSegmentKind.Default;
        }

        var startedInEmote = state.InEmote;

        if (CountOccurrences(word, '*') % 2 == 1)
        {
            state.InEmote = !state.InEmote;
        }

        var startedInOoc = state.OocDepth > 0;
        var touchedOoc = startedInOoc;
        if (!IsMessagePartCounter(word))
        {
            foreach (var ch in word)
            {
                if (ch == '(')
                {
                    state.OocDepth++;
                    touchedOoc = true;
                }
                else if (ch == ')' && state.OocDepth > 0)
                {
                    state.OocDepth--;
                    touchedOoc = true;
                }
            }
        }

        var isOoc = touchedOoc;
        var isEmote = !isOoc && IsWholeWordEmote(word, startedInEmote, state.InEmote);

        return isOoc ? TextSegmentKind.Ooc : (isEmote ? TextSegmentKind.Emote : TextSegmentKind.Default);
    }

    private static bool IsWholeWordEmote(string word, bool startedInEmote, bool endedInEmote)
    {
        var firstStar = word.IndexOf('*');
        if (firstStar < 0)
        {
            return startedInEmote;
        }

        var lastStar = word.LastIndexOf('*');
        var leftDirty = !startedInEmote && firstStar > 0;
        var rightDirty = !endedInEmote && lastStar < word.Length - 1;
        return !leftDirty && !rightDirty;
    }

    private static bool IsMessagePartCounter(string word)
    {
        if (word.Length < 5 || word[0] != '(' || word[^1] != ')')
        {
            return false;
        }

        var slashIndex = -1;
        for (var i = 1; i < word.Length - 1; i++)
        {
            if (word[i] == '/')
            {
                if (slashIndex >= 0)
                {
                    return false;
                }

                slashIndex = i;
            }
            else if (!char.IsDigit(word[i]))
            {
                return false;
            }
        }

        return slashIndex > 1 && slashIndex < word.Length - 2;
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
}
