using System.Collections.Generic;

namespace CelinesChat.Services.Web;

/// <summary>
/// One already-colored run of text within a message, as produced by ColoredTextSegmenter - the
/// browser only ever needs to render `&lt;span style="color:{ColorCss}"&gt;{Text}&lt;/span&gt;`,
/// with the OOC/emote/mention/link color rules already resolved server-side (see
/// ColoredTextRenderer.ResolveColor) rather than reimplemented in JavaScript.
/// </summary>
internal sealed class WebTextSegmentDto
{
    public string Text { get; set; } = string.Empty;

    /// <summary>A CSS "rgba(r,g,b,a)" string, ready to assign directly to an element's style.color.</summary>
    public string ColorCss { get; set; } = string.Empty;

    /// <summary>
    /// Index into the owning message's ChatLogEntry.Payloads if this segment is a clickable link,
    /// null otherwise - sent back verbatim by the browser on click (see WebRoutes' /api/links/click),
    /// resolved back to the real Payload object server-side. Never interpreted client-side beyond
    /// that round trip.
    /// </summary>
    public int? LinkIndex { get; set; }
}

/// <summary>One chat message, ready for the browser to render directly.</summary>
internal sealed class WebMessageDto
{
    public long Sequence { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string SenderColorCss { get; set; } = string.Empty;

    public string ChannelTag { get; set; } = string.Empty;

    public string ChannelColorCss { get; set; } = string.Empty;

    public List<WebTextSegmentDto> Segments { get; set; } = new();

    /// <summary>Local time "HH:mm", matching the desktop log's own timestamp format exactly.</summary>
    public string Time { get; set; } = string.Empty;
}

/// <summary>One selectable tab in the "which conversation am I viewing" picker.</summary>
internal sealed class WebTabDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>One entry in the "which whisper conversation am I viewing" picker.</summary>
internal sealed class WebWhisperDto
{
    public string Target { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>The shared current view + send-channel, and the full list of tabs/whispers to choose from - sent whenever either changes, from any connected device.</summary>
internal sealed class WebViewDto
{
    public List<WebTabDto> Tabs { get; set; } = new();

    public List<WebWhisperDto> Whispers { get; set; } = new();

    /// <summary>Null if a whisper conversation is the active view instead of a fixed tab.</summary>
    public string? ActiveTabId { get; set; }

    /// <summary>Empty if a fixed tab is the active view instead of a whisper conversation.</summary>
    public string ActiveWhisperTarget { get; set; } = string.Empty;

    /// <summary>The current send-channel (ChatChannel enum name, e.g. "Say"/"Party"/"Whisper").</summary>
    public string Channel { get; set; } = string.Empty;
}

internal sealed class WebAuthRequest
{
    public string Code { get; set; } = string.Empty;
}

internal sealed class WebSendRequest
{
    public string Text { get; set; } = string.Empty;
}

internal sealed class WebTabSwitchRequest
{
    public string TabId { get; set; } = string.Empty;
}

internal sealed class WebWhisperSwitchRequest
{
    public string Target { get; set; } = string.Empty;
}

internal sealed class WebChannelSwitchRequest
{
    public string Channel { get; set; } = string.Empty;

    public int? Number { get; set; }
}

internal sealed class WebLinkClickRequest
{
    public long Sequence { get; set; }

    public int LinkIndex { get; set; }
}

internal sealed class WebErrorResponse
{
    public string Error { get; set; } = string.Empty;

    public WebErrorResponse()
    {
    }

    public WebErrorResponse(string error)
    {
        Error = error;
    }
}
