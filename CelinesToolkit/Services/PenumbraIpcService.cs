using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Dalamud.Plugin;

namespace CelinesToolkit.Services;

public sealed class PenumbraIpcService
{
    private const byte CollectionTypeYourself = 0;

    private readonly IDalamudPluginInterface pluginInterface;

    public PenumbraIpcService(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public string? GetModDirectory()
    {
        try
        {
            var directory = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory").InvokeFunc();
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }
        catch
        {
        }

        return GetModDirectoryFromConfig();
    }

    public Dictionary<string, string> GetModList()
    {
        try
        {
            return pluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList").InvokeFunc();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public Dictionary<string, bool> GetEnabledStates()
    {
        try
        {
            var collection = pluginInterface
                .GetIpcSubscriber<byte, (Guid Id, string Name)?>("Penumbra.GetCollection")
                .InvokeFunc(CollectionTypeYourself);

            if (collection == null)
            {
                return new Dictionary<string, bool>();
            }

            var (ec, settings) = pluginInterface
                .GetIpcSubscriber<Guid, bool, bool, int, (int Ec, Dictionary<string, (bool Enabled, int Priority, Dictionary<string, List<string>> Settings, bool Temporary, bool ForceInherit)>? Settings)>("Penumbra.GetAllModSettings")
                .InvokeFunc(collection.Value.Id, false, false, 0);

            if (ec != 0 || settings == null)
            {
                return new Dictionary<string, bool>();
            }

            var result = new Dictionary<string, bool>();
            foreach (var (folderName, modSettings) in settings)
            {
                result[folderName] = modSettings.Enabled;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, bool>();
        }
    }

    private static string? GetModDirectoryFromConfig()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "Penumbra.json");

            if (!File.Exists(configPath))
            {
                return null;
            }

            var json = File.ReadAllText(configPath);
            var match = Regex.Match(json, "\"ModDirectory\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\") : null;
        }
        catch
        {
            return null;
        }
    }
}
