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
    }

    internal static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm, TokenizeState state)
    {
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
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

            var kind = isMention ? TextSegmentKind.Mention : (isOoc ? TextSegmentKind.Ooc : (isEmote ? TextSegmentKind.Emote : TextSegmentKind.Default));
            yield return (word, kind);
        }
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
