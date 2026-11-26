using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace C4TChat.Services;

internal static class ThemeTransfer
{
    private const string Prefix = "C4TTheme1:";

    internal static readonly string[] Properties =
    {
        nameof(Configuration.Design),
        nameof(Configuration.ShowC4tLogo),
        nameof(Configuration.ShowC4tWatermark),
        nameof(Configuration.SendAccentColor),
        nameof(Configuration.DefaultTextColor),
        nameof(Configuration.EmoteTextColor),
        nameof(Configuration.OocTextColor),
        nameof(Configuration.MentionColor),
        nameof(Configuration.TimestampColor),
        nameof(Configuration.ValidCommandColor),
        nameof(Configuration.InvalidCommandColor),
        nameof(Configuration.ChatColours),
        nameof(Configuration.LinkshellColours),
        nameof(Configuration.CrossWorldLinkshellColours),
        nameof(Configuration.FriendNameColor),
        nameof(Configuration.FcMemberNameColor),
        nameof(Configuration.FavoriteNameColor),
        nameof(Configuration.TabStripBackgroundColorEnabled),
        nameof(Configuration.TabStripBackgroundColor),
        nameof(Configuration.ToolbarIconPack),
        nameof(Configuration.ToolbarIconBackgroundColor),
        nameof(Configuration.ToolbarIconColor),
        nameof(Configuration.ToolbarUseCustomIconColor),
        nameof(Configuration.WindowOpacity),
        nameof(Configuration.UnfocusedWindowOpacity),
        nameof(Configuration.ChatLogBackgroundOpacity),
        nameof(Configuration.ChromeBackgroundOpacity),
        nameof(Configuration.TabStripBackgroundOpacity),
        nameof(Configuration.SocialWindowOpacity),
    };

    internal sealed record Preset(string NameKey, Action<Configuration> Apply);

    internal static readonly Preset[] Presets =
    {
        new("Theme.PresetC4tPurple", c =>
        {
            c.Design = ChatDesign.C4TPurple;
            c.SendAccentColor = Rgb(0xB0, 0x26, 0xFF);
            c.FavoriteNameColor = Rgb(0xFF, 0x3E, 0xC8);
            c.FriendNameColor = Rgb(0xC9, 0x9B, 0xFF);
            c.MentionColor = Rgb(0xFF, 0xC8, 0x3D);
            c.WindowOpacity = 0.85f;
            c.UnfocusedWindowOpacity = 0.7f;
            c.ToolbarIconPack = ToolbarIconPack.Flat;
        }),
        new("Theme.PresetC4tBlue", c =>
        {
            c.Design = ChatDesign.C4T;
            c.SendAccentColor = Rgb(0x28, 0x78, 0xFF);
            c.FavoriteNameColor = Rgb(0x00, 0xE6, 0xC8);
            c.FriendNameColor = Rgb(0x8C, 0xC7, 0xFF);
            c.MentionColor = Rgb(0xFF, 0xD2, 0x4D);
            c.WindowOpacity = 0.85f;
            c.UnfocusedWindowOpacity = 0.7f;
            c.ToolbarIconPack = ToolbarIconPack.Flat;
        }),
        new("Theme.PresetMidnight", c =>
        {
            c.Design = ChatDesign.Standard;
            c.SendAccentColor = Rgb(0xF2, 0x8C, 0x28);
            c.FavoriteNameColor = Rgb(0xFF, 0x8C, 0xC6);
            c.FriendNameColor = Rgb(0x8C, 0xC7, 0xFF);
            c.FcMemberNameColor = Rgb(0x8C, 0xE0, 0x9A);
            c.MentionColor = Rgb(0xFF, 0xD9, 0x40);
            c.WindowOpacity = 0.9f;
            c.UnfocusedWindowOpacity = 0.75f;
            c.ToolbarIconPack = ToolbarIconPack.Flat;
        }),
        new("Theme.PresetSakura", c =>
        {
            c.Design = ChatDesign.Standard;
            c.SendAccentColor = Rgb(0xF2, 0x7B, 0xB5);
            c.FavoriteNameColor = Rgb(0xFF, 0xA8, 0xD2);
            c.FriendNameColor = Rgb(0xC6, 0xB8, 0xFF);
            c.FcMemberNameColor = Rgb(0xA8, 0xE6, 0xCF);
            c.MentionColor = Rgb(0xFF, 0xE0, 0x82);
            c.EmoteTextColor = Rgb(0xE8, 0xA0, 0xFF);
            c.WindowOpacity = 0.8f;
            c.UnfocusedWindowOpacity = 0.65f;
            c.ToolbarIconPack = ToolbarIconPack.Flat;
        }),
    };

    internal static string Export(Configuration config)
    {
        var all = JObject.FromObject(config, JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None }));
        var theme = new JObject();
        foreach (var name in Properties)
        {
            if (all[name] is { } value)
            {
                theme[name] = value;
            }
        }

        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(theme.ToString(Formatting.None)));
    }

    internal static JObject? Parse(string text)
    {
        text = text.Trim();
        try
        {
            if (text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                text = Encoding.UTF8.GetString(Convert.FromBase64String(text[Prefix.Length..]));
            }

            var json = JObject.Parse(text);
            var theme = new JObject();
            foreach (var name in Properties)
            {
                if (json[name] is { } value)
                {
                    theme[name] = value;
                }
            }

            return theme.Count > 0 ? theme : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void Apply(Configuration config, JObject theme)
    {
        using var reader = theme.CreateReader();
        JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace }).Populate(reader, config);
        config.RestoreComparers();
    }

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
