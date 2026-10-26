using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network;

namespace CelinesToolkit.Services;

/// <summary>
/// Periodically measures round-trip time to the current zone server. FFXIVClientStructs has no
/// mapped field for the game's own internally-computed ping (only NetworkModule.LobbyPing, which is
/// character-select only, not in-game) - verified directly against the actual struct layout via
/// reflection rather than guessed, so this measures its own.
///
/// Tries a real ICMP echo (System.Net.NetworkInformation.Ping) to the same host first - reported
/// as consistently reading ~0ms with only the TCP fallback below in use, while a separate plugin
/// that actually measures the real path (ICMP or genuine packet-level RTT) showed plausible,
/// nonzero numbers on the same connection. The TCP-connect approach measures how fast a *new*
/// connection attempt gets answered, not the real round-trip of the already-established game
/// session - for game infrastructure fronted by a nearby load-balancer/edge node (common for
/// large-scale server operators), that new-connection handshake can be answered almost instantly
/// by that nearby node regardless of how far away the actual zone server is, which is exactly a
/// "always reads ~0ms" symptom the TCP method alone can't tell apart from a genuinely fast
/// connection. ICMP measures the real path directly when it's allowed through - the TCP-connect
/// timing is kept only as a fallback for the servers that do drop/deprioritize ICMP at the edge
/// (the original, still-valid reason it was added at all), so there's still *some* reading instead
/// of a hard failure on those.
/// </summary>
internal sealed class FpsPingService : IDisposable
{
    private const int PingIntervalMs = 2000;
    private const int IcmpTimeoutMs = 1000;
    private const int PingTimeoutMs = 2000;

    private readonly IFramework framework;
    private readonly IPluginLog log;

    private bool enabled;
    private bool pingInFlight;
    private long lastPingSentTicks;

    public int? LastPingMs { get; private set; }

    public FpsPingService(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log = log;
    }

    public void SetEnabled(bool value)
    {
        if (value == enabled)
        {
            return;
        }

        enabled = value;
        if (enabled)
        {
            framework.Update += OnFrameworkUpdate;
        }
        else
        {
            framework.Update -= OnFrameworkUpdate;
            LastPingMs = null;
        }
    }

    private void OnFrameworkUpdate(IFramework frameworkInstance)
    {
        if (pingInFlight || Environment.TickCount64 - lastPingSentTicks < PingIntervalMs)
        {
            return;
        }

        var (host, port) = GetCurrentZoneEndpoint();
        if (string.IsNullOrEmpty(host) || port == 0)
        {
            return;
        }

        lastPingSentTicks = Environment.TickCount64;
        pingInFlight = true;
        _ = MeasureAndStoreAsync(host, port);
    }

    private async Task MeasureAndStoreAsync(string host, int port)
    {
        try
        {
            LastPingMs = await MeasureIcmpMs(host) ?? await MeasureTcpConnectMs(host, port);
        }
        catch (Exception ex)
        {
            log.Debug($"[CelinesToolkit][FpsPing] Ping measurement failed: {ex.Message}");
            LastPingMs = null;
        }
        finally
        {
            pingInFlight = false;
        }
    }

    private static async Task<int?> MeasureIcmpMs(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, IcmpTimeoutMs);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
        }
        catch
        {
            // Blocked/filtered ICMP, a resolver failure, or any other reason this specific
            // technique can't get an answer - null here just means MeasureAndStoreAsync falls
            // through to the TCP-connect timing below instead of surfacing a hard failure.
            return null;
        }
    }

    private static async Task<int?> MeasureTcpConnectMs(string host, int port)
    {
        using var client = new TcpClient();

        // Stopwatch (QueryPerformanceCounter), not Environment.TickCount64 - TickCount64 only
        // advances in ~15.6ms steps on Windows by default, so a genuinely fast connect (well under
        // that) could read the exact same tick before and after, always reporting 0ms regardless of
        // the real value. Stopwatch has real sub-millisecond resolution.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var connectTask = client.ConnectAsync(host, port);
        var timeoutTask = Task.Delay(PingTimeoutMs);
        var completed = await Task.WhenAny(connectTask, timeoutTask);

        if (completed != connectTask || !client.Connected)
        {
            return null;
        }

        // Observe the (already-completed) connect task so a faulted ConnectAsync doesn't surface
        // as an unobserved task exception later.
        await connectTask;
        stopwatch.Stop();
        return (int)stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// Framework.Instance()->GetNetworkModuleProxy()->NetworkModule->ZoneClient->Host/Port - the
    /// actual server the game is currently talking to for zone traffic. Verified field-by-field
    /// against the real FFXIVClientStructs.dll via reflection (not guessed from memory) before use.
    /// NetworkModule.ZoneClient itself is mapped as a private field (unlike its sibling ActiveLobby*/
    /// Front* fields), so it's read via a raw pointer offset instead of the normal field access C#
    /// would otherwise block - the 0xA70 offset comes from that same reflection pass, not a guess,
    /// but being a raw offset rather than a public API it could in principle shift on a future
    /// FFXIVClientStructs update, unlike this project's other, publicly-exposed struct usages.
    /// </summary>
    private static unsafe (string? Host, int Port) GetCurrentZoneEndpoint()
    {
        var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (framework == null)
        {
            return (null, 0);
        }

        var proxy = framework->GetNetworkModuleProxy();
        var module = proxy != null ? proxy->NetworkModule : null;
        if (module == null)
        {
            return (null, 0);
        }

        const int zoneClientOffset = 0xA70;
        var zoneClient = *(ZoneClient**)((byte*)module + zoneClientOffset);
        if (zoneClient == null)
        {
            return (null, 0);
        }

        var host = zoneClient->Host.ToString();
        return string.IsNullOrWhiteSpace(host) ? (null, 0) : (host, zoneClient->Port);
    }

    public void Dispose() => SetEnabled(false);
}
