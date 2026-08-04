using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using CelinesToolkit.Services.ShoppingList;

namespace CelinesToolkit.Windows.Pages;

internal sealed class ShoppingListPage
{
    private readonly ItemLookupService itemLookup;
    private readonly ShoppingListPricingService pricingService;
    private readonly FileDialogManager fileDialogManager;

    private List<ShoppingListEntry> entries = new();
    private List<PricedShoppingListItem>? pricedItems;
    private string? loadedFileName;
    private bool homeWorldOnly;
    private string? statusMessage;
    private bool statusIsError;
    private DateTime? lastUpdated;
    private Task<List<PricedShoppingListItem>>? pendingPriceTask;
    private CancellationTokenSource? pendingCts;

    public ShoppingListPage(ItemLookupService itemLookup, ShoppingListPricingService pricingService, FileDialogManager fileDialogManager)
    {
        this.itemLookup = itemLookup;
        this.pricingService = pricingService;
        this.fileDialogManager = fileDialogManager;
    }

    public void Draw()
    {
        itemLookup.EnsureBuildStarted();
        PollPendingPriceTask();

        ImGui.TextWrapped(Loc.T("ShoppingList.Description"));
        ImGui.Spacing();

        if (ImGui.Button(Loc.T("ShoppingList.ImportButton")))
        {
            fileDialogManager.OpenFileDialog(
                Loc.T("ShoppingList.ImportButton"),
                "MakePlace Shopping List{.txt}",
                (success, path) =>
                {
                    if (!success)
                    {
                        return;
                    }

                    try
                    {
                        var text = File.ReadAllText(path);
                        entries = ShoppingListParser.Parse(text);
                        loadedFileName = Path.GetFileName(path);
                        pricedItems = null;
                        lastUpdated = null;
                        statusMessage = null;
                    }
                    catch (Exception ex)
                    {
                        statusMessage = ex.Message;
                        statusIsError = true;
                    }
                });
        }

        if (loadedFileName != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(loadedFileName);
        }

        var homeOnly = homeWorldOnly;
        if (ImGui.Checkbox(Loc.T("ShoppingList.HomeWorldOnlyCheckbox"), ref homeOnly))
        {
            homeWorldOnly = homeOnly;
        }

        ImGui.SameLine();

        var fetchBusy = pendingPriceTask != null;
        var lookupReady = itemLookup.IsReady;
        ImGui.BeginDisabled(entries.Count == 0 || fetchBusy || !lookupReady);
        if (ImGui.Button(Loc.T("ShoppingList.FetchPrices")))
        {
            var cts = new CancellationTokenSource();
            pendingCts = cts;
            pendingPriceTask = pricingService.PriceListAsync(entries, homeWorldOnly, cts.Token);
        }

        ImGui.EndDisabled();

        if (!lookupReady)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T("ShoppingList.LookupLoading"));
        }
        else if (fetchBusy)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T("ShoppingList.Fetching"));
        }

        if (statusMessage != null)
        {
            ImGui.TextColored(statusIsError ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.4f, 1f, 0.4f, 1f), statusMessage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (entries.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("ShoppingList.NoListLoaded"));
            return;
        }

        if (pricedItems == null)
        {
            foreach (var group in GroupByCategory(entries))
            {
                ImGui.TextDisabled(Loc.T("ShoppingList.ItemCount", group.Value));
                ImGui.SameLine();
                ImGui.Text(group.Key);
            }

            return;
        }

        DrawResultsTable(pricedItems);
    }

    private void DrawResultsTable(List<PricedShoppingListItem> items)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##shoppingListTable", 6, flags, new Vector2(0, -60)))
        {
            return;
        }

        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnCategory"), ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnItem"));
        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnQuantity"), ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnUnitPrice"), ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnWorld"), ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn(Loc.T("ShoppingList.ColumnTotal"), ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableHeadersRow();

        long grandTotal = 0;
        var warningCount = 0;

        foreach (var item in items)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Category);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Quantity.ToString());
            ImGui.TableNextColumn();

            switch (item.Status)
            {
                case PriceStatus.Ok:
                    ImGui.TextUnformatted(FormatGil(item.UnitPrice));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.DataCenterName != null ? $"{item.WorldName} ({item.DataCenterName})" : item.WorldName ?? "-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatGil(item.Total));
                    grandTotal += item.Total;
                    break;
                case PriceStatus.NotFound:
                    ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("ShoppingList.StatusNotFound"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    warningCount++;
                    break;
                case PriceStatus.NotTradable:
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("ShoppingList.StatusNotTradable"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    break;
                case PriceStatus.NoListings:
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("ShoppingList.StatusNoListings"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    warningCount++;
                    break;
                case PriceStatus.Error:
                    ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("ShoppingList.StatusError"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    warningCount++;
                    break;
                default:
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    break;
            }
        }

        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.Text(Loc.T("ShoppingList.GrandTotal", FormatGil(grandTotal)));

        if (warningCount > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("ShoppingList.WarningCount", warningCount));
        }

        if (lastUpdated is { } updated)
        {
            ImGui.TextDisabled(Loc.T("ShoppingList.LastUpdated", updated.ToString("t")));
        }
    }

    private void PollPendingPriceTask()
    {
        if (pendingPriceTask is not { IsCompleted: true })
        {
            return;
        }

        var task = pendingPriceTask;
        pendingPriceTask = null;
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
            return;
        }

        pricedItems = task.Result;
        lastUpdated = DateTime.Now;
        statusMessage = null;
    }

    private static Dictionary<string, int> GroupByCategory(List<ShoppingListEntry> entries)
    {
        var groups = new Dictionary<string, int>();
        foreach (var entry in entries)
        {
            groups[entry.Category] = groups.GetValueOrDefault(entry.Category) + 1;
        }

        return groups;
    }

    private static string FormatGil(long value)
    {
        return value.ToString("N0") + " Gil";
    }
}
