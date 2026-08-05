using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services.ShoppingList;

public sealed class ShoppingListPricingService
{
    private readonly ItemLookupService itemLookup;
    private readonly UniversalisClient universalisClient;
    private readonly IObjectTable objectTable;

    internal ShoppingListPricingService(ItemLookupService itemLookup, UniversalisClient universalisClient, IObjectTable objectTable)
    {
        this.itemLookup = itemLookup;
        this.universalisClient = universalisClient;
        this.objectTable = objectTable;
    }

    public async Task<List<PricedShoppingListItem>> PriceListAsync(IReadOnlyList<ShoppingListEntry> entries, bool homeWorldOnly, CancellationToken cancellationToken)
    {
        var priced = new List<PricedShoppingListItem>(entries.Count);
        var idsToQuery = new List<uint>();

        foreach (var entry in entries)
        {
            var lookupName = string.Equals(entry.Category, "Dyes", StringComparison.OrdinalIgnoreCase)
                ? entry.Name + " Dye"
                : entry.Name;

            var item = itemLookup.FindByName(lookupName);
            var priceItem = new PricedShoppingListItem
            {
                Category = entry.Category,
                Name = entry.Name,
                Quantity = entry.Quantity,
            };

            if (item == null)
            {
                priceItem.Status = PriceStatus.NotFound;
            }
            else
            {
                priceItem.ItemId = item.Value.RowId;
                priceItem.Icon = item.Value.Icon;
                if (item.Value.IsUntradable)
                {
                    priceItem.Status = PriceStatus.NotTradable;
                }
                else
                {
                    idsToQuery.Add(item.Value.RowId);
                }
            }

            priced.Add(priceItem);
        }

        if (idsToQuery.Count == 0)
        {
            return priced;
        }

        var homeWorld = objectTable.LocalPlayer?.HomeWorld;
        if (homeWorld is not { IsValid: true })
        {
            foreach (var item in priced)
            {
                if (item.Status == PriceStatus.Pending)
                {
                    item.Status = PriceStatus.Error;
                }
            }

            return priced;
        }

        var homeWorldName = homeWorld.Value.Value.Name.ExtractText();

        await universalisClient.EnsureWorldDataLoadedAsync();

        var scope = homeWorldOnly ? homeWorldName : universalisClient.GetRegionForWorld(homeWorldName) ?? homeWorldName;
        var homeDataCenter = universalisClient.GetDataCenterForWorldName(homeWorldName);

        var prices = await universalisClient.GetCheapestPricesAsync(idsToQuery, scope, cancellationToken);

        foreach (var item in priced)
        {
            if (item.ItemId is not uint id || !prices.TryGetValue(id, out var price))
            {
                continue;
            }

            if (!price.HasData)
            {
                item.Status = PriceStatus.NoListings;
                continue;
            }

            item.UnitPrice = price.UnitPrice;
            item.Status = PriceStatus.Ok;

            if (homeWorldOnly)
            {
                item.WorldName = homeWorldName;
                item.DataCenterName = homeDataCenter;
            }
            else
            {
                item.WorldName = price.WorldName;
                item.DataCenterName = price.WorldId is uint worldId ? universalisClient.GetDataCenterForWorldId(worldId) : null;
            }
        }

        return priced;
    }
}
