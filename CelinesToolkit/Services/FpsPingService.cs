using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network;

namespace CelinesToolkit.Services;

/// <summary>
/// Periodically measures round-trip time to the current zone server. FFXIVClientStructs has no
/// mapped field for the game's own internally-computed ping (only NetworkModule.LobbyPing, which is
/// character-select only, not in-game) - verified directly against the actual struct layout via
/// reflection rather than guessed, so this measures its own. A plain ICMP echo (System.Net.
/// NetworkInformation.Ping) was considered but rejected: many game server operators drop/deprioritize
/// ICMP at the network edge for security even though the actual game port is fully reachable, which
/// would make the reading show as constantly timed out despite the connection being fine. Timing a
/// TCP connect handshake to the real host:port the game itself is connected to doesn't have that
/// problem and is a closer proxy to "how it actually feels to play" besides.
/// </summary>
internal sealed class FpsPingService : IDisposable
{
    private const int PingIntervalMs = 2000;
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
            LastPingMs = await MeasureTcpConnectMs(host, port);
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
