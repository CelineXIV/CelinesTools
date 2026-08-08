using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using CelinesChat.Services.Web;

namespace CelinesChat.Services;

/// <summary>
/// Turns a message into the same OOC/emote/mention/link-colored segments ColoredTextRenderer
/// draws in ImGui, but as plain data instead of draw calls - reuses ColoredTextRenderer's own
/// tokenizer (BuildRuns/Tokenize) and color-precedence rule (ResolveColor) instead of duplicating
/// any of that parsing in JavaScript. The browser only ever needs to render one &lt;span&gt; per
/// segment with an inline color.
/// </summary>
internal static class ColoredTextSegmenter
{
    /// <summary>For a live entry (Payloads != null) - preserves clickable links.</summary>
    public static List<WebTextSegmentDto> BuildSegments(
        IReadOnlyList<Payload> payloads,
        string mentionTerm,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor)
    {
        return Merge(ColoredTextRenderer.BuildRuns(payloads, mentionTerm), defaultColor, emoteColor, oocColor, mentionColor, linkColor, payloads);
    }

    /// <summary>For a history entry reloaded from a log file (Payloads == null, plain text only, never any links).</summary>
    public static List<WebTextSegmentDto> BuildSegments(
        string text,
        string mentionTerm,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor)
    {
        var state = new ColoredTextRenderer.TokenizeState();
        return Merge(Widen(ColoredTextRenderer.Tokenize(text, mentionTerm, state)), defaultColor, emoteColor, oocColor, mentionColor, default, null);

        // Mirrors ColoredTextRenderer.Draw's own local "Enumerate" helper - plain tokenized words
        // have no link/native-foreground-color concept, so both are always null here.
        static IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> Widen(
            IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind)> words)
        {
            foreach (var (word, kind) in words)
            {
                yield return (word, kind, null, null);
            }
        }
    }

    /// <summary>
    /// Resolves each word's color, then merges consecutive same-color/same-link words into one
    /// segment - the same run-buffering idea DrawSequence uses to cut down its own ImGui call
    /// count, just emitting data instead of draw calls.
    /// </summary>
    private static List<WebTextSegmentDto> Merge(
        IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind, Payload? Link, Vector4? Foreground)> words,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        IReadOnlyList<Payload>? payloads)
    {
        var segments = new List<WebTextSegmentDto>();
        List<string>? runWords = null;
        var runColor = default(Vector4);
        Payload? runLink = null;

        void Flush()
        {
            if (runWords == null)
            {
                return;
            }

            segments.Add(new WebTextSegmentDto
            {
                Text = string.Join(' ', runWords),
                ColorCss = ToCssColor(runColor),
                LinkIndex = FindPayloadIndex(payloads, runLink),
            });
            runWords = null;
        }

        foreach (var (word, kind, link, foreground) in words)
        {
            var color = ColoredTextRenderer.ResolveColor(foreground, link, kind, defaultColor, emoteColor, oocColor, mentionColor, linkColor);
            var sameRun = runWords != null && color == runColor && link == runLink;

            if (sameRun)
            {
                runWords!.Add(word);
                continue;
            }

            Flush();
            runWords = new List<string> { word };
            runColor = color;
            runLink = link;
        }

        Flush();
        return segments;
    }

    // Reference equality on purpose - we're looking for the exact same Payload instance the
    // message's own entry.Payloads holds, not a value-equal one, so /api/links/click can index
    // straight back into that same list later.
    private static int? FindPayloadIndex(IReadOnlyList<Payload>? payloads, Payload? link)
    {
        if (payloads == null || link == null)
        {
            return null;
        }

        for (var i = 0; i < payloads.Count; i++)
        {
            if (ReferenceEquals(payloads[i], link))
            {
                return i;
            }
        }

        return null;
    }

    private static string ToCssColor(Vector4 color)
    {
        var r = (int)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (int)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (int)(Math.Clamp(color.Z, 0f, 1f) * 255f);
        var a = Math.Clamp(color.W, 0f, 1f);
        return string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.00})", r, g, b, a);
    }
}
