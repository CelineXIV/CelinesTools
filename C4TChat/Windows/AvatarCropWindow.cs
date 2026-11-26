using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace C4TChat.Windows;

internal sealed class AvatarCropWindow : Window
{
    private enum DragMode
    {
        None,
        Move,
        Resize,
        Jump,
    }

    private readonly Plugin plugin;
    private string? fileName;
    private string label = string.Empty;
    private bool initialized;
    private float cropX;
    private float cropY;
    private float cropSize;
    private DragMode drag;
    private int pushedColors;

    public AvatarCropWindow(Plugin plugin)
        : base(Loc.T("Crop.Title") + "###celineschatAvatarCrop", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
    }

    internal void Open(string storedFileName, string forWhom)
    {
        fileName = storedFileName;
        label = forWhom;
        initialized = false;
        drag = DragMode.None;
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        pushedColors = C4tTheme.PushColors(plugin.Configuration);
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(pushedColors);
        pushedColors = 0;
    }

    public override void Draw()
    {
        if (fileName == null)
        {
            IsOpen = false;
            return;
        }

        var wrap = plugin.TextureProvider.GetFromFile(new FileInfo(plugin.Avatars.CustomPath(fileName))).GetWrapOrDefault();
        if (wrap == null || wrap.Width <= 0 || wrap.Height <= 0)
        {
            ImGui.TextDisabled(Loc.T("Social.Loading"));
            return;
        }

        float w = wrap.Width;
        float h = wrap.Height;
        var shortSide = MathF.Min(w, h);
        if (!initialized)
        {
            initialized = true;
            if (plugin.Configuration.CustomAvatarCrops.TryGetValue(fileName, out var saved))
            {
                cropX = saved.X * w;
                cropY = saved.Y * h;
                cropSize = (saved.Z - saved.X) * w;
            }
            else
            {
                ResetCrop(w, h);
            }
        }

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextUnformatted(label);
        ImGui.TextDisabled(Loc.T("Crop.Hint"));

        var maxSide = 380f * scale;
        var display = MathF.Min(maxSide / w, maxSide / h);
        var displaySize = new Vector2(w * display, h * display);

        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image(wrap.Handle, displaySize);
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##cropArea", displaySize);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        var mouse = io.MousePos;

        var selMin = origin + (new Vector2(cropX, cropY) * display);
        var selMax = selMin + new Vector2(cropSize * display);
        var handle = 12f * scale;
        var handleMin = selMax - new Vector2(handle);
        var onHandle = mouse.X >= handleMin.X && mouse.Y >= handleMin.Y && mouse.X <= selMax.X + 4f && mouse.Y <= selMax.Y + 4f;
        var inSelection = mouse.X >= selMin.X && mouse.Y >= selMin.Y && mouse.X <= selMax.X && mouse.Y <= selMax.Y;

        if (ImGui.IsItemActivated())
        {
            drag = onHandle ? DragMode.Resize : inSelection ? DragMode.Move : DragMode.Jump;
        }

        if (ImGui.IsItemActive())
        {
            var delta = io.MouseDelta / display;
            switch (drag)
            {
                case DragMode.Move:
                    cropX += delta.X;
                    cropY += delta.Y;
                    break;
                case DragMode.Resize:
                    cropSize += MathF.Max(delta.X, delta.Y);
                    break;
                case DragMode.Jump:
                    cropX = ((mouse.X - origin.X) / display) - (cropSize / 2f);
                    cropY = ((mouse.Y - origin.Y) / display) - (cropSize / 2f);
                    break;
            }
        }
        else
        {
            drag = DragMode.None;
        }

        if (hovered && io.MouseWheel != 0f)
        {
            Zoom(1f - (io.MouseWheel * 0.08f), shortSide);
        }

        if (hovered || ImGui.IsItemActive())
        {
            ImGui.SetMouseCursor(onHandle || drag == DragMode.Resize ? ImGuiMouseCursor.ResizeNwse : inSelection ? ImGuiMouseCursor.ResizeAll : ImGuiMouseCursor.Hand);
        }

        Clamp(w, h, shortSide);
        selMin = origin + (new Vector2(cropX, cropY) * display);
        selMax = selMin + new Vector2(cropSize * display);

        var drawList = ImGui.GetWindowDrawList();
        var shade = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f));
        var imageMax = origin + displaySize;
        drawList.AddRectFilled(origin, new Vector2(imageMax.X, selMin.Y), shade);
        drawList.AddRectFilled(new Vector2(origin.X, selMax.Y), imageMax, shade);
        drawList.AddRectFilled(new Vector2(origin.X, selMin.Y), new Vector2(selMin.X, selMax.Y), shade);
        drawList.AddRectFilled(new Vector2(selMax.X, selMin.Y), new Vector2(imageMax.X, selMax.Y), shade);

        var accent = ImGui.GetColorU32(C4tTheme.ActiveMarkerColor(plugin.Configuration));
        var center = (selMin + selMax) / 2f;
        drawList.AddCircle(center, (selMax.X - selMin.X) / 2f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)), 0, 1.5f * scale);
        drawList.AddRect(selMin, selMax, accent, 0f, ImDrawFlags.None, 2f * scale);
        drawList.AddRectFilled(selMax - new Vector2(handle), selMax, accent);

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.TextDisabled(Loc.T("Crop.Preview"));
            var uv0 = new Vector2(cropX / w, cropY / h);
            var uv1 = new Vector2((cropX + cropSize) / w, (cropY + cropSize) / h);
            foreach (var previewSize in new[] { 96f, 48f, 28f })
            {
                var size = new Vector2(previewSize * scale);
                var min = ImGui.GetCursorScreenPos();
                ImGui.Dummy(size);
                drawList.AddImageRounded(wrap.Handle, min, min + size, uv0, uv1, ImGui.GetColorU32(Vector4.One), size.X / 2f);
            }
        }

        ImGui.SetNextItemWidth(displaySize.X * 0.6f);
        var percent = cropSize / shortSide * 100f;
        if (ImGui.SliderFloat(Loc.T("Crop.Size"), ref percent, 10f, 100f, "%.0f %%"))
        {
            Zoom(percent / 100f * shortSide / cropSize, shortSide);
            Clamp(w, h, shortSide);
        }

        ImGui.Spacing();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, Loc.T("Crop.Apply")))
        {
            plugin.Configuration.CustomAvatarCrops[fileName] = new Vector4(cropX / w, cropY / h, (cropX + cropSize) / w, (cropY + cropSize) / h);
            plugin.SaveConfiguration();
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, Loc.T("Crop.Reset")))
        {
            ResetCrop(w, h);
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, Loc.T("Social.No")))
        {
            IsOpen = false;
        }
    }

    private void ResetCrop(float w, float h)
    {
        cropSize = MathF.Min(w, h);
        cropX = (w - cropSize) / 2f;
        cropY = (h - cropSize) / 2f;
    }

    private void Zoom(float factor, float shortSide)
    {
        var centerX = cropX + (cropSize / 2f);
        var centerY = cropY + (cropSize / 2f);
        cropSize = Math.Clamp(cropSize * factor, shortSide * 0.1f, shortSide);
        cropX = centerX - (cropSize / 2f);
        cropY = centerY - (cropSize / 2f);
    }

    private void Clamp(float w, float h, float shortSide)
    {
        cropSize = Math.Clamp(cropSize, shortSide * 0.1f, shortSide);
        cropX = Math.Clamp(cropX, 0f, w - cropSize);
        cropY = Math.Clamp(cropY, 0f, h - cropSize);
    }
}
