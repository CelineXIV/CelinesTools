using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class InfoPage
{
    private readonly Plugin plugin;

    public InfoPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UndoAlt, Loc.T("Settings.Reset")))
        {
            ColorsPage.ResetToDefaults(config);
            config.MaxMessageLength = 400;
            config.DelayMs = 600;
            config.SendOnEnter = true;
            config.FileLogSay = true;
            config.FileLogParty = true;
            config.FileLogTell = true;
            config.FileLogYell = true;
            config.FileLogShout = true;
            config.FileLogFreeCompany = true;
            config.FileLogLinkshell = true;
            config.PlaySounds = true;
            config.WhisperSoundEnabled = true;
            config.WhisperSoundEffect = 3;
            config.MentionSoundEnabled = false;
            config.MentionSoundEffect = 6;
            config.ChatWindowLocked = false;
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
    }
}
