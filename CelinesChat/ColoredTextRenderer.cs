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
    // internal (not private) so Services/ColoredTextSegmenter can reuse this exact same
    // payload-walking/tokenization logic to build data for the web client, instead of
    // reimplementing OOC/emote/mention/link parsing a second time in JavaScript.
    internal static IEnumerable<(string Word, TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> BuildRuns(IReadOnlyList<Payload> payloads, string mentionTerm)
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

    /// <summary>
    /// The color-precedence rule for one tokenized word: a native foreground color (e.g. an item
    /// link's rarity color, which the game already wraps the item name in) wins over the generic
    /// link color, which wins over the word's OOC/emote/mention/plain kind - a link is still
    /// clickable regardless of which color it's actually drawn in. Extracted out of DrawSequence
    /// (its only caller until now) so Services/ColoredTextSegmenter can resolve the exact same
    /// color for the web client's JSON without re-deriving this precedence rule a second time.
    /// </summary>
    internal static Vector4 ResolveColor(Vector4? foreground, Payload? link, TextSegmentKind kind, Vector4 defaultColor, Vector4 emoteColor, Vector4 oocColor, Vector4 mentionColor, Vector4 linkColor)
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

        // Consecutive words that end up the same color/link get buffered and drawn as a single
        // ImGui call once a line/formatting break actually forces a flush, instead of one call
        // (plus CalcTextSize, SameLine, item-rect queries, ...) per individual word. SameLine(0,0)
        // between separately drawn same-colored words was already pixel-identical to one
        // continuous string, so this changes nothing on screen for the common case (most messages
        // are a single color throughout) - it just cuts the per-frame ImGui call count roughly
        // from one-per-word to one-per-formatting-run, which matters once the full scrollback (up
        // to 500 entries) gets fully re-walked and re-drawn every single frame regardless of
        // scroll position - this was the biggest concrete difference found comparing against
        // Chat2's own per-line (not per-word) text drawing.
        List<string>? runWords = null;
        var runWidth = 0f;
        Vector4 runColor = default;
        Payload? runLink = null;

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
                // A single "word" wider than the whole line (a long URL, a wall of text with no
                // spaces at all, ...) can never fit no matter which line it starts on, so (same as
                // before this method buffered runs) it always starts its own fresh line rather
                // than continuing whatever came before it - flush whatever's already buffered
                // (finishing the previous line) and force a wrap first. Hard-wraps just this one
                // word internally instead of rendering it as one unbroken line running off the
                // edge of the window; left alone otherwise, enabling wrap-pos unconditionally
                // previously shaved a character or two off ordinary short words too, since Dear
                // ImGui measures wrapped vs. unwrapped text slightly differently.
                FlushRun();
                startOfLine = true;
                lineWidthUsed = 0f;

                ImGui.PushTextWrapPos(ImGui.GetCursorScreenPos().X + wrapWidth);
                ImGui.TextColored(color, word + " ");
                ImGui.PopTextWrapPos();
                lineWidthUsed = size.X;
                startOfLine = false;
                continue;
            }

            var sameRun = runWords != null && color == runColor && link == runLink;
            var fitsCurrentLine = lineWidthUsed + runWidth + size.X <= wrapWidth;

            if (!fitsCurrentLine)
            {
                // The next word doesn't fit on the current line even with whatever's already
                // buffered - flush the buffer (finishing this line), then start a fresh line.
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

    /// <summary>
    /// Draws one already-fitted piece of text (a single word, or several consecutive same-format
    /// words merged by DrawSequence's run buffering) plus its link hover/click/underline
    /// handling. Callers own line-wrap decisions and call ImGui.SameLine beforehand as needed -
    /// this only draws.
    /// </summary>
    private static void DrawUnit(string display, string bareText, Vector4 color, Payload? link, Action<Payload>? onLinkClicked, Action<Payload>? onLinkHovered)
    {
        ImGui.TextColored(color, display);

        if (link == null || !ImGui.IsItemHovered())
        {
            return;
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        onLinkHovered?.Invoke(link);

        // A cursor-shape change alone is easy to miss (the mouse might not even be looking at the
        // cursor). An underline directly under the hovered text is the same affordance a normal
        // web hyperlink gives on hover. Stops at the text itself, not the trailing space baked
        // into "display"'s measured width.
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

    // internal (not private) - Services/ColoredTextSegmenter needs to construct one of these to
    // call Tokenize, same reason as Tokenize/BuildRuns above.
    internal sealed class TokenizeState
    {
        public bool InEmote;

        // A depth (not just a bool) so "(((nested))" and similar don't prematurely drop out of
        // OOC after only one of several closes - and, just as importantly, so a *stray* ')' with
        // nothing open (a ":)" or "8)" smiley, a typo) can never push it negative and doesn't
        // count as a close at all. See Tokenize's per-character scan below.
        public int OocDepth;
    }

    internal enum TextSegmentKind
    {
        Default,
        Emote,
        Ooc,
        Mention,
    }

    // internal (not private) - see BuildRuns' remarks above, same reason.
    internal static IEnumerable<(string Word, TextSegmentKind Kind)> Tokenize(string text, string mentionTerm, TokenizeState state)
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

            if (CountOccurrences(word, '*') % 2 == 1)
            {
                state.InEmote = !state.InEmote;
            }

            // Single "(aside)" and double "((aside))" parens both go grey the same way - depth
            // simply reaches 1 (or 2) instead of caring which. Scanned character-by-character
            // (not just an aggregate open/close count) specifically so a lone, never-opened ')'
            // - a ":)" or "8)" smiley, a stray typo - can't decrement below zero and doesn't
            // touch OOC at all, the way a naive "does this word contain ')'" check would have.
            //
            // A "(2/7)" split-message counter (see MessageSplitter) is a single self-contained
            // paren pair by construction, so it would otherwise go grey by this same rule - it's
            // our own bookkeeping, not something the sender wrote, so it's excluded outright
            // rather than colored as if it were part of the message.
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

    /// <summary>
    /// True for exactly "(&lt;digits&gt;/&lt;digits&gt;)" - the split-message counter
    /// MessageSplitter.BuildMessages appends (" (2/7)" etc.), and nothing else. Deliberately
    /// strict (no other characters allowed inside) so it can't accidentally swallow something a
    /// person actually wrote that happens to contain a slash between two numbers as part of a
    /// longer word.
    /// </summary>
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
