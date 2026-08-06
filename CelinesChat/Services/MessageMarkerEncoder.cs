using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CelinesChat.Services;

/// <summary>
/// Turns compose text containing "[[Display Text#group.key]]" auto-translate markers (see
/// ChatWindow's Tab-triggered picker) into the raw byte buffer the game's chat box actually needs
/// to send: plain segments UTF8-encoded as normal, and each marker replaced with the real, raw
/// AutoTranslatePayload bytes instead of its literal bracketed text. ProcessChatBoxEntry (see
/// ChatSender) accepts exactly this kind of mixed text+payload buffer - it's the same thing the
/// native chat box's own buffer contains after using Tab there.
///
/// Note this is NOT used for map flags or item links - those arrive as literal placeholder/text
/// (see ChatActivationWatcher and ChatWindow.InsertChatPrefillText), which the game itself expands
/// into the real payload at send time, the same way the native chat box does. That text passes
/// through Encode below completely unmodified, same as any other plain text.
///
/// The group/key are embedded directly in the marker rather than looked up from a separate
/// dictionary keyed by display text - an earlier version did that, and it broke whenever two
/// different Completion sheet entries happened to share the exact same display text (e.g.
/// "retainer" existing as its own entry in more than one category): inserting the second one
/// silently overwrote the first's dictionary entry, so sending the first sent the second's
/// payload instead. A self-contained marker can't collide with anything.
/// </summary>
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
                // Unterminated marker (shouldn't normally happen, but don't just drop the rest
                // of the message if it does) - treat everything from here on as literal text.
                result.AddRange(Encoding.UTF8.GetBytes(text[markerStart..]));
                break;
            }

            if (TryParseMarker(text, markerStart, markerEnd, out var group, out var key))
            {
                result.AddRange(new AutoTranslatePayload(group, key).Encode());
            }
            else
            {
                // Looks like a marker but isn't one of ours (e.g. the user genuinely typed
                // literal double brackets) - send it as plain text rather than silently eating it.
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
