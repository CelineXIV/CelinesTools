using System.Threading;
using Dalamud.Plugin.Services;
using CelinesChat.Services.Web.Http;

namespace CelinesChat.Services.Web;

internal sealed class WebServerService : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly string webRootDir;
    private readonly WebSseHub hub = new();

    private MiniHttpServer? host;
    private WebRoutes? routes;
    private Timer? heartbeatTimer;

    public WebServerService(Plugin plugin, IPluginLog log, string webRootDir)
    {
        this.plugin = plugin;
        this.log = log;
        this.webRootDir = webRootDir;
    }

    public bool IsRunning => host is { IsListening: true };

    public string? LastError { get; private set; }

    public int ConnectedClientCount => hub.ConnectedCount;

    public IReadOnlyList<string> GetDisplayUrls(int port)
    {
        var urls = new List<string> { $"http://localhost:{port}/" };
        try
        {
            urls.Insert(0, $"http://{System.Net.Dns.GetHostName()}:{port}/");
        }
        catch (Exception)
        {
        }

        return urls;
    }

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        try
        {
            var port = plugin.Configuration.WebClientPort;
            var newHost = new MiniHttpServer(port, DefaultRoute);
            newHost.Events.ExceptionEncountered += (_, args) => log.Error(args.Exception, "[CelinesChat] Web-Client-Server-Fehler.");
            newHost.Events.Logger = message => log.Verbose("[CelinesChat/Web] " + message);

            routes = new WebRoutes(plugin, hub, newHost, webRootDir);
            newHost.Start();

            host = newHost;
            LastError = null;
            heartbeatTimer = new Timer(_ => hub.Broadcast("event: heartbeat\ndata: {}\n\n"), null, HeartbeatInterval, HeartbeatInterval);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[CelinesChat] Web-Client-Server konnte nicht gestartet werden.");
            LastError = ex.Message;
            routes?.Dispose();
            routes = null;
            host = null;
            return false;
        }
    }

    public void Stop()
    {
        heartbeatTimer?.Dispose();
        heartbeatTimer = null;

        routes?.Dispose();
        routes = null;

        if (host != null)
        {
            try
            {
                host.Stop();
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[CelinesChat] Fehler beim Stoppen des Web-Client-Servers.");
            }

            host.Dispose();
            host = null;
        }
    }

    private static async Task DefaultRoute(HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.Send();
    }

    public void Dispose() => Stop();
}
