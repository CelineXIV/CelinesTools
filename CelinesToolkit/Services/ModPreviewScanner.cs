using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CelinesToolkit.Services;

public sealed class ModPreviewScanner
{
    private static readonly string[] PreviewBaseNames = { "preview", "cover" };
    private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly PenumbraIpcService penumbraIpc;

    public ModPreviewScanner(PenumbraIpcService penumbraIpc)
    {
        this.penumbraIpc = penumbraIpc;
    }

    public List<ModPreviewInfo> Scan(out string? modDirectory)
    {
        var result = new List<ModPreviewInfo>();
        modDirectory = penumbraIpc.GetModDirectory();
        if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
        {
            return result;
        }

        var enabledStates = penumbraIpc.GetEnabledStates();

        foreach (var (folderName, displayName) in penumbraIpc.GetModList())
        {
            var fullPath = Path.Combine(modDirectory, folderName);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            var info = new ModPreviewInfo
            {
                FolderName = folderName,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? folderName : displayName,
                FullPath = fullPath,
                PreviewImagePath = FindPreviewImage(fullPath),
                IsEnabled = enabledStates.TryGetValue(folderName, out var enabled) ? enabled : null,
            };

            ApplyMetaJson(info);
            result.Add(info);
        }

        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static string? FindPreviewImage(string modFolder)
    {
        foreach (var baseName in PreviewBaseNames)
        {
            foreach (var ext in PreviewExtensions)
            {
                var candidate = Path.Combine(modFolder, baseName + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void ApplyMetaJson(ModPreviewInfo info)
    {
        var metaPath = Path.Combine(info.FullPath, "meta.json");
        if (!File.Exists(metaPath))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("Author", out var authorProp))
            {
                info.Author = authorProp.GetString();
            }

            if (root.TryGetProperty("Version", out var versionProp))
            {
                info.Version = versionProp.GetString();
            }
        }
        catch
        {
        }
    }
}
