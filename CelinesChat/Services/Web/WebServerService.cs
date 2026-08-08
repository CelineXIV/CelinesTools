using System.Threading;
using Dalamud.Plugin.Services;
using CelinesChat.Services.Web.Http;

namespace CelinesChat.Services.Web;

/// <summary>
/// Owns the embedded HTTP server's start/stop lifecycle - the actual route handlers live in
/// WebRoutes, this class is just "is it running, start it, stop it, tell me why it failed."
/// Entirely inert (no listener, no thread, no timer) unless/until Start() is actually called -
/// Configuration.WebClientEnabled gates whether Plugin ever calls that at all.
/// </summary>
internal sealed class WebServerService : IDisposable
{
    // How often a heartbeat SSE comment goes out to every connected client, independent of real
    // traffic - keeps NAT/mobile idle-socket timers from silently killing a connection that has
    // nothing to say for a while, and gives the browser's own watchdog (see app.js) something to
    // reset its "did we hear anything recently" clock against.
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

    /// <summary>
    /// Every currently-reachable "open this in a browser" URL for the settings page to show -
    /// the machine's own hostname (works from other devices on the same LAN, which is the whole
    /// point) plus localhost (handy for testing from the same PC without needing a phone).
    /// </summary>
    public IReadOnlyList<string> GetDisplayUrls(int port)
    {
        var urls = new List<string> { $"http://localhost:{port}/" };
        try
        {
            urls.Insert(0, $"http://{System.Net.Dns.GetHostName()}:{port}/");
        }
        catch (Exception)
        {
            // Hostname resolution failing is a "your network stack is unusual" problem, not
            // something worth surfacing as a plugin error - localhost above still works either way.
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
            // A proper named event (not a bare SSE comment) deliberately - a comment keeps the
            // TCP connection alive but is invisible to the browser's EventSource API entirely, so
            // app.js's own "have I heard anything recently" watchdog (see its remarks) would have
            // nothing to observe. This one frame does both jobs at once.
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
