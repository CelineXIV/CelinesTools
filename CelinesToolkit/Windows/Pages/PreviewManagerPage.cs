using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows.Pages;

internal sealed class PreviewManagerPage
{
    private readonly Plugin plugin;
    private readonly ModPreviewScanner scanner;
    private readonly PreviewImageService imageService;
    private readonly PreviewTextureCache textureCache;
    private readonly FileDialogManager fileDialogManager;

    private List<ModPreviewInfo> mods = new();
    private string? modDirectory;
    private ModPreviewInfo? selected;
    private bool showOnlyMissing;
    private string searchFilter = string.Empty;
    private string urlInput = string.Empty;
    private string fileInput = string.Empty;
    private string? statusMessage;
    private bool statusIsError;
    private System.Threading.Tasks.Task<(bool Success, string? Error)>? pendingUrlTask;
    private bool scanned;

    public PreviewManagerPage(Plugin plugin, ModPreviewScanner scanner, PreviewImageService imageService, PreviewTextureCache textureCache, FileDialogManager fileDialogManager)
    {
        this.plugin = plugin;
        this.scanner = scanner;
        this.imageService = imageService;
        this.textureCache = textureCache;
        this.fileDialogManager = fileDialogManager;
    }

    public void Draw()
    {
        if (!scanned)
        {
            Refresh();
        }

        PollPendingUrlTask();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Sync, Loc.T("PreviewManager.Refresh")))
        {
            Refresh();
        }

        ImGui.SameLine();
        var onlyMissing = showOnlyMissing;
        if (ImGui.Checkbox(Loc.T("PreviewManager.OnlyMissing"), ref onlyMissing))
        {
            showOnlyMissing = onlyMissing;
        }

        var showInPenumbra = plugin.Configuration.ShowPreviewInPenumbra;
        if (ImGui.Checkbox(Loc.T("PreviewManager.ShowInPenumbra"), ref showInPenumbra))
        {
            plugin.Configuration.ShowPreviewInPenumbra = showInPenumbra;
            plugin.SaveConfiguration();
        }

        if (modDirectory == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T("PreviewManager.PenumbraNotFound"));
            return;
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##previewSearch", Loc.T("PreviewManager.SearchHint"), ref searchFilter, 200);

        var visibleCount = 0;
        foreach (var mod in mods)
        {
            if (MatchesFilters(mod))
            {
                visibleCount++;
            }
        }

        ImGui.TextDisabled(visibleCount == mods.Count
            ? Loc.T("PreviewManager.TotalCount", mods.Count)
            : Loc.T("PreviewManager.FilteredCount", visibleCount, mods.Count));

        ImGui.Separator();

        ImGui.BeginChild("##previewModList", new Vector2(260, 0), true);
        foreach (var mod in mods)
        {
            if (!MatchesFilters(mod))
            {
                continue;
            }

            var enabledColor = mod.IsEnabled switch
            {
                true => new Vector4(0.4f, 1f, 0.4f, 1f),
                false => new Vector4(0.6f, 0.6f, 0.6f, 1f),
                null => new Vector4(0.5f, 0.5f, 0.5f, 0.6f),
            };
            ImGui.TextColored(enabledColor, "●");
            ImGui.SameLine();

            var label = mod.HasPreview ? mod.DisplayName : mod.DisplayName + " *";
            if (ImGui.Selectable(label + "##" + mod.FolderName, selected == mod))
            {
                selected = mod;
                statusMessage = null;
                urlInput = string.Empty;
                fileInput = string.Empty;
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##previewModDetails", Vector2.Zero, false);
        DrawDetails();
        ImGui.EndChild();
    }

    private void DrawDetails()
    {
        if (selected == null)
        {
            ImGui.TextDisabled(Loc.T("PreviewManager.SelectHint"));
            return;
        }

        var mod = selected;
        ImGui.Text(mod.DisplayName);
        ImGui.SameLine();
        var (statusText, statusColor) = mod.IsEnabled switch
        {
            true => (Loc.T("PreviewManager.Enabled"), new Vector4(0.4f, 1f, 0.4f, 1f)),
            false => (Loc.T("PreviewManager.Disabled"), new Vector4(0.7f, 0.7f, 0.7f, 1f)),
            null => (Loc.T("PreviewManager.EnabledUnknown"), new Vector4(0.5f, 0.5f, 0.5f, 1f)),
        };
        ImGui.TextColored(statusColor, "(" + statusText + ")");

        if (!string.IsNullOrEmpty(mod.Author))
        {
            ImGui.TextDisabled(Loc.T("PreviewManager.Author", mod.Author));
        }

        if (!string.IsNullOrEmpty(mod.Version))
        {
            ImGui.TextDisabled(Loc.T("PreviewManager.Version", mod.Version));
        }

        ImGui.Spacing();

        if (mod.PreviewImagePath != null)
        {
            var texture = textureCache.GetOrLoad(mod.PreviewImagePath);
            if (texture != null)
            {
                var maxWidth = 256f;
                var scale = texture.Width > 0 ? Math.Min(1f, maxWidth / texture.Width) : 1f;
                ImGui.Image(texture.Handle, new Vector2(texture.Width * scale, texture.Height * scale));
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("PreviewManager.NoPreview"));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Loc.T("PreviewManager.FromUrl"));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##previewUrlInput", ref urlInput, 512);
        var urlBusy = pendingUrlTask != null;
        ImGui.BeginDisabled(urlBusy || string.IsNullOrWhiteSpace(urlInput));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CloudDownloadAlt, Loc.T("PreviewManager.GrabFromUrl")))
        {
            pendingUrlTask = imageService.SaveFromUrlAsync(urlInput, mod.FullPath);
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
        ImGui.InputText("##previewFileInput", ref fileInput, 1024);
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
            if (imageService.TrySaveFromFile(fileInput, mod.FullPath, out var error))
            {
                ApplySuccess(mod);
            }
            else
            {
                ApplyError(error);
            }
        }

        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginDisabled(!PreviewImageService.IsImageInClipboard());
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, Loc.T("PreviewManager.PasteFromClipboard")))
        {
            if (imageService.TrySaveFromClipboard(mod.FullPath, out var error))
            {
                ApplySuccess(mod);
            }
            else
            {
                ApplyError(error);
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

        if (success && selected != null)
        {
            ApplySuccess(selected);
        }
        else
        {
            ApplyError(error);
        }
    }

    private void ApplySuccess(ModPreviewInfo mod)
    {
        mod.PreviewImagePath = ModPreviewScanner.FindPreviewImage(mod.FullPath);
        statusMessage = Loc.T("PreviewManager.Saved");
        statusIsError = false;
    }

    private void ApplyError(string? error)
    {
        statusMessage = string.IsNullOrEmpty(error) ? Loc.T("PreviewManager.Error.Generic") : error;
        statusIsError = true;
    }

    private bool MatchesFilters(ModPreviewInfo mod)
    {
        if (showOnlyMissing && mod.HasPreview)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(searchFilter) && mod.DisplayName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private void Refresh()
    {
        mods = scanner.Scan(out modDirectory);
        scanned = true;
        if (selected != null)
        {
            selected = mods.Find(m => m.FolderName == selected.FolderName);
        }
    }
}
