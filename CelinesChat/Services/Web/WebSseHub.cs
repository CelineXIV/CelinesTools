using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CelinesChat.Services.Web;

internal sealed class WebSseClient
{
    public Guid Id { get; } = Guid.NewGuid();

    public Channel<string> Outbound { get; } = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
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
