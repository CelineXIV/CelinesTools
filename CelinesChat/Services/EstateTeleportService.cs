using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace CelinesChat.Services;

/// <summary>
/// "Right-click a friend's name in chat -> Estate Teleportation." Friend detection goes through
/// InfoProxyFriendList.GetEntryByName; the trigger itself is
/// AgentFriendlist.OpenFriendEstateTeleportation(contentId) - a small, purpose-built native function
/// that needs only the friend's ContentId (also read straight off InfoProxyFriendList's CharacterData).
/// This replaced an earlier approach that opened the Friend List's own right-click context menu and
/// clicked through it by simulated FireCallback calls - that worked, but only while the Friend List
/// window itself happened to be open on screen. This dedicated agent call needs no UI open at all.
/// </summary>
internal sealed unsafe class EstateTeleportService
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    private Dictionary<string, ushort>? worldIdsByName;

    public EstateTeleportService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    /// <summary>
    /// True if the given character is on the local player's Friend List. Works regardless of whether
    /// the Friend List window is open or the character is currently nearby.
    /// </summary>
    public bool IsFriend(string name, string? world)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(world))
        {
            return false;
        }

        if (!TryGetWorldId(world, out var worldId))
        {
            return false;
        }

        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
        {
            return false;
        }

        return proxy->GetEntryByName(name, worldId) != null;
    }

    public void TryTriggerEstateTeleport(string name, string? world)
    {
        if (string.IsNullOrEmpty(world))
        {
            log.Warning($"[CelinesChat][EstateTeleport] '{name}' hat keine bekannte Welt - abgebrochen.");
            return;
        }

        if (!TryGetWorldId(world, out var worldId))
        {
            log.Warning($"[CelinesChat][EstateTeleport] Konnte die Welt '{world}' nicht aufloesen.");
            return;
        }

        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
        {
            log.Warning("[CelinesChat][EstateTeleport] InfoProxyFriendList war nicht verfuegbar.");
            return;
        }

        var target = proxy->GetEntryByName(name, worldId);
        if (target == null)
        {
            log.Warning($"[CelinesChat][EstateTeleport] '{name}' ist kein Freund - abgebrochen.");
            return;
        }

        var agent = AgentFriendlist.Instance();
        if (agent == null)
        {
            log.Warning("[CelinesChat][EstateTeleport] AgentFriendlist war nicht verfuegbar.");
            return;
        }

        agent->OpenFriendEstateTeleportation(target->ContentId);
        log.Info($"[CelinesChat][EstateTeleport] Anwesen-Teleport fuer '{name}' ausgeloest.");
    }

    private bool TryGetWorldId(string worldName, out ushort worldId)
    {
        worldIdsByName ??= BuildWorldIdLookup();
        return worldIdsByName.TryGetValue(worldName, out worldId);
    }

    private Dictionary<string, ushort> BuildWorldIdLookup()
    {
        var lookup = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var sheet = dataManager.GetExcelSheet<World>();
            foreach (var row in sheet)
            {
                var name = row.Name.ExtractText();
                if (!string.IsNullOrEmpty(name))
                {
                    lookup[name] = (ushort)row.RowId;
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat][EstateTeleport] Konnte World-Sheet nicht laden.");
        }

        return lookup;
    }
}
