using System.Collections.Generic;

namespace CelinesChat.Services.Web;

internal sealed class WebTextSegmentDto
{
    public string Text { get; set; } = string.Empty;

    public string ColorCss { get; set; } = string.Empty;

    public int? LinkIndex { get; set; }
}

internal sealed class WebMessageDto
{
    public long Sequence { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string SenderColorCss { get; set; } = string.Empty;

    public string ChannelTag { get; set; } = string.Empty;

    public string ChannelColorCss { get; set; } = string.Empty;

    public List<WebTextSegmentDto> Segments { get; set; } = new();

    public string Time { get; set; } = string.Empty;
}

internal sealed class WebTabDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

internal sealed class WebWhisperDto
{
    public string Target { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

internal sealed class WebViewDto
{
    public List<WebTabDto> Tabs { get; set; } = new();

    public List<WebWhisperDto> Whispers { get; set; } = new();

    public string? ActiveTabId { get; set; }

    public string ActiveWhisperTarget { get; set; } = string.Empty;

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
