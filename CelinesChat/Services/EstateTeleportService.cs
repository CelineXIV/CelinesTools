using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace CelinesChat.Services;

internal sealed unsafe class EstateTeleportService
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    private Dictionary<string, ushort>? worldIdsByName;
    private Dictionary<ushort, string>? worldNamesById;

    public EstateTeleportService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

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

    public List<(ulong ContentId, string Name, string? World)> GetFriends()
    {
        var result = new List<(ulong, string, string?)>();
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
        {
            return result;
        }

        worldIdsByName ??= BuildWorldIdLookup();
        var count = proxy->GetEntryCount();
        for (uint i = 0; i < count; i++)
        {
            var friend = proxy->GetEntry(i);
            if (friend == null || friend->ContentId == 0)
            {
                continue;
            }

            var name = friend->NameString;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            worldNamesById!.TryGetValue(friend->HomeWorld, out var world);
            result.Add((friend->ContentId, name, world));
        }

        return result;
    }

    private bool TryGetWorldId(string worldName, out ushort worldId)
    {
        worldIdsByName ??= BuildWorldIdLookup();
        return worldIdsByName.TryGetValue(worldName, out worldId);
    }

    private Dictionary<string, ushort> BuildWorldIdLookup()
    {
        var lookup = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        worldNamesById = new Dictionary<ushort, string>();

        try
        {
            var sheet = dataManager.GetExcelSheet<World>();
            foreach (var row in sheet)
            {
                var name = row.Name.ExtractText();
                if (!string.IsNullOrEmpty(name))
                {
                    lookup[name] = (ushort)row.RowId;
                    worldNamesById[(ushort)row.RowId] = name;
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
