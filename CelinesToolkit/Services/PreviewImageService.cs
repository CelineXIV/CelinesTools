using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CelinesToolkit.Services;

public sealed class PreviewImageService : IDisposable
{
    private const int MaxDimension = 1024;
    private const uint ClipboardFormatBitmap = 2u;

    private static readonly Regex OgImageRegex = new(
        "<meta\\s+property=[\"']og:image[\"']\\s+content=[\"']([^\"']+)[\"']",
        RegexOptions.IgnoreCase);

    private static readonly Regex XivModArchiveImageRegex = new(
        @"https?://static\.xivmodarchive\.com/mod-images/[a-fA-F0-9\-]+\.(?:jpg|jpeg|png|webp)",
        RegexOptions.IgnoreCase);

    private readonly HttpClient httpClient = new();

    public static bool IsImageInClipboard()
    {
        return IsClipboardFormatAvailable(ClipboardFormatBitmap);
    }

    public bool TrySaveFromClipboard(string modFolderPath, out string? error)
    {
        error = null;
        var image = GetImageFromClipboard();
        if (image == null)
        {
            error = Loc.T("PreviewManager.Error.ClipboardEmpty");
            return false;
        }

        using (image)
        {
            SaveImage(image, modFolderPath);
        }

        return true;
    }

    public bool TrySaveFromFile(string sourcePath, string modFolderPath, out string? error)
    {
        error = null;
        try
        {
            using var image = Image.FromFile(sourcePath);
            SaveImage(image, modFolderPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task<(bool Success, string? Error)> SaveFromUrlAsync(string url, string modFolderPath)
    {
        try
        {
            var imageUrl = url;
            if (url.Contains("xivmodarchive.com", StringComparison.OrdinalIgnoreCase) && !IsDirectImageUrl(url))
            {
                imageUrl = await ResolveXivModArchiveImageUrlAsync(url);
                if (imageUrl == null)
                {
                    return (false, Loc.T("PreviewManager.Error.NoImageFound"));
                }
            }

            var bytes = await httpClient.GetByteArrayAsync(imageUrl);
            using var ms = new MemoryStream(bytes);
            using var image = Image.FromStream(ms);
            SaveImage(image, modFolderPath);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<string?> ResolveXivModArchiveImageUrlAsync(string pageUrl)
    {
        var html = await httpClient.GetStringAsync(pageUrl);
        var match = OgImageRegex.Match(html);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var fallbackMatch = XivModArchiveImageRegex.Match(html);
        return fallbackMatch.Success ? fallbackMatch.Value : null;
    }

    private static bool IsDirectImageUrl(string url)
    {
        return url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveImage(Image originalImage, string modFolderPath)
    {
        var width = originalImage.Width;
        var height = originalImage.Height;
        if (width > MaxDimension || height > MaxDimension)
        {
            var ratio = (float)width / height;
            if (ratio > 1f)
            {
                width = MaxDimension;
                height = (int)(MaxDimension / ratio);
            }
            else
            {
                height = MaxDimension;
                width = (int)(MaxDimension * ratio);
            }
        }

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(originalImage, new Rectangle(0, 0, width, height));
        }

        var targetPath = Path.Combine(modFolderPath, "preview.png");
        bitmap.Save(targetPath, ImageFormat.Png);
    }

    private static Image? GetImageFromClipboard()
    {
        if (!IsClipboardFormatAvailable(ClipboardFormatBitmap))
        {
            return null;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(ClipboardFormatBitmap);
            return handle != IntPtr.Zero ? Image.FromHbitmap(handle) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);
}
