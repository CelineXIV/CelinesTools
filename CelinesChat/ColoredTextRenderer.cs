using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CelinesChat;

internal static class ColoredTextRenderer
{
    public static void Draw(
        string text,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        float availableWidth,
        string mentionTerm = "",
        float initialLineWidthUsed = 0f,
        string highlightTerm = "",
        Vector4? highlightColor = null)
    {
        var state = new TokenizeState();
        DrawSequence(
            Enumerate(Tokenize(text, mentionTerm, state)),
            defaultColor, emoteColor, oocColor, mentionColor, default,
            availableWidth, initialLineWidthUsed, highlightTerm, highlightColor, null);

        static IEnumerable<(string Word, TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> Enumerate(IEnumerable<(string Word, TextSegmentKind Kind)> words)
        {
            foreach (var (word, kind) in words)
            {
                yield return (word, kind, null, null);
            }
        }
    }

    /// <summary>
    /// Same as <see cref="Draw"/>, but walks a message's actual SeString payloads instead of a
    /// flattened string - lets map links, item links, and Dalamud links (from other plugins, e.g.
    /// Lifestream) render as part of the normal wrapped text flow instead of a separate widget
    /// bolted on afterward, and makes them clickable via <paramref name="onLinkClicked"/>.
    ///
    /// A link payload (MapLink/Item/DalamudLink) itself carries no visible text - the game encodes
    /// the clickable label as a plain TextPayload immediately after it, terminated by
    /// <see cref="RawPayload.LinkTerminator"/>. This is the same structure Chat2's own
    /// ChunkUtil.ToChunks walks (verified against their real source) - it's what lets the exact
    /// same text that's already part of the message double as the click target, rather than this
    /// needing to synthesize and display a second, separate copy of it.
    /// </summary>
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
        Action<Payload> onLinkClicked,
        Action<Payload>? onLinkHovered = null)
    {
        DrawSequence(
            BuildRuns(payloads, mentionTerm),
            defaultColor, emoteColor, oocColor, mentionColor, linkColor,
            availableWidth, initialLineWidthUsed, highlightTerm, highlightColor, onLinkClicked, onLinkHovered);
    }

    /// <summary>
    /// Walks a message's payloads, tracking which link (if any) and which native foreground color
    /// (if any) currently apply, and tokenizing each run of text under that state - mirrors
    /// Chat2's own ChunkUtil.ToChunks push/pop handling of UIForegroundPayload (verified against
    /// their real source). This is what makes e.g. an item link's rarity color (which the game
    /// itself already wraps the item's name in via UIForegroundPayload/UIColor) show up correctly
    /// instead of every link rendering in one flat link color regardless of what it actually is.
    /// </summary>
    private static IEnumerable<(string Word, TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> BuildRuns(IReadOnlyList<Payload> payloads, string mentionTerm)
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
                    foreach (var (word, kind) in Tokenize(textProvider.Text, mentionTerm, state))
                    {
                        yield return (word, kind, currentLink, currentForeground);
                    }

                    break;
            }
        }
    }

    // UIColor.Dark/Light (surfaced as UIForegroundPayload.RGBA) is packed as RRGGBBAA - verified
    // against Chat2's own Util/ColourUtil.cs, which does the exact same byte layout.
    private static Vector4 RgbaToVector4(uint rgba)
    {
        var r = (byte)((rgba & 0xFF000000) >> 24);
        var g = (byte)((rgba & 0xFF0000) >> 16);
        var b = (byte)((rgba & 0xFF00) >> 8);
        var a = (byte)(rgba & 0xFF);
        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private static void DrawSequence(
        IEnumerable<(string Word, TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> words,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        float availableWidth,
        float initialLineWidthUsed,
        string highlightTerm,
        Vector4? highlightColor,
        Action<Payload>? onLinkClicked,
        Action<Payload>? onLinkHovered = null)
    {
        // See the remarks on Draw's initialLineWidthUsed parameter in git history/callers - this
        // is measured once per frame by the caller, at the left edge of the log area before any
        // entry is drawn, not re-derived here from the current cursor position. The margin below
        // is sized off the theme's actual scrollbar width since content region math can still land
        // text right at that edge - a too-small margin clips the last character or two of a line.
        var wrapWidth = availableWidth - ImGui.GetStyle().ScrollbarSize - 6f;
        var lineWidthUsed = initialLineWidthUsed;
        var startOfLine = initialLineWidthUsed <= 0f;

        foreach (var (word, kind, link, foreground) in words)
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

            // A native foreground color (e.g. an item link's rarity color, which the game already
            // wraps the item name in) wins over the generic link color - a link is still clickable
            // regardless of which color it's actually drawn in.
            var color = foreground ?? (link != null
                ? linkColor
                : kind switch
                {
                    TextSegmentKind.Mention => mentionColor,
                    TextSegmentKind.Ooc => oocColor,
                    TextSegmentKind.Emote => emoteColor,
                    _ => defaultColor,
                });

            if (highlightTerm.Length > 0 && word.Contains(highlightTerm, StringComparison.OrdinalIgnoreCase))
            {
                color = highlightColor ?? color;
            }

            if (size.X > wrapWidth)
            {
                // A single "word" wider than the whole line (a long URL, a wall of text with no
                // spaces at all, ...) can never fit no matter which line it starts on - hard-wrap
                // just this one word internally instead of rendering it as one unbroken line
                // running off the edge of the window. Left alone otherwise: enabling wrap-pos
                // unconditionally previously shaved a character or two off ordinary short words
                // too, since Dear ImGui measures wrapped vs. unwrapped text slightly differently.
                ImGui.PushTextWrapPos(ImGui.GetCursorScreenPos().X + wrapWidth);
                ImGui.TextColored(color, display);
                ImGui.PopTextWrapPos();
            }
            else
            {
                ImGui.TextColored(color, display);
            }

            if (link != null && ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                onLinkHovered?.Invoke(link);

                // A cursor-shape change alone is easy to miss (the mouse might not even be
                // looking at the cursor). An underline directly under the hovered word is the
                // same affordance a normal web hyperlink gives on hover. Stops at the word itself,
                // not the trailing space baked into "display"'s measured width.
                var wordMin = ImGui.GetItemRectMin();
                var wordMax = ImGui.GetItemRectMax();
                var underlineEndX = wordMin.X + ImGui.CalcTextSize(word).X;
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(wordMin.X, wordMax.Y - 2),
                    new Vector2(underlineEndX, wordMax.Y - 2),
                    ImGui.GetColorU32(color));

                if (ImGui.IsItemClicked())
                {
                    onLinkClicked?.Invoke(link);
                }
            }

            lineWidthUsed += size.X;
            startOfLine = false;
        }

        if (startOfLine)
        {
            ImGui.TextColored(defaultColor, " ");
        }
    }

    private sealed class TokenizeState
    {
        public bool InEmote;
        public bool InOoc;
    }

    private enum TextSegmentKind
    {
        Default,
        Emote,
        Ooc,
        Mention,
    }

    private static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm, TokenizeState state)
    {
        // '\r'/'\n' matter here specifically because of NewLinePayload (Dalamud's
        // PayloadType.NewLine): its .Text is a literal Environment.NewLine, and it implements
        // ITextProvider like any other text, so it flows through the exact same tokenizer as
        // everything else. Without treating it as a word separator, that literal newline character
        // rides along inside whatever "word" it ends up attached to and forces a hard, seemingly
        // random line break wherever the game happened to place one (observed right between a map
        // link's place name and region name) - splitting on it here instead just treats it as
        // another wrap point, letting the normal wrap logic decide where lines actually break.
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var startedInEmote = state.InEmote;
            var startedInOoc = state.InOoc;

            if (CountOccurrences(word, '*') % 2 == 1)
            {
                state.InEmote = !state.InEmote;
            }

            var oocOpens = CountOccurrences(word, "((");
            var oocCloses = CountOccurrences(word, "))");
            if (oocOpens > oocCloses)
            {
                state.InOoc = true;
            }
            else if (oocCloses > oocOpens)
            {
                state.InOoc = false;
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
