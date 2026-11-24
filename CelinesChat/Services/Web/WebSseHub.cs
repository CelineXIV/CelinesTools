using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CelinesChat.Services.Web;

internal sealed class WebSseClient
{
    public Guid Id { get; } = Guid.NewGuid();

    // Bounded: a client that stops reading without its connection dropping (a phone with the browser
    // in the background, say) would otherwise make this queue - and the plugin's memory - grow for as
    // long as chat keeps coming in. When it's full the oldest frames go; a reconnect catches up anyway.
    public Channel<string> Outbound { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    });
}

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

    public void Broadcast(string frame)
    {
        foreach (var client in clients.Values)
        {
            client.Outbound.Writer.TryWrite(frame);
        }
    }
}
