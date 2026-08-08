using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows;

/// <summary>
/// Standalone window that live-mirrors Glamourer's own design list (via GlamourerIpcService,
/// since Glamourer's public IPC has no draw-hook to inject into its own window the way Penumbra's
/// PreSettingsTabBarDraw does - see PenumbraPanelIntegration for that contrast), lets the user
/// attach a preview image per design (reusing the exact same PreviewImageService/PreviewTextureCache
/// machinery the Penumbra Preview Manager already uses, just pointed at a per-design-GUID folder
/// instead of a mod folder), and applies a design to the local player directly from here.
/// </summary>
internal sealed class GlamourerPreviewWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly GlamourerIpcService glamourerIpc;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly PreviewTextureCache textureCache;
    private readonly PreviewImageService imageService;
    private readonly FileDialogManager fileDialogManager;
    private readonly string previewRootDirectory;

    private Dictionary<Guid, (string DisplayName, string FullPath, uint DisplayColor, bool ShownInQdb)> designs = new();
    private GlamourerOrganization organization = new(new Dictionary<string, uint>(), new Dictionary<string, uint>());
    private bool glamourerAvailable;
    private Guid? selected;
    private string searchFilter = string.Empty;
    private string urlInput = string.Empty;
    private string fileInput = string.Empty;
    private string? statusMessage;
    private bool statusIsError;
    private Task<(bool Success, string? Error)>? pendingUrlTask;
    private DateTime lastRefresh = DateTime.MinValue;

    public GlamourerPreviewWindow(IDalamudPluginInterface pluginInterface, GlamourerIpcService glamourerIpc, IObjectTable objectTable, ITargetManager targetManager, PreviewTextureCache textureCache, PreviewImageService imageService, FileDialogManager fileDialogManager)
        : base("Glamourer Preview##CelinesToolkitGlamourerPreview")
    {
        this.glamourerIpc = glamourerIpc;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.textureCache = textureCache;
        this.imageService = imageService;
        this.fileDialogManager = fileDialogManager;
        previewRootDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "GlamourerPreviews");

        Size = new Vector2(700, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        RefreshIfDue();
        PollPendingUrlTask();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Sync, Loc.T("GlamourerPreview.Refresh")))
        {
            Refresh();
        }

        if (!glamourerAvailable)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("GlamourerPreview.NotFound"));
            return;
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##glamourerPreviewSearch", Loc.T("GlamourerPreview.SearchHint"), ref searchFilter, 200);

        var visibleCount = 0;
        foreach (var design in designs.Values)
        {
            if (MatchesFilter(design.DisplayName))
            {
                visibleCount++;
            }
        }

        ImGui.TextDisabled(visibleCount == designs.Count
            ? Loc.T("GlamourerPreview.TotalCount", designs.Count)
            : Loc.T("GlamourerPreview.FilteredCount", visibleCount, designs.Count));

        ImGui.Separator();

        ImGui.BeginChild("##glamourerPreviewList", new Vector2(260, 0), true);
        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            // No filter active - mirror Glamourer's own folder tree exactly (FullPath already
            // encodes it as "Folder/Subfolder/DesignName"), rather than a flat list.
            DrawTree(BuildTree(designs, organization.SeparatorColors), string.Empty);
        }
        else
        {
            foreach (var (guid, design) in designs.OrderBy(d => d.Value.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (!MatchesFilter(design.DisplayName))
                {
                    continue;
                }

                DrawDesignRow(guid, design.DisplayName, design.DisplayColor);
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##glamourerPreviewDetails", Vector2.Zero, false);
        DrawDetails();
        ImGui.EndChild();
    }

    private sealed class DesignTreeNode
    {
        public SortedDictionary<string, DesignTreeNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(Guid Guid, string Name, uint Color)> Designs { get; } = new();

        /// <summary>Pure visual dividers, e.g. an alphabetical break like "D" - never have children of their own.</summary>
        public List<(string Name, uint Color)> Separators { get; } = new();
    }

    private static DesignTreeNode BuildTree(Dictionary<Guid, (string DisplayName, string FullPath, uint DisplayColor, bool ShownInQdb)> designs, Dictionary<string, uint> separatorColors)
    {
        var root = new DesignTreeNode();

        static DesignTreeNode Walk(DesignTreeNode root, string[] segments, int folderSegmentCount)
        {
            var node = root;
            for (var i = 0; i < folderSegmentCount; i++)
            {
                if (!node.Folders.TryGetValue(segments[i], out var child))
                {
                    child = new DesignTreeNode();
                    node.Folders[segments[i]] = child;
                }

                node = child;
            }

            return node;
        }

        foreach (var (guid, design) in designs)
        {
            var segments = design.FullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = Walk(root, segments, segments.Length - 1);
            var leafName = segments.Length > 0 ? segments[^1] : design.DisplayName;
            node.Designs.Add((guid, leafName, design.DisplayColor));
        }

        // Separators aren't tied to any design's FullPath at all (they're empty dividers), so
        // they have to be inserted from Glamourer's own organization.json data directly - same
        // "/"-joined path convention as everything else, last segment is the separator's own name.
        foreach (var (path, color) in separatorColors)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var node = Walk(root, segments, segments.Length - 1);
            node.Separators.Add((segments[^1], color));
        }

        return root;
    }

    private void DrawTree(DesignTreeNode node, string idPrefix)
    {
        // Glamourer's own organization.json carries no ordering data between folders, separators,
        // and designs at all - the only way a single-letter separator like "D" can visually act as
        // a section divider is if everything at this level is just sorted together alphabetically
        // by name, so that's exactly what's mirrored here rather than grouping folders first.
        var items = new List<(string SortKey, Action Draw)>();

        foreach (var (folderName, child) in node.Folders)
        {
            var folderPath = idPrefix + folderName;
            items.Add((folderName, () => DrawFolder(folderName, folderPath, child)));
        }

        foreach (var (name, color) in node.Separators)
        {
            // The name only exists to control sort position (that's what Glamourer's own "Sort
            // Order Path" field is for) - Glamourer never actually displays it, just the divider.
            items.Add((name, () => DrawSeparatorRow(color)));
        }

        foreach (var (guid, name, color) in node.Designs)
        {
            items.Add((name, () => DrawDesignRow(guid, name, color)));
        }

        foreach (var item in items.OrderBy(i => i.SortKey, StringComparer.OrdinalIgnoreCase))
        {
            item.Draw();
        }
    }

    private void DrawFolder(string folderName, string folderPath, DesignTreeNode child)
    {
        var hasColor = organization.FolderColors.TryGetValue(folderPath, out var folderColor);
        if (hasColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertU32ToFloat4(folderColor));
        }

        var open = ImGui.TreeNodeEx(folderName + "##" + folderPath, ImGuiTreeNodeFlags.SpanAvailWidth);

        if (hasColor)
        {
            ImGui.PopStyleColor();
        }

        if (open)
        {
            DrawTree(child, folderPath + "/");
            ImGui.TreePop();
        }
    }

    private static void DrawSeparatorRow(uint color)
    {
        ImGui.PushStyleColor(ImGuiCol.Separator, ImGui.ColorConvertU32ToFloat4(color));
        ImGui.Separator();
        ImGui.PopStyleColor();
    }

    private void DrawDesignRow(Guid guid, string label, uint displayColor)
    {
        var color = ImGui.ColorConvertU32ToFloat4(displayColor);
        ImGui.TextColored(color, "●");
        ImGui.SameLine();

        if (ImGui.Selectable(label + "##" + guid, selected == guid))
        {
            selected = guid;
            statusMessage = null;
            urlInput = string.Empty;
            fileInput = string.Empty;
        }

        DrawApplyContextMenu(guid);
    }

    /// <summary>Right-click quick-apply menu directly on a list row - the modifier-click on the details panel's Apply button does the same thing, but a menu makes the three scopes discoverable without needing to hold a key.</summary>
    private void DrawApplyContextMenu(Guid guid)
    {
        if (!ImGui.BeginPopupContextItem("##glamourerPreviewContext" + guid))
        {
            return;
        }

        var target = targetManager.Target;

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplySelfAll")))
        {
            ApplyAndReport(guid, objectTable.LocalPlayer, ApplyFlagEx.DesignDefault);
        }

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplySelfEquipment")))
        {
            ApplyAndReport(guid, objectTable.LocalPlayer, ApplyFlag.Once | ApplyFlag.Equipment);
        }

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplySelfCustomization")))
        {
            ApplyAndReport(guid, objectTable.LocalPlayer, ApplyFlag.Once | ApplyFlag.Customization);
        }

        ImGui.Separator();

        ImGui.BeginDisabled(target == null);
        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplyTargetAll")))
        {
            ApplyAndReport(guid, target, ApplyFlagEx.DesignDefault);
        }

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplyTargetEquipment")))
        {
            ApplyAndReport(guid, target, ApplyFlag.Once | ApplyFlag.Equipment);
        }

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.Context.ApplyTargetCustomization")))
        {
            ApplyAndReport(guid, target, ApplyFlag.Once | ApplyFlag.Customization);
        }

        ImGui.EndDisabled();

        ImGui.Separator();

        if (ImGui.MenuItem(Loc.T("GlamourerPreview.OpenInGlamourer")))
        {
            glamourerIpc.OpenInGlamourer(guid);
        }

        ImGui.EndPopup();
    }

    private void ApplyAndReport(Guid guid, IGameObject? actor, ApplyFlag flags)
    {
        var result = glamourerIpc.Apply(guid, actor, flags);
        if (result == GlamourerApiEc.Success)
        {
            statusMessage = Loc.T("GlamourerPreview.ApplySuccess");
            statusIsError = false;
        }
        else
        {
            statusMessage = Loc.T("GlamourerPreview.ApplyError", result);
            statusIsError = true;
        }
    }

    /// <summary>Ctrl/Shift-click modifier scope, matching Glamourer's own apply-hotkey convention.</summary>
    private static ApplyFlag ModifierFlags()
    {
        var io = ImGui.GetIO();
        return io.KeyCtrl
            ? ApplyFlag.Once | ApplyFlag.Equipment
            : io.KeyShift
                ? ApplyFlag.Once | ApplyFlag.Customization
                : ApplyFlagEx.DesignDefault;
    }

    private void DrawDetails()
    {
        if (selected is not { } guid || !designs.TryGetValue(guid, out var design))
        {
            ImGui.TextDisabled(Loc.T("GlamourerPreview.SelectHint"));
            return;
        }

        ImGui.Text(design.DisplayName);
        ImGui.Spacing();

        var designFolder = Path.Combine(previewRootDirectory, guid.ToString());
        var previewPath = ModPreviewScanner.FindPreviewImage(designFolder);
        if (previewPath != null)
        {
            var texture = textureCache.GetOrLoad(previewPath);
            if (texture != null)
            {
                var maxWidth = 256f;
                var scale = texture.Width > 0 ? Math.Min(1f, maxWidth / texture.Width) : 1f;
                ImGui.Image(texture.Handle, new Vector2(texture.Width * scale, texture.Height * scale));
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("GlamourerPreview.NoPreview"));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PlayCircle, Loc.T("GlamourerPreview.Apply")))
        {
            // Mirrors Glamourer's own apply-hotkey convention (its quick design bar/design panel
            // use the same Ctrl/Shift modifiers for a partial apply), so it behaves the same way
            // here as it already does inside Glamourer itself.
            ApplyAndReport(guid, objectTable.LocalPlayer, ModifierFlags());
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.T("GlamourerPreview.ApplyHotkeyHint"));

        var target = targetManager.Target;
        ImGui.BeginDisabled(target == null);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bullseye, Loc.T("GlamourerPreview.ApplyToTarget")))
        {
            ApplyAndReport(guid, target, ModifierFlags());
        }

        ImGui.EndDisabled();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, Loc.T("GlamourerPreview.OpenInGlamourer")))
        {
            glamourerIpc.OpenInGlamourer(guid);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Loc.T("GlamourerPreview.FromUrl"));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##glamourerPreviewUrlInput", ref urlInput, 512);
        var urlBusy = pendingUrlTask != null;
        ImGui.BeginDisabled(urlBusy || string.IsNullOrWhiteSpace(urlInput));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CloudDownloadAlt, Loc.T("PreviewManager.GrabFromUrl")))
        {
            Directory.CreateDirectory(designFolder);
            pendingUrlTask = imageService.SaveFromUrlAsync(urlInput, designFolder);
        }

        ImGui.EndDisabled();
        if (urlBusy)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T("PreviewManager.Loading"));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Loc.T("PreviewManager.FromFile"));
        ImGui.SetNextItemWidth(-80);
        ImGui.InputText("##glamourerPreviewFileInput", ref fileInput, 1024);
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FolderOpen, Loc.T("PreviewManager.Browse")))
        {
            fileDialogManager.OpenFileDialog(
                Loc.T("PreviewManager.Browse"),
                "Image Files{.png,.jpg,.jpeg,.webp,.bmp,.gif}",
                (success, path) =>
                {
                    if (success)
                    {
                        fileInput = path;
                    }
                });
        }

        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(fileInput));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, Loc.T("PreviewManager.SetLocalImage")))
        {
            Directory.CreateDirectory(designFolder);
            if (imageService.TrySaveFromFile(fileInput, designFolder, out var error))
            {
                ApplyImageSuccess();
            }
            else
            {
                ApplyImageError(error);
            }
        }

        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginDisabled(!PreviewImageService.IsImageInClipboard());
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, Loc.T("PreviewManager.PasteFromClipboard")))
        {
            Directory.CreateDirectory(designFolder);
            if (imageService.TrySaveFromClipboard(designFolder, out var error))
            {
                ApplyImageSuccess();
            }
            else
            {
                ApplyImageError(error);
            }
        }

        ImGui.EndDisabled();

        if (statusMessage != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(statusIsError ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.4f, 1f, 0.4f, 1f), statusMessage);
        }
    }

    private void PollPendingUrlTask()
    {
        if (pendingUrlTask is not { IsCompleted: true })
        {
            return;
        }

        var (success, error) = pendingUrlTask.Result;
        pendingUrlTask = null;

        if (success)
        {
            ApplyImageSuccess();
        }
        else
        {
            ApplyImageError(error);
        }
    }

    private void ApplyImageSuccess()
    {
        statusMessage = Loc.T("GlamourerPreview.Saved");
        statusIsError = false;
    }

    private void ApplyImageError(string? error)
    {
        statusMessage = string.IsNullOrEmpty(error) ? Loc.T("PreviewManager.Error.Generic") : error;
        statusIsError = true;
    }

    private bool MatchesFilter(string displayName)
    {
        return string.IsNullOrWhiteSpace(searchFilter) || displayName.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshIfDue()
    {
        if (DateTime.UtcNow - lastRefresh >= RefreshInterval)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var result = glamourerIpc.GetDesignList();
        glamourerAvailable = result != null;
        designs = result ?? designs;
        if (result != null)
        {
            organization = glamourerIpc.GetOrganization();
        }

        lastRefresh = DateTime.UtcNow;
        if (selected is { } guid && !designs.ContainsKey(guid))
        {
            selected = null;
        }
    }
}
