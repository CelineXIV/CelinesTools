using System;
using System.Diagnostics;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class BackupPage
{
    private static readonly string[] ExcludedProperties =
    {
        nameof(Configuration.WebClientAuthCode),
        nameof(Configuration.WebClientAuthStore),
        nameof(Configuration.WebClientSessions),
    };

    private readonly Plugin plugin;
    private readonly FileDialogManager fileDialog = new();
    private string? statusMessage;
    private bool statusIsError;
    private bool confirmImport;
    private string? pendingImportPath;

    public BackupPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    private string BackupFolder => Path.Combine(plugin.ConfigDirectory, "Backups");

    public void Draw()
    {
        ImGui.TextUnformatted(Loc.T("Backup.Header"));
        DrawHint(Loc.T("Backup.Hint"));
        ImGui.Spacing();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileExport, Loc.T("Backup.Export")))
        {
            Export();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileImport, Loc.T("Backup.Import")))
        {
            Directory.CreateDirectory(BackupFolder);
            fileDialog.OpenFileDialog(
                Loc.T("Backup.Import"),
                ".json",
                (ok, paths) =>
                {
                    if (ok && paths.Count > 0)
                    {
                        pendingImportPath = paths[0];
                        confirmImport = true;
                    }
                },
                1,
                BackupFolder,
                false);
        }

        if (confirmImport && pendingImportPath != null)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f)))
            {
                ImGui.TextWrapped(Loc.T("Backup.ConfirmImport", Path.GetFileName(pendingImportPath)));
            }

            if (ImGui.Button(Loc.T("Backup.ConfirmYes")))
            {
                Import(pendingImportPath);
                confirmImport = false;
                pendingImportPath = null;
            }

            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Common.Cancel")))
            {
                confirmImport = false;
                pendingImportPath = null;
            }
        }

        if (statusMessage != null)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, statusIsError ? new System.Numerics.Vector4(1f, 0.45f, 0.45f, 1f) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            {
                ImGui.TextWrapped(statusMessage);
            }
        }
    }

    public void DrawDialogs() => fileDialog.Draw();

    private void Export()
    {
        try
        {
            Directory.CreateDirectory(BackupFolder);
            var json = JObject.FromObject(plugin.Configuration);
            foreach (var name in ExcludedProperties)
            {
                json.Remove(name);
            }

            var path = Path.Combine(BackupFolder, $"CelinesChat-Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
            File.WriteAllText(path, json.ToString(Formatting.Indented));
            SetStatus(Loc.T("Backup.ExportDone", Path.GetFileName(path)), false);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[CelinesChat] Export der Einstellungen fehlgeschlagen.");
            SetStatus(Loc.T("Backup.Failed", ex.Message), true);
        }
    }

    private void Import(string path)
    {
        try
        {
            var json = JObject.Parse(File.ReadAllText(path));
            if (json[nameof(Configuration.ChatTabs)] == null)
            {
                SetStatus(Loc.T("Backup.NotABackup"), true);
                return;
            }

            foreach (var name in ExcludedProperties)
            {
                json.Remove(name);
            }

            using (var reader = json.CreateReader())
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                serializer.Populate(reader, plugin.Configuration);
            }

            plugin.OnConfigurationImported();
            SetStatus(Loc.T("Backup.ImportDone"), false);
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[CelinesChat] Import der Einstellungen fehlgeschlagen.");
            SetStatus(Loc.T("Backup.Failed", ex.Message), true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        statusMessage = message;
        statusIsError = isError;
    }

    private static void DrawHint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(text);
        }
    }
}
