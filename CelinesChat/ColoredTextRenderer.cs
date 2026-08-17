using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

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

    internal static IEnumerable<(string Word, TextSegmentKind Kind, LinkTarget? Link, Vector4? Foreground)> BuildPlainRuns(string text, string mentionTerm)
    {
        var state = new TokenizeState();
        foreach (var (word, kind) in Tokenize(text, mentionTerm, state))
        {
            var link = TryGetUrl(word, out var url) ? new LinkTarget(url) : null;
            yield return (word, kind, link, null);
        }
    }

    /// Same as Draw/DrawRich, but computes the (word, kind, link, foreground) breakdown once per
    /// entry and reuses it on every later call instead of re-tokenizing entry.Text/entry.Payloads
    /// from scratch every frame - see ChatLogEntry.CachedRuns' own remarks for why this matters.
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
        Action<LinkTarget>? onLinkHovered = null)
    {
        entry.CachedRuns ??= entry.Payloads is { } payloads
            ? BuildRuns(payloads, mentionTerm).ToList()
            : BuildPlainRuns(entry.Text, mentionTerm).ToList();

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

    internal static IEnumerable<(string Word, TextSegmentKind Kind, LinkTarget? Link, Vector4? Foreground)> BuildRuns(IReadOnlyList<Payload> payloads, string mentionTerm)
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
                    break;
                case ITextProvider textProvider:
                    var currentForeground = foregroundStack.Count > 0 ? foregroundStack.Peek() : (Vector4?)null;
                    var currentLinkTarget = currentLink != null ? new LinkTarget(currentLink) : null;
                    foreach (var (word, kind) in Tokenize(textProvider.Text, mentionTerm, state))
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

            // Icon-glyph words need Dalamud's default font (the only one with these private-use-area
            // glyphs - see IsIconGlyphWord's own remarks) and are never batched into a run with
            // neighboring text, since a run is drawn as a single ImGui call under a single font.
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

                ImGui.PushTextWrapPos(ImGui.GetCursorScreenPos().X + wrapWidth);
                DrawUnit(word + " ", word, color, link, onLinkClicked, onLinkHovered);
                ImGui.PopTextWrapPos();
                lineWidthUsed = size.X;
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

    /// Draws one icon-glyph word using Dalamud's default font instead of whatever custom chat font
    /// is currently pushed - see IsIconGlyphWord's own remarks for why that's necessary at all.
    /// FontDefault is ImGui's io.FontDefault, which Dalamud sets to its own bundled default font
    /// (the one already proven to have these glyphs, since that's what briefly showed correctly
    /// during the one frame a font rebuild left the custom chat font not-yet-pushed).
    private static void DrawIconGlyphUnit(string display, Vector4 color)
    {
        var defaultFont = ImGui.GetIO().FontDefault;
        var pushedFont = !defaultFont.IsNull;
        if (pushedFont)
        {
            ImGui.PushFont(defaultFont);
        }

        ImGui.TextColored(color, display);

        if (pushedFont)
        {
            ImGui.PopFont();
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

    // The game encodes small decorative glyphs (cross-world/linkshell markers, map-flag icons, item
    // level badges, ...) as private-use-area characters directly in chat text, in the range Dalamud's
    // own SeIconChar enum uses (roughly U+E020-U+E0E9). Dalamud's default UI font has these glyphs -
    // the custom chat fonts this plugin lets users pick (bundled CJK/Inconsolata assets) do not, so
    // without special handling they fell back to that font's "missing glyph" placeholder (which
    // renders as a plain "=" in those particular fonts) instead of the actual icon.
    private const int IconGlyphRangeStart = 0xE020;
    private const int IconGlyphRangeEnd = 0xE0FF;

    private static bool IsIconGlyphChar(char c) => c >= IconGlyphRangeStart && c <= IconGlyphRangeEnd;

    /// Splits one whitespace-delimited word into runs of consecutive icon-glyph characters vs.
    /// everything else. The game glues these icon markers directly onto the text that follows them
    /// (no space in between - e.g. a map-flag icon immediately followed by "Labyrinthos"), so they
    /// often aren't their own whole word; a simple "is this entire word an icon" check missed those,
    /// only catching icons that happened to be standalone words with real spaces around them.
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

    internal static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm, TokenizeState state)
    {
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            foreach (var (segment, isIconGlyph) in SplitIconGlyphRuns(word))
            {
                if (isIconGlyph)
                {
                    yield return (segment, TextSegmentKind.IconGlyph);
                    continue;
                }

                yield return (segment, ClassifySegment(segment, mentionTerm, state));
            }
        }
    }

    private static TextSegmentKind ClassifySegment(string word, string mentionTerm, TokenizeState state)
    {
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
        var isEmote = !isOoc && (startedInEmote || word.Contains('*'));
        var isMention = !string.IsNullOrEmpty(mentionTerm) && string.Equals(StripPunctuation(word), mentionTerm, StringComparison.OrdinalIgnoreCase);

        return isMention ? TextSegmentKind.Mention : (isOoc ? TextSegmentKind.Ooc : (isEmote ? TextSegmentKind.Emote : TextSegmentKind.Default));
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
