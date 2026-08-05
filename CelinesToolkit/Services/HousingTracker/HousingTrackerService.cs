using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services.HousingTracker;

public sealed class HousingPlotRow
{
    public required string DistrictName { get; init; }

    public required int WardNumber { get; init; }

    public required int PlotNumber { get; init; }

    public required HouseSize Size { get; init; }

    public required long Price { get; init; }

    public required PurchaseSystem PurchaseSystem { get; init; }

    public int? LottoEntries { get; init; }

    public LotteryPhase? LottoPhase { get; init; }

    public System.DateTimeOffset? LottoPhaseUntil { get; init; }
}

public sealed class HousingTrackerResult
{
    public required string WorldName { get; init; }

    public required List<HousingPlotRow> Plots { get; init; }
}

public sealed class HousingTrackerService
{
    private readonly PaissaClient client;
    private readonly IObjectTable objectTable;

    internal HousingTrackerService(PaissaClient client, IObjectTable objectTable)
    {
        this.client = client;
        this.objectTable = objectTable;
    }

    public async Task<HousingTrackerResult?> FetchAsync(CancellationToken cancellationToken)
    {
        var homeWorld = objectTable.LocalPlayer?.HomeWorld;
        if (homeWorld is not { IsValid: true })
        {
            return null;
        }

        var worldId = homeWorld.Value.RowId;
        var worldName = homeWorld.Value.Value.Name.ExtractText();

        var detail = await client.GetWorldDetailAsync(worldId, cancellationToken);
        if (detail == null)
        {
            return null;
        }

        var rows = new List<HousingPlotRow>();
        foreach (var district in detail.Districts)
        {
            foreach (var plot in district.OpenPlots)
            {
                rows.Add(new HousingPlotRow
                {
                    DistrictName = district.Name,
                    WardNumber = plot.WardNumber,
                    PlotNumber = plot.PlotNumber,
                    Size = plot.Size,
                    Price = plot.Price,
                    PurchaseSystem = plot.PurchaseSystem,
                    LottoEntries = plot.LottoEntries,
                    LottoPhase = plot.LottoPhase,
                    LottoPhaseUntil = plot.LottoPhaseUntil,
                });
            }
        }

        return new HousingTrackerResult
        {
            WorldName = worldName,
            Plots = rows,
        };
    }
}
