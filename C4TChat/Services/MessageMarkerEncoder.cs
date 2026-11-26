using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace C4TChat.Services;

internal static class MessageMarkerEncoder
{
    public static string BuildAutoTranslateMarker(AutoTranslateService.Entry entry) =>
        $"[[{entry.Text}#{entry.Group}.{entry.Key}]]";

    public static string BuildItemMarker(string name, uint itemId, Dalamud.Utility.ItemKind kind) =>
        $"[[{name.Replace("]]", "] ]").Replace("#", string.Empty)}#i.{itemId}.{(int)kind}]]";

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

            if (TryParseItemMarker(text, markerStart, markerEnd, out var itemId, out var kind))
            {
                result.AddRange(Dalamud.Game.Text.SeStringHandling.SeString.CreateItemLink(itemId, kind, null).Encode());
            }
            else if (TryParseMarker(text, markerStart, markerEnd, out var group, out var key))
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

    public static bool TryGetMarkerEnd(string text, int markerStart, out int endExclusive)
    {
        endExclusive = markerStart;
        if (markerStart + 1 >= text.Length || text[markerStart] != '[' || text[markerStart + 1] != '[')
        {
            return false;
        }

        var markerEnd = text.IndexOf("]]", markerStart + 2, StringComparison.Ordinal);
        if (markerEnd < 0 || (!TryParseMarker(text, markerStart, markerEnd, out _, out _) && !TryParseItemMarker(text, markerStart, markerEnd, out _, out _)))
        {
            return false;
        }

        endExclusive = markerEnd + 2;
        return true;
    }

    private static bool TryParseItemMarker(string text, int markerStart, int markerEnd, out uint itemId, out Dalamud.Utility.ItemKind kind)
    {
        itemId = 0;
        kind = Dalamud.Utility.ItemKind.Normal;
        var inner = text[(markerStart + 2)..markerEnd];
        var hashIndex = inner.LastIndexOf('#');
        if (hashIndex < 0 || !inner[(hashIndex + 1)..].StartsWith("i.", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = inner[(hashIndex + 3)..].Split('.');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out itemId) || !int.TryParse(parts[1], out var kindValue))
        {
            return false;
        }

        kind = (Dalamud.Utility.ItemKind)kindValue;
        return itemId != 0;
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
