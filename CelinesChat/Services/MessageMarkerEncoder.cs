using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CelinesChat.Services;

internal static class MessageMarkerEncoder
{
    public static string BuildAutoTranslateMarker(AutoTranslateService.Entry entry) =>
        $"[[{entry.Text}#{entry.Group}.{entry.Key}]]";

    public static byte[] Encode(string text)
    {
        if (!text.Contains("[[", StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetBytes(text);
        }

        var result = new List<byte>(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var markerStart = text.IndexOf("[[", i, StringComparison.Ordinal);
            if (markerStart < 0)
            {
                result.AddRange(Encoding.UTF8.GetBytes(text[i..]));
                break;
            }

            result.AddRange(Encoding.UTF8.GetBytes(text[i..markerStart]));

            var markerEnd = text.IndexOf("]]", markerStart + 2, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                result.AddRange(Encoding.UTF8.GetBytes(text[markerStart..]));
                break;
            }

            if (TryParseMarker(text, markerStart, markerEnd, out var group, out var key))
            {
                result.AddRange(new AutoTranslatePayload(group, key).Encode());
            }
            else
            {
                result.AddRange(Encoding.UTF8.GetBytes(text[markerStart..(markerEnd + 2)]));
            }

            i = markerEnd + 2;
        }

        return result.ToArray();
    }

    private static bool TryParseMarker(string text, int markerStart, int markerEnd, out uint group, out uint key)
    {
        group = 0;
        key = 0;

        var inner = text[(markerStart + 2)..markerEnd];
        var hashIndex = inner.LastIndexOf('#');
        if (hashIndex < 0)
        {
            return false;
        }

        var dotIndex = inner.IndexOf('.', hashIndex + 1);
        if (dotIndex < 0)
        {
            return false;
        }

        return uint.TryParse(inner[(hashIndex + 1)..dotIndex], out group)
            && uint.TryParse(inner[(dotIndex + 1)..], out key);
    }
}
