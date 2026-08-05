using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using CelinesToolkit.Services.ShoppingList;

namespace CelinesToolkit.Windows.Pages;

internal sealed class ShoppingListPage
{
    private static readonly Vector4 FetchButtonColor = new(0.16f, 0.45f, 0.24f, 1f);
    private static readonly Vector4 FetchButtonHoveredColor = new(0.20f, 0.56f, 0.30f, 1f);

    private readonly ItemLookupService itemLookup;
    private readonly ShoppingListPricingService pricingService;
    private readonly FileDialogManager fileDialogManager;

    private List<ShoppingListEntry> entries = new();
    private List<PricedShoppingListItem>? pricedItems;
    private readonly List<string> importedSourceLabels = new();
    private bool homeWorldOnly;
    private string? statusMessage;
    private bool statusIsError;
    private DateTime? lastUpdated;
    private Task<List<PricedShoppingListItem>>? pendingPriceTask;
    private CancellationTokenSource? pendingCts;
    private string searchFilter = string.Empty;

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

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileImport, Loc.T("ShoppingList.ImportButton")))
        {
            fileDialogManager.OpenFileDialog(
                Loc.T("ShoppingList.ImportButton"),
                "Shopping List{.txt}",
                (success, path) =>
                {
                    if (!success)
                    {
                        return;
                    }

                    try
                    {
                        var text = File.ReadAllText(path);
                        ImportEntries(ShoppingListParser.Parse(text), Path.GetFileName(path));
                    }
                    catch (Exception ex)
                    {
                        statusMessage = ex.Message;
                        statusIsError = true;
                    }
                });
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, Loc.T("ShoppingList.PasteButton")))
        {
            try
            {
                var text = ImGui.GetClipboardText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    statusMessage = Loc.T("ShoppingList.Error.ClipboardEmpty");
                    statusIsError = true;
                }
                else
                {
                    ImportEntries(ShoppingListParser.Parse(text), Loc.T("ShoppingList.ClipboardSourceLabel"));
                }
            }
            catch (Exception ex)
            {
                statusMessage = ex.Message;
                statusIsError = true;
            }
        }

        if (entries.Count > 0)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(pendingPriceTask != null);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Eraser, Loc.T("ShoppingList.ClearButton")))
            {
                entries = new List<ShoppingListEntry>();
                importedSourceLabels.Clear();
                pricedItems = null;
                lastUpdated = null;
                statusMessage = null;
            }

            ImGui.EndDisabled();
        }

        if (importedSourceLabels.Count > 0)
        {
            ImGui.TextDisabled(string.Join(", ", importedSourceLabels));
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
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.DollarSign, Loc.T("ShoppingList.FetchPrices"), FetchButtonColor, FetchButtonColor, FetchButtonHoveredColor))
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
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##shoppingListSearch", Loc.T("ShoppingList.SearchHint"), ref searchFilter, 200);
        ImGui.Spacing();

        long grandTotal = 0;
        var warningCount = 0;
        foreach (var item in items)
        {
            if (item.Status == PriceStatus.Ok)
            {
                grandTotal += item.Total;
            }
            else if (item.Status is PriceStatus.NotFound or PriceStatus.NoListings or PriceStatus.Error)
            {
                warningCount++;
            }
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable;
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

        var sortSpecs = ImGui.TableGetSortSpecs();
        if (!sortSpecs.IsNull && sortSpecs.SpecsCount > 0 && sortSpecs.SpecsDirty)
        {
            SortItems(items, sortSpecs.Specs[0]);
            sortSpecs.SpecsDirty = false;
        }

        foreach (var item in items)
        {
            if (searchFilter.Length > 0 && item.Name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

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
                    break;
                case PriceStatus.NotFound:
                    ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("ShoppingList.StatusNotFound"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
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
                    break;
                case PriceStatus.Error:
                    ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("ShoppingList.StatusError"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("-");
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

    private void ImportEntries(List<ShoppingListEntry> imported, string sourceLabel)
    {
        entries = ShoppingListParser.Merge(entries, imported);
        importedSourceLabels.Add(sourceLabel);
        pricedItems = null;
        lastUpdated = null;
        statusMessage = null;
    }

    private static void SortItems(List<PricedShoppingListItem> items, ImGuiTableColumnSortSpecs spec)
    {
        Comparison<PricedShoppingListItem> comparison = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            2 => (a, b) => a.Quantity.CompareTo(b.Quantity),
            3 => (a, b) => a.UnitPrice.CompareTo(b.UnitPrice),
            4 => (a, b) => string.Compare(a.WorldName, b.WorldName, StringComparison.OrdinalIgnoreCase),
            5 => (a, b) => a.Total.CompareTo(b.Total),
            _ => (_, _) => 0,
        };

        items.Sort(comparison);
        if (spec.SortDirection == ImGuiSortDirection.Descending)
        {
            items.Reverse();
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
