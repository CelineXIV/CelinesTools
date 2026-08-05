using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services.HousingTracker;

internal sealed class PaissaClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly IPluginLog log;

    public PaissaClient(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://paissadb.zhu.codes/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public async Task<WorldDetail?> GetWorldDetailAsync(uint worldId, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await httpClient.GetStringAsync($"worlds/{worldId}", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            log.Warning(ex, $"[CelinesToolkit] PaissaDB-Abfrage fuer Welt {worldId} fehlgeschlagen.");
            return null;
        }

        if (JsonNode.Parse(json) is not JsonObject root)
        {
            return null;
        }

        var districts = new List<DistrictDetail>();
        if (root.TryGetPropertyValue("districts", out var districtsNode) && districtsNode is JsonArray districtsArray)
        {
            foreach (var districtNode in districtsArray)
            {
                if (districtNode is not JsonObject districtObj)
                {
                    continue;
                }

                var districtId = GetInt(districtObj, "id") ?? 0;
                var districtName = GetString(districtObj, "name") ?? "?";
                var plots = new List<OpenPlotDetail>();

                if (districtObj.TryGetPropertyValue("open_plots", out var plotsNode) && plotsNode is JsonArray plotsArray)
                {
                    foreach (var plotNode in plotsArray)
                    {
                        if (plotNode is not JsonObject plotObj)
                        {
                            continue;
                        }

                        var lottoPhaseRaw = GetInt(plotObj, "lotto_phase");
                        var lottoPhaseUntilRaw = GetLong(plotObj, "lotto_phase_until");

                        plots.Add(new OpenPlotDetail
                        {
                            WardNumber = GetInt(plotObj, "ward_number") ?? 0,
                            PlotNumber = GetInt(plotObj, "plot_number") ?? 0,
                            Size = (HouseSize)(GetInt(plotObj, "size") ?? 0),
                            Price = GetLong(plotObj, "price") ?? 0,
                            PurchaseSystem = (PurchaseSystem)(GetInt(plotObj, "purchase_system") ?? 0),
                            LottoEntries = GetInt(plotObj, "lotto_entries"),
                            LottoPhase = lottoPhaseRaw is int phase ? (LotteryPhase)phase : null,
                            LottoPhaseUntil = lottoPhaseUntilRaw is long until ? DateTimeOffset.FromUnixTimeSeconds(until) : null,
                        });
                    }
                }

                districts.Add(new DistrictDetail
                {
                    Id = districtId,
                    Name = districtName,
                    OpenPlots = plots,
                });
            }
        }

        return new WorldDetail
        {
            Id = GetInt(root, "id") ?? (int)worldId,
            Name = GetString(root, "name") ?? "?",
            Districts = districts,
        };
    }

    private static string? GetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node != null ? node.GetValue<string>() : null;
    }

    private static int? GetInt(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node != null ? (int)node.GetValue<long>() : null;
    }

    private static long? GetLong(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node != null ? node.GetValue<long>() : null;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
