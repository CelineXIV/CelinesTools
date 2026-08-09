using System;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CelinesToolkit.Services;

/// <summary>
/// Auto-accepts a trade once a whitelisted partner has put items into it, and confirms the follow-up
/// "Complete trade?" dialog. FireCallback(close: true, [Int=0, Undefined]) = click Accept/Trade on the
/// Trade addon, and FireCallback("SelectYesno", true, [0]) = confirm Yes - both captured from a real
/// test trade with a diagnostic build logging every AtkValue a real click actually sends.
///
/// There is no client-observable signal for "the OTHER player has clicked their own Accept" - three
/// separate live-test diagnostic rounds all came back empty, most likely because the game deliberately
/// doesn't expose that state client-side. So this accepts as soon as a whitelisted partner has items in
/// the trade, without waiting for their own click - which is safe in practice because nothing is
/// exchanged until BOTH sides have also confirmed the final Yes/No dialog.
///
/// The partner's item slots are polled every framework tick (via IGameGui.GetAddonByName, not addon
/// lifecycle events - those turned out to fire unreliably/inconsistently around the actual accept
/// moment during earlier tests) and only accepted once the offer has held perfectly steady for
/// DebounceMs. Two real bugs drove this shape: (1) a one-shot "already accepted" flag left the trade
/// stuck if the partner added a second item afterwards - fixed by comparing against a snapshot of the
/// current offer instead of a bool; (2) reacting to every single change immediately caused a live
/// "hiccup" while the partner was still dragging/typing a quantity for a stacked item, since the offer
/// updates continuously during that - fixed by requiring the snapshot to stay unchanged for a short
/// window before treating it as final.
/// </summary>
internal sealed unsafe class TradeAutoAcceptService : IDisposable
{
    private const string LogPrefix = "[CelinesToolkit][AutoAcceptTrade]";

    /// <summary>How long the partner's offer must stay completely unchanged before it's treated as final and accepted - long enough to ride out a stack-quantity picker being actively adjusted.</summary>
    private const long DebounceMs = 600;

    /// <summary>Text node holding the trade partner's display name, e.g. "Lumayra Evergarden" - found by walking the Trade addon's own node list during diagnostics. No world is shown here, hence the IObjectTable lookup below.</summary>
    private const uint PartnerNameNodeId = 17;

    /// <summary>Text node holding the SelectYesno prompt text - checked against "trade" so an unrelated Yes/No dialog that happens to pop up around the same time is never blindly confirmed.</summary>
    private const uint PromptTextNodeId = 2;

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Configuration configuration;

    private bool listenersRegistered;
    private bool waitingForOwnConfirm;
    private string? lastAcceptedItemsSnapshot;
    private string? pendingItemsSnapshot;
    private long pendingSinceTicks;

    public TradeAutoAcceptService(IFramework framework, IGameGui gameGui, IAddonLifecycle addonLifecycle, IObjectTable objectTable, IPluginLog log, Configuration configuration)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.objectTable = objectTable;
        this.log = log;
        this.configuration = configuration;
    }

    /// <summary>Called once at startup (to resume a persisted on-state) and whenever the settings checkbox is toggled.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == listenersRegistered)
        {
            return;
        }

        if (enabled)
        {
            framework.Update += OnFrameworkUpdate;
            addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnSelectYesnoOpened);

            ResetTradeState();
            listenersRegistered = true;
            log.Info($"{LogPrefix} Enabled.");
        }
        else
        {
            framework.Update -= OnFrameworkUpdate;
            addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", OnSelectYesnoOpened);

            listenersRegistered = false;
            log.Info($"{LogPrefix} Disabled.");
        }
    }

    private void ResetTradeState()
    {
        waitingForOwnConfirm = false;
        lastAcceptedItemsSnapshot = null;
        pendingItemsSnapshot = null;
    }

    private void OnFrameworkUpdate(IFramework frameworkInstance)
    {
        if (!configuration.AutoAcceptTradeEnabled)
        {
            return;
        }

        try
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>("Trade");
            if (addon == null)
            {
                // Trade window isn't open - clear any state left over from a prior trade.
                if (waitingForOwnConfirm || lastAcceptedItemsSnapshot != null || pendingItemsSnapshot != null)
                {
                    ResetTradeState();
                }

                return;
            }

            if (waitingForOwnConfirm)
            {
                // Already accepted the current offer and waiting on the confirm dialog - don't
                // evaluate further changes until that's resolved.
                return;
            }

            var partnerName = FindTextNode(addon, PartnerNameNodeId)?.Trim();
            if (string.IsNullOrWhiteSpace(partnerName) || !IsWhitelisted(partnerName))
            {
                pendingItemsSnapshot = null;
                return;
            }

            var itemsSnapshot = GetPartnerItemsSnapshot();
            if (itemsSnapshot == null || itemsSnapshot == lastAcceptedItemsSnapshot)
            {
                pendingItemsSnapshot = null;
                return;
            }

            if (itemsSnapshot != pendingItemsSnapshot)
            {
                // Offer just changed (or is still changing, e.g. a stack-quantity picker being
                // dragged) - remember it and wait for it to hold steady before treating it as final.
                pendingItemsSnapshot = itemsSnapshot;
                pendingSinceTicks = Environment.TickCount64;
                return;
            }

            if (Environment.TickCount64 - pendingSinceTicks < DebounceMs)
            {
                return;
            }

            log.Info($"{LogPrefix} Whitelisted partner \"{partnerName}\" has items in the trade - auto-accepting.");
            lastAcceptedItemsSnapshot = itemsSnapshot;
            pendingItemsSnapshot = null;
            waitingForOwnConfirm = true;

            var values = stackalloc AtkValue[2];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            values[0].Int = 0;
            addon->FireCallback(2, values, true);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"{LogPrefix} Failed while checking the trade partner.");
        }
    }

    private void OnSelectYesnoOpened(AddonEvent type, AddonArgs args)
    {
        if (!configuration.AutoAcceptTradeEnabled || !waitingForOwnConfirm)
        {
            return;
        }

        try
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            var prompt = FindTextNode(addon, PromptTextNodeId);

            // Not our own trade-completion prompt (e.g. some unrelated Yes/No dialog happened to
            // open right now) - leave it alone rather than blindly confirming whatever it is.
            if (prompt == null || !prompt.Contains("trade", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            log.Info($"{LogPrefix} Confirming trade completion dialog: \"{prompt}\"");
            waitingForOwnConfirm = false;

            var value = stackalloc AtkValue[1];
            value[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            value[0].Int = 0;
            addon->FireCallback(1, value, true);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"{LogPrefix} Failed while confirming the trade dialog.");
        }
    }

    /// <summary>
    /// The Trade addon only ever shows the partner's bare display name, no world - resolving it
    /// through IObjectTable (only characters actually nearby, which trading already requires) ties
    /// the whitelist check to a real game object's actual home world instead of trusting a
    /// potentially ambiguous UI string. If more than one nearby character shares that name, this
    /// deliberately refuses to guess and does not auto-accept - a same-named character on a
    /// different world is exactly the kind of impersonation risk a trade whitelist exists to avoid.
    /// </summary>
    private bool IsWhitelisted(string partnerName)
    {
        IPlayerCharacter? match = null;
        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter character || !string.Equals(character.Name.TextValue, partnerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                log.Warning($"{LogPrefix} Multiple nearby characters named \"{partnerName}\" - refusing to auto-accept (ambiguous identity).");
                return false;
            }

            match = character;
        }

        if (match == null)
        {
            log.Warning($"{LogPrefix} Could not resolve \"{partnerName}\" to a nearby character - refusing to auto-accept.");
            return false;
        }

        var world = match.HomeWorld.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrEmpty(world))
        {
            return false;
        }

        var identity = $"{partnerName}@{world}";
        foreach (var entry in configuration.AutoAcceptTradeWhitelist)
        {
            if (string.Equals(entry.Trim(), identity, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// AgentTrade (the game-state object behind the Trade addon, distinct from the UI itself) keeps
    /// the 5 item slots the partner is offering us in ItemsReviece (typo is upstream in
    /// FFXIVClientStructs, not ours). Returns null while all 5 slots are empty (nothing to accept
    /// yet), otherwise a snapshot string covering every slot's item id + stack size - comparing this
    /// against the last-accepted snapshot is what lets a later item addition (which invalidates an
    /// earlier accept in-game) trigger a fresh accept instead of being silently ignored.
    /// </summary>
    private static unsafe string? GetPartnerItemsSnapshot()
    {
        var agent = AgentTrade.Instance();
        if (agent == null)
        {
            return null;
        }

        var hasItems = false;
        var snapshot = new StringBuilder();
        foreach (var item in agent->ItemsReviece)
        {
            if (item.Id != 0)
            {
                hasItems = true;
            }

            snapshot.Append(item.Id).Append(':').Append(item.StackSize).Append(',');
        }

        return hasItems ? snapshot.ToString() : null;
    }

    private static string? FindTextNode(AtkUnitBase* addon, uint nodeId)
    {
        if (addon == null)
        {
            return null;
        }

        var nodes = addon->UldManager.Nodes;
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i].Value;
            if (node != null && node->NodeId == nodeId && node->Type == NodeType.Text)
            {
                return ((AtkTextNode*)node)->NodeText.ToString();
            }
        }

        return null;
    }

    public void Dispose() => SetEnabled(false);
}
