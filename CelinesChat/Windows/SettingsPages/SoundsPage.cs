using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI;
using CelinesChat.Services;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class SoundsPage
{
    private readonly Plugin plugin;

    public SoundsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var config = plugin.Configuration;

        var playSounds = config.PlaySounds;
        if (ImGui.Checkbox(Loc.T("Settings.PlaySounds"), ref playSounds))
        {
            config.PlaySounds = playSounds;
            plugin.SaveConfiguration();
        }

        using (ImRaii.Disabled(!playSounds))
        {
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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted(Loc.T("Settings.ChannelSoundsHeader"));
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            {
                ImGui.TextWrapped(Loc.T("Settings.ChannelSoundsHint"));
            }

            foreach (var category in ChannelSoundCategories)
            {
                config.ChannelSounds.TryGetValue(category, out var channelSound);
                var enabled = channelSound?.Enabled ?? false;

                if (ImGui.Checkbox($"{Loc.T($"Channel.{category}")}##channelSoundEnabled{category}", ref enabled))
                {
                    channelSound = GetOrCreateChannelSound(config, category);
                    channelSound.Enabled = enabled;
                    plugin.SaveConfiguration();
                }

                DrawSoundEffectPicker(
                    $"##channelSfx{category}",
                    channelSound?.SoundEffect ?? 1,
                    value => GetOrCreateChannelSound(config, category).SoundEffect = value,
                    !enabled);
            }
        }
    }

    private void DrawSoundEffectPicker(string id, int currentValue, Action<int> setValue, bool disabled)
    {
        using (ImRaii.Disabled(disabled))
        using (ImRaii.PushIndent())
        {
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
        }
    }

    // Whisper has its own dedicated setting above; Mention is cross-cutting rather than tied to one
    // channel. Deliberately excludes the high-volume system/combat-log categories in ChatCategory -
    // this list is only the channels people actually talk in.
    private static readonly ChatCategory[] ChannelSoundCategories =
    {
        ChatCategory.Say,
        ChatCategory.Party,
        ChatCategory.Yell,
        ChatCategory.Shout,
        ChatCategory.FreeCompany,
        ChatCategory.Alliance,
        ChatCategory.Linkshell,
        ChatCategory.CrossWorldLinkshell,
        ChatCategory.PvpTeam,
        ChatCategory.NoviceNetwork,
    };

    private static ChannelSoundSetting GetOrCreateChannelSound(Configuration config, ChatCategory category)
    {
        if (!config.ChannelSounds.TryGetValue(category, out var setting))
        {
            setting = new ChannelSoundSetting();
            config.ChannelSounds[category] = setting;
        }

        return setting;
    }
}
