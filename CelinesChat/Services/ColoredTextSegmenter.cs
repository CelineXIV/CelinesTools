using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using CelinesChat.Services.Web;

namespace CelinesChat.Services;

internal static class ColoredTextSegmenter
{
    public static List<WebTextSegmentDto> BuildSegments(
        IReadOnlyList<Payload> payloads,
        string mentionTerm,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        Vector4 linkColor,
        bool allowRpFormatting = true)
    {
        return Merge(ColoredTextRenderer.BuildRuns(payloads, mentionTerm, allowRpFormatting), defaultColor, emoteColor, oocColor, mentionColor, linkColor, payloads);
    }

    public static List<WebTextSegmentDto> BuildSegments(
        string text,
        string mentionTerm,
        Vector4 defaultColor,
        Vector4 emoteColor,
        Vector4 oocColor,
        Vector4 mentionColor,
        bool allowRpFormatting = true)
    {
        var state = new ColoredTextRenderer.TokenizeState();
        return Merge(Widen(ColoredTextRenderer.Tokenize(text, mentionTerm, state, allowRpFormatting)), defaultColor, emoteColor, oocColor, mentionColor, default, null);

        static IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind, ColoredTextRenderer.LinkTarget? Link, Vector4? Foreground)> Widen(
            IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind)> words)
        {
            foreach (var (word, kind) in words)
            {
                yield return (word, kind, null, null);
            }
        }
    }

    private static List<WebTextSegmentDto> Merge(
        IEnumerable<(string Word, ColoredTextRenderer.TextSegmentKind Kind, ColoredTextRenderer.LinkTarget? Link, Vector4? Foreground)> words,
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
        ColoredTextRenderer.LinkTarget? runLink = null;

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
                LinkIndex = FindPayloadIndex(payloads, runLink?.Payload),
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
