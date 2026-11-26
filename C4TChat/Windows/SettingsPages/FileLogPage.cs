using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using C4TChat.Services;

namespace C4TChat.Windows.SettingsPages;

internal sealed class FileLogPage
{
    private readonly Plugin plugin;
    private Task<long>? sizeTask;
    private long? folderSize;

    public FileLogPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        var fileLogSay = config.FileLogSay;
        if (ImGui.Checkbox(Loc.T("Channel.Say") + "##fileLogSay", ref fileLogSay))
        {
            config.FileLogSay = fileLogSay;
            plugin.SaveConfiguration();
        }

        var fileLogParty = config.FileLogParty;
        if (ImGui.Checkbox(Loc.T("Channel.Party") + "##fileLogParty", ref fileLogParty))
        {
            config.FileLogParty = fileLogParty;
            plugin.SaveConfiguration();
        }

        using (ImRaii.Disabled())
        {
            var whisperAlwaysLogged = true;
            ImGui.Checkbox(Loc.T("Channel.Whisper") + "##fileLogTellAlwaysOn", ref whisperAlwaysLogged);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.FileLogTellAlwaysOnHint"));
        }

        var fileLogYell = config.FileLogYell;
        if (ImGui.Checkbox(Loc.T("Channel.Yell") + "##fileLogYell", ref fileLogYell))
        {
            config.FileLogYell = fileLogYell;
            plugin.SaveConfiguration();
        }

        var fileLogShout = config.FileLogShout;
        if (ImGui.Checkbox(Loc.T("Channel.Shout") + "##fileLogShout", ref fileLogShout))
        {
            config.FileLogShout = fileLogShout;
            plugin.SaveConfiguration();
        }

        var fileLogFc = config.FileLogFreeCompany;
        if (ImGui.Checkbox(Loc.T("Channel.FreeCompany") + "##fileLogFc", ref fileLogFc))
        {
            config.FileLogFreeCompany = fileLogFc;
            plugin.SaveConfiguration();
        }

        var fileLogLs = config.FileLogLinkshell;
        if (ImGui.Checkbox(Loc.T("Channel.Linkshell") + "##fileLogLs", ref fileLogLs))
        {
            config.FileLogLinkshell = fileLogLs;
            plugin.SaveConfiguration();
        }

        var fileLogAlliance = config.FileLogAlliance;
        if (ImGui.Checkbox(Loc.T("Channel.Alliance") + "##fileLogAlliance", ref fileLogAlliance))
        {
            config.FileLogAlliance = fileLogAlliance;
            plugin.SaveConfiguration();
        }

        var fileLogPvpTeam = config.FileLogPvpTeam;
        if (ImGui.Checkbox(Loc.T("Channel.PvpTeam") + "##fileLogPvpTeam", ref fileLogPvpTeam))
        {
            config.FileLogPvpTeam = fileLogPvpTeam;
            plugin.SaveConfiguration();
        }

        var fileLogNovice = config.FileLogNoviceNetwork;
        if (ImGui.Checkbox(Loc.T("Channel.NoviceNetwork") + "##fileLogNovice", ref fileLogNovice))
        {
            config.FileLogNoviceNetwork = fileLogNovice;
            plugin.SaveConfiguration();
        }

        DrawCleanupSection(config);
    }

    private void DrawCleanupSection(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("FileLog.CleanupHeader"));
        ImGui.TextWrapped(Loc.T("FileLog.CleanupHint"));

        var archiveDays = config.LogArchiveAfterDays;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt(Loc.T("FileLog.ArchiveAfter"), ref archiveDays, 1, 7))
        {
            config.LogArchiveAfterDays = Math.Clamp(archiveDays, 0, 3650);
            plugin.SaveConfiguration();
        }

        var deleteDays = config.LogDeleteAfterDays;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt(Loc.T("FileLog.DeleteAfter"), ref deleteDays, 1, 30))
        {
            config.LogDeleteAfterDays = Math.Clamp(deleteDays, 0, 3650);
            plugin.SaveConfiguration();
        }

        if (config.LogDeleteAfterDays > 0 && config.LogArchiveAfterDays >= config.LogDeleteAfterDays)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.75f, 0.3f, 1f), Loc.T("FileLog.ArchiveNeverHint"));
        }

        using (ImRaii.Disabled(plugin.IsLogMaintenanceRunning || (config.LogArchiveAfterDays <= 0 && config.LogDeleteAfterDays <= 0)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Broom, Loc.T("FileLog.RunNow")))
            {
                plugin.RunLogMaintenance(true);
                folderSize = null;
            }
        }

        if (plugin.LastLogMaintenanceResult is { } result)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(result);
        }

        if (sizeTask is { IsCompleted: true } done)
        {
            folderSize = done.IsCompletedSuccessfully ? done.Result : null;
            sizeTask = null;
        }

        if (folderSize == null && sizeTask == null)
        {
            var folder = plugin.ChatLog.LogsFolderPath;
            sizeTask = Task.Run(() => LogArchive.FolderSize(folder));
        }

        ImGui.TextDisabled(folderSize is { } size ? Loc.T("FileLog.FolderSize", LogArchive.FormatSize(size)) : Loc.T("FileLog.FolderSizeMeasuring"));
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt))
        {
            folderSize = null;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FolderOpen, Loc.T("FileLog.OpenFolder")))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = plugin.ChatLog.LogsFolderPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                plugin.Log.Warning(ex, "[C4TChat] Log-Ordner konnte nicht geöffnet werden.");
            }
        }
    }
}
