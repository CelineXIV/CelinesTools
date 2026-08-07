using System;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class GeneralPage
{
    private readonly Plugin plugin;

    public GeneralPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(120);
        var maxLength = config.MaxMessageLength;
        if (ImGui.InputInt(Loc.T("Settings.MaxLength"), ref maxLength, 10, 50))
        {
            config.MaxMessageLength = Math.Max(20, maxLength);
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(120);
        var delay = config.DelayMs;
        if (ImGui.InputInt(Loc.T("Settings.Delay"), ref delay, 50, 200))
        {
            config.DelayMs = Math.Max(0, delay);
            plugin.SaveConfiguration();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.WhisperDelayHint", Plugin.MinWhisperDelayMs));
        ImGui.PopStyleColor();

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.HotkeysHeader"));

        var sendOnEnter = config.SendOnEnter;
        if (ImGui.Checkbox(Loc.T("Settings.SendOnEnter"), ref sendOnEnter))
        {
            config.SendOnEnter = sendOnEnter;
            plugin.SaveConfiguration();
        }

        if (!sendOnEnter)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(Loc.T("Settings.SendOnEnterOff"));
            ImGui.PopStyleColor();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.SoundsHeader"));

        var playSounds = config.PlaySounds;
        if (ImGui.Checkbox(Loc.T("Settings.PlaySounds"), ref playSounds))
        {
            config.PlaySounds = playSounds;
            plugin.SaveConfiguration();
        }

        ImGui.BeginDisabled(!playSounds);

        var whisperSoundEnabled = config.WhisperSoundEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.WhisperSound"), ref whisperSoundEnabled))
        {
            config.WhisperSoundEnabled = whisperSoundEnabled;
            plugin.SaveConfiguration();
        }

        DrawSoundEffectPicker("##whisperSfx", config.WhisperSoundEffect, value => config.WhisperSoundEffect = value, !whisperSoundEnabled);

        var mentionSoundEnabled = config.MentionSoundEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.MentionSound"), ref mentionSoundEnabled))
        {
            config.MentionSoundEnabled = mentionSoundEnabled;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Settings.MentionSoundHint"));
        }

        DrawSoundEffectPicker("##mentionSfx", config.MentionSoundEffect, value => config.MentionSoundEffect = value, !mentionSoundEnabled);

        ImGui.EndDisabled();
    }

    /// <summary>
    /// A 1-16 picker for one of the game's own built-in sound effect slots (the same ones
    /// &lt;se.1&gt;-&lt;se.16&gt; macros use) plus a button to preview it immediately - there's no
    /// canonical "the tell sound", so letting people pick-and-preview beats guessing one for them.
    /// </summary>
    private void DrawSoundEffectPicker(string id, int currentValue, Action<int> setValue, bool disabled)
    {
        ImGui.BeginDisabled(disabled);

        ImGui.Indent();
        ImGui.SetNextItemWidth(120);
        var value = currentValue;
        if (ImGui.SliderInt(id, ref value, 1, 16, Loc.T("Settings.SoundEffectFormat")))
        {
            setValue(Math.Clamp(value, 1, 16));
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Settings.SoundEffectTest") + id))
        {
            unsafe
            {
                UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(currentValue, 1, 16));
            }
        }

        ImGui.Unindent();

        ImGui.EndDisabled();
    }
}
