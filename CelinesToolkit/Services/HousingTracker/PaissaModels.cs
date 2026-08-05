using System;
using System.Collections.Generic;

namespace CelinesToolkit.Services.HousingTracker;

public enum HouseSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

[Flags]
public enum PurchaseSystem
{
    None = 0,
    Lottery = 1,
    FreeCompany = 2,
    Individual = 4,
}

public enum LotteryPhase
{
    None = 0,
    EntryOpen = 1,
    Results = 2,
    Unavailable = 3,
}

public sealed class OpenPlotDetail
{
    public required int WardNumber { get; init; }

    public required int PlotNumber { get; init; }

    public required HouseSize Size { get; init; }

    public required long Price { get; init; }

    public required PurchaseSystem PurchaseSystem { get; init; }

    public int? LottoEntries { get; init; }

    public LotteryPhase? LottoPhase { get; init; }

    public DateTimeOffset? LottoPhaseUntil { get; init; }
}

public sealed class DistrictDetail
{
    public required int Id { get; init; }

    public required string Name { get; init; } = string.Empty;

    public required List<OpenPlotDetail> OpenPlots { get; init; } = new();
}

public sealed class WorldDetail
{
    public required int Id { get; init; }

    public required string Name { get; init; } = string.Empty;

    public required List<DistrictDetail> Districts { get; init; } = new();
}
