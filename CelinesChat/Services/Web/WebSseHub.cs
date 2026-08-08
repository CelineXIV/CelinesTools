using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CelinesChat.Services.Web;

/// <summary>
/// One connected browser's outbound event stream - an unbounded queue of already-formatted raw
/// SSE frame strings (e.g. "id: 5\ndata: {...}\n\n"), drained by WebRoutes' /api/events handler
/// for as long as that particular HTTP request stays open. Unbounded rather than capped
/// deliberately: a chat plugin's own message volume can never realistically outpace a browser
/// reading its own SSE stream fast enough to matter, so there's no real backpressure scenario
/// worth adding capacity-drop logic for.
/// </summary>
internal sealed class WebSseClient
{
    public Guid Id { get; } = Guid.NewGuid();

    public Channel<string> Outbound { get; } = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
}

/// <summary>
/// Tracks every currently-connected web client and broadcasts raw SSE frame strings to all of
/// them at once - deliberately dumb about what those strings actually mean (JSON shape, event
/// type, ...); that's WebRoutes' concern. There is exactly one shared chat "view" across every
/// connected client (matching the desktop window - see ChatWindow.ActiveTabId/
/// ActiveWhisperTabTarget), so a single broadcast is always correct, never per-connection
/// filtering logic to maintain here.
/// </summary>
internal sealed class WebSseHub
{
    private readonly ConcurrentDictionary<Guid, WebSseClient> clients = new();

    public int ConnectedCount => clients.Count;

    public WebSseClient Register()
    {
        var client = new WebSseClient();
        clients[client.Id] = client;
        return client;
    }

    public void Unregister(Guid id) => clients.TryRemove(id, out _);

    /// <summary>Enqueues one raw SSE frame onto every currently-connected client - never blocks (unbounded channel, TryWrite always succeeds).</summary>
    public void Broadcast(string frame)
    {
        foreach (var client in clients.Values)
        {
            client.Outbound.Writer.TryWrite(frame);
        }
    }
}
