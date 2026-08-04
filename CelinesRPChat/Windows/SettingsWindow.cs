using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CelinesRPChat.Windows;

internal sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;

    public SettingsWindow(Plugin plugin) : base(WindowTitles.Settings)
    {
        this.plugin = plugin;
        Size = new Vector2(380, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(plugin.Configuration.WindowOpacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);

        var config = plugin.Configuration;

        var defaultColor = config.DefaultTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.DefaultColor"), ref defaultColor))
        {
            config.DefaultTextColor = defaultColor;
            plugin.SaveConfiguration();
        }

        var emoteColor = config.EmoteTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.EmoteColor"), ref emoteColor))
        {
            config.EmoteTextColor = emoteColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(emoteColor, Loc.T("Settings.EmotePreview"));

        var oocColor = config.OocTextColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.OocColor"), ref oocColor))
        {
            config.OocTextColor = oocColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(oocColor, Loc.T("Settings.OocPreview"));

        var senderColor = config.ChatLogSenderNameColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.SenderNameColor"), ref senderColor))
        {
            config.ChatLogSenderNameColor = senderColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(senderColor, Loc.T("Settings.SenderNamePreview"));

        var mentionColor = config.MentionColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.MentionColor"), ref mentionColor))
        {
            config.MentionColor = mentionColor;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextColored(mentionColor, Loc.T("Settings.MentionPreview"));

        ImGui.Separator();

        ImGui.SetNextItemWidth(120);
        var maxLength = config.MaxMessageLength;
        if (ImGui.InputInt(Loc.T("Settings.MaxLength"), ref maxLength, 10, 50))
        {
            config.MaxMessageLength = System.Math.Max(20, maxLength);
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(120);
        var delay = config.DelayMs;
        if (ImGui.InputInt(Loc.T("Settings.Delay"), ref delay, 50, 200))
        {
            config.DelayMs = System.Math.Max(0, delay);
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.FileLogHeader"));

        var fileLogSay = config.FileLogSay;
        if (ImGui.Checkbox(Loc.T("Channel.Say") + "##fileLogSay", ref fileLogSay))
        {
            config.FileLogSay = fileLogSay;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var fileLogParty = config.FileLogParty;
        if (ImGui.Checkbox(Loc.T("Channel.Party") + "##fileLogParty", ref fileLogParty))
        {
            config.FileLogParty = fileLogParty;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
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

        ImGui.SameLine();
        var fileLogShout = config.FileLogShout;
        if (ImGui.Checkbox(Loc.T("Channel.Shout") + "##fileLogShout", ref fileLogShout))
        {
            config.FileLogShout = fileLogShout;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var fileLogFc = config.FileLogFreeCompany;
        if (ImGui.Checkbox(Loc.T("Channel.FreeCompany") + "##fileLogFc", ref fileLogFc))
        {
            config.FileLogFreeCompany = fileLogFc;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var fileLogLs = config.FileLogLinkshell;
        if (ImGui.Checkbox(Loc.T("Channel.Linkshell") + "##fileLogLs", ref fileLogLs))
        {
            config.FileLogLinkshell = fileLogLs;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();

        ImGui.SetNextItemWidth(150);
        var fontScale = config.FontScale;
        if (ImGui.SliderFloat(Loc.T("Settings.FontScale"), ref fontScale, 0.7f, 2.0f))
        {
            config.FontScale = fontScale;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(150);
        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.WindowOpacity"), ref opacity, 0.2f, 1.0f))
        {
            config.WindowOpacity = opacity;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();

        if (ImGui.Button(Loc.T("Settings.Reset")))
        {
            config.DefaultTextColor = new Vector4(1f, 1f, 1f, 1f);
            config.EmoteTextColor = new Vector4(0.68f, 0.42f, 0.87f, 1f);
            config.OocTextColor = new Vector4(0.6f, 0.6f, 0.6f, 1f);
            config.ChatLogSenderNameColor = new Vector4(0.55f, 0.75f, 1f, 1f);
            config.MentionColor = new Vector4(1f, 0.85f, 0.2f, 1f);
            config.MaxMessageLength = 400;
            config.DelayMs = 600;
            config.FileLogSay = true;
            config.FileLogParty = true;
            config.FileLogTell = true;
            config.FileLogYell = true;
            config.FileLogShout = true;
            config.FileLogFreeCompany = true;
            config.FileLogLinkshell = true;
            config.FontScale = 1f;
            config.WindowOpacity = 1f;
            plugin.SaveConfiguration();
        }
    }
}
