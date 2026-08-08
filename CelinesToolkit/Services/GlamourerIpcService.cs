using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;

namespace CelinesToolkit.Services;

/// <summary>
/// Thin wrapper around Glamourer's public IPC (the "Glamourer.Api" NuGet package) - mirrors
/// PenumbraIpcService's style of catching every call so a missing/unloaded Glamourer degrades to
/// "unavailable" instead of throwing into caller code.
/// </summary>
public sealed class GlamourerIpcService
{
    private readonly IDalamudPluginInterface pluginInterface;

    public GlamourerIpcService(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    /// <summary>Null means Glamourer's IPC could not be reached at all (not installed/loaded) - distinct from a non-null empty dictionary, which just means Glamourer is available but the user has no designs yet.</summary>
    public Dictionary<Guid, (string DisplayName, string FullPath, uint DisplayColor, bool ShownInQdb)>? GetDesignList()
    {
        try
        {
            return new GetDesignListExtended(pluginInterface).Invoke();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Applies a design to any actor (the local player, the current target, ...) - the caller decides which.</summary>
    public GlamourerApiEc Apply(Guid designId, IGameObject? actor, ApplyFlag flags)
    {
        if (actor == null)
        {
            return GlamourerApiEc.ActorNotFound;
        }

        try
        {
            return new ApplyDesign(pluginInterface).Invoke(designId, actor.ObjectIndex, 0, flags);
        }
        catch
        {
            return GlamourerApiEc.UnknownError;
        }
    }

    /// <summary>Opens Glamourer's own main window on the Designs tab with this design selected - lets the user jump straight to editing it there.</summary>
    public void OpenInGlamourer(Guid designId)
    {
        try
        {
            new OpenDesign(pluginInterface).Invoke(designId);
        }
        catch
        {
            // Glamourer not installed/loaded - nothing to open.
        }
    }

    /// <summary>
    /// Folder colors and separators aren't exposed by Glamourer's public IPC (only per-design
    /// DisplayColor is, via GetDesignListExtended) - they only exist in Glamourer's own private
    /// design_filesystem/organization.json, which also has no explicit ordering data at all: not
    /// for folders, not for separators, not relative to designs. That means Glamourer's own
    /// browser must just be sorting everything (folders, separators, and designs alike) together
    /// alphabetically by name at render time - which is exactly why a single-letter separator like
    /// "D" visually acts as a section divider in the first place. Read the file directly instead,
    /// the same way PenumbraIpcService.GetModDirectoryFromConfig already falls back to reading
    /// Penumbra's own config file when IPC doesn't cover something. Both dictionaries are keyed by
    /// full path (matching FullPath's "/"-joined segments), value is Glamourer's own packed color.
    /// </summary>
    public GlamourerOrganization GetOrganization()
    {
        var folderColors = new Dictionary<string, uint>();
        var separatorColors = new Dictionary<string, uint>();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "Glamourer", "design_filesystem", "organization.json");

            if (!File.Exists(path))
            {
                return new GlamourerOrganization(folderColors, separatorColors);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Folders", out var folders))
            {
                foreach (var folder in folders.EnumerateObject())
                {
                    if (folder.Value.TryGetProperty("CollapsedColor", out var colorProp) && colorProp.TryGetUInt32(out var color) && color != 0)
                    {
                        folderColors[folder.Name] = color;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("Separators", out var separators))
            {
                foreach (var separator in separators.EnumerateObject())
                {
                    var color = separator.Value.TryGetProperty("Color", out var colorProp) && colorProp.TryGetUInt32(out var parsedColor)
                        ? parsedColor
                        : 0u;
                    separatorColors[separator.Name] = color;
                }
            }
        }
        catch
        {
            // Glamourer not installed, config format changed, or the file is mid-write - missing
            // organization data is a purely cosmetic degradation, not worth surfacing.
        }

        return new GlamourerOrganization(folderColors, separatorColors);
    }
}

/// <summary>Folder colors keyed by full path, and separators (path-keyed too, value is their own color) - see GlamourerIpcService.GetOrganization.</summary>
public sealed record GlamourerOrganization(Dictionary<string, uint> FolderColors, Dictionary<string, uint> SeparatorColors);
