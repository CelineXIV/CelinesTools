using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using CelinesToolkit.Services.HousingTracker;

namespace CelinesToolkit.Windows.Pages;

internal sealed class HousingTrackerPage
{
    private static readonly Vector4 EntryOpenColor = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 ResultsColor = new(1f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 UnavailableColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 DirectPurchaseColor = new(0.5f, 0.7f, 1f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private readonly HousingTrackerService service;

    private HousingTrackerResult? result;
    private Task<HousingTrackerResult?>? pendingTask;
    private CancellationTokenSource? pendingCts;
    private string? statusMessage;
    private bool statusIsError;
    private DateTime? lastUpdated;

    private HouseSize? sizeFilter;
    private string? districtFilter;

    public HousingTrackerPage(HousingTrackerService service)
    {
        this.service = service;
    }

    public void Draw()
    {
        PollPendingTask();

        ImGui.TextWrapped(Loc.T("HousingTracker.Description"));
        ImGui.Spacing();

        var fetchBusy = pendingTask != null;
        ImGui.BeginDisabled(fetchBusy);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Sync, Loc.T("HousingTracker.FetchButton")))
        {
            var cts = new CancellationTokenSource();
            pendingCts = cts;
            pendingTask = service.FetchAsync(cts.Token);
        }

        ImGui.EndDisabled();

        if (fetchBusy)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T("HousingTracker.Fetching"));
        }

        if (statusMessage != null)
        {
            ImGui.TextColored(statusIsError ? ErrorColor : new Vector4(0.4f, 1f, 0.4f, 1f), statusMessage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (result == null)
        {
            ImGui.TextDisabled(Loc.T("HousingTracker.NoDataYet"));
            return;
        }

        if (result.Plots.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("HousingTracker.NoPlotsOpen"));
            return;
        }

        DrawFilters(result.Plots);
        ImGui.Spacing();

        var filtered = result.Plots
            .Where(p => sizeFilter == null || p.Size == sizeFilter)
            .Where(p => districtFilter == null || p.DistrictName == districtFilter)
            .ToList();

        DrawTable(filtered);

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("HousingTracker.ResultCount", filtered.Count, result.Plots.Count));

        if (lastUpdated is { } updated)
        {
            ImGui.TextDisabled(Loc.T("HousingTracker.LastUpdated", updated.ToString("t")));
        }
    }

    private void DrawFilters(List<HousingPlotRow> allPlots)
    {
        var districts = allPlots.Select(p => p.DistrictName).Distinct().ToList();

        ImGui.SetNextItemWidth(160);
        var sizeLabel = sizeFilter switch
        {
            HouseSize.Small => Loc.T("HousingTracker.Size.Small"),
            HouseSize.Medium => Loc.T("HousingTracker.Size.Medium"),
            HouseSize.Large => Loc.T("HousingTracker.Size.Large"),
            _ => Loc.T("HousingTracker.FilterAll"),
        };
        if (ImGui.BeginCombo(Loc.T("HousingTracker.SizeFilterLabel"), sizeLabel))
        {
            if (ImGui.Selectable(Loc.T("HousingTracker.FilterAll"), sizeFilter == null))
            {
                sizeFilter = null;
            }

            foreach (var size in new[] { HouseSize.Small, HouseSize.Medium, HouseSize.Large })
            {
                var label = size switch
                {
                    HouseSize.Small => Loc.T("HousingTracker.Size.Small"),
                    HouseSize.Medium => Loc.T("HousingTracker.Size.Medium"),
                    _ => Loc.T("HousingTracker.Size.Large"),
                };
                if (ImGui.Selectable(label, sizeFilter == size))
                {
                    sizeFilter = size;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo(Loc.T("HousingTracker.DistrictFilterLabel"), districtFilter ?? Loc.T("HousingTracker.FilterAll")))
        {
            if (ImGui.Selectable(Loc.T("HousingTracker.FilterAll"), districtFilter == null))
            {
                districtFilter = null;
            }

            foreach (var district in districts)
            {
                if (ImGui.Selectable(district, districtFilter == district))
                {
                    districtFilter = district;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawTable(List<HousingPlotRow> plots)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable;
        if (!ImGui.BeginTable("##housingTrackerTable", 8, flags, new Vector2(0, -40)))
        {
            return;
        }

        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnDistrict"), ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnWard"), ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnPlot"), ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnSize"), ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnPrice"), ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnPhase"), ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnTickets"), ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn(Loc.T("HousingTracker.ColumnPhaseUntil"), ImGuiTableColumnFlags.WidthFixed, 140f);
        ImGui.TableHeadersRow();

        var sortSpecs = ImGui.TableGetSortSpecs();
        if (!sortSpecs.IsNull && sortSpecs.SpecsCount > 0 && sortSpecs.SpecsDirty)
        {
            SortPlots(plots, sortSpecs.Specs[0]);
            sortSpecs.SpecsDirty = false;
        }

        foreach (var plot in plots)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plot.DistrictName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((plot.WardNumber + 1).ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((plot.PlotNumber + 1).ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(SizeLabel(plot.Size));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(plot.Price));
            ImGui.TableNextColumn();
            var (phaseLabel, phaseColor) = PhaseLabelAndColor(plot);
            ImGui.TextColored(phaseColor, phaseLabel);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plot.LottoEntries?.ToString() ?? "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plot.LottoPhaseUntil is { } until ? until.LocalDateTime.ToString("g") : "-");
        }

        ImGui.EndTable();
    }

    private static void SortPlots(List<HousingPlotRow> plots, ImGuiTableColumnSortSpecs spec)
    {
        Comparison<HousingPlotRow> comparison = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.DistrictName, b.DistrictName, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.WardNumber.CompareTo(b.WardNumber),
            2 => (a, b) => a.PlotNumber.CompareTo(b.PlotNumber),
            3 => (a, b) => a.Size.CompareTo(b.Size),
            4 => (a, b) => a.Price.CompareTo(b.Price),
            5 => (a, b) => (a.LottoPhase ?? 0).CompareTo(b.LottoPhase ?? 0),
            6 => (a, b) => (a.LottoEntries ?? -1).CompareTo(b.LottoEntries ?? -1),
            7 => (a, b) => Nullable.Compare(a.LottoPhaseUntil, b.LottoPhaseUntil),
            _ => (_, _) => 0,
        };

        plots.Sort(comparison);
        if (spec.SortDirection == ImGuiSortDirection.Descending)
        {
            plots.Reverse();
        }
    }

    private static string SizeLabel(HouseSize size)
    {
        return size switch
        {
            HouseSize.Small => Loc.T("HousingTracker.Size.Small"),
            HouseSize.Medium => Loc.T("HousingTracker.Size.Medium"),
            HouseSize.Large => Loc.T("HousingTracker.Size.Large"),
            _ => size.ToString(),
        };
    }

    private static (string Label, Vector4 Color) PhaseLabelAndColor(HousingPlotRow plot)
    {
        return plot.LottoPhase switch
        {
            LotteryPhase.EntryOpen => (Loc.T("HousingTracker.Phase.EntryOpen"), EntryOpenColor),
            LotteryPhase.Results => (Loc.T("HousingTracker.Phase.Results"), ResultsColor),
            LotteryPhase.Unavailable => (Loc.T("HousingTracker.Phase.Unavailable"), UnavailableColor),
            _ => (Loc.T("HousingTracker.Phase.DirectPurchase"), DirectPurchaseColor),
        };
    }

    private static string FormatGil(long value)
    {
        return value.ToString("N0") + " Gil";
    }

    private void PollPendingTask()
    {
        if (pendingTask is not { IsCompleted: true })
        {
            return;
        }

        var task = pendingTask;
        pendingTask = null;
        pendingCts?.Dispose();
        pendingCts = null;

        if (task.IsFaulted)
        {
            statusMessage = task.Exception?.GetBaseException().Message;
            statusIsError = true;
            return;
        }

        if (task.IsCanceled)
        {
            statusMessage = Loc.T("HousingTracker.Error.TimedOut");
            statusIsError = true;
            return;
        }

        if (task.Result == null)
        {
            statusMessage = Loc.T("HousingTracker.Error.NoHomeWorld");
            statusIsError = true;
            return;
        }

        result = task.Result;
        lastUpdated = DateTime.Now;
        statusMessage = null;
    }
}
