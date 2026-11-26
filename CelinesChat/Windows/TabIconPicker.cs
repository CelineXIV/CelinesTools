using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using CelinesChat.Services;

namespace CelinesChat.Windows;

internal static class TabIconPicker
{
    internal static readonly FontAwesomeIcon[] Icons =
    {
        FontAwesomeIcon.Users,
        FontAwesomeIcon.UserFriends,
        FontAwesomeIcon.User,
        FontAwesomeIcon.Comments,
        FontAwesomeIcon.CommentDots,
        FontAwesomeIcon.Envelope,
        FontAwesomeIcon.Bullhorn,
        FontAwesomeIcon.Bell,
        FontAwesomeIcon.ShieldAlt,
        FontAwesomeIcon.Heart,
        FontAwesomeIcon.Star,
        FontAwesomeIcon.Crown,
        FontAwesomeIcon.Home,
        FontAwesomeIcon.Globe,
        FontAwesomeIcon.Flag,
        FontAwesomeIcon.Map,
        FontAwesomeIcon.TheaterMasks,
        FontAwesomeIcon.BookOpen,
        FontAwesomeIcon.Scroll,
        FontAwesomeIcon.Feather,
        FontAwesomeIcon.Music,
        FontAwesomeIcon.Dice,
        FontAwesomeIcon.Gamepad,
        FontAwesomeIcon.Trophy,
        FontAwesomeIcon.Dragon,
        FontAwesomeIcon.Fire,
        FontAwesomeIcon.Bolt,
        FontAwesomeIcon.Moon,
        FontAwesomeIcon.Sun,
        FontAwesomeIcon.Leaf,
        FontAwesomeIcon.Seedling,
        FontAwesomeIcon.Paw,
        FontAwesomeIcon.Cat,
        FontAwesomeIcon.Ghost,
        FontAwesomeIcon.Gem,
        FontAwesomeIcon.Coins,
        FontAwesomeIcon.Store,
        FontAwesomeIcon.Hammer,
        FontAwesomeIcon.Flask,
        FontAwesomeIcon.Cog,
    };

    private const int PerRow = 8;

    internal static bool DrawGrid(ChatTab tab, Plugin plugin)
    {
        var changed = false;
        var size = new Vector2(ImGui.GetFrameHeight() * 1.1f);

        using (ImRaii.PushId("tabIcons" + tab.Id))
        {
            if (ImGui.Button(Loc.T("Tabs.IconNone"), new Vector2((size.X * PerRow) + (ImGui.GetStyle().ItemSpacing.X * (PerRow - 1)), 0f)))
            {
                tab.Icon = null;
                changed = true;
            }

            for (var i = 0; i < Icons.Length; i++)
            {
                if (i % PerRow != 0)
                {
                    ImGui.SameLine();
                }

                var icon = Icons[i];
                var selected = tab.Icon == icon;
                bool pressed;
                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], selected))
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    pressed = ImGui.Button(icon.ToIconString() + "##icon" + i, size);
                }

                if (pressed)
                {
                    tab.Icon = icon;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            plugin.SaveConfiguration();
        }

        return changed;
    }

    internal static void DrawMenu(ChatTab tab, Plugin plugin)
    {
        using var menu = ImRaii.Menu(Loc.T("Tabs.Icon") + "##iconMenu" + tab.Id);
        if (menu.Success)
        {
            DrawGrid(tab, plugin);
        }
    }

    internal static void DrawButton(ChatTab tab, Plugin plugin)
    {
        var popupId = "##tabIconPopup" + tab.Id;
        bool pressed;
        using (ImRaii.PushFont(UiBuilder.IconFont, tab.Icon != null))
        {
            pressed = ImGui.Button((tab.Icon?.ToIconString() ?? Loc.T("Tabs.IconNone")) + "##iconButton" + tab.Id, new Vector2(0f, ImGui.GetFrameHeight()));
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Loc.T("Tabs.Icon"));

        if (pressed)
        {
            ImGui.OpenPopup(popupId);
        }

        using var popup = ImRaii.Popup(popupId);
        if (popup.Success && DrawGrid(tab, plugin))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    internal static float IconWidth(FontAwesomeIcon? icon)
    {
        if (icon is not { } value)
        {
            return 0f;
        }

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            return ImGui.CalcTextSize(value.ToIconString()).X * 0.85f;
        }
    }

    internal static void DrawIcon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 pos, float lineHeight, uint color)
    {
        var fontSize = ImGui.GetFontSize() * 0.85f;
        drawList.AddText(UiBuilder.IconFont, fontSize, pos + new Vector2(0f, (lineHeight - fontSize) / 2f + (1f * ImGuiHelpers.GlobalScale)), color, icon.ToIconString());
    }
}
