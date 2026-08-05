namespace CelinesToolkit.Services;

public sealed class ModPreviewInfo
{
    public required string FolderName { get; init; }

    public required string DisplayName { get; init; }

    public required string FullPath { get; init; }

    public string? Author { get; set; }

    public string? Version { get; set; }

    public string? PreviewImagePath { get; set; }

    public bool? IsEnabled { get; set; }

    public bool HasPreview => PreviewImagePath != null;
}
