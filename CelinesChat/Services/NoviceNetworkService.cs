using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CelinesChat.Services;

/// <summary>
/// Wraps the native Novice Network connect/disconnect functions (InfoProxyNoviceNetwork - the same
/// InfoProxy pattern the game uses for Friend List/Blacklist/Party Finder results) plus opening the
/// game's own native member-list window, so the chat window can offer a one-click connect/disconnect
/// and a live "am I connected" indicator without needing the native Social menu this plugin
/// otherwise fully replaces - while still using the actual native list (not a custom-built one) for
/// "who's currently in", the same way ChatTwo/Chat2 does.
/// </summary>
internal static unsafe class NoviceNetworkService
{
    private const string LogPrefix = "[CelinesChat][NoviceNetwork]";

    /// <summary>How long a join attempt gets before it's considered to have failed (e.g. the channel is full) - the game shows its own native error for this, but that goes to the (hidden) native chat log and never reaches ours, so this is a state-based fallback instead of trying to catch that specific message.</summary>
    private const long JoinTimeoutMs = 4000;

    /// <summary>How often an auto-retry re-sends the join request while enabled and not yet connected - the channel filling and freeing up a slot again is entirely outside the client's control, so this just keeps knocking until one opens up rather than trying to detect availability up front.</summary>
    private const long AutoRetryIntervalMs = 5000;

    private static long? pendingJoinSinceTicks;
    private static long lastAutoRetryAttemptTicks;

    /// <summary>
    /// Whether TickAutoRetry (called every framework update from Plugin.OnFrameworkUpdate,
    /// regardless of whether the chat window is even open) should keep re-sending join requests
    /// until one actually succeeds - set from the Wifi icon's right-click context menu for anyone
    /// who keeps getting bounced from a full channel and would rather not sit there mashing
    /// Connect themselves.
    /// </summary>
    public static bool AutoRetryEnabled { get; private set; }

    /// <summary>
    /// Whether the local client is currently connected to the Novice Network channel.
    /// InfoProxyNoviceNetwork's own Flags byte isn't documented anywhere - this (non-zero) is a
    /// first attempt pending confirmation from a live test; if it turns out wrong, the actual bit
    /// can be found by logging Flags before/after clicking Connect.
    /// </summary>
    public static bool IsConnected
    {
        get
        {
            var proxy = InfoProxyNoviceNetwork.Instance();
            return proxy != null && proxy->Flags != 0;
        }
    }

    /// <summary>
    /// True once a join attempt has had JoinTimeoutMs to succeed and still hasn't - covers "the
    /// channel is full" and any other silent rejection alike, without needing to know the exact
    /// native error text (which never reaches our chat log anyway - it's a native banner, not a
    /// chat line the game hands to Dalamud's ChatMessage event).
    /// </summary>
    public static bool JoinFailed
    {
        get
        {
            if (pendingJoinSinceTicks == null)
            {
                return false;
            }

            if (IsConnected)
            {
                pendingJoinSinceTicks = null;
                return false;
            }

            return Environment.TickCount64 - pendingJoinSinceTicks.Value > JoinTimeoutMs;
        }
    }

    /// <summary>
    /// True for the JoinTimeoutMs window right after a join request was sent, before it's had
    /// time to either succeed or be judged failed - lets the UI show "connecting..." feedback the
    /// instant Connect is clicked instead of going silent until either the green (connected) or
    /// red (JoinFailed) state appears several seconds later.
    /// </summary>
    public static bool IsJoining => pendingJoinSinceTicks != null && !IsConnected && !JoinFailed;

    public static void Connect(IPluginLog log)
    {
        try
        {
            var proxy = InfoProxyNoviceNetwork.Instance();
            if (proxy == null)
            {
                log.Warning($"{LogPrefix} InfoProxyNoviceNetwork nicht verfuegbar.");
                return;
            }

            pendingJoinSinceTicks = Environment.TickCount64;
            proxy->SendJoinRequest();
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte dem Novice Network nicht beitreten.");
        }
    }

    public static void Disconnect(IPluginLog log)
    {
        try
        {
            pendingJoinSinceTicks = null;
            AutoRetryEnabled = false;
            InfoProxyNoviceNetwork.Leave();
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte das Novice Network nicht verlassen.");
        }
    }

    /// <summary>
    /// Starts (or restarts) auto-retrying: sends one join request immediately, then TickAutoRetry
    /// keeps sending another every AutoRetryIntervalMs until IsConnected becomes true, at which
    /// point it turns itself back off. No attempt cap - a full Novice Network channel opens back
    /// up whenever anyone else leaves, which could take anywhere from seconds to a long while.
    /// </summary>
    public static void StartAutoRetry(IPluginLog log)
    {
        AutoRetryEnabled = true;
        lastAutoRetryAttemptTicks = Environment.TickCount64;
        Connect(log);
    }

    public static void StopAutoRetry()
    {
        AutoRetryEnabled = false;
    }

    /// <summary>
    /// Called every framework update (see Plugin.OnFrameworkUpdate) regardless of whether the chat
    /// window is open - a retry loop that only ran while a specific window happened to be visible
    /// would silently stop working the moment someone hid the chat window or was in a cutscene.
    /// </summary>
    public static void TickAutoRetry(IPluginLog log)
    {
        if (!AutoRetryEnabled)
        {
            return;
        }

        if (IsConnected)
        {
            AutoRetryEnabled = false;
            return;
        }

        if (Environment.TickCount64 - lastAutoRetryAttemptTicks < AutoRetryIntervalMs)
        {
            return;
        }

        lastAutoRetryAttemptTicks = Environment.TickCount64;
        Connect(log);
    }

    /// <summary>
    /// Opens the game's own native Novice Network member list - the same one built into the native
    /// chat log's own UI, invoked by sending it the exact event ChatTwo/Chat2 uses for its own
    /// "Novice Network" button (verified against their actual source, GameFunctions.cs'
    /// ClickNoviceNetworkButton, rather than guessed): AgentChatLog.ReceiveEvent with a single
    /// Int=3 AtkValue. No custom-built list needed - this really is the real native window.
    /// </summary>
    public static void OpenNativeMemberList(IPluginLog log)
    {
        try
        {
            var agent = AgentChatLog.Instance();
            if (agent == null)
            {
                log.Warning($"{LogPrefix} AgentChatLog nicht verfuegbar.");
                return;
            }

            var value = new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int, Int = 3 };
            var returnValue = default(AtkValue);
            agent->ReceiveEvent(&returnValue, &value, 0, 0);
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte die native Mitgliederliste nicht oeffnen.");
        }
    }
}
