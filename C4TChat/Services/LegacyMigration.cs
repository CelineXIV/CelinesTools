using System;
using System.IO;
using System.Text.RegularExpressions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace C4TChat.Services;

internal static class LegacyMigration
{
    internal const string LegacyInternalName = "CelinesChat";

    private static readonly Regex TypeName = new("(\"\\$type\"\\s*:\\s*\")([^\"]*)(\")", RegexOptions.Compiled);

    internal static bool Run(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        try
        {
            var configFile = pluginInterface.ConfigFile;
            var root = configFile.DirectoryName;
            if (configFile.Exists || root == null)
            {
                return false;
            }

            var legacyFile = Path.Combine(root, LegacyInternalName + ".json");
            if (!File.Exists(legacyFile))
            {
                return false;
            }

            var json = File.ReadAllText(legacyFile);
            json = TypeName.Replace(json, m => m.Groups[1].Value + m.Groups[2].Value.Replace(LegacyInternalName, "C4TChat") + m.Groups[3].Value);

            var legacyDirectory = Path.Combine(root, LegacyInternalName);
            if (Directory.Exists(legacyDirectory))
            {
                CopyDirectory(legacyDirectory, pluginInterface.ConfigDirectory.FullName);
            }

            File.WriteAllText(configFile.FullName, json);
            log.Info("[C4TChat] Einstellungen, Logs und Bilder von CelinesChat übernommen.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Übernahme der CelinesChat-Daten fehlgeschlagen.");
            return false;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }
    }

    internal static LegacyState Check(IDalamudPluginInterface pluginInterface)
    {
        foreach (var plugin in pluginInterface.InstalledPlugins)
        {
            if (!string.Equals(plugin.InternalName, LegacyInternalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!plugin.IsLoaded)
            {
                return LegacyState.InstalledInactive;
            }

            return plugin.Version >= new Version(1, 3, 2, 0) ? LegacyState.Bridge : LegacyState.RunningOld;
        }

        return LegacyState.None;
    }

    internal enum LegacyState
    {
        None,
        InstalledInactive,
        Bridge,
        RunningOld,
    }
}
