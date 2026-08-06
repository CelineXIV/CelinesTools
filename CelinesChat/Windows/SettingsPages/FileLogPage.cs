using Dalamud.Bindings.ImGui;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class FileLogPage
{
    private readonly Plugin plugin;

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

        var fileLogTell = config.FileLogTell;
        if (ImGui.Checkbox(Loc.T("Channel.Whisper") + "##fileLogTell", ref fileLogTell))
        {
            config.FileLogTell = fileLogTell;
            plugin.SaveConfiguration();
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
    }
}
