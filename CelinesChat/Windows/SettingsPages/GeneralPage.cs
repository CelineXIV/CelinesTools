using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.UI;
using CelinesChat.Windows;

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

        var keepFocus = config.KeepInputFocusAfterSend;
        if (ImGui.Checkbox(Loc.T("Settings.KeepInputFocusAfterSend"), ref keepFocus))
        {
            config.KeepInputFocusAfterSend = keepFocus;
            plugin.SaveConfiguration();
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

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.VisibilityHeader"));

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(Loc.T("Settings.VisibilityHint"));
        ImGui.PopStyleColor();

        var showDuringCutscenes = config.ShowChatDuringCutscenes;
        if (ImGui.Checkbox(Loc.T("Settings.ShowDuringCutscenes"), ref showDuringCutscenes))
        {
            config.ShowChatDuringCutscenes = showDuringCutscenes;
            plugin.SaveConfiguration();
        }

        var showDuringLoadingScreens = config.ShowChatDuringLoadingScreens;
        if (ImGui.Checkbox(Loc.T("Settings.ShowDuringLoadingScreens"), ref showDuringLoadingScreens))
        {
            config.ShowChatDuringLoadingScreens = showDuringLoadingScreens;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.TabsHeader"));

        var tabWheelScroll = config.TabStripWheelScrollEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.TabStripWheelScroll"), ref tabWheelScroll))
        {
            config.TabStripWheelScrollEnabled = tabWheelScroll;
            plugin.SaveConfiguration();
        }

        var tabBgEnabled = config.TabStripBackgroundColorEnabled;
        if (ImGui.Checkbox(Loc.T("Settings.TabStripBackgroundEnabled"), ref tabBgEnabled))
        {
            config.TabStripBackgroundColorEnabled = tabBgEnabled;
            plugin.SaveConfiguration();
        }

        var blinkWhisperTabs = config.BlinkUnreadWhisperTabs;
        if (ImGui.Checkbox(Loc.T("Settings.BlinkUnreadWhisperTabs"), ref blinkWhisperTabs))
        {
            config.BlinkUnreadWhisperTabs = blinkWhisperTabs;
            plugin.SaveConfiguration();
        }

        // Applies to every tab that doesn't have its own override - a fixed tab via its
        // right-click quick-edit popup, a whisper tab via the same popup's color picker.
        ImGui.BeginDisabled(!tabBgEnabled);
        ImGui.Indent();
        var tabBgColor = config.TabStripBackgroundColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.TabStripBackgroundColor"), ref tabBgColor))
        {
            config.TabStripBackgroundColor = tabBgColor;
            plugin.SaveConfiguration();
        }

        ImGui.Unindent();
        ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Settings.ToolbarHeader"));

        ImGui.SetNextItemWidth(180);
        var iconPackIndex = (int)config.ToolbarIconPack;
        if (ImGui.Combo(Loc.T("Settings.ToolbarIconPack"), ref iconPackIndex, ToolbarIconPackLabels, ToolbarIconPackLabels.Length))
        {
            config.ToolbarIconPack = (ToolbarIconPack)iconPackIndex;
            plugin.SaveConfiguration();
        }

        // Only meaningful for the glass packs - Classic keeps using ImGui's own theme colors
        // untouched, same as it always did.
        var isGlassPack = config.ToolbarIconPack != ToolbarIconPack.Classic;
        ImGui.BeginDisabled(!isGlassPack);
        ImGui.Indent();

        var iconBgColor = config.ToolbarIconBackgroundColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.ToolbarIconBackgroundColor"), ref iconBgColor))
        {
            config.ToolbarIconBackgroundColor = iconBgColor;
            plugin.SaveConfiguration();
        }

        var useCustomIconColor = config.ToolbarUseCustomIconColor;
        if (ImGui.Checkbox(Loc.T("Settings.ToolbarUseCustomIconColor"), ref useCustomIconColor))
        {
            config.ToolbarUseCustomIconColor = useCustomIconColor;
            plugin.SaveConfiguration();
        }

        ImGui.BeginDisabled(!useCustomIconColor);
        var iconColor = config.ToolbarIconColor;
        if (ImGui.ColorEdit4(Loc.T("Settings.ToolbarIconColor"), ref iconColor))
        {
            config.ToolbarIconColor = iconColor;
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();

        ImGui.Unindent();
        ImGui.EndDisabled();

        // A couple of real toolbar buttons rendered right here in whichever pack/colors are
        // currently selected - a combo label and color swatches alone don't convey what the
        // result actually looks like.
        ImGui.TextDisabled(Loc.T("Settings.ToolbarIconPackPreview"));
        ImGui.SameLine();

        if (config.ToolbarIconPack == ToolbarIconPack.Classic)
        {
            ImGuiComponents.IconButton(FontAwesomeIcon.Cog);
            ImGui.SameLine();
            ImGuiComponents.IconButton(FontAwesomeIcon.Wifi);
            ImGui.SameLine();
            ImGuiComponents.IconButton(FontAwesomeIcon.Users);
        }
        else
        {
            var previewIconColor = useCustomIconColor ? iconColor : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            GlassIconButton.Draw(FontAwesomeIcon.Cog, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview1");
            ImGui.SameLine();
            GlassIconButton.Draw(FontAwesomeIcon.Wifi, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview2");
            ImGui.SameLine();
            GlassIconButton.Draw(FontAwesomeIcon.Users, config.ToolbarIconPack, iconBgColor, previewIconColor, "##preview3");
        }
    }

    private static readonly string[] ToolbarIconPackLabels =
    {
        Loc.T("Settings.ToolbarIconPack.Classic"),
        Loc.T("Settings.ToolbarIconPack.AeroGlass"),
        Loc.T("Settings.ToolbarIconPack.FrostedGlass"),
    };

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
