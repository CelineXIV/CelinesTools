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

    private readonly Plugin plugin;
    private readonly GlamourerIpcService glamourerIpc;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly PreviewTextureCache textureCache;
    private readonly PreviewImageService imageService;
    private readonly FileDialogManager fileDialogManager;
    private readonly GlamourerZoomWindow zoomWindow;
    private readonly string previewRootDirectory;

    private Dictionary<Guid, (string DisplayName, string FullPath, uint DisplayColor, bool ShownInQdb)> designs = new();

    /// <summary>
    /// FindPreviewImage does up to 8 File.Exists checks - fine for the single selected design the
    /// classic details panel looks up once a frame, but the grid view looks up every visible
    /// card's preview every frame, which for a large design list turns into hundreds of filesystem
    /// stats a frame for no reason (the on-disk preview folder essentially never changes between
    /// one frame and the next). Cleared on every Refresh() (already runs on a 2s timer) and
    /// whenever a new preview is actually saved, so it never goes meaningfully stale.
    /// </summary>
    private readonly Dictionary<Guid, string?> previewPathCache = new();
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

    /// <summary>Current folder navigation path in the grid view (Explorer-style) - empty means the root. See DrawGridView/DrawBreadcrumb.</summary>
    private readonly List<string> gridPath = new();

    // The same accent blue used for links/hover highlights throughout CelinesChat - reused here
    // so this window's "modern" look actually matches rather than just also being rounded.
    private static readonly Vector4 AccentColor = new(0.4f, 0.7f, 1f, 1f);

    public GlamourerPreviewWindow(Plugin plugin, IDalamudPluginInterface pluginInterface, GlamourerIpcService glamourerIpc, IObjectTable objectTable, ITargetManager targetManager, PreviewTextureCache textureCache, PreviewImageService imageService, FileDialogManager fileDialogManager, GlamourerZoomWindow zoomWindow)
        : base("Glamourer Preview##CelinesToolkitGlamourerPreview")
    {
        this.plugin = plugin;
        this.glamourerIpc = glamourerIpc;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.textureCache = textureCache;
        this.imageService = imageService;
        this.fileDialogManager = fileDialogManager;
        this.zoomWindow = zoomWindow;
        previewRootDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "GlamourerPreviews");

        Size = new Vector2(700, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Same "modern chrome" recipe CelinesChat's ChatWindow/SecondaryChatWindow use - rounded
    // frames/children/popups/scrollbars instead of Dear ImGui's sharp-cornered defaults - so this
    // window reads as part of the same toolset instead of a visually older, plainer one.
    private const int StyleVarCount = 8;

    private static void PushModernStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
    }

    public override void Draw()
    {
        RefreshIfDue();
        PollPendingUrlTask();
        PushModernStyle();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Sync, Loc.T("GlamourerPreview.Refresh")))
        {
            Refresh();
        }

        ImGui.SameLine();
        var gridView = plugin.Configuration.GlamourerPreviewGridView;
        if (ImGuiComponents.IconButtonWithText(gridView ? FontAwesomeIcon.List : FontAwesomeIcon.Th, gridView ? Loc.T("GlamourerPreview.ClassicView") : Loc.T("GlamourerPreview.GridView")))
        {
            plugin.Configuration.GlamourerPreviewGridView = !gridView;
            plugin.SaveConfiguration();
        }

        if (!glamourerAvailable)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("GlamourerPreview.NotFound"));
            ImGui.PopStyleVar(StyleVarCount);
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

        if (gridView)
        {
            DrawGridView();
            ImGui.PopStyleVar(StyleVarCount);
            return;
        }

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

        ImGui.PopStyleVar(StyleVarCount);
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

    /// <summary>
    /// Alternate to the classic tree+details layout - a Windows Explorer-style thumbnail grid
    /// instead of the tab-row this view originally shipped with (which the user found less
    /// natural than actually navigating into a folder). Folders are cards you click into, a
    /// breadcrumb across the top takes you back out, and only the current folder's own direct
    /// contents are shown (subfolders appear as their own folder cards to click into, not
    /// flattened) - the same browsing model as any desktop file browser.
    /// </summary>
    private void DrawGridView()
    {
        var root = BuildTree(designs, organization.SeparatorColors);
        var searching = !string.IsNullOrWhiteSpace(searchFilter);

        List<Action> cardDrawers;
        if (searching)
        {
            // A search result can come from any folder at any depth - showing it as a flat list
            // regardless of the current breadcrumb position matches the classic view's own
            // search behavior, and browsing into folders one at a time to find a match would
            // defeat the point of searching in the first place.
            cardDrawers = designs
                .Where(d => MatchesFilter(d.Value.DisplayName))
                .OrderBy(d => d.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(d => (Action)(() => DrawDesignCard(d.Key, d.Value.DisplayName, d.Value.DisplayColor)))
                .ToList();
        }
        else
        {
            // Self-healing against a folder that got renamed/deleted in Glamourer since the last
            // refresh - trims gridPath back to whatever's still actually resolvable.
            var node = ResolveNode(root, gridPath);

            DrawBreadcrumb();
            ImGui.Spacing();

            var pathPrefix = gridPath.Count > 0 ? string.Join('/', gridPath) + "/" : string.Empty;
            cardDrawers = new List<Action>();
            foreach (var folderName in node.Folders.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = pathPrefix + folderName;
                cardDrawers.Add(() => DrawFolderCard(folderName, fullPath));
            }

            foreach (var (guid, name, color) in node.Designs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                cardDrawers.Add(() => DrawDesignCard(guid, name, color));
            }
        }

        ImGui.BeginChild("##glamourerPreviewGrid", Vector2.Zero, false);

        if (cardDrawers.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("GlamourerPreview.Grid.Empty"));
        }
        else
        {
            DrawCardGrid(cardDrawers);
        }

        ImGui.EndChild();
    }

    /// <summary>Walks folder names in order, stopping (and trimming path) at the first one that no longer exists rather than throwing or silently pointing at the wrong node.</summary>
    private static DesignTreeNode ResolveNode(DesignTreeNode root, List<string> path)
    {
        var node = root;
        for (var i = 0; i < path.Count; i++)
        {
            if (!node.Folders.TryGetValue(path[i], out var child))
            {
                path.RemoveRange(i, path.Count - i);
                break;
            }

            node = child;
        }

        return node;
    }

    private void DrawBreadcrumb()
    {
        DrawBreadcrumbSegment(Loc.T("GlamourerPreview.Grid.Root"), gridPath.Count == 0, () => gridPath.Clear());

        for (var i = 0; i < gridPath.Count; i++)
        {
            ImGui.SameLine(0, 4);
            ImGui.TextDisabled(">");
            ImGui.SameLine(0, 4);

            var removeFrom = i + 1;
            DrawBreadcrumbSegment(gridPath[i], i == gridPath.Count - 1, () => gridPath.RemoveRange(removeFrom, gridPath.Count - removeFrom));
        }
    }

    private static void DrawBreadcrumbSegment(string label, bool isCurrent, Action onClick)
    {
        if (isCurrent)
        {
            ImGui.TextUnformatted(label);
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 0.75f, 1f, 1f), label);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
            {
                onClick();
            }
        }
    }

    // Fixed so every card - folder or design, every thumbnail regardless of its own original
    // image size - ends up visually identical in footprint, which is the whole point: the user
    // specifically didn't want a grid where some cards are taller/shorter than others or need
    // their own scrollbar depending on what image happens to be attached.
    private const float GridCardWidth = 150f;
    private const float GridCardHeight = 215f;
    private const float GridThumbSize = 128f;

    private void DrawCardGrid(List<Action> cardDrawers)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)((availableWidth + spacing) / (GridCardWidth + spacing)));

        for (var i = 0; i < cardDrawers.Count; i++)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }

            cardDrawers[i]();
        }
    }

    /// <summary>
    /// Vertically centers whatever DrawContent draws within a GridThumbSize-tall band (adding a
    /// Dummy above and below it), horizontally centered too - the shared mechanism behind every
    /// card's image area (a design's actual thumbnail, or a folder's icon) always occupying
    /// exactly the same footprint and sitting at exactly the same row regardless of what's
    /// actually inside it, which is the core of the "every card the same height/spacing" ask.
    /// </summary>
    private static void DrawCenteredInImageArea(float innerWidth, Vector2 contentSize, Action drawContent)
    {
        var topGap = Math.Max(0f, (GridThumbSize - contentSize.Y) / 2f);
        ImGui.Dummy(new Vector2(1f, topGap));
        CenterCursorX(contentSize.X, innerWidth);
        drawContent();
        var bottomGap = Math.Max(0f, GridThumbSize - topGap - contentSize.Y);
        ImGui.Dummy(new Vector2(1f, bottomGap));
    }

    /// <summary>
    /// NoScrollbar/NoScrollWithMouse deliberately - every card is drawn at exactly GridCardHeight
    /// regardless of content, so nothing inside should ever legitimately need to scroll; if
    /// something did overflow (an unusually long design name, say) this clips it instead of
    /// showing a scrollbar, which is what was actually asked for here.
    /// </summary>
    private void DrawFolderCard(string folderName, string fullPath)
    {
        ImGui.PushID("folder_" + folderName);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.BeginChild("##folderCard", new Vector2(GridCardWidth, GridCardHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var innerWidth = ImGui.GetContentRegionAvail().X;
        var clickAreaTop = ImGui.GetCursorPos();
        var clicked = ImGui.InvisibleButton("##folderClick", new Vector2(innerWidth, GridCardHeight - ImGui.GetStyle().WindowPadding.Y * 2f));
        // Without this, this full-card catcher (drawn first) would swallow hover/clicks from
        // the name text's own tooltip drawn on top of it afterward - same fix as the tab strip's
        // own drop-zone needed elsewhere in this session's work.
        ImGui.SetItemAllowOverlap();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        ImGui.SetCursorPos(clickAreaTop);

        // Same "dot + name" row shape as a design card's, at the same top position, so folder and
        // design cards line up row-for-row in a mixed grid - the dot uses Glamourer's own folder
        // color when the user's set one (same data DrawFolder's classic-view tree already reads),
        // falling back to a plain folder-yellow otherwise.
        var dotColor = organization.FolderColors.TryGetValue(fullPath, out var folderColor)
            ? ImGui.ColorConvertU32ToFloat4(folderColor)
            : new Vector4(1f, 0.85f, 0.4f, 1f);
        ImGui.TextColored(dotColor, "●");
        ImGui.SameLine();
        var folderNameWidth = innerWidth - ImGui.CalcTextSize("●").X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.TextUnformatted(TruncateForWidth(folderName, folderNameWidth));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(folderName);
        }

        ImGui.Spacing();

        // Scaled up well beyond the icon font's own baked-in size so an empty folder card reads
        // with roughly the same visual weight as a design card's 128px thumbnail, rather than a
        // tiny glyph floating in a mostly-empty box - a static, non-interactive glyph like this
        // has none of the caret/text-input instability a similar scale trick caused elsewhere in
        // this codebase for live-edited text.
        const float iconScale = 3f;
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(iconScale);
        var iconLabel = FontAwesomeIcon.Folder.ToIconString();
        // CalcTextSize already reflects the window font scale just set above.
        var iconSize = ImGui.CalcTextSize(iconLabel);
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();

        DrawCenteredInImageArea(innerWidth, iconSize, () =>
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(iconScale);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.4f, 1f));
            ImGui.TextUnformatted(iconLabel);
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
        });

        ImGui.Spacing();

        // No action for a folder - reserved at the same height Apply takes on a design card
        // anyway, so the bottom of every card still lines up regardless of type.
        ImGui.Dummy(new Vector2(1f, ImGui.GetFrameHeight()));

        DrawCardHoverHighlight();

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();

        if (clicked)
        {
            gridPath.Add(folderName);
        }
    }

    private void DrawDesignCard(Guid guid, string name, uint displayColor)
    {
        ImGui.PushID(guid.ToString());
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.BeginChild("##card", new Vector2(GridCardWidth, GridCardHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var innerWidth = ImGui.GetContentRegionAvail().X;

        // Name on top, per the user's ask - always a single line. This used to *wrap* a too-long
        // name onto a second line instead of truncating it, which silently grew that one card's
        // content taller than GridCardHeight actually budgets for - with NoScrollbar/NoScroll
        // WithMouse (see the card's own remarks on why), the overflow just got clipped off the
        // *bottom* of the card instead of showing a scrollbar, which is exactly what was reported
        // as "buttons cut off sometimes" - only ever the cards whose name happened to wrap.
        ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(displayColor), "●");
        ImGui.SameLine();
        var nameWidth = innerWidth - ImGui.CalcTextSize("●").X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.TextUnformatted(TruncateForWidth(name, nameWidth));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(name);
        }

        ImGui.Spacing();

        var previewPath = GetPreviewPath(guid);
        var texture = previewPath != null ? textureCache.GetOrLoad(previewPath) : null;
        if (texture != null)
        {
            // "Contain" fit (the whole image, never cropped) rather than a crop-to-fill - the
            // user specifically wants every picture "gut erkennbar" (clearly recognizable), which
            // a center-crop can defeat for a tall/wide glamour shot by cutting off the top/bottom
            // or sides. Every thumbnail still occupies the exact same GridThumbSize x GridThumbSize
            // footprint either way (via DrawCenteredInImageArea) - that's what makes them "the
            // same size" without needing to crop anything away to get there.
            var scale = texture.Width > 0 && texture.Height > 0
                ? Math.Min(GridThumbSize / texture.Width, GridThumbSize / texture.Height)
                : 1f;
            var size = new Vector2(texture.Width * scale, texture.Height * scale);

            DrawCenteredInImageArea(innerWidth, size, () =>
            {
                ImGui.Image(texture.Handle, size);

                // Click the thumbnail itself to open the zoom window - the button below stays
                // dedicated to actually applying the design, so a click can't accidentally do the
                // wrong one of those two things.
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsItemClicked())
                    {
                        zoomWindow.Open(guid, name);
                    }

                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(AccentColor), 3f, ImDrawFlags.None, 2f);
                }
            });
        }
        else
        {
            DrawCenteredInImageArea(innerWidth, new Vector2(GridThumbSize, GridThumbSize), () => DrawNoPreviewPlaceholder(() => zoomWindow.Open(guid, name)));
        }

        ImGui.Spacing();

        CenterCursorX(ImGui.GetFrameHeight(), innerWidth);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(AccentColor.X, AccentColor.Y, AccentColor.Z, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(AccentColor.X, AccentColor.Y, AccentColor.Z, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(AccentColor.X, AccentColor.Y, AccentColor.Z, 0.8f));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.PlayCircle))
        {
            ApplyAndReport(guid, objectTable.LocalPlayer, ModifierFlags());
        }

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("GlamourerPreview.ApplyHotkeyHint"));
        }

        // Right-click the Apply button for the same scoped-apply menu the classic list rows and
        // details panel already offer (self/target × all/equipment/appearance) - see
        // DrawApplyContextMenu.
        DrawApplyContextMenu(guid);

        DrawCardHoverHighlight();

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopID();
    }

    /// <summary>
    /// A small centered icon instead of the old always-visible "No Preview" wording, which -
    /// especially in German - reliably wrapped into a ragged 3-line block in a 128px-wide box.
    /// Also clickable, same as a real thumbnail - this is in fact the *more* important case to
    /// make clickable, since a design with no preview yet is exactly when someone would want to
    /// open the zoom window and use its upload controls to set one.
    /// </summary>
    private static void DrawNoPreviewPlaceholder(Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.03f));
        ImGui.BeginChild("##cardNoPreview", new Vector2(GridThumbSize, GridThumbSize), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushFont(UiBuilder.IconFont);
        var icon = FontAwesomeIcon.Image.ToIconString();
        var iconSize = ImGui.CalcTextSize(icon);
        var innerWidth = GridThumbSize - ImGui.GetStyle().WindowPadding.X * 2f;
        var topGap = Math.Max(0f, (GridThumbSize - ImGui.GetStyle().WindowPadding.Y * 2f - iconSize.Y) / 2f);
        ImGui.PopFont();

        // An invisible button spanning the whole placeholder (rather than relying on the icon
        // text's own small hit-box) so the entire box is clickable, not just the glyph itself.
        var clickAreaTop = ImGui.GetCursorPos();
        var clicked = ImGui.InvisibleButton("##noPreviewClick", new Vector2(innerWidth, GridThumbSize - ImGui.GetStyle().WindowPadding.Y * 2f));
        ImGui.SetItemAllowOverlap();
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("GlamourerPreview.Grid.NoPreviewClickHint"));
        }

        ImGui.SetCursorPos(clickAreaTop);
        ImGui.Dummy(new Vector2(1f, topGap));
        CenterCursorX(iconSize.X, innerWidth);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextDisabled(icon);
        ImGui.PopFont();

        if (hovered)
        {
            var min = ImGui.GetWindowPos();
            var max = min + ImGui.GetWindowSize();
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(AccentColor), 3f, ImDrawFlags.None, 2f);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (clicked)
        {
            onClick();
        }
    }

    /// <summary>A bright border around whichever card the mouse is currently over - call while still inside that card's own BeginChild/EndChild, so GetWindowPos/Size below refer to the card itself.</summary>
    private static void DrawCardHoverHighlight()
    {
        if (!ImGui.IsWindowHovered())
        {
            return;
        }

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(AccentColor), 6f, ImDrawFlags.None, 1.5f);
    }

    /// <summary>Chops text down (binary search on length) until it plus "..." fits maxWidth - returns it unchanged if it already fits. Used instead of PushTextWrapPos wherever a fixed-height card can't afford a second line.</summary>
    private static string TruncateForWidth(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisWidth <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low <= 0 ? ellipsis : text[..low] + ellipsis;
    }

    private static void CenterCursorX(float contentWidth, float availableWidth)
    {
        var offset = (availableWidth - contentWidth) / 2f;
        if (offset > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }
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
        var previewPath = GetPreviewPath(guid);
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
        // The design a new preview was just saved for is always the currently selected one (see
        // every ApplyImageSuccess call site) - invalidate just that entry so it picks up the new
        // image immediately rather than waiting for the next 2s auto-refresh to clear it.
        if (selected is { } guid)
        {
            previewPathCache.Remove(guid);
        }

        statusMessage = Loc.T("GlamourerPreview.Saved");
        statusIsError = false;
    }

    private string? GetPreviewPath(Guid guid)
    {
        if (!previewPathCache.TryGetValue(guid, out var path))
        {
            path = ModPreviewScanner.FindPreviewImage(Path.Combine(previewRootDirectory, guid.ToString()));
            previewPathCache[guid] = path;
        }

        return path;
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

        // Cheap insurance against a stale cache (a preview image added/removed on disk by hand
        // while the window's open, a design's GUID somehow reused, ...) - this only runs once
        // every RefreshInterval (2s), not per-frame, so re-doing the lookups here is negligible.
        previewPathCache.Clear();
    }
}
