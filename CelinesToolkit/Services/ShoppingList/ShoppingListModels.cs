namespace CelinesToolkit.Services.ShoppingList;

public sealed class ShoppingListEntry
{
    public required string Category { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }
}

public enum PriceStatus
{
    Pending,
    NotFound,
    NotTradable,
    NoListings,
    Ok,
    Error,
}

public sealed class PricedShoppingListItem
{
    public required string Category { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public uint? ItemId { get; set; }

    public ushort Icon { get; set; }

    public PriceStatus Status { get; set; } = PriceStatus.Pending;

    public long UnitPrice { get; set; }

    public string? WorldName { get; set; }

    public string? DataCenterName { get; set; }

    public long Total => UnitPrice * Quantity;
}
