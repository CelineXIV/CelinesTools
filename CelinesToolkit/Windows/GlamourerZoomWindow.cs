using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using CelinesToolkit.Services;

namespace CelinesToolkit.Windows;

/// <summary>
/// Full-size look at a single Glamourer design's preview image, opened by clicking its thumbnail
/// in GlamourerPreviewWindow's grid view - also hosts that view's own "set a preview image"
/// controls (URL/file/clipboard), since the classic view's details panel already had them and
/// the grid view otherwise had no equivalent at all.
///
/// A real WindowSystem-registered Window, not an ad-hoc raw ImGui.Begin call issued from inside
/// another window's own Draw (which is what this replaced) - that approach reliably ended up
/// rendering *behind* other windows instead of on top, since it wasn't part of Dalamud's own
/// window focus/z-order bookkeeping at all. Every other window in this plugin already relies on
/// that system instead of fighting it; this one now does too.
/// </summary>
internal sealed class GlamourerZoomWindow : Window
{
    private readonly GlamourerIpcService glamourerIpc;
    private readonly PreviewTextureCache textureCache;
    private readonly PreviewImageService imageService;
    private readonly FileDialogManager fileDialogManager;
    private readonly string previewRootDirectory;

    private Guid designGuid;
    private string urlInput = string.Empty;
    private string fileInput = string.Empty;
    private string? statusMessage;
    private bool statusIsError;
    private Task<(bool Success, string? Error)>? pendingUrlTask;

    // Set the moment a file-browse dialog is opened, cleared once its callback fires - guards the
    // outside-click-closes check below from treating a click *inside that dialog* (a genuinely
    // separate window) as "clicked away from the zoom", which would close this out from under an
    // in-progress file pick.
    private bool fileDialogPending;

    // True for exactly the first Draw() after Open() - guards against the very click that opened
    // this window (still "active" for IsMouseClicked that whole frame) being immediately read as
    // an outside-click if this window happens to get its first Draw call the same frame it was
    // opened on: the mouse is still positioned over whatever card was clicked to open it, not over
    // this window (which appears somewhere else), so without this the close-on-outside-click
    // check would fire before the window was ever actually seen.
    private bool justOpened;

    // Consumed once by PreDraw - see its own remarks on why forcing focus every frame (instead of
    // just once here) broke normal button interaction inside this window.
    private bool focusPending;

    public GlamourerZoomWindow(IDalamudPluginInterface pluginInterface, GlamourerIpcService glamourerIpc, PreviewTextureCache textureCache, PreviewImageService imageService, FileDialogManager fileDialogManager)
        : base("##CelinesToolkitGlamourerZoom", ImGuiWindowFlags.NoCollapse)
    {
        this.glamourerIpc = glamourerIpc;
        this.textureCache = textureCache;
        this.imageService = imageService;
        this.fileDialogManager = fileDialogManager;
        previewRootDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "GlamourerPreviews");

        Size = new Vector2(420, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Open(Guid guid, string name)
    {
        designGuid = guid;
        urlInput = string.Empty;
        fileInput = string.Empty;
        statusMessage = null;
        pendingUrlTask = null;
        fileDialogPending = false;
        justOpened = true;
        focusPending = true;
        WindowName = name + "##CelinesToolkitGlamourerZoom";
        IsOpen = true;
        Collapsed = false;
    }

    public override void PreDraw()
    {
        // Only forced to front once, right when it's opened (or reopened for a different
        // design) - not every single frame. Forcing focus on *every* frame turned out to
        // interfere with normal button press/release tracking for widgets inside this same
        // window: a click on e.g. "Paste from Clipboard" would never actually register as a
        // completed click on the button (its press-and-hold state kept getting reset from under
        // it by the repeated focus force), leaving that same unclaimed click to fall through to
        // the outside-click-closes check below instead - reported as "clicking paste just closes
        // the window instead of uploading". A single one-time focus on open is enough to fix the
        // original "renders behind everything" problem without that side effect; after that, it
        // stays on top the normal way any focused window does; ordinary interaction with it.
        if (focusPending)
        {
            ImGui.SetNextWindowFocus();
            focusPending = false;
        }
    }

    public override void Draw()
    {
        PollPendingUrlTask();

        var designFolder = Path.Combine(previewRootDirectory, designGuid.ToString());
        var previewPath = ModPreviewScanner.FindPreviewImage(designFolder);
        var texture = previewPath != null ? textureCache.GetOrLoad(previewPath) : null;

        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (texture != null)
        {
            var scale = texture.Width > 0 ? Math.Min(1f, availableWidth / texture.Width) : 1f;
            var size = new Vector2(texture.Width * scale, texture.Height * scale);
            var offsetX = (availableWidth - size.X) / 2f;
            if (offsetX > 0f)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
            }

            ImGui.Image(texture.Handle, size);
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("GlamourerPreview.NoPreview"));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, Loc.T("GlamourerPreview.OpenInGlamourer")))
        {
            glamourerIpc.OpenInGlamourer(designGuid);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawUploadControls(designFolder);

        // Closing outside-click detection last, once the window's own final size/position for
        // this frame is known - a click is "outside" this window's own rect, not currently mid
        // file-dialog (see fileDialogPending's own remarks), and not the very click that opened
        // this window in the first place (see justOpened's own remarks).
        //
        // AllowWhenBlockedByActiveItem is required here, not optional: without it, IsWindowHovered
        // returns false for the entire frame you *press down* on any button inside this same
        // window (that press makes the button the "active item", and by default ImGui treats the
        // window as "blocked"/not hovered while any item is active) - combined with IsMouseClicked
        // being true on that same press frame, this check misfired as "clicked outside" on every
        // single button in here, closing the window before the button underneath ever got to
        // register the click itself. Reported as "trying to use any button just closes it".
        const ImGuiHoveredFlags outsideClickHoverFlags = ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem;
        if (!justOpened && !fileDialogPending && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered(outsideClickHoverFlags))
        {
            IsOpen = false;
        }

        justOpened = false;
    }

    private void DrawUploadControls(string designFolder)
    {
        ImGui.Text(Loc.T("GlamourerPreview.FromUrl"));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##glamourerZoomUrlInput", ref urlInput, 512);
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
        ImGui.InputText("##glamourerZoomFileInput", ref fileInput, 1024);
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FolderOpen, Loc.T("PreviewManager.Browse")))
        {
            fileDialogPending = true;
            fileDialogManager.OpenFileDialog(
                Loc.T("PreviewManager.Browse"),
                "Image Files{.png,.jpg,.jpeg,.webp,.bmp,.gif}",
                (success, path) =>
                {
                    fileDialogPending = false;
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
        // GlamourerPreviewWindow's own grid caches preview-path lookups (see its remarks) and
        // won't see this new file until its next 2s auto-refresh - a short, one-time delay judged
        // not worth a tighter coupling between these two windows just to shave off.
        statusMessage = Loc.T("GlamourerPreview.Saved");
        statusIsError = false;
    }

    private void ApplyImageError(string? error)
    {
        statusMessage = string.IsNullOrEmpty(error) ? Loc.T("PreviewManager.Error.Generic") : error;
        statusIsError = true;
    }
}
