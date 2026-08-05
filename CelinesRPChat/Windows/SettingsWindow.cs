using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));

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

        var sendColor = config.SendAccentColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.SendAccentColor"), ref sendColor))
        {
            config.SendAccentColor = sendColor;
            plugin.SaveConfiguration();
        }

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

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.WhisperDelayHint", Plugin.MinWhisperDelayMs));
        ImGui.PopStyleColor();

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
        ImGui.TextUnformatted(Loc.T("Settings.LogVisibilityHeader"));

        var showSay = config.ChatLogShowSay;
        if (ImGui.Checkbox(Loc.T("Channel.Say") + "##showSay", ref showSay))
        {
            config.ChatLogShowSay = showSay;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showParty = config.ChatLogShowParty;
        if (ImGui.Checkbox(Loc.T("Channel.Party") + "##showParty", ref showParty))
        {
            config.ChatLogShowParty = showParty;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showTell = config.ChatLogShowTell;
        if (ImGui.Checkbox(Loc.T("Channel.Whisper") + "##showTell", ref showTell))
        {
            config.ChatLogShowTell = showTell;
            plugin.SaveConfiguration();
        }

        var showYell = config.ChatLogShowYell;
        if (ImGui.Checkbox(Loc.T("Channel.Yell") + "##showYell", ref showYell))
        {
            config.ChatLogShowYell = showYell;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showShout = config.ChatLogShowShout;
        if (ImGui.Checkbox(Loc.T("Channel.Shout") + "##showShout", ref showShout))
        {
            config.ChatLogShowShout = showShout;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showFc = config.ChatLogShowFreeCompany;
        if (ImGui.Checkbox(Loc.T("Channel.FreeCompany") + "##showFc", ref showFc))
        {
            config.ChatLogShowFreeCompany = showFc;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showLs = config.ChatLogShowLinkshell;
        if (ImGui.Checkbox(Loc.T("Channel.Linkshell") + "##showLs", ref showLs))
        {
            config.ChatLogShowLinkshell = showLs;
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

        ImGui.SetNextItemWidth(150);
        var unfocusedOpacity = config.UnfocusedWindowOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.UnfocusedOpacity"), ref unfocusedOpacity, 0.0f, 1.0f))
        {
            config.UnfocusedWindowOpacity = unfocusedOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.UnfocusedOpacityHint"));
        ImGui.PopStyleColor();

        ImGui.SetNextItemWidth(150);
        var logBgOpacity = config.ChatLogBackgroundOpacity;
        if (ImGui.SliderFloat(Loc.T("Settings.LogBackgroundOpacity"), ref logBgOpacity, 0.0f, 1.0f))
        {
            config.ChatLogBackgroundOpacity = logBgOpacity;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UndoAlt, Loc.T("Settings.Reset")))
        {
            config.DefaultTextColor = new Vector4(1f, 1f, 1f, 1f);
            config.EmoteTextColor = new Vector4(0.68f, 0.42f, 0.87f, 1f);
            config.OocTextColor = new Vector4(0.6f, 0.6f, 0.6f, 1f);
            config.ChatLogSenderNameColor = new Vector4(0.55f, 0.75f, 1f, 1f);
            config.MentionColor = new Vector4(1f, 0.85f, 0.2f, 1f);
            config.SendAccentColor = new Vector4(0.16f, 0.45f, 0.24f, 1f);
            config.MaxMessageLength = 400;
            config.DelayMs = 600;
            config.FileLogSay = true;
            config.FileLogParty = true;
            config.FileLogTell = true;
            config.FileLogYell = true;
            config.FileLogShout = true;
            config.FileLogFreeCompany = true;
            config.FileLogLinkshell = true;
            config.ChatLogShowSay = true;
            config.ChatLogShowParty = true;
            config.ChatLogShowTell = true;
            config.ChatLogShowYell = false;
            config.ChatLogShowShout = false;
            config.ChatLogShowFreeCompany = false;
            config.ChatLogShowLinkshell = false;
            config.FontScale = 1f;
            config.WindowOpacity = 1f;
            config.UnfocusedWindowOpacity = 0.35f;
            config.ChatLogBackgroundOpacity = 0.15f;
            config.ComposeAreaHeight = 130f;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.InfoCircle, Loc.T("Compose.Changelog")))
        {
            ImGui.OpenPopup("##changelogPopup");
        }

        if (ImGui.BeginPopup("##changelogPopup"))
        {
            for (var i = Changelog.Entries.Length - 1; i >= 0; i--)
            {
                var (version, text) = Changelog.Entries[i];
                ImGui.TextUnformatted($"{version}: {text}");
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar(2);
    }
}
