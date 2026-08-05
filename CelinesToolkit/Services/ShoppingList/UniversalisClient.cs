using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services.ShoppingList;

public readonly record struct MarketPriceResult(uint ItemId, bool HasData, long UnitPrice, string? WorldName, uint? WorldId)
{
    public static MarketPriceResult NoData(uint itemId) => new(itemId, false, 0, null, null);
}

internal sealed class UniversalisClient : IDisposable
{
    private const int BatchSize = 90;

    private readonly HttpClient httpClient;
    private readonly IPluginLog log;

    private Dictionary<string, uint> worldIdByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<uint, string> dataCenterByWorldId = new();
    private Dictionary<uint, string> regionByWorldId = new();
    private Task? worldDataTask;

    public UniversalisClient(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://universalis.app/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public async Task EnsureWorldDataLoadedAsync()
    {
        // Cached so repeated fetches don't reload world/DC data every time, but if the underlying
        // fetch failed (e.g. a transient network hiccup) the failed task must not be cached forever -
        // otherwise the very first failure permanently breaks price fetching for the rest of the
        // session, since every future call would just rethrow that same stale failure.
        var task = worldDataTask ??= LoadWorldDataAsync();
        try
        {
            await task;
        }
        catch
        {
            if (worldDataTask == task)
            {
                worldDataTask = null;
            }

            throw;
        }
    }

    public string? GetRegionForWorld(string worldName)
    {
        return worldIdByName.TryGetValue(worldName, out var worldId) && regionByWorldId.TryGetValue(worldId, out var region)
            ? region
            : null;
    }

    public string? GetDataCenterForWorldName(string worldName)
    {
        return worldIdByName.TryGetValue(worldName, out var worldId) ? GetDataCenterForWorldId(worldId) : null;
    }

    public string? GetDataCenterForWorldId(uint worldId)
    {
        return dataCenterByWorldId.TryGetValue(worldId, out var dc) ? dc : null;
    }

    public async Task<Dictionary<uint, MarketPriceResult>> GetCheapestPricesAsync(IReadOnlyList<uint> itemIds, string scopeName, CancellationToken cancellationToken)
    {
        var result = new Dictionary<uint, MarketPriceResult>();

        foreach (var chunk in itemIds.Chunk(BatchSize))
        {
            var idsParam = string.Join(",", chunk);
            var url = $"api/v2/{Uri.EscapeDataString(scopeName)}/{idsParam}?listings=1&entries=0";
            var requestedSet = new HashSet<uint>(chunk);

            string json;
            try
            {
                json = await httpClient.GetStringAsync(url, cancellationToken);
            }
            // A caught OperationCanceledException here almost always means the HttpClient's own
            // 20s request timeout fired (a TaskCanceledException, which is an OperationCanceledException),
            // not that our own cancellationToken was cancelled - only the latter should be allowed to
            // propagate and cancel the whole fetch; a plain timeout should just mark this chunk as
            // having no data and let the rest of the fetch continue/complete normally.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                log.Warning(ex, $"[CelinesToolkit] Universalis-Abfrage fehlgeschlagen fuer {chunk.Length} Item(s).");
                foreach (var id in requestedSet)
                {
                    result[id] = MarketPriceResult.NoData(id);
                }

                continue;
            }

            var root = JsonNode.Parse(json);
            if (root is JsonObject rootObj)
            {
                if (rootObj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonObject itemsObj)
                {
                    foreach (var kv in itemsObj)
                    {
                        if (kv.Value is not JsonObject itemObj || !uint.TryParse(kv.Key, out var itemId))
                        {
                            continue;
                        }

                        result[itemId] = ParseItemPayload(itemId, itemObj);
                        requestedSet.Remove(itemId);
                    }

                    if (rootObj.TryGetPropertyValue("unresolvedItems", out var unresolvedNode) && unresolvedNode is JsonArray unresolvedArray)
                    {
                        foreach (var idNode in unresolvedArray)
                        {
                            if (idNode == null)
                            {
                                continue;
                            }

                            var itemId = (uint)idNode.GetValue<long>();
                            result[itemId] = MarketPriceResult.NoData(itemId);
                            requestedSet.Remove(itemId);
                        }
                    }
                }
                else if (rootObj.TryGetPropertyValue("itemID", out var idNode2) && idNode2 != null)
                {
                    var itemId = (uint)idNode2.GetValue<long>();
                    result[itemId] = ParseItemPayload(itemId, rootObj);
                    requestedSet.Remove(itemId);
                }
            }

            foreach (var missingId in requestedSet)
            {
                result[missingId] = MarketPriceResult.NoData(missingId);
            }
        }

        return result;
    }

    private static MarketPriceResult ParseItemPayload(uint itemId, JsonObject payload)
    {
        if (payload.TryGetPropertyValue("listings", out var listingsNode)
            && listingsNode is JsonArray { Count: > 0 } listings
            && listings[0] is JsonObject firstListing
            && firstListing.TryGetPropertyValue("pricePerUnit", out var priceNode)
            && priceNode != null)
        {
            var price = priceNode.GetValue<long>();
            string? worldName = firstListing.TryGetPropertyValue("worldName", out var worldNameNode) ? worldNameNode?.GetValue<string>() : null;
            uint? worldId = firstListing.TryGetPropertyValue("worldID", out var worldIdNode) && worldIdNode != null
                ? (uint)worldIdNode.GetValue<long>()
                : null;
            return new MarketPriceResult(itemId, true, price, worldName, worldId);
        }

        return MarketPriceResult.NoData(itemId);
    }

    private async Task LoadWorldDataAsync()
    {
        var worldsJson = await httpClient.GetStringAsync("api/v2/worlds");
        var dcJson = await httpClient.GetStringAsync("api/v2/data-centers");

        var idByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (JsonNode.Parse(worldsJson) is JsonArray worldsArray)
        {
            foreach (var worldNode in worldsArray)
            {
                if (worldNode is not JsonObject worldObj
                    || !worldObj.TryGetPropertyValue("id", out var idNode) || idNode == null
                    || !worldObj.TryGetPropertyValue("name", out var nameNode) || nameNode == null)
                {
                    continue;
                }

                idByName[nameNode.GetValue<string>()] = (uint)idNode.GetValue<long>();
            }
        }

        var dcByWorld = new Dictionary<uint, string>();
        var regionByWorld = new Dictionary<uint, string>();
        if (JsonNode.Parse(dcJson) is JsonArray dcArray)
        {
            foreach (var dcNode in dcArray)
            {
                if (dcNode is not JsonObject dcObj
                    || !dcObj.TryGetPropertyValue("name", out var dcNameNode) || dcNameNode == null
                    || !dcObj.TryGetPropertyValue("region", out var regionNode) || regionNode == null
                    || !dcObj.TryGetPropertyValue("worlds", out var worldsNode) || worldsNode is not JsonArray worldIdsArray)
                {
                    continue;
                }

                var dcName = dcNameNode.GetValue<string>();
                var region = regionNode.GetValue<string>();
                foreach (var worldIdNode in worldIdsArray)
                {
                    if (worldIdNode == null)
                    {
                        continue;
                    }

                    var worldId = (uint)worldIdNode.GetValue<long>();
                    dcByWorld[worldId] = dcName;
                    regionByWorld[worldId] = region;
                }
            }
        }

        worldIdByName = idByName;
        dataCenterByWorldId = dcByWorld;
        regionByWorldId = regionByWorld;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
